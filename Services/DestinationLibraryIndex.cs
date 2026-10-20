using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PS3Butler.Services;

public sealed class DestinationLibraryIndex
{
    private readonly Dictionary<string, string> _isoByGameName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _jbFolderByName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ExistingGame> _jbByTitleId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ParamSfoService _paramSfoService;

    public string DestinationDirectory { get; }

    public DestinationLibraryIndex(
        string destinationDirectory,
        ParamSfoService paramSfoService,
        CancellationToken cancellationToken = default)
    {
        DestinationDirectory =
            destinationDirectory;

        _paramSfoService =
            paramSfoService;

        Build(cancellationToken);
    }

    public string? FindExistingGame(
        string gameName)
    {
        var existingIso =
            FindExistingIso(
                gameName);

        if (existingIso is not null)
        {
            return existingIso;
        }

        return _jbFolderByName.TryGetValue(
            gameName,
            out var folderPath)
                ? folderPath
                : null;
    }

    public string? FindExistingIso(
        string gameName)
    {
        return _isoByGameName.TryGetValue(
            gameName,
            out var isoPath)
                ? isoPath
                : null;
    }

    public string? FindExistingEncryptedIsoWithKey(
        string gameName)
    {
        var existingIso =
            FindExistingIso(
                gameName);

        if (existingIso is null)
        {
            return null;
        }

        var keyPath =
            Path.ChangeExtension(
                existingIso,
                ".dkey");

        return File.Exists(keyPath)
            ? existingIso
            : null;
    }

    public ExistingGame? FindJbGameByTitleId(
        string titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        return _jbByTitleId.TryGetValue(
            titleId,
            out var game)
                ? game
                : null;
    }

    private void Build(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(
                DestinationDirectory))
        {
            return;
        }

        if (!Directory.Exists(
                DestinationDirectory))
        {
            return;
        }

        ScanIsoFiles(cancellationToken);
        ScanJbFolders(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ScanIsoFiles(CancellationToken cancellationToken)
    {
        foreach (var path in
                 Directory.EnumerateFiles(
                     DestinationDirectory,
                     "*.iso",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName =
                Path.GetFileName(path);

            /*
             * Ignore PS3 Butler's own transactional staging
             * files if preflight happens while another job is
             * publishing.
             */
            if (fileName.StartsWith(
                    ".ps3butler-",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gameName =
                Path.GetFileNameWithoutExtension(
                    path);

            var normalizedName =
                NormalizeIsoName(
                    gameName);

            _isoByGameName.TryAdd(
                normalizedName,
                path);
        }
    }

    private void ScanJbFolders(CancellationToken cancellationToken)
    {
        foreach (var directory in
                 Directory.EnumerateDirectories(
                     DestinationDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderName =
                Path.GetFileName(
                    directory);

            /*
             * A transactional JB copy can contain PS3_GAME
             * before it is complete. Never index staging folders.
             */
            if (folderName.Contains(
                    ".ps3butler-",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ps3GameDirectory =
                Path.Combine(
                    directory,
                    "PS3_GAME");

            if (!Directory.Exists(
                    ps3GameDirectory))
            {
                continue;
            }

            _jbFolderByName.TryAdd(
                folderName,
                directory);

            var paramSfoPath =
                Path.Combine(
                    ps3GameDirectory,
                    "PARAM.SFO");

            if (!File.Exists(
                    paramSfoPath))
            {
                continue;
            }

            var metadata =
                _paramSfoService.Read(
                    paramSfoPath);

            if (metadata is null ||
                string.IsNullOrWhiteSpace(
                    metadata.TitleId))
            {
                continue;
            }

            _jbByTitleId.TryAdd(
                metadata.TitleId,
                new ExistingGame
                {
                    Path =
                        directory,

                    TitleId =
                        metadata.TitleId,

                    Title =
                        metadata.Title,

                    Format =
                        "JB"
                });
        }
    }

    private static string NormalizeIsoName(
        string fileName)
    {
        const string decryptedSuffix =
            ".decrypted";

        if (fileName.EndsWith(
                decryptedSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return fileName[
                ..^decryptedSuffix.Length];
        }

        return fileName;
    }
}
