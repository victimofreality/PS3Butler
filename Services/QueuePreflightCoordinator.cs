using PS3Butler.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public sealed class QueuePreflightCoordinator
{
    private readonly MetadataCacheService _metadataCacheService;

    public QueuePreflightCoordinator(
        MetadataCacheService metadataCacheService)
    {
        _metadataCacheService =
            metadataCacheService;
    }

    public GamePreflightService CreateService(
        KeyIndexService keyIndexService,
        string redumpKeysPath,
        string sevenZipPath,
        string isoDestinationPath,
        string jbDestinationPath,
        PreflightReservationSet reservations)
    {
        var archiveInspectionService =
            new ArchiveInspectionService(
                sevenZipPath);

        var destinationInspectionService =
            new DestinationInspectionService();

        return new GamePreflightService(
            keyIndexService,
            archiveInspectionService,
            destinationInspectionService,
            _metadataCacheService,
            isoDestinationPath,
            jbDestinationPath,
            reservations);
    }

    public async Task PreflightQueueAsync(
        IEnumerable<GameJob> jobs,
        KeyIndexService keyIndexService,
        string redumpKeysPath,
        string sevenZipPath,
        string isoDestinationPath,
        string jbDestinationPath,
        PreflightReservationSet reservations)
    {
        /*
         * The key directory can contain thousands of files. Enumerating it is
         * synchronous filesystem work, so do it away from the Avalonia UI
         * thread before constructing the preflight service.
         */
        await Task.Run(() =>
            keyIndexService.Build(
                redumpKeysPath));

        var preflightService =
            CreateService(
                keyIndexService,
                redumpKeysPath,
                sevenZipPath,
                isoDestinationPath,
                jbDestinationPath,
                reservations);

        foreach (var job in jobs)
        {
            if (job.State == JobState.Complete)
            {
                continue;
            }

            await preflightService.PreflightAsync(
                job);
        }
    }
}
