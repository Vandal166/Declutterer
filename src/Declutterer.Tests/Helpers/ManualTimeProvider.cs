namespace Declutterer.Tests.Helpers;

/// <summary>A clock the tests move by hand. The file system keeps real timestamps; only "now" is simulated.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public DateTime UtcNow => _now.UtcDateTime;

    public void Advance(TimeSpan by) => _now += by;

    public void AdvanceDays(double days) => _now += TimeSpan.FromDays(days);
}
