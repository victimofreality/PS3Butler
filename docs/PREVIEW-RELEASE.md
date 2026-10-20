# PS3 Butler 0.1.0-preview.1

Windows x64 portable development preview for preparing and delivering PS3 game images.

## Getting started

Download the ZIP and extract the entire folder to a writable local location, then open `PS3Butler.exe`. The .NET runtime is included. Read `START-HERE.md` for setup and backup instructions.

Configure your installed 7-Zip, Redrrx PS3Dec (`ps3dec.exe` v3+ or older `ps3decrs.exe`), Redump keys, work directory and destinations. External tools, games and keys are not bundled. Start with one game and an isolated local destination.

Keep `portable.txt` beside the executable. Settings, queue inputs and metadata use the adjacent `Data` folder. Close the app and back up `Data` before upgrading.

## Included

- Local ISO/ZIP/7z/RAR and direct HTTP/HTTPS sources.
- Decrypted ISO, JB folder, or encrypted ISO with its matching key.
- Local and SMB/UNC destinations, duplicate checks, disk budgeting and transactional publishing.
- Sequential download and processing lanes that can overlap, with download pause/resume and cancellation.
- Background queue saving/restoration and shutdown that waits for active work and queue saving.
- Redrrx v3 compatibility and Unicode archive filename inspection.
- Visible preview/build identity and self-contained Windows packaging.

## Validation and limits

Validation includes real PS3 images and local/SMB output workflows, process interruption and cleanup, native-window shutdown/save failures, queue recovery, controlled HTTP/HTTPS transport, and ZIP/7z/RAR transport fixtures. The user accepted the manual preview check. This does not cover every server, game or archive variant.

This preview is unsigned and has no installer. Password-protected archives, legacy RAR variants, interactive download-provider login, proxies and public-provider-specific behavior need further coverage. Multi-volume RAR testing covers a local first volume with adjacent parts; automatic downloading of all parts is not provided. Persistent download resume, Xbox and webMAN integration are not included.

## Build and checksum

Release tag: `v0.1.0-preview.1`

Source commit: `a59646dbae08bea6d0ee6cd55df2a992867ee7fe`

Build identity: `0.1.0-preview.1+20261020053338.f7da39be.a59646dbae08`

Asset: `PS3Butler-0.1.0-preview.1-win-x64-20261020053338.f7da39be.zip`

SHA-256: `862CF5A3642F5E71AC6EFAEFE5387742FE3A9D1A8C308768FF33F7B7567B5C20`

The companion `.zip.sha256` file records the same hash. Include the identity shown beneath the app heading or in `BUILD.txt` when reporting problems.
