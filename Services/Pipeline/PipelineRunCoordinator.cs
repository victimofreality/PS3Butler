using PS3Butler.Models;
using PS3Butler.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Pipeline;

public sealed class PipelineRunCoordinator
{
    private readonly KeyIndexService _keyIndexService;
    private readonly MetadataCacheService _metadataCacheService;
    private readonly QueuePreflightCoordinator _preflightCoordinator;

    private CancellationTokenSource? _pipelineCancellationTokenSource;
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private PauseTokenSource? _downloadPauseTokenSource;

    public PipelineRunCoordinator(
        KeyIndexService keyIndexService,
        MetadataCacheService metadataCacheService,
        QueuePreflightCoordinator preflightCoordinator)
    {
        _keyIndexService =
            keyIndexService;

        _metadataCacheService =
            metadataCacheService;

        _preflightCoordinator =
            preflightCoordinator;
    }

    public async Task RunAsync(
        IReadOnlyCollection<GameJob> jobs,
        PipelineRunSettings runSettings)
    {
        _pipelineCancellationTokenSource?.Dispose();

        _pipelineCancellationTokenSource =
            new CancellationTokenSource();

        var pipelineCancellationToken =
            _pipelineCancellationTokenSource.Token;

        try
        {
            var preflightReservations =
                new PreflightReservationSet();

            await _preflightCoordinator.PreflightQueueAsync(
                jobs,
                _keyIndexService,
                runSettings.RedumpKeysPath,
                runSettings.SevenZipPath,
                runSettings.IsoDestinationPath,
                runSettings.JbDestinationPath,
                preflightReservations);

            var queuedJobs =
                jobs.ToList();

            var processingTasks =
                new List<Task>();

            using var processingGate =
                new SemaphoreSlim(1, 1);

            async Task ProcessQueuedJobAsync(
                GameJob queuedJob)
            {
                var gateEntered = false;

                try
                {
                    await processingGate.WaitAsync(
                        pipelineCancellationToken);

                    gateEntered = true;

                    if (pipelineCancellationToken.IsCancellationRequested)
                    {
                        MarkCancelled(
                            queuedJob);

                        return;
                    }

                    await ProcessSingleJobAsync(
                        queuedJob,
                        runSettings,
                        pipelineCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    MarkCancelled(
                        queuedJob);
                }
                finally
                {
                    if (gateEntered)
                    {
                        processingGate.Release();
                    }
                }
            }

            foreach (var job in queuedJobs)
            {
                if (pipelineCancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (job.State == JobState.Ready)
                {
                    processingTasks.Add(
                        ProcessQueuedJobAsync(
                            job));
                }
            }

            var downloadService =
                new DownloadService();

            var preflightService =
                _preflightCoordinator.CreateService(
                    _keyIndexService,
                    runSettings.RedumpKeysPath,
                    runSettings.SevenZipPath,
                    runSettings.IsoDestinationPath,
                    runSettings.JbDestinationPath,
                    preflightReservations);

            foreach (var job in queuedJobs)
            {
                if (pipelineCancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (job.SourceType != GameSourceType.Url ||
                    job.State == JobState.Complete ||
                    job.State == JobState.AlreadyExists)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(
                        job.LocalSourcePath) &&
                    File.Exists(
                        job.LocalSourcePath))
                {
                    continue;
                }

                _downloadCancellationTokenSource?.Dispose();

                _downloadCancellationTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        pipelineCancellationToken);

                _downloadPauseTokenSource =
                    new PauseTokenSource();

                job.State = JobState.Running;
                job.Stage = JobStage.Downloading;
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Downloading source file...";

                var downloadDirectory =
                    Path.Combine(
                        runSettings.WorkDirectory,
                        "download",
                        Guid.NewGuid().ToString("N"));

                var downloadProgress =
                    new Progress<TransferProgress>(value =>
                    {
                        job.Progress =
                            value.Percentage;

                        job.TransferSpeedMbps =
                            value.MegabytesPerSecond;

                        if (value.EstimatedRemaining.HasValue)
                        {
                            var eta =
                                value.EstimatedRemaining.Value;

                            job.EtaText =
                                $"Downloading — " +
                                $"{value.MegabytesPerSecond:F1} MB/s — " +
                                $"{eta:hh\\:mm\\:ss} remaining";
                        }
                        else
                        {
                            job.EtaText =
                                $"Downloading — " +
                                $"{value.MegabytesPerSecond:F1} MB/s";
                        }
                    });

                var downloadResult =
                    await downloadService.DownloadAsync(
                        job.SourcePath,
                        downloadDirectory,
                        downloadProgress,
                        _downloadCancellationTokenSource.Token,
                        _downloadPauseTokenSource);

                if (!downloadResult.Success)
                {
                    TemporaryDownloadCleanupService.CleanupDirectory(
                        downloadDirectory,
                        runSettings.WorkDirectory);

                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        downloadResult.Error;

                    continue;
                }

                job.LocalSourcePath =
                    downloadResult.OutputPath;

                job.State = JobState.Waiting;
                job.Stage = JobStage.None;
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Download complete. Preflighting...";

                await preflightService.PreflightAsync(
                    job);

                if (job.State == JobState.Ready)
                {
                    processingTasks.Add(
                        ProcessQueuedJobAsync(
                            job));
                }
                else
                {
                    TemporaryDownloadCleanupService.CleanupFile(
                        job.LocalSourcePath,
                        runSettings.WorkDirectory);

                    job.LocalSourcePath = null;
                }
            }

            try
            {
                await Task.WhenAll(
                    processingTasks);
            }
            catch (OperationCanceledException)
            {
                /*
                 * Individual queued jobs mark themselves cancelled. Swallow the
                 * aggregate cancellation so the caller can cleanly unwind.
                 */
            }
        }
        finally
        {
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            _downloadPauseTokenSource = null;

            _pipelineCancellationTokenSource?.Dispose();
            _pipelineCancellationTokenSource = null;
        }
    }

    public void PauseDownload()
    {
        _downloadPauseTokenSource?.Pause();
    }

    public void ResumeDownload()
    {
        _downloadPauseTokenSource?.Resume();
    }

    public void Cancel()
    {
        _pipelineCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource?.Cancel();
    }

    private async Task ProcessSingleJobAsync(
        GameJob job,
        PipelineRunSettings runSettings,
        CancellationToken cancellationToken)
    {
        var options =
            new JobExecutionOptions
            {
                RedrrxPs3DecPath =
                    runSettings.RedrrxPs3DecPath,

                SevenZipPath =
                    runSettings.SevenZipPath,

                WorkDirectory =
                    runSettings.WorkDirectory,

                IsoDestinationPath =
                    runSettings.IsoDestinationPath,

                JbDestinationPath =
                    runSettings.JbDestinationPath,

                Ps3DecThreadCount =
                    runSettings.Ps3DecThreadCount,

                RemovePs3Update =
                    runSettings.RemovePs3Update
            };

        var jobExecutionService =
            new JobExecutionService(
                _metadataCacheService,
                options);

        await jobExecutionService.ProcessAsync(
            job,
            cancellationToken);
    }

    private static void MarkCancelled(
        GameJob job)
    {
        job.State = JobState.Error;
        job.Stage = JobStage.Error;
        job.CurrentStage = "Cancelled";
        job.Details =
            "Processing cancelled.";
    }
}
