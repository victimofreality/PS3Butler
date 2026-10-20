# PS3 Butler

PS3 Butler is a Windows-first desktop application for automating the annoying parts of preparing PlayStation 3 game dumps for use with a jailbroken PS3.

Instead of manually extracting archives, hunting for keys, decrypting ISOs, converting formats and copying everything to network storage, PS3 Butler turns the whole process into a queue.

Drop in a game, pick an output format, and let the butler deal with the boring shit.

## Features

- Local ISO, ZIP, 7z and RAR sources
- Direct HTTP/HTTPS downloads
- Automatic Redump `.dkey` lookup
- Supports text and binary Redump keys
- PS3 ISO decryption using Redrrx PS3Dec (`ps3dec.exe` for v3+, or older `ps3decrs.exe` builds)
- Archive inspection and extraction using 7-Zip
- Output as:
  - Decrypted ISO
  - JB folder
  - Encrypted ISO + `.dkey`
- Local and SMB/UNC network destinations
- Queue persistence across restarts
- Duplicate detection
- Title ID / metadata inspection
- Disk-space checks before processing
- Transactional destination publishing
- Download pause/resume
- Pipeline cancellation
- Cleanup of temporary and incomplete files
- Sequential downloads with overlapping processing

## How it works

PS3 Butler uses a simple pipeline:

1. Inspect the source
2. Find and validate the appropriate Redump key
3. Extract archives if required
4. Decrypt the ISO when required
5. Read game metadata
6. Produce the requested output format
7. Copy/publish the result to the configured destination

Downloads and processing can overlap, but PS3 Butler intentionally runs only one downloader and one processing job at a time.

## Output formats

### Decrypted ISO

Produces a decrypted ISO suitable for use with common PS3 homebrew loaders.

### JB Folder

Extracts the game into a traditional PS3 JB folder structure and names it using metadata where available.

Optional removal of `PS3_UPDATE` is supported.

### Encrypted ISO + key

Keeps the original encrypted ISO and places the matching `.dkey` beside it.

Useful for loaders that support encrypted Redump images directly.

## Network storage

PS3 Butler supports normal local paths as well as Windows SMB/UNC paths, making it suitable for setups where games are stored on a NAS or home server and streamed to the console.

The original motivation for the project was automating game preparation for a PS3 using network-hosted storage.

## Safety and reliability

PS3 Butler tries hard not to leave broken half-copied games behind.

Destination outputs are staged first and only published after validation succeeds.

It also includes:

- no-overwrite protection
- disk-space preflight checks
- temporary file cleanup
- stale staging cleanup
- cancellation handling
- duplicate detection

## Requirements

Currently tested primarily on Windows.

You will need:

- .NET 10
- 7-Zip
- Redrrx PS3Dec (`ps3dec.exe` for v3+, or `ps3decrs.exe` for older builds)
- Redump `.dkey` files for encrypted PS3 images

Paths to external tools and destinations can be configured in the application.

## Build a portable Windows preview

From PowerShell at the repository root, run `./scripts/Package-Windows.ps1`.
The script uses the SDK under `artifacts/dotnet` when present, otherwise the SDK on PATH; an SDK can also be selected with `-DotNetPath`.
It publishes a self-contained Windows x64 Release build and creates a fresh ZIP plus SHA-256 checksum under `artifacts/packages`.
The package includes the .NET runtime, portable-mode marker and [setup guide](docs/PORTABLE.md); external tools, keys, games and saved user data are not bundled.
Building from source requires the .NET 10 SDK. Runtime packages may need downloading during publishing.

## Current status

PS3 Butler is under active development.

The core PS3 workflow is functional and has been tested with real game images and network storage.

The project is currently undergoing architecture cleanup and UI polish before larger feature additions.

## Planned features

Some things on the roadmap:

- Xbox 360 support
- FTP delivery
- download resume
- improved library indexing
- webMAN integration
- further UI polish
- broader platform support

Xbox 360 support is planned around the existing queue/pipeline architecture rather than as a completely separate application.

## Why?

Because doing this manually gets old very quickly.

Also because owning a jailbroken PS3 apparently means eventually writing software to manage the software you use to manage the software on the PS3.

## Disclaimer

PS3 Butler is intended for use with game dumps and keys you are legally entitled to use.

No copyrighted game data or decryption keys are included with the project.
