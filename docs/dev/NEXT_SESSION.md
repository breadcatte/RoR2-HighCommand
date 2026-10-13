# Handoff for the next session (2026-10-13)

Read first: `CLAUDE.md` (rules, paths, commands), then `CHANGELOG.md` (top sections 0.13.0 / 0.12.x).

## State
* Mod "High Command" (RTS mode for Risk of Rain 2), repo `C:\Users\user\Documents\RoR2Strategy`,
  git: `_tools\mingit\cmd\git.exe`. Latest release: 0.13.0 (tag `v0.13.0`, 2026-10-13).
* Published: GitHub https://github.com/kantemiir/RoR2-HighCommand (release v0.13.0 with the zip) and Thunderstore
  https://thunderstore.io/c/riskofrain2/p/kantemiir/HighCommand/ (0.13.0, categories AI Generated + Gamemodes + Mods).
* How to publish (worked for 0.13.0): GitHub release with `gh` (`C:\Users\user\AppData\Local\Temp\claude\gh_bin\bin\gh.exe`,
  logged in as kantemiir): `gh release create vX.Y.Z dist\HighCommand-X.Y.Z.zip --repo kantemiir/RoR2-HighCommand --notes-file ...`.
  Thunderstore: the user's Chrome (Claude in Chrome, already signed in): https://thunderstore.io/package/create/,
  `file_upload` the zip (copy it into the repo root first, delete after), team kantemiir, community Risk of Rain 2 only,
  categories by clicking the options (Enter does not pick one). Check with the API:
  https://thunderstore.io/api/experimental/package/kantemiir/HighCommand/
* `tools\package.ps1` writes zip entries with `/` (fixed for 0.13.0; earlier zips had `profiles\...`).
* Push needs `-c credential.interactive=always`. Never push `refs/backup/*`, never `--all/--mirror`.
* Thunderstore strips discord.com links from README: the Discord contact is plain text on purpose.
* Uploading files from the browser works only from the session folder: copy the zip into the working directory first,
  delete the copy afterwards.

## Done in the last session (0.13.0)
Order lines along the real way on the ground graph (one line per group, ribbon with pulses, fade of long ways, edge
marks for targets off the screen; see CLAUDE.md), interface hidden while the pause menu is open, zip paths with `/`.

## Not verified by the user's eyes
The 0.13.0 order lines in motion (checked by scenario screenshots `tools\qa\route.txt`, also while the camera pans);
the edge mark at the bottom edge was never on a shot; the settings menu itself (pause covers it, `tools\qa\pause.txt`).

## Ideas offered, not requested
Reddit r/riskofrain post, a short gameplay video, Discord RoR2 Modding post (stopped at a CAPTCHA the user must solve).

## Working rules (from the user)
Russian, short answers; user is not a programmer. Single player only, don't touch networking. Never say "works"
without a scenario/screenshot. End every answer with a «Для Sonnet» block (or «нет»). Keep CHANGELOG.md and CLAUDE.md
current. Commits in English with `Co-Authored-By: Claude <noreply@anthropic.com>`. No push / publish / download without
an explicit «да». Don't erase `kantemiir.HighCommand.cfg`; check `HighCommand.autoexec.txt` is gone after tests.
