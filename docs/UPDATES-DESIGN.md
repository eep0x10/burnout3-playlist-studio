# Portable update contract

## Outcome and ownership

Use stable releases of `eep0x10/burnout3-playlist-studio` only. The user has authorized automatic updates; normal checks need no per-version confirmation. Provide an automatic-update preference and an explicit manual check/install action. Offline/rate-limit errors must not prevent library use or game preparation.

Core owns release/version parsing, bounded HTTP, package checksum validation, safe staging, an apply manifest, parent process identity/waiting, program-file backup/replacement and rollback. App owns visible status, safe lifecycle decisions, launching a copied helper and restarting only the validated manager. No updater launches the game.

## Data boundary

Replace only explicitly approved program files: the EXE, companion launcher, notices/readmes and documented image/Markdown assets. Never copy over `data`, music, `tools`, user settings, game config/saves or unknown user files. Reject archive traversal, absolute paths, case-insensitive collisions, links/reparse points and unexpected executables. Check the exact release asset SHA256 before extracting and recheck staged files before replacement.

Updates use the matching ZIP and SHA256SUMS from the fixed official release. This verifies package consistency with that release; it is not an independent digital signature or protection against a compromised repository account.

## Lifecycle and recovery

Normal Studio sessions check asynchronously and hand off only when idle, no modal/active operation is present and the game is closed. Companion sessions may check/stage but never close for an update during gameplay/startup. On their normal close after the game, a prepared update may apply without restarting the manager. This avoids the gap between manager startup and the launcher's game process creation.

The external copied helper waits for the exact parent PID and creation time; it never kills a process. It checks game availability before writes, validates target/stage roots and expected hashes, backs up approved files and rolls back partial application on error. Restart preserves approved data/config/minimized arguments and happens only when the original session requested it. Failed-version cooldown prevents repeated rollback/restart loops. Interrupted/locked updates preserve the old program or expose recovery details instead of silently continuing.

## Acceptance evidence

Synthetic latest-release responses: newer/same/older/prerelease/draft/malformed/rate limited; bad checksum; oversized downloads; malicious ZIP paths/links/collisions/data entries; stage cancellation; target changes; parent wait failure; locked-file mid-apply and rollback; user library/music/settings/tools sentinels unchanged. Use harmless owned files/probes in sandbox only. Native UI checks use fake services without updating the installed application or opening a game. Independently review the destructive file boundary and helper arguments before publishing.
