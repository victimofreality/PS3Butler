# Preview review checklist

Use a freshly extracted preview, isolated work/output folders, and one game you are entitled to use. Keep production destinations out of this walkthrough.

Automated Release window checks cover URL text/button bindings, invalid URLs and unsupported schemes, selected-job removal, settings validation without overwriting saved settings, minimum-width Cancel/Clear Queue placement, startup restoration, preflight retry/cancellation, active decryption shutdown, and save failure/retry. They do not replace the visual checks below.

- At normal size and the minimum window size, scroll through Settings and Queue. Check labels, paths, status messages and queue rows are readable, including at your usual Windows display scaling.
- Use each Browse button. Cancel once and confirm the previous value remains. Select the intended executable/folder and confirm the field changes. Test Add Local Games and a selected job's destination override picker.
- Use Tab and Shift+Tab to move through controls, and activate buttons with the keyboard. Confirm focus is visible and you can reach the queue and its output-format choices.
- Save settings with isolated work/output paths. Add one local game, select its output format, and run preflight. Confirm the key, destination and result make sense before starting. Check the completed output in the isolated destination.
- Close and reopen. Confirm saved settings and unfinished queue inputs return. Completed games should not return as pending work.

Record the preview ZIP name, Windows scaling, and any awkward controls or unexpected messages before making a release decision. RAR and remote HTTPS/authentication scenarios still need separate fixtures/coverage.
