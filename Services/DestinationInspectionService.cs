using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public class DestinationInspectionService
{
    private readonly ParamSfoService _paramSfoService =
        new();

    public List<ExistingGame> ScanJbGames(
        string destinationDirectory)
    {
        var games = new List<ExistingGame>();

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return games;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            return games;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     destinationDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var paramSfoPath =
                Path.Combine(
                    directory,
                    "PS3_GAME",
                    "PARAM.SFO");

            if (!File.Exists(paramSfoPath))
            {
                continue;
            }

            var metadata =
                _paramSfoService.Read(paramSfoPath);

            if (metadata is null)
            {
                continue;
            }

            games.Add(new ExistingGame
            {
                Path = directory,
                TitleId = metadata.TitleId,
                Title = metadata.Title,
                Format = "JB"
            });
        }

        return games;
    }

    public ExistingGame? FindJbGameByTitleId(
        string destinationDirectory,
        string titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        return ScanJbGames(destinationDirectory)
            .FirstOrDefault(game =>
                string.Equals(
                    game.TitleId,
                    titleId,
                    StringComparison.OrdinalIgnoreCase));
    }

    public string? FindExistingGame(
        string destinationDirectory,
        string gameName)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return null;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            return null;
        }

        var existingIso =
            FindExistingIso(
                destinationDirectory,
                gameName);

        if (existingIso is not null)
        {
            return existingIso;
        }

        var existingJbFolder =
            FindExistingJbFolderByName(
                destinationDirectory,
                gameName);

        return existingJbFolder;
    }

    public string? FindExistingEncryptedIsoWithKey(
        string destinationDirectory,
        string gameName)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return null;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            return null;
        }

        var existingIso =
            FindExistingIso(
                destinationDirectory,
                gameName);

        if (existingIso is null)
        {
            return null;
        }

        var keyPath =
            Path.ChangeExtension(
                existingIso,
                ".dkey");

        if (!File.Exists(keyPath))
        {
            return null;
        }

        return existingIso;
    }

    private static string? FindExistingIso(
        string destinationDirectory,
        string gameName)
    {
        foreach (var path in Directory.EnumerateFiles(
                     destinationDirectory,
                     "*.iso",
                     SearchOption.TopDirectoryOnly))
        {
            var fileName =
                Path.GetFileNameWithoutExtension(path);

            var normalizedName =
                NormalizeIsoName(fileName);

            if (string.Equals(
                    normalizedName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private static string? FindExistingJbFolderByName(
        string destinationDirectory,
        string gameName)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     destinationDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var ps3GameDirectory =
                Path.Combine(directory, "PS3_GAME");

            if (!Directory.Exists(ps3GameDirectory))
            {
                continue;
            }

            var folderName =
                Path.GetFileName(directory);

            if (string.Equals(
                    folderName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }

    private static string NormalizeIsoName(string fileName)
    {
        const string decryptedSuffix = ".decrypted";

        if (fileName.EndsWith(
                decryptedSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^decryptedSuffix.Length];
        }

        return fileName;
    }
}