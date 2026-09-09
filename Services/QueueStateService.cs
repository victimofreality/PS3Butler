using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PS3Butler.Services;

public sealed class QueueStateService
{
    private readonly string _queueFilePath;

    public QueueStateService(
        SettingsService settingsService)
    {
        _queueFilePath =
            settingsService.QueueFilePath;
    }

    public IReadOnlyList<GameJob> Load()
    {
        if (!File.Exists(_queueFilePath))
        {
            return [];
        }

        try
        {
            var json =
                File.ReadAllText(
                    _queueFilePath);

            var persistedJobs =
                JsonSerializer.Deserialize<List<PersistedGameJob>>(
                    json) ??
                [];

            return persistedJobs
                .Where(job =>
                    !string.IsNullOrWhiteSpace(
                        job.SourcePath))
                .Select(ToGameJob)
                .ToList();
        }
        catch
        {
            /*
             * Queue persistence is convenience, not something that should
             * prevent Butler from starting. A corrupt queue therefore behaves
             * like an empty queue.
             */
            return [];
        }
    }

    public void Save(
        IEnumerable<GameJob> jobs)
    {
        var persistedJobs =
            jobs
                .Where(job =>
                    job.State != JobState.Complete &&
                    job.State != JobState.AlreadyExists)
                .Select(job =>
                    new PersistedGameJob
                    {
                        SourcePath =
                            job.SourcePath,

                        SourceType =
                            job.SourceType,

                        OutputFormat =
                            job.OutputFormat,

                        DestinationOverride =
                            job.DestinationOverride
                    })
                .ToList();

        var directory =
            Path.GetDirectoryName(
                _queueFilePath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            return;
        }

        Directory.CreateDirectory(
            directory);

        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        var json =
            JsonSerializer.Serialize(
                persistedJobs,
                options);

        var temporaryPath =
            _queueFilePath + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                _queueFilePath,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Best-effort temporary-file cleanup only.
            }
        }
    }

    private static GameJob ToGameJob(
        PersistedGameJob persistedJob)
    {
        return new GameJob
        {
            SourcePath =
                persistedJob.SourcePath,

            SourceType =
                persistedJob.SourceType,

            OutputFormat =
                persistedJob.OutputFormat,

            DestinationOverride =
                persistedJob.DestinationOverride,

            /*
             * Never restore transient execution state. Any job that survived
             * an app exit starts again from a clean preflightable state.
             */
            LocalSourcePath = null,
            State = JobState.Waiting,
            Details =
                persistedJob.SourceType ==
                GameSourceType.Url
                    ? "Restored from previous session. Waiting to download."
                    : "Restored from previous session."
        };
    }

    private sealed class PersistedGameJob
    {
        public string SourcePath { get; set; } =
            string.Empty;

        public GameSourceType SourceType { get; set; } =
            GameSourceType.LocalFile;

        public OutputFormat OutputFormat { get; set; } =
            OutputFormat.DecryptedIso;

        public string? DestinationOverride { get; set; }
    }
}
