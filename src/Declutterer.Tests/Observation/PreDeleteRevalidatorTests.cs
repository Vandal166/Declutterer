using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Tests.Observation;

public class PreDeleteRevalidatorTests : IDisposable
{
    private readonly CheckInRig _r = new();
    private PreDeleteRevalidator _v = null!;
    private Func<string, string?> _gate = _ => null;

    public PreDeleteRevalidatorTests()
    {
        _r.Build();
        Rebuild();
    }

    public void Dispose() => _r.Dispose();

    private void Rebuild() => _v = new PreDeleteRevalidator(_r.Ledger, _r.Walker, _r.Options, p => _gate(p), _r.Time);

    private sealed class FakeHistory : IDeletionHistoryRepository
    {
        public List<DeletionHistoryEntry> Entries { get; } = new();
        public Task AddEntryAsync(DeletionHistoryEntry entry) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IList<DeletionHistoryEntry>> GetEntriesAsync() => Task.FromResult<IList<DeletionHistoryEntry>>(Entries);
        public Task<IList<DeletionHistoryEntry>> GetEntriesBetweenAsync(DateTime from, DateTime to) => GetEntriesAsync();
        public Task<IList<DeletionHistoryEntry>> GetEntriesByPathAsync(string pathPattern) => GetEntriesAsync();
        public Task DeleteEntryAsync(string entryId) => Task.CompletedTask;
        public Task ClearHistoryAsync() => Task.CompletedTask;
        public Task<int> GetEntryCountAsync() => Task.FromResult(Entries.Count);
    }

    private static TreeNode Node(string path) => new() { Name = Path.GetFileName(path), FullPath = path, IsDirectory = true, Size = 1000 };

    private async Task<WatchEntry> Watch(string rel = "item")
    {
        _r.CreateItem(rel);
        return await _r.EnrollAsync(rel, requiredDays: 5);
    }

    private DeleteService Deleter() => new(new FakeHistory(), _v);

    // ── the revalidator ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnItemWithoutHistory_IsAlwaysAllowed_AndTheGateIsNotConsulted()
    {
        _r.CreateItem("plain");
        _gate = _ => "should not be asked";

        var result = await _v.RevalidateAsync(Node(_r.Item("plain")));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task AnUnchangedWatchedItem_IsAllowed()
    {
        await Watch();
        await _r.RunAfterDaysAsync(1);

        Assert.True((await _v.RevalidateAsync(Node(_r.Item("item")))).Allowed);
    }

    [Fact]
    public async Task AChangeSinceTheLastCheckIn_BlocksTheItem_AndMarksItAlive()
    {
        var e = await Watch();
        await _r.RunAfterDaysAsync(1);
        _r.Dir.CreateFile("item/sub/appeared.dat", 55);

        var result = await _v.RevalidateAsync(Node(_r.Item("item")));

        Assert.False(result.Allowed);
        Assert.Contains("changed since it was last checked", result.Reason);
        var now = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Alive, now.State);
        Assert.Equal(_r.Time.UtcNow.AddDays(_r.Options.AliveCooldownDays), now.AliveUntilUtc);
        Assert.Contains("found while deleting", now.LastActiveDetail);
        Assert.Equal(CheckInVerdict.StrongUse, (await _r.Ledger.GetCheckInsAsync(e.Id, 1))[0].Verdict);
    }

    [Fact]
    public async Task AWatchedFolderInsideTheItem_IsCheckedToo()
    {
        _r.CreateItem("parent/child");
        var e = await _r.EnrollAsync("parent/child", requiredDays: 5);
        Directory.CreateDirectory(_r.Item("parent/other"));

        Assert.True((await _v.RevalidateAsync(Node(_r.Item("parent")))).Allowed);

        File.WriteAllText(_r.Item("parent/child/sub/changed.dat"), "x");
        var blocked = await _v.RevalidateAsync(Node(_r.Item("parent")));

        Assert.False(blocked.Allowed);
        Assert.Equal(WatchState.Alive, (await _r.GetAsync(e.Id)).State);
    }

    [Fact]
    public async Task AWatchedFolderThatIsGone_BlocksOnce_AndIsResolvedAsMissing()
    {
        _r.CreateItem("parent/child");
        var e = await _r.EnrollAsync("parent/child");
        Directory.Delete(_r.Item("parent/child"), recursive: true);

        var result = await _v.RevalidateAsync(Node(_r.Item("parent")));

        Assert.False(result.Allowed);
        Assert.Equal(WatchResolution.Missing, (await _r.GetAsync(e.Id)).Resolution);
        Assert.True((await _v.RevalidateAsync(Node(_r.Item("parent")))).Allowed); // nothing left to doubt
    }

    [Fact]
    public async Task AnIncompleteFingerprint_Blocks()
    {
        await Watch();
        _r.Options.FingerprintMaxEntries = 2;

        var result = await _v.RevalidateAsync(Node(_r.Item("item")));

        Assert.False(result.Allowed);
        Assert.Contains("could not be re-checked completely", result.Reason);
    }

    [Fact]
    public async Task TheSafetyGate_IsRunAgain_ForItemsWithHistory()
    {
        await Watch();
        _gate = p => "it is a system folder";

        var result = await _v.RevalidateAsync(Node(_r.Item("item")));

        Assert.False(result.Allowed);
        Assert.Contains("it is a system folder", result.Reason);
    }

    [Fact]
    public async Task ShellDroppingsAlone_AreNotUse()
    {
        await Watch();
        File.WriteAllText(_r.Item("item/Thumbs.db"), "explorer");

        Assert.True((await _v.RevalidateAsync(Node(_r.Item("item")))).Allowed);
    }

    [Fact]
    public async Task WithTheFeatureOffOrTheLedgerBroken_NothingChanges()
    {
        await Watch();
        _r.Dir.CreateFile("item/sub/appeared.dat", 55);

        _r.Options.Enabled = false;
        Assert.True((await _v.RevalidateAsync(Node(_r.Item("item")))).Allowed);

        _r.Options.Enabled = true;
        var broken = new SqliteObservationLedger(Path.Combine(_r.Dir.Path, "nowhere", "x"), _r.Time);
        Directory.CreateDirectory(Path.Combine(_r.Dir.Path, "nowhere", "x")); // a directory where the file should be
        var v = new PreDeleteRevalidator(broken, _r.Walker, _r.Options, null, _r.Time);
        Assert.True((await v.RevalidateAsync(Node(_r.Item("item")))).Allowed);
    }

    [Fact]
    public async Task OnlyActiveEntriesCount_AGivenUpItemIsNotRevalidated()
    {
        var e = await Watch();
        await _r.Ledger.UpdateAsync(e with { State = WatchState.Unobservable });
        _r.Dir.CreateFile("item/sub/appeared.dat", 55);

        Assert.True((await _v.RevalidateAsync(Node(_r.Item("item")))).Allowed);
    }

    // ── through the delete service ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteService_DeletesAnUnchangedWatchedItem_AndFinishesItsEntry()
    {
        var e = await Watch();
        await _r.RunAfterDaysAsync(1);
        var items = new System.Collections.ObjectModel.ObservableCollection<TreeNode> { Node(_r.Item("item")) };

        var result = await Deleter().DeletePermanentlyAsync(items);

        Assert.True(result.Success);
        Assert.Equal(1, result.DeletedCount);
        Assert.False(Directory.Exists(_r.Item("item")));
        Assert.Empty(items);
        var done = await _r.GetAsync(e.Id);
        Assert.Equal(WatchState.Resolved, done.State);
        Assert.Equal(WatchResolution.Deleted, done.Resolution);
    }

    [Fact]
    public async Task DeleteService_RefusesAChangedItem_KeepsItInTheList_AndReportsWhy()
    {
        var e = await Watch();
        await _r.RunAfterDaysAsync(1);
        _r.Dir.CreateFile("item/sub/appeared.dat", 55);
        _r.Dir.CreateFile("other/f.dat", 10);
        var items = new System.Collections.ObjectModel.ObservableCollection<TreeNode> { Node(_r.Item("item")), Node(_r.Item("other")) };

        var result = await Deleter().DeletePermanentlyAsync(items);

        Assert.False(result.Success);
        Assert.Equal(1, result.DeletedCount);   // the unwatched one went through
        Assert.Equal(1, result.FailedCount);
        Assert.Contains("changed since it was last checked", result.Errors.Single().ErrorMessage);
        Assert.True(Directory.Exists(_r.Item("item")));
        Assert.Single(items);
        Assert.Equal(WatchState.Alive, (await _r.GetAsync(e.Id)).State);
    }

    [Fact]
    public async Task DeleteService_RecycleBinPath_IsRevalidatedToo()
    {
        await Watch();
        _r.Dir.CreateFile("item/sub/appeared.dat", 55);
        var items = new System.Collections.ObjectModel.ObservableCollection<TreeNode> { Node(_r.Item("item")) };

        var result = await Deleter().MoveToRecycleBinAsync(items);

        Assert.Equal(1, result.FailedCount);
        Assert.Contains("changed since it was last checked", result.Errors.Single().ErrorMessage);
        Assert.True(Directory.Exists(_r.Item("item"))); // nothing was moved
    }

    [Fact]
    public async Task DeleteService_WithoutARevalidator_IsExactlyWhatItWas()
    {
        _r.CreateItem("plain");
        var items = new System.Collections.ObjectModel.ObservableCollection<TreeNode> { Node(_r.Item("plain")) };

        var result = await new DeleteService(new FakeHistory()).DeletePermanentlyAsync(items);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(_r.Item("plain")));
    }
}
