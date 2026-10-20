# PS3 Butler portable preview

1. Extract the entire ZIP into a writable local folder. Do not run it from inside the ZIP.
2. Open `PS3Butler.exe`. The Windows x64 package includes the .NET runtime.
3. In Settings, select your installed 7-Zip executable and Redrrx PS3Dec executable (`ps3dec.exe` v3+, or older `ps3decrs.exe`). These external tools are not bundled.
4. Select your Redump keys directory, a local work directory with sufficient free space, and ISO/JB destination folders. No games or keys are bundled.
5. Start with one game and an empty local destination. Run preflight, check the proposed output, then start processing.

The build identity appears below the PS3 Butler heading and in `BUILD.txt`. Include it when reporting a problem. Packaged builds show the preview version, UTC build stamp and source commit; `.dirty` means the source contained uncommitted changes when packaged.

Keep `portable.txt` beside the executable. Settings, the saved queue, and metadata are stored in the adjacent `Data` folder. Back up that folder before replacing the application. Keep it out of shared distribution ZIPs. Configured work/output paths can be outside the portable folder and should be backed up separately as needed.

To update, close PS3 Butler and allow active work and queue saving to finish. Extract the replacement into a new folder, then copy your backed-up `Data` folder beside the new executable before starting it. Keep the old folder until you have verified your settings and queue.

This is a development preview. RAR archives and remote HTTPS/authentication scenarios have less validation than the local ISO, ZIP/7z and SMB workflows. Before using a production destination, test a single game in an isolated destination. Windows may display a warning for an unsigned application; this preview has no signing certificate.
