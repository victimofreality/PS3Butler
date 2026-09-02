using PS3Butler.Configuration;
using System;
using System.IO;
using System.Text.Json;

namespace PS3Butler.Services;

public class SettingsService
{
    private const string PortableMarkerFileName = "portable.txt";
    private const string SettingsFileName = "settings.json";

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    public bool IsPortableMode { get; }

    public string SettingsFilePath => _settingsFilePath;

    public SettingsService()
    {
        var appDirectory = AppContext.BaseDirectory;
        var portableMarkerPath = Path.Combine(appDirectory, PortableMarkerFileName);

        IsPortableMode = File.Exists(portableMarkerPath);

        if (IsPortableMode)
        {
            _settingsDirectory = Path.Combine(appDirectory, "Data");
        }
        else
        {
            var appDataDirectory =
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _settingsDirectory = Path.Combine(appDataDirectory, "PS3Butler");
        }

        _settingsFilePath = Path.Combine(_settingsDirectory, SettingsFileName);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);

            return JsonSerializer.Deserialize<AppSettings>(json)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_settingsDirectory);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(settings, options);

        File.WriteAllText(_settingsFilePath, json);
    }
}