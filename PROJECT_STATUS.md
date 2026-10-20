# PS3 Butler — Project Status

## Recovery baseline
The project was recovered from Git after an SSD failure. AGENTS.md and this file were recreated from memory; the initial status described work absent from the recovered checkout. Treat code and validation below as the current baseline. Recovered HEAD: `198ed33` (Create README.md). Recovery checkpoint: `449a5a0` (Restore safe shutdown and background queue persistence). Real-game validation checkpoint: `ff53897` (Validate real-game outputs and fix cancellation stage). Subsequent download/copy interruption coverage is not yet committed.

## Implemented PS3 workflow
- Local ISO/ZIP/7z/RAR and HTTP/HTTPS sources.
- Redump text/binary keys and ps3decrs decryption.
- Decrypted ISO, JB folder, and encrypted ISO plus key outputs.
- Local/SMB/UNC delivery, transactional publishing and no-overwrite protection.
- Destination indexing, metadata/title-ID checks and same-queue reservations.
- Work/destination disk budgeting and staging/download cleanup.
- One sequential downloader and one sequential processing lane, with overlap.
- Active-download pause/resume, cancellation and run-start settings snapshots.

Local ISO workflows for all three output formats have now been rerun after recovery. Archive/URL and SMB workflows still need current runtime validation.

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
- Real-game tests passed using Back to the Future: The Game (BLUS30886), local Redump keys, ps3decrs and 7-Zip. All three output formats completed, duplicate detection worked for each, JB metadata/naming and optional update removal were checked, encrypted output matched the source hash, and temporary ISOs were cleaned.
- Shutdown during active decryption passed: no final output, partial decrypt removed, cancelled job restored waiting, source hash unchanged. This exposed and fixed a pipeline display issue: tool-returned cancellation failures previously left JobStage.Error instead of JobStage.Cancelled. Completed outputs remain complete.
- Successful outputs are retained under C:/PS3tools/ISO/ButlerSmoke-20261020-035245 and C:/PS3tools/Games/ButlerSmoke-20261020-035245. The earlier diagnostic run's outputs under ButlerSmoke-20261020-035150 are also retained.
- Interruption runner: 12 checks passed using a throttled local HTTP server and the real source ISO. Download pause/resume/cancellation, partial-file cleanup, pipeline download shutdown, URL queue restoration, active copy cancellation, and no-overwrite protection when a final destination appears mid-copy were validated. No product changes were needed for this milestone.

Commands with a system SDK:
```
dotnet build
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --native
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --real C:\PS3tools
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --interrupt C:\PS3tools
```
The native check uses an invisible real window without loading user settings. The opt-in real-game check expects one ISO, Keys, ps3decrs.exe, and ISO/Games destinations under its supplied tools directory, plus installed 7-Zip. It writes uniquely named output subfolders and keeps them for inspection. It uses the smoke runner's own portable metadata cache. Use ./artifacts/dotnet/dotnet.exe instead of dotnet on this machine for now.

The opt-in interruption check also reads the source ISO and key directory. Its HTTP server listens only on loopback; generated payloads and copy destinations stay in ignored artifacts/interruption subfolders. No remote download service is used and no existing user output is modified.

## Remaining risks and technical debt
- Real-image decryption cancellation/shutdown, active download interruption and single-file publishing cancellation/no-overwrite races are validated. Runtime coverage remains for SMB, archive extraction interruption, JB folder publishing interruption, preflight interruption, and full MainWindow command/shutdown integration. Native close handling and real pipeline cancellation were tested separately.
- Non-cancellable filesystem/network preflight or startup cleanup can delay exit.
- Persistence restore still reads synchronously during startup.
- Background saves are serialized without coalescing; large bursts can queue writes.
- Settings-save and tool-scan tasks are outside the queue shutdown barrier.
- Persistence schema/versioning and broader recovery behavior remain future work.
- Remaining magic strings and the temporary state/stage bridge need review.
- Two obsolete Watermark properties should become PlaceholderText in a separate UI cleanup. MainViewModel may still have extractable responsibilities.

## Recommended next step
Checkpoint the download/copy interruption smoke coverage. Next validate the full MainWindow queue-command/shutdown integration, then archive/JB interruption and SMB coverage before release. Review remaining typed-state consumers conservatively. Do not assume the pre-recovery status document's validation results apply to this checkout.

## Future work (not implemented here)
UI/release polish, Xbox 360 support, webMAN post-run refresh, FTP delivery, persistent download resume, library indexing/UI, and possible macOS portability. Xbox and webMAN work require explicit requests. Preserve shared queue/pipeline architecture and all existing PS3 safety invariants.
