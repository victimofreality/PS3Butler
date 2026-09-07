using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Execution;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default)
    {
        using var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName = request.FileName,
                        Arguments = request.Arguments,
                        WorkingDirectory =
                            request.WorkingDirectory ??
                            string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput =
                            request.RedirectStandardOutput,
                        RedirectStandardError =
                            request.RedirectStandardError
                    }
            };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult
                {
                    ExitCode = -1,
                    StandardError =
                        $"Failed to start process: {request.FileName}"
                };
            }

            var standardOutputTask =
                request.RedirectStandardOutput
                    ? process.StandardOutput.ReadToEndAsync()
                    : Task.FromResult(string.Empty);

            var standardErrorTask =
                request.RedirectStandardError
                    ? process.StandardError.ReadToEndAsync()
                    : Task.FromResult(string.Empty);

            using var timeoutCancellationTokenSource =
                request.Timeout.HasValue
                    ? new CancellationTokenSource(
                        request.Timeout.Value)
                    : null;

            using var linkedCancellationTokenSource =
                timeoutCancellationTokenSource is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        timeoutCancellationTokenSource.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

            try
            {
                await process.WaitForExitAsync(
                    linkedCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);

                try
                {
                    await process.WaitForExitAsync();
                }
                catch
                {
                    // Best effort only.
                }

                var standardOutput =
                    await GetCompletedOutputAsync(
                        standardOutputTask);

                var standardError =
                    await GetCompletedOutputAsync(
                        standardErrorTask);

                var timedOut =
                    timeoutCancellationTokenSource?.IsCancellationRequested ==
                    true &&
                    !cancellationToken.IsCancellationRequested;

                return new ProcessRunResult
                {
                    ExitCode =
                        process.HasExited
                            ? process.ExitCode
                            : -1,

                    StandardOutput =
                        standardOutput,

                    StandardError =
                        standardError,

                    WasCancelled =
                        !timedOut,

                    TimedOut =
                        timedOut
                };
            }

            var finalStandardOutput =
                await standardOutputTask;

            var finalStandardError =
                await standardErrorTask;

            return new ProcessRunResult
            {
                ExitCode =
                    process.ExitCode,

                StandardOutput =
                    finalStandardOutput,

                StandardError =
                    finalStandardError
            };
        }
        catch (Exception ex)
        {
            TryKillProcessTree(process);

            return new ProcessRunResult
            {
                ExitCode = -1,
                StandardError = ex.Message
            };
        }
    }

    private static void TryKillProcessTree(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort process cleanup only.
        }
    }

    private static async Task<string> GetCompletedOutputAsync(
        Task<string> outputTask)
    {
        try
        {
            return await outputTask;
        }
        catch
        {
            return string.Empty;
        }
    }
}