# PS3 Butler — Project Status

## Recovery baseline
The project was recovered from Git after an SSD failure. AGENTS.md and this file were recreated from memory; the initial status described work absent from the recovered checkout. Treat code and validation below as the current baseline. Recovered HEAD: `198ed33` (Create README.md). No recovery commit has been made.

## Implemented PS3 workflow
- Local ISO/ZIP/7z/RAR and HTTP/HTTPS sources.
- Redump text/binary keys and ps3decrs decryption.
- Decrypted ISO, JB folder, and encrypted ISO plus key outputs.
- Local/SMB/UNC delivery, transactional publishing and no-overwrite protection.
- Destination indexing, metadata/title-ID checks and same-queue reservations.
- Work/destination disk budgeting and staging/download cleanup.
- One sequential downloader and one sequential processing lane, with overlap.
- Active-download pause/resume, cancellation and run-start settings snapshots.

These workflows had documented real-world testing before recovery. They have not all been rerun against this recovery change.

## Recovery milestone: persistence and shutdown
- QueuePersistenceCoordinator snapshots user inputs on the calling/UI thread, serializes background writes, detaches subscriptions on collection reset/stop, and writes a final snapshot at shutdown.
- Completed/already-existing jobs are omitted; restored jobs start waiting. Corrupt queue files safely restore an empty queue. The existing JSON shape remains.
- QueueShutdownCoordinator enters stopping state, cancels pipeline/startup waits, waits for active queue commands and startup cleanup, then stops persistence.
- MainViewModel blocks queue commands while stopping, including picker results returning after shutdown starts.
- WindowShutdownBehavior blocks native window closure until shutdown settles. Repeated close requests share the pending operation. Save failures appear in SettingsStatus and keep the window open; another close retries saving.
- JobStage.Cancelled and its legacy display bridge are restored.

## Architecture and typed states
ProcessRunner, QueuePreflightCoordinator, PipelineRunCoordinator, JobExecutionService, storage/disk abstractions and run settings snapshots exist. GamePreflightService and JobExecutionService already use typed states extensively. Some legacy status/stage display assignments remain. Keep the GameJob bridge until all consumers and bindings have been reviewed and validated.

## Latest validation
- .NET SDK 10.0.402 installed locally in ignored artifacts/dotnet because no SDK was found on PATH or at the checked standard installation locations.
- Application build passed; two existing AVLN5001 warnings concern obsolete TextBox.Watermark properties. No build errors.
- Recovery smoke runner: 14 checks passed for persistence, snapshots, terminal filtering, corruption, save failure/retry, shutdown ordering and cancellation. Includes a job completing while shutdown waits, which must not return as pending work after restart.
- Native Windows/Avalonia close-handler smoke runner: 3 checks passed for save failure, repeated close requests, and deferred closure.
- The earlier claims of 26 persistence and 9 native-window tests could not be reproduced: those projects were absent from the recovered checkout.

Commands with a system SDK:
```
dotnet build
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --native
```
The native check uses an invisible real window without loading user settings. Use ./artifacts/dotnet/dotnet.exe instead of dotnet on this machine for now.

## Remaining risks and technical debt
- Real-image/SMB runtime checks still needed for close during download, extraction/decryption, publishing, and preflight; restart and verify pending inputs survive and completed outputs do not return as pending work.
- Non-cancellable filesystem/network preflight or startup cleanup can delay exit.
- Persistence restore still reads synchronously during startup.
- Background saves are serialized without coalescing; large bursts can queue writes.
- Settings-save and tool-scan tasks are outside the queue shutdown barrier.
- Persistence schema/versioning and broader recovery behavior remain future work.
- Remaining magic strings and the temporary state/stage bridge need review.
- Two obsolete Watermark properties should become PlaceholderText in a separate UI cleanup. MainViewModel may still have extractable responsibilities.

## Recommended next step
Perform the real-image/SMB shutdown checks above before a checkpoint or release. Then review remaining typed-state consumers conservatively. Do not assume the pre-recovery status document's validation results apply to this checkout.

## Future work (not implemented here)
UI/release polish, Xbox 360 support, webMAN post-run refresh, FTP delivery, persistent download resume, library indexing/UI, and possible macOS portability. Xbox and webMAN work require explicit requests. Preserve shared queue/pipeline architecture and all existing PS3 safety invariants.
