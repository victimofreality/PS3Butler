namespace PS3Butler.Tests.TestInfrastructure;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PS3Butler.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string CreateDirectory(string relativePath)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(
        string relativePath,
        byte[]? contents = null)
    {
        var path = GetPath(relativePath);
        var parent = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(path, contents ?? []);
        return path;
    }

    public string CreateTextFile(
        string relativePath,
        string contents)
    {
        var path = GetPath(relativePath);
        var parent = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, contents);
        return path;
    }

    public string GetPath(string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace(
            System.IO.Path.AltDirectorySeparatorChar,
            System.IO.Path.DirectorySeparatorChar);

        return System.IO.Path.Combine(Path, normalizedRelativePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best-effort. A failing assertion should remain
            // the primary test result.
        }
    }
}
