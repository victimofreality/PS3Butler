using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public sealed class DiskBudgetService : IDiskBudgetService
{
    public Task<DiskBudgetResult> CheckAsync(
        string path,
        long requiredBytes,
        long safetyMarginBytes = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(
                DiskBudgetResult.Insufficient(
                    requiredBytes,
                    0,
                    safetyMarginBytes,
                    "Disk-space check path is empty."));
        }

        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredBytes));
        }

        if (safetyMarginBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(safetyMarginBytes));
        }

        try
        {
            var pathToCheck =
                ResolveExistingPath(path);

            if (OperatingSystem.IsWindows())
            {
                return Task.FromResult(
                    CheckWindows(
                        pathToCheck,
                        requiredBytes,
                        safetyMarginBytes));
            }

            return Task.FromResult(
                CheckWithDriveInfo(
                    pathToCheck,
                    requiredBytes,
                    safetyMarginBytes));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                DiskBudgetResult.Insufficient(
                    requiredBytes,
                    0,
                    safetyMarginBytes,
                    $"Unable to determine free space for \"{path}\": {ex.Message}"));
        }
    }

    private static DiskBudgetResult CheckWindows(
        string path,
        long requiredBytes,
        long safetyMarginBytes)
    {
        if (!GetDiskFreeSpaceEx(
                path,
                out var freeBytesAvailable,
                out _,
                out _))
        {
            var errorCode =
                Marshal.GetLastWin32Error();

            return DiskBudgetResult.Insufficient(
                requiredBytes,
                0,
                safetyMarginBytes,
                $"Unable to determine free space for \"{path}\". " +
                $"Windows error {errorCode}.");
        }

        var availableBytes =
            freeBytesAvailable > long.MaxValue
                ? long.MaxValue
                : (long)freeBytesAvailable;

        return BuildResult(
            requiredBytes,
            availableBytes,
            safetyMarginBytes,
            path);
    }

    private static DiskBudgetResult CheckWithDriveInfo(
        string path,
        long requiredBytes,
        long safetyMarginBytes)
    {
        var fullPath =
            Path.GetFullPath(path);

        var root =
            Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root))
        {
            return DiskBudgetResult.Insufficient(
                requiredBytes,
                0,
                safetyMarginBytes,
                $"Unable to determine filesystem root for \"{path}\".");
        }

        var drive =
            new DriveInfo(root);

        return BuildResult(
            requiredBytes,
            drive.AvailableFreeSpace,
            safetyMarginBytes,
            path);
    }

    private static DiskBudgetResult BuildResult(
        long requiredBytes,
        long availableBytes,
        long safetyMarginBytes,
        string path)
    {
        var totalRequired =
            requiredBytes > long.MaxValue - safetyMarginBytes
                ? long.MaxValue
                : requiredBytes + safetyMarginBytes;

        if (availableBytes >= totalRequired)
        {
            return DiskBudgetResult.Ok(
                requiredBytes,
                availableBytes,
                safetyMarginBytes);
        }

        return DiskBudgetResult.Insufficient(
            requiredBytes,
            availableBytes,
            safetyMarginBytes,
            $"Insufficient free space at \"{path}\". " +
            $"Required: {FormatBytes(requiredBytes)} " +
            $"+ {FormatBytes(safetyMarginBytes)} safety margin. " +
            $"Available: {FormatBytes(availableBytes)}.");
    }

    private static string ResolveExistingPath(
        string path)
    {
        var candidate =
            Path.GetFullPath(path);

        while (!Directory.Exists(candidate) &&
               !File.Exists(candidate))
        {
            var parent =
                Directory.GetParent(candidate);

            if (parent is null)
            {
                break;
            }

            candidate =
                parent.FullName;
        }

        return candidate;
    }

    private static string FormatBytes(
        long bytes)
    {
        const double gib =
            1024d * 1024d * 1024d;

        return
            $"{bytes / gib:N2} GiB";
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}