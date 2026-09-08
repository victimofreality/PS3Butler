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
            var bytes =
                File.ReadAllBytes(keyPath);

            /*
             * Redump disc keys may be stored either as:
             *
             * - 16 raw binary bytes
             * - 32 hexadecimal text characters
             *
             * Internally Butler always uses the 32-character
             * hexadecimal representation.
             */
            if (bytes.Length == 16)
            {
                key =
                    Convert.ToHexString(bytes);

                return true;
            }

            var contents =
                File.ReadAllText(keyPath).Trim();

            if (contents.Length != 32)
            {
                error =
                    $"Key is neither a 16-byte binary key nor a " +
                    $"32-character hexadecimal key.";

                return false;
            }

            foreach (var character in contents)
            {
                if (!Uri.IsHexDigit(character))
                {
                    error =
                        "Key contains non-hexadecimal characters.";

                    return false;
                }
            }

            key =
                contents.ToUpperInvariant();

            return true;
        }
        catch (Exception ex)
        {
            error =
                $"Could not read key file: {ex.Message}";

            return false;
        }
    }
}