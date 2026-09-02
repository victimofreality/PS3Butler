using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PS3Butler.Services;

public class MetadataCacheService
{
    private const string CacheFileName = "metadata-cache.json";

    private readonly string _cacheFilePath;

    public string CacheFilePath => _cacheFilePath;

    public MetadataCacheService(SettingsService settingsService)
    {
        var settingsDirectory =
            Path.GetDirectoryName(settingsService.SettingsFilePath)
            ?? AppContext.BaseDirectory;

        _cacheFilePath =
            Path.Combine(
                settingsDirectory,
                CacheFileName);
    }

    public List<GameMetadataCacheEntry> Load()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return [];
        }

        try
        {
            var json =
                File.ReadAllText(_cacheFilePath);

            return JsonSerializer.Deserialize<List<GameMetadataCacheEntry>>(json)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public GameMetadataCacheEntry? FindByGameName(
        string gameName)
    {
        return Load()
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.GameName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdate(
        string gameName,
        string titleId,
        string title)
    {
        var entries = Load();

        var existing =
            entries.FirstOrDefault(entry =>
                string.Equals(
                    entry.GameName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            entries.Add(new GameMetadataCacheEntry
            {
                GameName = gameName,
                TitleId = titleId,
                Title = title
            });
        }
        else
        {
            existing.TitleId = titleId;
            existing.Title = title;
        }

        Save(entries);
    }

    private void Save(
        List<GameMetadataCacheEntry> entries)
    {
        var directory =
            Path.GetDirectoryName(_cacheFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        var json =
            JsonSerializer.Serialize(
                entries,
                options);

        File.WriteAllText(
            _cacheFilePath,
            json);
    }
}