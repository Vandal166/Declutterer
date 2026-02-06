using Serilog.Core;
using Serilog.Events;

namespace Declutterer.Benchmark;

/// <summary>
/// Null sink to suppress Serilog output during benchmarks.
/// </summary>
public class NullSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        // Do nothing - this suppresses all log output
    }
}