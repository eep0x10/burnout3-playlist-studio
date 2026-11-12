# Playlist synchronization — implementation contract

## User outcome

Playlist Studio is the bridge between a music playlist and one active Takedawn soundtrack. Importing or editing automatically prepares managed audio. **Atualizar** refreshes a linked external source and prepares changes; there are no separate download buttons. **Usar no jogo** chooses the soundtrack. The manager does not edit the YouTube/Spotify playlist itself.

## Ownership and state

- Persist one original playlist URL/provider and last refresh result per playlist; never infer an old collection URL from individual video matches.
- Remote entries have stable identity and occurrence handling. Preserve existing local track IDs, downloaded audio, user metadata and selected YouTube matches where identity survives. Manually added entries remain independent.
- Fetch into a staged snapshot before reconciling the real library. A failed or partial response is never authoritative evidence that missing tracks were removed.
- Complete responses reconcile additions, removals and ordering. Partial responses can add entries but preserve missing entries and expose a warning. Spotify public embeds are incomplete unless independently verified; the API path needs authorized access.
- Network credentials are not stored in the portable JSON library.

## Files and game integration

- Delete only known manager-owned files within the portable managed namespace; never delete a local import's source file. Validate containment, links/reparse points, identity and recorded ownership before cleanup. Unknown or externally modified files are preserved and reported.
- Removing a track/playlist schedules owned cache/lyrics and obsolete exports for cleanup. Persist unfinished work so cancellation and locked files are recoverable.
- Download/preparation may run while Takedawn is open. Changing its active export/configuration and removing files currently used by the game are deferred until it is closed. Do not launch or close the game to make progress.
- Keep one chosen playlist active. Importing an unrelated list does not silently switch the game to it.
- Clearing/deleting the active list removes only the application's own custom soundtrack configuration, preserving unrelated game settings. Back up configuration writes and respect external edits.

## Acceptance evidence

Use synthetic HTTP/tool fixtures and sandbox exports: complete add/remove/reorder; stable IDs and duplicate occurrences; partial/error/cancel preservation; automatic processing; local original preservation; whole-playlist and empty-active cleanup; containment/reparse/modified-file refusal; pending game application; retry without duplicate entries/downloads. Native UI evidence covers import/update, source status, removal copy, cancellation and absence of download buttons. Actual game execution is outside this task's validation boundary at the user's request.
