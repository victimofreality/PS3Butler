using PS3Butler.Models;
using System.Collections.Generic;
using System.IO;

namespace PS3Butler.Services;

public class ToolDetectionService
{
    public List<ToolStatus> DetectTools()
    {
        return
        [
            CheckFile(
                "PS3Dec (Redrrx)",
                @"C:\PS3Tools\ps3decrs.exe"),

            CheckFile(
                "PS3Dec (Classic)",
                @"C:\PS3Tools\PS3Dec.exe"),

            CheckFile(
                "7-Zip",
                @"C:\Program Files\7-Zip\7z.exe"),

            CheckDirectory(
                "Redump Keys",
                @"C:\PS3Tools\Keys")
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

    private static ToolStatus CheckDirectory(string name, string path)
    {
        return new ToolStatus
        {
            Name = name,
            Path = path,
            IsAvailable = Directory.Exists(path)
        };
    }
}