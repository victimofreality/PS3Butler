using System.Security.Cryptography;
using PS3Butler.Services;
using PS3Butler.Services.Execution;

internal static class ArchiveFormatSmoke
{
    public static async Task RunAsync()
    {
        const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        var root = Path.GetFullPath(Path.Combine("artifacts", "archive-formats", Guid.NewGuid().ToString("N")));
        var input = Path.Combine(root, "input", "Nested folder — café");
        Directory.CreateDirectory(input);
        var source = Path.Combine(input, "Smoke Game 日本語.ISO");
        // These fixtures exercise archive transport, not PS3 image validity.
        var payload = new byte[256 * 1024];
        new Random(42).NextBytes(payload.AsSpan(0, 8192));
        await File.WriteAllBytesAsync(source, payload);
        await File.WriteAllTextAsync(Path.Combine(input, "readme.txt"), "Non-ISO companion file");
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
        var inspection = new ArchiveInspectionService(sevenZip);
        var extraction = new ArchiveExtractionService(sevenZip);
        var checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS: {label}");
            checks++;
        }

        foreach (var (name, format, method) in new[]
        {
            ("zip-deflate", "zip", "Deflate"),
            ("zip-bzip2", "zip", "BZip2"),
            ("7z-lzma", "7z", "LZMA"),
            ("7z-lzma2", "7z", "LZMA2")
        })
        {
            var archive = Path.Combine(root, name + "." + format);
            await PackAsync(archive, format, method, Path.Combine(root, "input", "*"));
            Check(new FileInfo(archive).Length < payload.Length, $"{name}: fixture is compressed");
            Check(await inspection.FindIsoNameAsync(archive) == Path.GetFileName(source), $"{name}: inspect nested Unicode ISO name");
            var result = await extraction.ExtractIsoAsync(archive, Path.Combine(root, name + " output"));
            Check(result.Success, $"{name}: extraction succeeds ({result.Error})");
            Check(result.Success && Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(result.IsoPath))) == expectedHash,
                $"{name}: extracted bytes match source");
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(result.IsoPath)!, "readme.txt")), $"{name}: companion file and nested layout preserved");
        }

        var invalidInput = Path.Combine(root, "invalid-input");
        Directory.CreateDirectory(invalidInput);
        await File.WriteAllTextAsync(Path.Combine(invalidInput, "readme.txt"), "No ISO");
        var noIso = Path.Combine(root, "no-iso.zip");
        await PackAsync(noIso, "zip", "Deflate", Path.Combine(invalidInput, "*"));
        Check(await inspection.FindIsoNameAsync(noIso) is null, "no-ISO archive rejected by inspection");
        var noIsoResult = await extraction.ExtractIsoAsync(noIso, Path.Combine(root, "no-iso-output"));
        Check(!noIsoResult.Success && noIsoResult.Error.Contains("no ISO"), "no-ISO extraction reports reason");

        File.Copy(source, Path.Combine(invalidInput, "first.iso"));
        File.Copy(source, Path.Combine(invalidInput, "second.iso"));
        var multiple = Path.Combine(root, "multiple.7z");
        await PackAsync(multiple, "7z", "LZMA2", Path.Combine(invalidInput, "*"));
        Check(await inspection.FindIsoNameAsync(multiple) is null, "multiple-ISO archive rejected by inspection");
        var multipleResult = await extraction.ExtractIsoAsync(multiple, Path.Combine(root, "multiple-output"));
        Check(!multipleResult.Success && multipleResult.Error.Contains("multiple ISOs"), "multiple-ISO extraction reports ambiguity");

        var corrupt = Path.Combine(root, "corrupt.7z");
        await File.WriteAllTextAsync(corrupt, "This is not an archive.");
        Check(await inspection.FindIsoNameAsync(corrupt) is null, "corrupt archive rejected by inspection");
        var corruptResult = await extraction.ExtractIsoAsync(corrupt, Path.Combine(root, "corrupt-output"));
        Check(!corruptResult.Success && corruptResult.Error.Contains("7-Zip exited with code"), "corrupt archive reports tool failure");
        Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))) == expectedHash, "source fixture unchanged");
        Console.WriteLine($"Archive format smoke: {checks} checks passed. Fixtures: {root}");

        async Task PackAsync(string archive, string format, string method, string files)
        {
            var arguments = new List<string> { "a", "-t" + format, "-mx=5", "-m0=" + method };
            if (format == "zip") arguments[3] = "-mm=" + method;
            else arguments.Add("-ms=on");
            arguments.Add(archive);
            arguments.Add(files);
            var result = await new ProcessRunner().RunAsync(new ProcessRunRequest
            {
                FileName = sevenZip,
                ArgumentList = arguments,
                Timeout = TimeSpan.FromSeconds(30)
            });
            if (!result.Success) throw new InvalidOperationException($"Fixture creation failed: {result.StandardError}");
        }
    }
}
