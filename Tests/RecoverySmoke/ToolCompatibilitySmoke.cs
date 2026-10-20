using PS3Butler.Configuration;
using PS3Butler.Services;
using PS3Butler.Services.Execution;

public static class ToolCompatibilitySmoke
{
    public static async Task RunAsync(string toolsDirectory, string v3Path)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "tool-compat", "checks-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        var keyPath = Path.Combine(toolsDirectory, "Keys", Path.GetFileNameWithoutExtension(source) + ".dkey");
        Require(KeyIndexService.TryReadKey(keyPath, out var key, out _), "test key available");

        foreach (var executable in new[] { Path.Combine(toolsDirectory, "ps3decrs.exe"), Path.GetFullPath(v3Path) })
        {
            var name = Path.GetFileName(executable);
            var detected = new ToolDetectionService(new AppSettings { RedrrxPs3DecPath = executable }).DetectTools()[0];
            Require(detected.IsAvailable && detected.Path == executable, $"tool detection accepts {name}");
            var output = Path.Combine(root, name, "real output with spaces");
            var progress = new List<double>();
            var result = await new Ps3DecService(executable, 4).DecryptAsync(source, key, output,
                new InlineProgress<double>(progress.Add));
            Require(result.Success && new FileInfo(result.OutputPath).Length == new FileInfo(source).Length,
                $"real {name} decrypts with unchanged arguments: {result.Error}");
            Require(progress.Any(value => value > 0 && value < 100), $"{name} reports progress before completion");
            Require(!Directory.EnumerateFiles(output).Any(path => path.EndsWith(".part") || path.EndsWith(".resume")),
                $"{name} leaves no recovery sidecars after success");

            // Capture the real request without running a child process, exercising
            // result-based cancellation, timeout and error cleanup for both names.
            foreach (var outcome in new[] { "cancel", "timeout", "error", "missing" })
            {
                var destination = Path.Combine(root, name, outcome);
                var runner = new ResultRunner(outcome, executable, source, key, destination);
                var failed = await new Ps3DecService(executable, 4, runner).DecryptAsync(source, key, destination);
                Require(!failed.Success && !Directory.EnumerateFiles(destination).Any(), $"{name} cleans output on {outcome}");
            }
        }
        Console.WriteLine("Redrrx tool compatibility smoke checks passed.");
    }

    private sealed class ResultRunner(string outcome, string executable, string source, string key, string destination) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var outputName = Path.GetFileNameWithoutExtension(source) + ".decrypted";
            var expected = new[] { "--dk", key, "--tc", "4", "--output-dir", destination, "--output-name", outputName, "--skip", source };
            if (request.FileName != executable || request.ArgumentList is null || !request.ArgumentList.SequenceEqual(expected))
                throw new Exception("Redrrx argument compatibility regressed.");
            var output = Path.Combine(destination, outputName + ".iso");
            if (outcome != "missing") File.WriteAllText(output, "partial output");
            File.WriteAllText(output + ".part", "partial data");
            File.WriteAllText(output + ".resume", "checkpoint");
            return Task.FromResult(new ProcessRunResult
            {
                WasCancelled = outcome == "cancel",
                TimedOut = outcome == "timeout",
                ExitCode = outcome == "error" ? 1 : 0
            });
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
