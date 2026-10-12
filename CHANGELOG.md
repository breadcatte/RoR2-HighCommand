# Changelog

## 0.9.1 - QA pass (single player)

Fixes
* The camera hands the stock camera rig back when the mode ends; bigger obstruction buffer for the camera.
* A manual cast cut short by another order no longer leaks its timer; a cast is no longer reported "not ready" while held.
* Help sheet (F1): two columns, opaque, no overflow.
* No more "ActivityManager ... unable to determine GameMode" error at every run start.
* Hotkey hints no longer overlap button captions; the money line of the top bar shrinks instead of overflowing.
* Zero compiler warnings (obsolete game API replaced).

Interface and controls
* `Interface/UiScale` (0.6-1.6) scales the whole interface; checked at 1920x1080, 1280x720 and 1024x768.
* `Esc` closes the targeting mode / help / item panel / recruiting panel first and does not open the pause menu at the same
  press; while the pause screen or the console is up the mode ignores clicks and the wheel and dims its interface.
* `Tab` (cycle) and `Ctrl+A` (select all) are configurable (`Keys/CycleSelection`, `Keys/SelectAll`).
* Alerts: a fallen or nearly dead hero gives a message, a sound and a mark; `Backspace` flies the camera there.

Performance
* Interface CPU cost halved (43 -> 20 ms per second with 6 heroes), frame time 6.0 -> 5.5 ms; garbage halved.

Dev
* `tools\qa-run.ps1`, scenarios in `tools\qa\`; console `hc_spawn`, `hc_ui`, frame time and GC in `hc_perf`.
## 0.9.0

First complete version of the mode.

* Game mode "High Command" in the lobby and in the Extra Game Modes menu; bodiless commander, AI survivors as the army.
* RTS camera over the stock camera rig, minimap, selection, orders (move, attack-move, attack, focus fire, stop, hold,
  follow, retreat, patrol, interact, cast), queue, stances, formations, rally point.
* Shared gold purse, recruiting by price that grows with every hero and with the difficulty, paid revival.
* Items drop from enemies straight into the army's pool; shared among the heroes by role, manual redistribution with
  drag and drop, equipment slots, split / mirror sharing modes.
* Teleporter flow: one button activates it, gathers the army while it charges and leaves the stage; defeat when no hero
  is alive and nobody can pay for a revival.
* Commander abilities: orbital strike, field medic, war cry, mark.
* Hero AI profiles per survivor (data file), automatic retreat of badly hurt heroes, per-skill usage rules.
* Command card, roster strip, inventory panel, tooltips, ru / en localisation, settings in Mod Options.
* Client -> server command channel for co-op.
* Lobby rules category (starting gold, hero prices, army limit, difficulty per hero, item sharing, drop rate).
