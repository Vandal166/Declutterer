using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;

namespace Declutterer.Integration.Scheduling;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false, // arguments are passed as a list: nothing is ever interpreted by a shell
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info);
            if (process is null)
                return new ProcessRunResult(-1, string.Empty, "the process could not be started");

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
                return new ProcessRunResult(-1, string.Empty, $"timed out after {timeout.TotalSeconds:0} s");
            }

            return new ProcessRunResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ProcessRunResult(-1, string.Empty, ex.Message); // typically: the program does not exist on this system
        }
    }
}
