using PS3Butler.Configuration;
using PS3Butler.Models;
using System;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public class GamePreflightService
{
    private static readonly string[] SupportedExtensions =
    [
        ".iso",
        ".zip",
        ".7z",
        ".rar"
    ];

    private readonly AppSettings _settings;

    public GamePreflightService(AppSettings settings)
    {
        _settings = settings;
    }

    public void Preflight(GameJob job)
    {
        job.KeyPath = null;
        job.Details = string.Empty;

        if (!File.Exists(job.SourcePath))
        {
            job.Status = "Error";
            job.Details = "Source file does not exist.";
            return;
        }

        var extension = Path.GetExtension(job.SourcePath);

        if (!SupportedExtensions.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            job.Status = "Error";
            job.Details = $"Unsupported file type: {extension}";
            return;
        }

        var gameName = Path.GetFileNameWithoutExtension(job.SourcePath);

        var keyPath = FindMatchingKey(gameName);

        if (keyPath is null)
        {
            job.Status = "Missing key";
            job.Details = $"No matching .dkey found for \"{gameName}\".";
            return;
        }

        job.KeyPath = keyPath;
        job.Status = "Ready";
        job.Details = $"Key found: {Path.GetFileName(keyPath)}";
    }

    private string? FindMatchingKey(string gameName)
    {
        if (!Directory.Exists(_settings.RedumpKeysPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(_settings.RedumpKeysPath)
            .FirstOrDefault(path =>
                string.Equals(
                    Path.GetExtension(path),
                    ".dkey",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    gameName,
                    StringComparison.OrdinalIgnoreCase));
    }
}