using System;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PS3Butler.Services;

// Transfer checkpoints belong to the work directory, not the user-input queue file.
internal sealed class DownloadResumeStore
{
    private readonly string _directory;
    public string PartialPath => Path.Combine(_directory, "download.part");
    private string MetadataPath => Path.Combine(_directory, "resume.json");

    public DownloadResumeStore(string directory) => _directory = directory;

    public static string GetDirectory(string workDirectory, string url) =>
        Path.Combine(workDirectory, "download", "resume",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));

    public Checkpoint? Load(string url)
    {
        try
        {
            var state = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(MetadataPath));
            if (state is null || state.Version != 1 || state.Url != url ||
                state.TotalBytes <= 0 ||
                !EntityTagHeaderValue.TryParse(state.ETag, out var tag) || tag.IsWeak || tag.Tag == "*" ||
                !Uri.TryCreate(state.ResponseUrl, UriKind.Absolute, out _) ||
                string.IsNullOrWhiteSpace(state.FileName) ||
                state.FileName is "." or ".." ||
                state.FileName.Contains('/') || state.FileName.Contains('\\') ||
                state.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !File.Exists(PartialPath))
                return null;

            var length = new FileInfo(PartialPath).Length;
            return length > 0 && length <= state.TotalBytes ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or UriFormatException)
        {
            return null;
        }
    }

    public void Save(Checkpoint state)
    {
        var temporaryPath = MetadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
        File.Move(temporaryPath, MetadataPath, overwrite: true);
    }

    public void Clear()
    {
        File.Delete(MetadataPath);
        File.Delete(MetadataPath + ".tmp");
        File.Delete(PartialPath);
    }

    public sealed class Checkpoint
    {
        public int Version { get; set; } = 1;
        public string Url { get; set; } = "";
        public string ResponseUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ETag { get; set; } = "";
        public long TotalBytes { get; set; }
    }
}
