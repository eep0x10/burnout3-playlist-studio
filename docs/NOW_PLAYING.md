# Aviso de música / Now playing

Feature introduced in 1.1.0. This is a notification drawn by Playlist Studio, not the game's native HUD.

## Using the notification

Enable **Aviso da música no jogo** and keep the manager open while playing. You may minimize it. When a managed MP3 is detected, a short title/artist notification appears at the lower-left of the foreground Takedawn window. **Testar aviso** shows a clearly labelled demonstration without opening the game.

The style is inspired by the original track-change banner, using the manager's charcoal, warm white and orange identity. No EA logo or game artwork is bundled. The notification shows title and artist, not lyrics or playback progress.

## Start together with Takedawn (1.1.1+)

In 1.1.3+, click **Criar atalho** in the manager and select `Takedawn.exe`. This creates **Takedawn + Playlist Studio** on your Windows desktop without starting the game. Replacement of an existing shortcut requires confirmation. Use the button again after moving the manager or game. The manual setup below remains available.

The portable package includes `Launch-Takedawn.vbs`. Create a Windows shortcut with this target, replacing both example paths with your actual locations:

```text
wscript.exe "C:\Games\Playlist Studio\Launch-Takedawn.vbs" "C:\Games\Takedawn\Takedawn.exe"
```

Keep the script beside `BurnoutPlaylistStudio.exe`. This shortcut starts the manager minimized with song notifications enabled, then starts Takedawn in its own folder. The launcher accepts the game path only; it does not forward extra game arguments. Use folders without percent signs. The manager stays available on the taskbar. An existing manager for the same data folder is reused. Launching the game executable directly bypasses this shortcut; no Windows startup service or game modification is installed.

Since 1.1.2, the launcher waits for the game process it started to exit, then asks the manager to close gracefully. This also applies when the manager was already open. Any active manager operation is cancelled and allowed to finish cleanup before exit; the manager is not forcibly terminated. If you close the manager first, the later close request exits silently. Starting the manager directly keeps it independent of the game's lifetime. Do not terminate the small `wscript.exe` launcher while playing, since it delivers the close request.

For a manager-only shortcut, use `BurnoutPlaylistStudio.exe --minimized --enable-song-notice`. Restore the original game shortcut target to `Takedawn.exe` to undo the integration. Moving the portable manager requires updating the shortcut's script path.

![Real preview window showing demonstration metadata, outside the game](screenshots/now-playing-preview.png)

## Behaviour and limits

- Notifications last approximately six seconds. A new event replaces the previous one.
- Placement follows the game window/monitor and Windows DPI, with safe margins.
- The window does not take focus or intercept clicks. It hides when you switch to another app.
- Fade animation follows the Windows client-area animation preference.
- Turning the feature off or closing the manager stops observation and removes the popup.
- The popup needs this manager's active, managed playlist. Unknown external songs are not guessed from a playlist clock.

## Detection boundary

Detection reads file-handle metadata only from Takedawn and compares it with exact active-manifest MP3 paths. No game patch, injected code, process-memory capture, music-byte reading or verbose game logging is required. It does not launch Takedawn or change its settings/saves.

The observer polls once per second and requires a stable candidate for at least 750 ms. Short file opens during scanning are ignored; multiple simultaneous candidates suppress notifications rather than selecting one arbitrarily. This can delay a popup by roughly one or two seconds. A file remaining open does not prove audible output or pause state. Reopening the same file after an observed gap permits another notification; a restart entirely between polls may be missed.

## Verification

The 84-check suite includes real Windows handle capture in the test process and a separate synthetic child, exact path matching, closing/reopening and rejection of a non-Takedawn process by the production reader. Other fixtures cover mapping, debounce and ambiguous candidates. No game process was opened for these tests.

The preview window was captured on the desktop with demonstration data. Foreground remained the manager during its approximately six-second lifetime; the popup was shown without activating its own window. Native topmost positioning and alpha updates are used directly to avoid WinForms activation/visibility side effects.

The actual fullscreen/gameplay appearance still requires a user test. The notification is external to the renderer and may not appear in capture software configured to record only the game's renderer. For diagnostic details and visual preview evidence, see [verification](VERIFICATION.md) and the native UI decision record.
