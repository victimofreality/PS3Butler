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
}