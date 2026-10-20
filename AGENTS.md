# PS3 Butler — Codex Working Instructions

## Project overview
PS3 Butler is a Windows-first Avalonia C#/.NET app for preparing and delivering PS3 game images. It supports local ISO/archive sources, HTTP/HTTPS downloads, Redump keys, `ps3decrs`, decrypted ISO/JB/encrypted ISO+key outputs, SMB/UNC destinations, queue persistence, duplicate detection, disk budgeting, transactional publishing, cancellation, pause/resume, and pipelined operation.

## Build
- Windows 11
- .NET 10
- Avalonia 12
- CommunityToolkit.Mvvm
- Solution: `PS3Butler.slnx`
- Project: `PS3Butler.csproj`

Build with:
```powershell
dotnet build
```

A clean build is not enough for pipeline/shutdown changes; also perform an appropriate runtime sanity test.

## Git workflow
Make small, reviewable changes. Do not commit unless explicitly asked. A request for a "checkpoint" authorizes a commit.

Before risky/broad changes:
```powershell
git status
git diff
git log --oneline -10
```

Do not use destructive Git operations (`git reset --hard`, `git clean -fd`, force-pushes, history rewrites) unless explicitly requested.

## PROJECT_STATUS.md maintenance
After a meaningful milestone, update `PROJECT_STATUS.md` with what completed, what remains, newly discovered technical debt, and the recommended next step. Do not update it for trivial edits. Prefer updating it before a checkpoint commit.

## Architectural invariants
- exactly one sequential downloader
- exactly one sequential processing lane
- download and processing may overlap
- no concurrent downloads
- no concurrent extract/decrypt/copy jobs unless explicitly requested
- pipeline settings are snapshotted at run start
- queue editing stays locked while pipeline is active
- outputs remain transactional
- no silent overwrite of final outputs
- blocking network/filesystem work stays off the Avalonia UI thread where appropriate

## Shutdown
Preserve shutdown ordering:
1. enter stopping state / block queue edits
2. cancel active queue/pipeline work
3. wait for active tasks to settle
4. flush/stop queue persistence
5. allow the window to close

Do not silently discard queue-save failures during shutdown.

## PS3 rules
Supported decryptor is `ps3decrs`. Do not reintroduce Classic `PS3Dec.exe` unless explicitly requested.

Redump `.dkey` supports:
- 32-character hex text
- raw 16-byte binary

Normalize internally to uppercase hex.

JB output should use metadata-derived naming where available. Root `PS3_UPDATE` removal is optional and settings-controlled.

## Process execution
External commands should use shared `ProcessRunner`, preserving stdout/stderr handling, cancellation, process-tree termination, optional timeouts, structured results, and `ArgumentList` where quoting/path handling is fragile.

## Disk budgeting
Preserve work/destination free-space checks, including UNC/SMB handling on Windows.

## Duplicate detection
Preserve destination indexing, ISO/JB checks, title-ID checks, and same-queue reservations. Per-job destination overrides may alter duplicate scope. Do not regress to repeated full rescans.

## Queue persistence
Persist only user-meaningful queue inputs, not volatile runtime state. Restored jobs return as fresh waiting jobs. Completed/already-existing jobs should not return as pending work. Corrupt persistence fails safely.

Background persistence must snapshot live UI-bound jobs before background writes. Writes should remain serialized.

## Typed state migration
The project is migrating from magic strings to `JobState` and `JobStage`.

`GameJob` currently bridges legacy `Status` / `CurrentStage` with typed `State` / `Stage`.

Prefer:
```csharp
job.State
job.Stage
```

Do not remove the bridge until callers/UI bindings are fully migrated and tested. `JobStage.Cancelled` exists and should be used for cancellation display/stage tracking.

## MainViewModel direction
Do not move orchestration back into `MainViewModel`.

Extracted responsibilities include:
- QueuePersistenceCoordinator
- QueueShutdownCoordinator
- QueuePreflightCoordinator
- PipelineRunCoordinator
- JobExecutionService
- process/storage/disk abstractions

Prefer small coordinators/services.

## Tests
Dedicated smoke-test projects exist for queue persistence and native-window shutdown. Run relevant tests when touching those areas. An isolated artifacts path is acceptable if a running app locks default build output.

## Future Xbox 360 support
Xbox support is planned later. Possible outputs/tools include GOD, raw ISO/network ISO, XEX/extracted folders, `iso2god-rs`, and `extract-xiso`.

Avoid unnecessary PS3 assumptions in shared queue/pipeline architecture. Do not implement Xbox support unless explicitly requested.

## Future webMAN integration
Possible future post-run behavior:
- after all jobs complete
- if PS3 reachable and at XMB, refresh webMAN
- if a game is running, skip/defer
- never refresh per job

Do not implement unless explicitly requested.

## Coding style
Favor conservative diffs, clear names, explicit control flow, small services, cancellation propagation, and existing abstractions. Avoid giant speculative refactors, unnecessary packages, global mutable state, blocking UI calls, and silent exception swallowing except explicitly best-effort cleanup.

## Before changing code
For non-trivial work, inspect first and summarize:
1. what you found
2. what you plan to change
3. behavior that must remain unchanged
4. files to touch

Then make the smallest useful change.

## After changing code
Build and report:
- files changed
- behavior changed
- behavior intentionally preserved
- build/test results
- recommended runtime sanity test
- risks/follow-up

Do not commit unless asked or asked for a checkpoint.
