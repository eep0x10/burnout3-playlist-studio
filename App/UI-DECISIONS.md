# Burnout 3 Playlist Studio — native UI decisions

Primary task: assemble a real MP3 playlist, review metadata/order, audition it, then expose its folder to Takedawn. Start with an empty persisted library, never simulated downloads or fabricated progress.

Three structures considered: (1) import wizard, useful for first-time setup but slow for repeated metadata review; (2) album-art wall, useful for browsing but weak for order and unavailable artwork; (3) pit wall, selected: playlist rail, ordered track table, metadata/lyrics inspector and persistent transport. Every visible region supports selection, review or playback rather than dashboard metrics.

Direction: asphalt/charcoal, burnt orange primary actions, restrained red failures, warm white text. Bahnschrift display headings and Segoe UI body/table. Functional native control surfaces; no artificial album art, logos or racing illustrations. Dense rows, explicit empty state and actual job messages. Main window can resize; inspector scrolls while transport remains accessible. Keyboard/table selection and explicit cancel/retry stay available.

App icon: original B3 raster generated with ImageGen, retained as `Assets/Studio.png`, mechanically converted to a multi-size ICO (16–256) for the executable/window. It contains no EA/Spotify logo. A small native orange header rule provides racing identity without external game assets.

Feedback: selection and search update immediately without losing playlist/filter; playback ticks update only time/progress; imports/downloads report actual service stages and support cancellation. Errors preserve inputs. Native desktop application: Anime.js, browser motion and a 390px web viewport do not apply. No decorative animation is needed.

Exclusive activation update: “Carregar playlist” requires every track to have a prepared local MP3 with no pending metadata. “Baixar e carregar” first prepares the complete playlist and activates only after success. The Core owns the isolated export, config/backup and single-playlist checks. UI badges describe configuration for a future execution or manual X/A selection, never observed in-game playback. Version/file checks and active audio hashes run away from the UI thread. Takedawn can be selected without executing it; its path is kept only in portable `data/ui-settings.json`. The update button opens the official releases page without installing. Generic multi-playlist folder configuration was removed from the UI to preserve exclusivity.

Owner: App UI agent. Core contracts, persistence, downloader/catalog integration and build/release documents belong to the main/Core agents. Desktop CUA evidence and remaining findings will be recorded after the first working slice by the main agent. No UI validation is claimed yet.
