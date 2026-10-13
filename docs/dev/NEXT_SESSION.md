# Handoff for the next session (2026-10-13)

Read first: `CLAUDE.md` (rules, paths, commands), then `CHANGELOG.md` (top sections 0.12.1 / 0.12.0 / 0.11.0).

## State
* Mod "High Command" (RTS mode for Risk of Rain 2), repo `C:\Users\user\Documents\RoR2Strategy`,
  git: `_tools\mingit\cmd\git.exe`. Latest commit: release 0.12.1 (tag `v0.12.1`).
* Published: GitHub https://github.com/kantemiir/RoR2-HighCommand (release v0.12.0 has the zip; v0.12.1 is a tag only,
  README text change) and Thunderstore https://thunderstore.io/c/riskofrain2/p/kantemiir/HighCommand/ (0.12.1,
  categories AI Generated + Gamemodes + Mods; the user logs in to Thunderstore himself).
* Push needs `-c credential.interactive=always`. Never push `refs/backup/*`, never `--all/--mirror`.
* Thunderstore strips discord.com links from README: the Discord contact is plain text on purpose.
* Uploading files from the browser works only from the session folder: copy the zip into the working directory first,
  delete the copy afterwards.

## Done in the last session
Monsters hunt the army (`Mode\EnemyHunt`), no friendly fire (`RunPatches.NoFriendlyFire`, registered in `Plugin.cs`),
orders Seek enemies (K) / Seek chests (L), new health bars, camera zoom to 220 m, interface pass (barracks, items,
F1 sheet, orders list), Discord contact in README.

## Not verified by the user's eyes
Everything above was checked by scripted runs (`tools\qa\hunt|seek|camfar|camkeys|uishot|bars.txt`) and screenshots,
not by the user playing. Once (scenario `seek`) `hc_cam dist` did nothing; not reproduced later.

## Ideas offered, not requested
Reddit r/riskofrain post, a short gameplay video, Discord RoR2 Modding post (stopped at a CAPTCHA the user must solve).

## Working rules (from the user)
Russian, short answers; user is not a programmer. Single player only, don't touch networking. Never say "works"
without a scenario/screenshot. End every answer with a «Для Sonnet» block (or «нет»). Keep CHANGELOG.md and CLAUDE.md
current. Commits in English with `Co-Authored-By: Claude <noreply@anthropic.com>`. No push / publish / download without
an explicit «да». Don't erase `kantemiir.HighCommand.cfg`; check `HighCommand.autoexec.txt` is gone after tests.
