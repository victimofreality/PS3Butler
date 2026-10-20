# PS3 Butler — Project Status

## Recovery baseline
The project was recovered from Git after an SSD failure. AGENTS.md and this file were recreated from memory; the initial status described work absent from the recovered checkout. Treat code and validation below as the current baseline. Recovered HEAD: `198ed33` (Create README.md). Recovery checkpoint: `449a5a0` (Restore safe shutdown and background queue persistence). Real-game validation checkpoint: `ff53897` (Validate real-game outputs and fix cancellation stage). Download/copy interruption checkpoint: `3c2987f` (Add download and publishing interruption coverage). Compatibility/integration checkpoint: `757572b` (Support Redrrx v3 and validate shutdown and archive workflows). SMB checkpoint: `06b0ac5` (Validate SMB delivery and interruption cleanup). Subsequent pipeline-ordering coverage is not yet committed.

## Implemented PS3 workflow
- Local ISO/ZIP/7z/RAR and HTTP/HTTPS sources.
- Redump text/binary keys and Redrrx PS3Dec decryption (`ps3dec.exe` v3+, or older `ps3decrs.exe`).
- Decrypted ISO, JB folder, and encrypted ISO plus key outputs.
- Local/SMB/UNC delivery, transactional publishing and no-overwrite protection.
- Destination indexing, metadata/title-ID checks and same-queue reservations.
- Work/destination disk budgeting and staging/download cleanup.
- One sequential downloader and one sequential processing lane, with overlap.
- Active-download pause/resume, cancellation and run-start settings snapshots.

Local ISO and SMB delivery workflows for all three output formats have now been rerun after recovery. ZIP archive processing, URL interruption and full local-HTTP-to-decrypted-ISO runs also have runtime coverage. Additional archive formats remain useful follow-up coverage.

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
- Full MainWindow integration runner: 16 checks previously passed for each real Redrrx binary (2.0.0 and 3.0.0); the expanded 23-check suite now passed with v3 after preflight changes. Uses invisible real application windows and isolated portable settings. Validates settings save/validation, processing and standalone-preflight queue locks, close/cancellation during decryption and preflight, retry and reservations, queue flush before close, waiting restoration, cleanup, and actual save failure/retry using an exclusive lock on the test queue file.
- Redrrx compatibility runner: 17 checks passed with the installed ps3decrs.exe 2.0.0 and official checksum-verified ps3dec.exe 3.0.0. Both performed real ISO decryption with unchanged arguments, reported progress before completion, and left no recovery sidecars on success. Captured requests verified the same ArgumentList for both names; simulated cancellation, timeout, error, and missing-output results cleaned partial outputs and sidecars. Recovery smoke runner also reran successfully (14 checks).
- Archive/JB runner: 25 checks passed with a ZIP containing the real ISO and Redrrx v3. Validated a complete archive-to-encrypted-ISO/key job (matching source hash), shutdown during archive extraction, JB extraction and JB publishing, original queue-input restoration, workspace/destination cleanup, and preservation of a destination folder created mid-copy. The original source ISO hash remained unchanged. No product fixes were needed. ZIP/RAR/7z formats were not all tested: this run used a store-mode ZIP.
- SMB runner: 42 checks passed with Redrrx v3 on the user-authorized production PS3ISO/GAMES UNC destinations. Validated UNC free-space budgeting, all three complete outputs and duplicate checks, ISO hashes and paired key, JB metadata/naming/update removal, cancellation during network publishing for all formats, partial-output/workspace cleanup, waiting queue restoration, and existing-output protection. No product changes were needed.
- SMB cleanup confirmed: only owned GUID test containers were written. Both were removed after testing and independently verified absent. Before/after top-level snapshots matched (69 PS3ISO entries, 64 GAMES entries). Local log: artifacts/smb-run.log; exact cleanup manifest: artifacts/smb/5ff801776d5a4e21b064c0cd21759181/manifest.json. No test outputs remain on those shares.
- Pipeline-ordering runner: 22 checks passed with Redrrx v3, two local ISO jobs and two complete HTTP downloads of the real ISO, using separate per-job destinations. Observed one downloader and one processing job at maximum, actual download/processing overlap, ordered HTTP requests and processing, and identical decrypted output hashes for all four jobs. Completed jobs did not restore as pending; work files and URL local paths were cleaned. A second run cancelled while downloading, processing and waiting at the processing gate: active/gated work settled, all four inputs restored waiting, and no outputs or work files remained. Original source hash unchanged. No product changes or SMB access were needed.
- Preflight cancellation runner: 21 checks passed, including pre-cancelled operations, cancellation during archive inspection, a real 7-Zip listing terminated through ProcessRunner, remaining-job skipping, unchanged reservations/override scope, retry-stage reset, normal initial-pipeline cancellation, and cleanup when cancelled between download completion and post-download preflight. Expanded native-window suite (23), pipeline ordering (22) and recovery suite (14) reran successfully: 80 checks across those four suites. Final build has no errors; two pre-existing Watermark deprecation warnings appear when XAML recompiles. Production SMB destinations were not accessed for this milestone.

## Preflight cancellation milestone (uncommitted)
Cancellation tokens now flow from standalone preflight and the pipeline into queue/job preflight, archive inspection, key enumeration and destination indexing. Checks between jobs/entries stop further work. Destination index caching, duplicate/title-ID checks, per-job override scope, reservations, transactional output behavior and sequential pipeline lanes are preserved.

Standalone preflight owns a cancellation source, locks queue editing and concurrent pipeline starts, and responds to the existing Cancel command. Cancellation is caught as a normal stop, displays JobStage.Cancelled for an interrupted job, and leaves later jobs untouched. Retry clears the cancelled stage and works normally. The button label now reads Cancel rather than Cancel Download.

Pipeline cancellation during initial or post-download preflight settles normally. After processing tasks settle, any remaining downloaded inputs are cleaned and local paths cleared, including jobs cancelled before entering the processing lane. Shutdown retains its wait-before-flush ordering; it does not abandon in-flight filesystem tasks.

## Redrrx v3 compatibility milestone
Detection and settings validation already accept any existing executable path; the picker already filters all *.exe files. The picker prompt, decrypting status, errors and documentation now name Redrrx PS3Dec and both binary names. Saved paths and the older default path are preserved; no automatic tool replacement or settings migration was added.

Compared against the tagged v3.0.0 CLI source and the official binary's --help: --dk, --tc, --output-dir, --output-name, --skip and the positional ISO path are compatible. The explicit output name still produces the expected .iso. No invocation changes or v3 batch/concurrency flags were introduced.

v3 writes .iso.part and .iso.resume files instead of writing the final ISO directly. Ps3DecService now polls the partial file for progress and cleans the exact sidecars on failure/cancellation and the checkpoint on success. It clears stale work files before starting, retaining Butler's existing fresh-run/cleanup policy rather than enabling native v3 resume. Older binaries continue polling their direct ISO output.

Sources: https://github.com/Redrrx/ps3dec/blob/v3.0.0/src/args.rs and https://github.com/Redrrx/ps3dec/blob/v3.0.0/src/recover.rs. The README's older --iso/underscore spellings differ from the tagged parser; the parser and actual --help were used for validation.

Commands with a system SDK:
```
dotnet build
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --native
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --real C:\PS3tools
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --interrupt C:\PS3tools
dotnet build Tests/RecoverySmoke/RecoverySmoke.csproj --output artifacts/window-smoke/bin
dotnet artifacts/window-smoke/bin/RecoverySmoke.dll --window C:\PS3tools
dotnet artifacts/window-smoke/bin/RecoverySmoke.dll --window C:\PS3tools C:\Projects\PS3Butler\artifacts\tool-compat\v3\ps3dec.exe
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --tools C:\PS3tools C:\Projects\PS3Butler\artifacts\tool-compat\v3\ps3dec.exe
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --archive C:\PS3tools C:\Projects\PS3Butler\artifacts\tool-compat\v3\ps3dec.exe
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --smb C:\PS3tools \\server\share\PS3ISO \\server\share\GAMES C:\Projects\PS3Butler\artifacts\tool-compat\v3\ps3dec.exe
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --ordering C:\PS3tools C:\Projects\PS3Butler\artifacts\tool-compat\v3\ps3dec.exe
dotnet run --project Tests/RecoverySmoke/RecoverySmoke.csproj -- --preflight
```
The native check uses an invisible real window without loading user settings. The opt-in real-game check expects one ISO, Keys, ps3decrs.exe, and ISO/Games destinations under its supplied tools directory, plus installed 7-Zip. It writes uniquely named output subfolders and keeps them for inspection. It uses the smoke runner's own portable metadata cache. Use ./artifacts/dotnet/dotnet.exe instead of dotnet on this machine for now.

The opt-in interruption check also reads the source ISO and key directory. Its HTTP server listens only on loopback; generated payloads and copy destinations stay in ignored artifacts/interruption subfolders. No remote download service is used and no existing user output is modified.

Run the window check from the separate build output above. It creates its own portable settings/queue alongside the smoke executable and uses unique work/output subfolders there. It exercises the actual MainWindow and MainViewModel without modifying normal user settings or queue data.

The archive/JB check creates its fixture ZIP and all work/output under ignored artifacts/archive-folder. It uses ProcessRunner for 7-Zip archive creation and the production pipeline for processing/cancellation. The optional last argument selects the decryptor; omitting it uses the older ps3decrs.exe under the tools directory.

The SMB runner requires authorized existing UNC destinations. It creates only immediate GUID-named test containers, records an ownership manifest locally before writes, and restricts cleanup by exact parent/leaf paths and ownership markers. Cleanup rejects reparse points, attempts both owned targets on failure, and checks production top-level snapshots afterward. Tests use isolated portable metadata and local working space. No share disconnection, server setting change or production game modification is performed.

The ordering runner serves the real ISO from a throttled loopback HTTP server using Content-Disposition filenames. It records job-state lane activity and server request concurrency/order. All destinations, queue files and work files are under ignored artifacts/ordering; metadata uses the smoke runner's portable mode. It does not contact production SMB folders. This validates local HTTP transport; remote HTTPS/authentication/redirect behavior is not covered by this run.

The preflight runner uses disposable fixtures under artifacts/preflight, a controlled cancellation-aware process double, a real 7-Zip listing, and a tiny loopback HTTP response to exercise the post-download cancellation gap. It needs installed 7-Zip but no real game/key data. The MainWindow suite adds standalone preflight locking/cancellation/retry and window-close-during-preflight checks. Use separate output folders for builds while a runner is executing to avoid Windows executable locks; final regressions used artifacts/preflight-validation/bin and artifacts/window-preflight/bin.

## Remaining risks and technical debt
- Real-image decryption cancellation/shutdown, active download interruption, archive/JB extraction interruption, ISO/JB publishing cancellation/no-overwrite races, preflight interruption, MainWindow shutdown/save-failure integration, and successful/cancelled SMB delivery are validated. Runtime coverage remains for unexpected network disconnection/permission failures, additional archive formats/compression methods, and late file-picker results during shutdown. Production network service was not disrupted for tests. File-picker guards were inspected but not automated with native dialogs.
- Cancellation is cooperative between filesystem operations. A filesystem/network call already blocked inside Windows, or shared startup cleanup, can still delay exit. These tasks are awaited rather than abandoned.
- Persistence restore still reads synchronously during startup.
- Background saves are serialized without coalescing; large bursts can queue writes.
- Settings-save and tool-scan tasks are outside the queue shutdown barrier.
- Persistence schema/versioning and broader recovery behavior remain future work.
- Remaining magic strings and the temporary state/stage bridge need review.
- Two obsolete Watermark properties should become PlaceholderText in a separate UI cleanup. MainViewModel may still have extractable responsibilities.

## Recommended next step
Review/checkpoint the scheduling coverage and preflight cancellation changes when requested. Next continue conservative typed-state cleanup, then address the small obsolete UI properties before release polish. Remaining network-failure scenarios should use controlled fixtures rather than disrupting production SMB. Do not assume the pre-recovery status document's validation results apply to this checkout.

## Future work (not implemented here)
UI/release polish, Xbox 360 support, webMAN post-run refresh, FTP delivery, persistent download resume, library indexing/UI, and possible macOS portability. Xbox and webMAN work require explicit requests. Preserve shared queue/pipeline architecture and all existing PS3 safety invariants.
