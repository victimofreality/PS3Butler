using PS3Butler.Configuration;
using PS3Butler.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public class ToolDetectionService
{
    private readonly AppSettings _settings;

    public ToolDetectionService(AppSettings settings)
    {
        _settings = settings;
    }

    public List<ToolStatus> DetectTools()
    {
        return
        [
            CheckFile(
                "PS3Dec (Redrrx)",
                _settings.RedrrxPs3DecPath),

            CheckFile(
                "PS3Dec (Classic)",
                _settings.ClassicPs3DecPath),

            CheckFile(
                "7-Zip",
                _settings.SevenZipPath),

            CheckKeyDirectory(
                "Redump Keys",
                _settings.RedumpKeysPath)
        ];
    }

    private static ToolStatus CheckFile(string name, string path)
    {
        return new ToolStatus
        {
            Name = name,
            Path = path,
            IsAvailable = File.Exists(path)
        };
    }

    private static ToolStatus CheckKeyDirectory(string name, string path)
    {
        var exists = Directory.Exists(path);

        var keyCount = exists
            ? Directory.EnumerateFiles(
                path,
                "*.dkey",
                SearchOption.TopDirectoryOnly).Count()
            : 0;

        return new ToolStatus
        {
            Name = name,
            Path = path,
            IsAvailable = exists,
            ItemCount = exists ? keyCount : null
        };
    }
}