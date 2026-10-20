using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
                        WorkingDirectory =
                            request.WorkingDirectory ??
                            string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput =
                            request.RedirectStandardOutput,
                        RedirectStandardError =
                            request.RedirectStandardError,
                        StandardOutputEncoding = request.StandardOutputEncoding
                    }
            };

        if (request.ArgumentList is not null)
        {
            foreach (var argument in request.ArgumentList)
            {
                process.StartInfo.ArgumentList.Add(
                    argument);
            }
        }
        else
        {
            process.StartInfo.Arguments =
                request.Arguments;
        }

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
                    ? ReadStreamAsync(
                        process.StandardOutput,
                        request.StandardOutputChunkReceived)
                    : Task.FromResult(string.Empty);

            var standardErrorTask =
                request.RedirectStandardError
                    ? ReadStreamAsync(
                        process.StandardError,
                        request.StandardErrorChunkReceived)
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

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        Action<string>? chunkReceived)
    {
        var output =
            new StringBuilder();

        var buffer =
            new char[1024];

        while (true)
        {
            var charsRead =
                await reader.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length));

            if (charsRead == 0)
            {
                break;
            }

            var chunk =
                new string(
                    buffer,
                    0,
                    charsRead);

            output.Append(chunk);

            chunkReceived?.Invoke(
                chunk);
        }

        return output.ToString();
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
