using PS3Butler.Models;
using System.Collections.Generic;

namespace PS3Butler.Services;

public interface IQueueStateStore
{
    IReadOnlyList<GameJob> Load();
    void SaveSnapshot(IReadOnlyList<QueueStateService.PersistedGameJob> snapshot);
}
