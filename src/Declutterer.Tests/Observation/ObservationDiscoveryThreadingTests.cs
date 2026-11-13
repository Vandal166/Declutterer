using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Domain.Services.Scanning;
using Declutterer.Tests.Cleanup;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Tests.Observation;

/// <summary>
/// Smart Select is started from the UI thread. Watch discovery does long synchronous work after its awaits (the per-folder
/// safety walks of stale folders, the nomination), so that work must run off the caller's thread and must not post its
/// continuations to the caller's synchronization context.
/// </summary>
public class ObservationDiscoveryThreadingTests : IDisposable
{
    private readonly CleanupScenario _s = new();

    public void Dispose() => _s.Dispose();

    /// <summary>Counts the continuations that were posted to it, the way a UI context receives them.</summary>
    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => d(state)); // never runs inline, so blocking on the task below can not deadlock
        }
    }

    private sealed class RecordingProgress : IProgress<SmartCleanupProgress>
    {
        public int SafetyReports;
        public int SafetyThreadId;

        public void Report(SmartCleanupProgress value)
        {
            if (value.Stage != "Checking stale folders for safety" || value.Total <= 0)
                return;

            Interlocked.CompareExchange(ref SafetyThreadId, Environment.CurrentManagedThreadId, 0);
            Interlocked.Increment(ref SafetyReports);
        }
    }

    [Fact]
    public void Discovery_StaleSafetyWorkRunsOffTheCallerThread_AndDoesNotPostToItsContext()
    {
        var old = DateTime.UtcNow.AddDays(-800);
        var settings = _s.File("AppData/Roaming/OldTool/settings.json", 800, 3000);
        var archive = _s.File("Misc/old/archive.bin", 1500, 5000);
        File.SetLastWriteTimeUtc(settings, old);
        File.SetLastWriteTimeUtc(archive, old);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(settings)!, old);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(archive)!, old);
        var observation = new ObservationOptions { Enabled = true, ObserveMinBytes = 0 };
        var service = new ObservationCandidateDiscoveryService(new ScanFilterService(new ScanFilterBuilder()), new JunkAssessmentEngine(observation));
        var roots = new[] { new TreeNode { Name = "test", FullPath = _s.Root, IsDirectory = true, HasChildren = true } };
        var machine = new SystemSnapshot(_s.Machine);
        var options = new SmartCleanupOptions { UseCreationTime = false, MaxDegreeOfParallelism = 2 };
        var context = new CountingContext();
        var progress = new RecordingProgress();
        var callerThreadId = Environment.CurrentManagedThreadId;
        var previous = SynchronizationContext.Current;

        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
#pragma warning disable xUnit1031 // the point of the test is to start the work on a context and observe where it continues
            var result = service.DiscoverAsync(roots, null, machine, options, observation, null, Array.Empty<string>(), progress, CancellationToken.None)
                .GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            Assert.NotNull(result);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(0, context.Posts);
        Assert.True(progress.SafetyReports > 0, "The fixture must exercise stale-folder safety finalization.");
        Assert.NotEqual(callerThreadId, progress.SafetyThreadId);
    }
}
