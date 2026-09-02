using System;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null)
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
            var sourceInfo = new FileInfo(sourcePath);
            var totalBytes = sourceInfo.Length;
            long copiedBytes = 0;

            var buffer = new byte[1024 * 1024];

            await using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                useAsync: true);

            await using var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                useAsync: true);

            int bytesRead;

            while ((bytesRead =
                    await sourceStream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await destinationStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead));

                copiedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percentage =
                        copiedBytes * 100.0 / totalBytes;

                    progress?.Report(percentage);
                }
            }

            await destinationStream.FlushAsync();
        }
        catch (Exception ex)
        {
            return (
                false,
                string.Empty,
                $"Copy failed: {ex.Message}");
        }

        var finalSourceInfo =
            new FileInfo(sourcePath);

        var destinationInfo =
            new FileInfo(destinationPath);

        if (!destinationInfo.Exists)
        {
            return (
                false,
                string.Empty,
                "Destination file was not created.");
        }

        if (finalSourceInfo.Length != destinationInfo.Length)
        {
            return (
                false,
                string.Empty,
                "Destination file size does not match source.");
        }

        progress?.Report(100);

        return (
            true,
            destinationPath,
            string.Empty);
    }
}