# Changelog

## 0.10.0 - army upgrades, veteran ranks, fairer economy

New
* Army upgrades in the barracks (`B`): Weapons (+10% damage), Armor (+8), Field medicine (+0.6 HP/s, grows with the
  hero's level), Logistics (+8% speed). Every hero gets them, recruits too; prices grow per level and with time/stages.
* Veteran ranks: 25 / 75 / 175 kills (minions' kills count for their owner) give rank 1-3, each +10% damage and +5 armor.
  Stars on the roster card, a message on promotion.
* The army starts with 2 heroes: the lobby's survivor and a random unlocked one (`Army/StartingHeroes`, 1-4; free).
* The whole order queue (Shift orders) is drawn on the ground, leg after leg.

Balance
* Hero prices follow time, stages and the difficulty only. Before, every recruit raised the run's difficulty (heroes
  count as players), which raised the next price on top of the growth per hero.
* The army counts as fewer extra players: rounded down (+0.25) instead of rounded, so 2 heroes = 1 player, 3 = 2, 4 = 3.
* A revival costs 40% of a FIRST recruit's price: it no longer grows with the number of heroes bought.
* Heroes chase enemies up to 60 m from their post (was 95; Railgunner 75, was 110).
* A ranged hero standing at its post while being hit sidesteps instead of standing still.

Fixes
* Long survivor descriptions no longer spill below the barracks panel.

Dev
* `hc_upgrade [0-3]` buys an upgrade and prints the heroes' stats; `hc_veteran <id>` promotes a hero.
* Single player only (decision of 2026-10-12): new features are not replicated over the network.

## 0.9.2 - a lone hero no longer loses the run by itself

Fixes
* The starting hero died on its own within 1-2 minutes and the run was lost 45 s later. Causes and fixes:
  * The game's AI rules for survivors always close in on the enemy (they were written for Umbras). Ranged heroes now
    keep their distance (`standoff` per survivor in `profiles\hero_profiles.json`): they back off and circle instead of
    walking into melee.
  * A fleeing hero ran blind and used no skill at all. It now uses its escape skill (`fleeSlot`: Huntress' blink,
    Commando's roll, Bandit's smoke bomb, Mercenary's dash, Void Fiend's trespass), aimed and moving away from the enemies.
  * The place to flee to could lie at the foot of a cliff (the hero walked off it and landed at 1 HP). It is now a
    ground node on the same level, away from the enemies, picked again every 1.5 s; a fleeing hero never steps off an edge.
  * An "escape" skill is no longer spent by a rule that runs at the enemy (Huntress' gap-closing blink at low HP).
* All heroes down while the army can pay for a revival: the first fallen hero is revived automatically after 4 s
  (`Run/AutoRevive`, `Run/AutoReviveDelay`); the top bar counts down and names the price. Without gold the run is lost
  as before.
* Void Fields, the Bazaar and other scenes where the game brings no allied AI along: the heroes were missing and the run
  was lost. The mode now brings them in at the player spawn.
* The rally point moves to the new stage's spawn at once: a revival right after a stage change no longer drops the hero
  at the previous map's coordinates (from high in the air).
* Recruited and revived heroes no longer land inside each other (all of them used the same nearest node).
* Heroes walking straight at an unreachable point no longer step off ledges; on stub node graphs (the Bazaar has 6 nodes)
  they walk without path finding instead of filling the log with "ComputePath exception".

Dev
* `hc_next_stage <scene>` goes to a named scene; `hc_dump_scenes` lists the scenes without allied AI entry.
* `tools\build.ps1` installs the hero profiles from `package\profiles` (it looked in a `profiles` folder that does not
  exist, so edits to `hero_profiles.json` never reached the game).
* `tools\qa-run.ps1` no longer destroys the user's `kantemiir.HighCommand.cfg`: it is copied aside before the test and
  put back afterwards, also when the run fails; the scenario file is never left in the config folder.
* `package\profiles\hero_profiles.json` lists `fleeSlot` and `standoff` for every survivor, equal to the built-in values.

## 0.9.1 - QA pass (single player)

Fixes
* The High Command button was not added to the Extra Game Mode menu on a real mouse click (the button switches the screen directly, the old hook never ran). Now added when the screen is opened.
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
