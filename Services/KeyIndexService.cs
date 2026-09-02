using System;
using System.Collections.Generic;
using System.IO;

namespace PS3Butler.Services;

public class KeyIndexService
{
    private readonly Dictionary<string, string> _keys =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _keys.Count;

    public void Build(string keyDirectory)
    {
        _keys.Clear();

        if (!Directory.Exists(keyDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     keyDirectory,
                     "*.dkey",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrWhiteSpace(name))
            {
                _keys[name] = path;
            }
        }
    }

    public string? Find(string gameName)
    {
        return _keys.TryGetValue(gameName, out var path)
            ? path
            : null;
    }

    public static bool TryReadKey(
        string keyPath,
        out string key,
        out string error)
    {
        key = string.Empty;
        error = string.Empty;

        if (!File.Exists(keyPath))
        {
            error = "Key file does not exist.";
            return false;
        }

        try
        {
            var contents = File.ReadAllText(keyPath).Trim();

            if (contents.Length != 32)
            {
                error =
                    $"Key is {contents.Length} characters long; expected 32.";
                return false;
            }

            foreach (var character in contents)
            {
                if (!Uri.IsHexDigit(character))
                {
                    error = "Key contains non-hexadecimal characters.";
                    return false;
                }
            }

            key = contents.ToUpperInvariant();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read key file: {ex.Message}";
            return false;
        }
    }
}