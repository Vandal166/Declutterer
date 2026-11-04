using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Serilog;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Runs the complete Smart Select engine on one watched item: a one-item tree, a fresh machine snapshot, a fresh analysis session
/// (so the descendant safety scan is done again, nothing is remembered) and the watch ledger's current state as evidence. The
/// verdict is stored on the entry. It has no UI dependencies, so the scheduled headless run can use it too.
/// </summary>
public sealed class ObservationReassessor : IObservationReassessor
{
    private readonly JunkAssessmentEngine _engine;
    private readonly ISystemSnapshotProvider _snapshots;
    private readonly IObservationLedger _ledger;
    private readonly FingerprintWalker _walker;
    private readonly SmartCleanupOptions _options;
    private readonly ObservationOptions _observation;
    private readonly TimeProvider _time;

    public ObservationReassessor(JunkAssessmentEngine engine, ISystemSnapshotProvider snapshots, IObservationLedger ledger, FingerprintWalker walker,
        ObservationOptions observation, SmartCleanupOptions? options = null, TimeProvider? timeProvider = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _walker = walker ?? throw new ArgumentNullException(nameof(walker));
        _observation = observation ?? throw new ArgumentNullException(nameof(observation));
        _options = options ?? new SmartCleanupOptions();
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReassessmentResult?> ReassessAsync(WatchEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = entry.DisplayPath;
            if (!Directory.Exists(path))
                return null;

            var now = _time.GetUtcNow().UtcDateTime;
            var machine = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var lookup = await _ledger.LoadLookupAsync(cancellationToken).ConfigureAwait(false);

            var walk = await Task.Run(() => _walker.Walk(path, new FingerprintWalkOptions
            {
                MaxEntries = _observation.FingerprintMaxEntries,
                MaxDepth = _observation.FingerprintMaxDepth,
                DeadlineUtc = now + _options.TimeBudget,
            }, cancellationToken), cancellationToken).ConfigureAwait(false);

            var item = NodeSnapshot.ForStandalone(path, walk.TotalBytes);
            var session = new AnalysisSession { Observations = lookup };
            var analysis = await _engine.AnalyzeAsync(new[] { item.Parent! }, machine, _options, null, cancellationToken, session).ConfigureAwait(false);
            if (!analysis.Assessments.TryGetValue(item, out var assessment))
                return null;

            var reasons = assessment.Explain(8);
            await _ledger.UpdateAssessmentAsync(entry.Id, assessment.Tier, assessment.JunkScore, reasons, now, cancellationToken).ConfigureAwait(false);
            return new ReassessmentResult(assessment.Tier, assessment.JunkScore, assessment.DeletionRisk, reasons, assessment);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reassessing watched item {Path} failed", entry.DisplayPath);
            return null;
        }
    }
}
