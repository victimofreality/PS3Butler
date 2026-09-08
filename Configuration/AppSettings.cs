using System.IO;

namespace PS3Butler.Configuration;

public class AppSettings
{
    public string RedrrxPs3DecPath { get; set; } =
        @"C:\PS3Tools\ps3decrs.exe";

    public string SevenZipPath { get; set; } =
        @"C:\Program Files\7-Zip\7z.exe";

    public string RedumpKeysPath { get; set; } =
        @"C:\PS3Tools\Keys";

    public string WorkDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "PS3Butler");

    public string IsoDestinationPath { get; set; } = string.Empty;

    public string JbDestinationPath { get; set; } = string.Empty;

    public int Ps3DecThreadCount { get; set; } = 32;

    public bool RemovePs3Update { get; set; } = true;
}