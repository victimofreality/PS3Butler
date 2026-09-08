using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace PS3Butler.Services;

public sealed class PreflightReservationSet
{
    private const string DefaultLibraryScope =
        "__default_library__";

    private readonly HashSet<string> _gameReservations =
        new(
            StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _titleIdReservations =
        new(
            StringComparer.OrdinalIgnoreCase);

    public bool IsGameReserved(
        GameJob job,
        string destinationDirectory,
        string gameName)
    {
        var key =
            BuildKey(
                job,
                destinationDirectory,
                gameName);

        return _gameReservations.Contains(
            key);
    }

    public bool IsTitleIdReserved(
        GameJob job,
        string destinationDirectory,
        string titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return false;
        }

        var key =
            BuildKey(
                job,
                destinationDirectory,
                titleId);

        return _titleIdReservations.Contains(
            key);
    }

    public void Reserve(
        GameJob job,
        string destinationDirectory,
        string gameName,
        string? titleId)
    {
        _gameReservations.Add(
            BuildKey(
                job,
                destinationDirectory,
                gameName));

        if (!string.IsNullOrWhiteSpace(titleId))
        {
            _titleIdReservations.Add(
                BuildKey(
                    job,
                    destinationDirectory,
                    titleId));
        }
    }

    private static string BuildKey(
        GameJob job,
        string destinationDirectory,
        string value)
    {
        var scope =
            GetScope(
                job,
                destinationDirectory);

        return
            $"{scope}|{value.Trim()}";
    }

    private static string GetScope(
        GameJob job,
        string destinationDirectory)
    {
        /*
         * Normal ISO and JB destinations are treated as one
         * logical PS3 library. This prevents two default-output
         * jobs for the same game from both becoming Ready even
         * when they target different physical folders.
         *
         * A per-job destination override is considered deliberate,
         * so reservations for overrides are scoped to that path.
         */
        if (string.IsNullOrWhiteSpace(
                job.DestinationOverride))
        {
            return DefaultLibraryScope;
        }

        var normalizedPath =
            destinationDirectory
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        return Path.GetFullPath(
            normalizedPath);
    }
}