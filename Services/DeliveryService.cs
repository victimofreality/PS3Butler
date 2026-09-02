using System;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourcePath,
        string destinationDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            return (
                false,
                string.Empty,
                "Source file does not exist.");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return (
                false,
                string.Empty,
                "Destination directory is not configured.");
        }

        Directory.CreateDirectory(destinationDirectory);

        var destinationPath =
            Path.Combine(
                destinationDirectory,
                Path.GetFileName(sourcePath));

        try
        {
            await using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);

            await using var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);

            await sourceStream.CopyToAsync(destinationStream);

            await destinationStream.FlushAsync();
        }
        catch (Exception ex)
        {
            return (
                false,
                string.Empty,
                $"Copy failed: {ex.Message}");
        }

        var sourceInfo = new FileInfo(sourcePath);
        var destinationInfo = new FileInfo(destinationPath);

        if (!destinationInfo.Exists)
        {
            return (
                false,
                string.Empty,
                "Destination file was not created.");
        }

        if (sourceInfo.Length != destinationInfo.Length)
        {
            return (
                false,
                string.Empty,
                "Destination file size does not match source.");
        }

        return (
            true,
            destinationPath,
            string.Empty);
    }
}