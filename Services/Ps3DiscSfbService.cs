using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PS3Butler.Services;

public class Ps3DiscSfbService
{
    private static readonly Regex TitleIdRegex =
        new(@"[A-Z]{4}[- ]?\d{5}", RegexOptions.Compiled);

    public string? ReadTitleId(string sfbPath)
    {
        if (!File.Exists(sfbPath))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(sfbPath);
            var text = Encoding.ASCII.GetString(bytes);

            var match = TitleIdRegex.Match(text);

            if (!match.Success)
            {
                return null;
            }

            return match.Value
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty)
                .ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }
}