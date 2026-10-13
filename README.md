# High Command — an RTS mode for Risk of Rain 2

**Stop running. Start commanding.**

[Русская версия](https://github.com/kantemiir/RoR2-HighCommand/blob/master/README.ru.md)

![The commander's orbital strike lands in the middle of a fight](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/strike.jpg)

High Command turns Risk of Rain 2 into a real-time strategy game. You have no body: you are the commander, looking
down at the stage from an RTS camera. Your army is made of real survivors (Huntress, MUL-T, Acrid, Engineer,
anyone you have unlocked), each driven by its own AI with that survivor's actual skills. You select them, give them orders,
spend the army's gold on new recruits and upgrades, and decide when to start the teleporter.

Everything else is the game you know: the same stages, enemies, director, elites, bosses, items, difficulties and
artifacts. It just plays very differently when you are the one holding the map.

> **Status: early but playable.** Single player only. I am actively working on it, and I would love your feedback (see
> [Feedback and help](#feedback-and-help)).

---

## What you get

### Command an army of survivors
Select heroes with a click or a box drag. A right click does the obvious thing: move, attack the enemy under the cursor,
follow a friend, open a chest. Attack-move, focus fire, hold, patrol, retreat to a rally point, formations, control
groups, Shift-queues: it is all there, with the hotkeys you would expect from an RTS.

![A battle: golems and lemurians against the army](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/battle.jpg)

### Heroes that fight like their survivor
Every hero uses its own kit. Ranged survivors keep their distance and kite, melee survivors dive in, and Engineer
drops turrets. Skills fire on their own, or you cast them yourself (`Z X C`, `Y` for equipment) and switch any skill's
automatic use on or off. Stances (aggressive / defensive / passive) decide how far they chase.

![Plan a route with Shift](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/orders.jpg)

### Get down into the fight
The camera is built like a modern RTS camera: zoom to the cursor, rotate, tilt, and zoom all the way in for a low,
cinematic view right next to your heroes. A rock or a roof that hides a hero fades out of the way. A hero behind a hill
is drawn as a silhouette, so you never lose anyone.

![A close, low view of Acrid and the army](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/close.jpg)

### One purse for the whole army
Kills, barrels and income fill one shared purse. Spend it on:
* **Recruits:** every unlocked survivor can join, and the price grows as the run goes on.
* **Army upgrades:** weapons, armor, field medicine and logistics, for every hero including future recruits.
* **Chests, shrines, printers and drones** across the map. Right-click a chest and the nearest hero walks over and opens
  it. Idle heroes open nearby chests on their own when it is safe and you can afford it.
* **Revivals** for fallen heroes.

![The barracks: recruit survivors and buy army upgrades](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/barracks.jpg)

![Right-click a chest: the nearest hero goes to open it](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/loot.jpg)

### Share the loot your way
Items go into the army's pool and are handed out to the heroes. Open the item panel (`I`) to see who carries what, and
drag an item onto another hero to give it.

![The item panel](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/items.jpg)

### The commander's own tricks
Four abilities on their own cooldowns: **Orbital strike** (`F5`), **Field medic** (`F6`), **War cry** (`F7`) and
**Mark** (`F8`: everyone focuses the target, and it takes more damage). Heroes earn **veteran ranks** from kills.

### The teleporter, the strategic way
One button (`H`): the nearest hero starts the teleporter, the army gathers in the zone, and when the boss is dead the
same button takes everyone to the next stage.

![The army holds the teleporter zone against a Wandering Vagrant](https://raw.githubusercontent.com/kantemiir/RoR2-HighCommand/master/docs/screenshots/teleporter.jpg)

---

## Installation

You need Risk of Rain 2 (Steam) with **BepInEx** and **RoR2BepInExPack**. [Risk of Options](https://thunderstore.io/package/Rune580/Risk_Of_Options/)
is optional and gives you an in-game settings window.

**The easy way: Thunderstore.** Open [HighCommand on Thunderstore](https://thunderstore.io/c/riskofrain2/p/kantemiir/HighCommand/)
and press *Install with App*, or search for "HighCommand" in r2modman / Thunderstore Mod Manager. The manager installs
BepInEx with it.

**From GitHub with a mod manager**
1. Download `HighCommand-<version>.zip` from [Releases](https://github.com/kantemiir/RoR2-HighCommand/releases).
2. In the manager: *Settings → Import local mod* and pick the zip.

**By hand**
1. Install BepInEx for Risk of Rain 2.
2. Unpack the zip into `Risk of Rain 2\BepInEx\plugins\HighCommand\`.

## How to play

*Main menu → Extra Game Modes → High Command*, or pick the mode in the lobby. Choose your survivor and difficulty as
usual: that survivor is your first hero, and a second random one joins it.

| | |
|---|---|
| **Camera** | `W A S D` or the screen edge to pan, wheel to zoom (to the cursor), middle mouse or `[` `]` to rotate, middle mouse up/down or `PgUp` `PgDn` to tilt, `Home` to reset the view, `Space` to fly to the selection (twice: follow it), `Backspace` for the last alert |
| **Select** | left click, box drag, `Shift` to add, double click for all of that kind, `Ctrl+A` for all, `Ctrl+1..9` to set a group, `1..9` to recall it, `Tab` for the next hero |
| **Orders** | right click (`Shift` queues), `Q` attack-move, `E` stop, `R` hold, `F` focus fire, `V` retreat, `T` patrol, `G` rally point, `N` formation, `F2 F3 F4` stances |
| **Skills** | `Z X C` secondary / utility / special, `Y` equipment, `Alt` + key toggles automatic use |
| **Army** | `B` barracks (recruits, upgrades), `I` items, `U` revive, `H` teleporter |
| **Commander** | `F5` orbital strike, `F6` field medic, `F7` war cry, `F8` mark |
| **Help** | `F1` shows every key in game; `Esc` closes things one by one |

You lose the run when every hero has fallen and there is no gold for a revival.

## Settings

* **Lobby:** a "High Command" rule category (starting gold, hero prices, army limit, how items are shared, ...).
* **In game:** *Settings → Mod Options → HighCommand* (with Risk of Options).
* **Config file:** `BepInEx\config\kantemiir.HighCommand.cfg`. Every key, the camera, the loot rules and `Interface/UiScale`
  are in there.
* **AI per survivor:** `profiles\hero_profiles.json` next to the dll sets ranges, retreat thresholds and when each skill is
  used (`auto / never / escape / engage / burst`). You can edit it without rebuilding.

## Known limitations

* **Single player only.** There is network code from early versions, but multiplayer is not supported or tested now.
* **Modded survivors** work through a generic profile and may play below their potential.
* **Final stages** (Commencement, the Planetarium and similar) have not been tested. Their scripted events were made for
  a player with a body.
* Tested on the current Steam version of the game. If something breaks after a game update, please open an issue.

---

## Feedback and help

This is my first big mod, and I am **open to criticism, ideas, feedback and help**. If something feels wrong, if a
survivor plays badly, if you have an idea for a feature, or if you want to work on the mod with me, please tell me.

* **Bugs:** open an [issue](https://github.com/kantemiir/RoR2-HighCommand/issues/new/choose). Attach
  `BepInEx\LogOutput.log` if you can, and say what you did and what happened.
* **Ideas and suggestions:** open an issue with the *Idea / suggestion* template, or start a thread in
  [Discussions](https://github.com/kantemiir/RoR2-HighCommand/discussions).
* **Code, balance, AI profiles, translations:** pull requests are welcome. Small ones are the easiest to review. See
  [CONTRIBUTING.md](https://github.com/kantemiir/RoR2-HighCommand/blob/master/CONTRIBUTING.md).

## For developers

* Source: `src\` (C#, BepInEx 5, HarmonyX, netstandard2.1). Build: `tools\build.ps1` (the game must be closed). Package:
  `tools\package.ps1`.
* The mod tests itself: `tools\qa-run.ps1 <scenario>` starts the game, plays a script from `tools\qa\*.txt` (synthetic
  mouse and keys, `hc_*` console commands, screenshots) and collects the log.
* Notes on the design and its history: `CHANGELOG.md` and `docs\dev\` (design, roadmap, audits; mostly in Russian). `CLAUDE.md` holds the working rules
  (much of the mod was built together with an AI assistant).
* No game code or assets are copied. The interface pictures are drawn in code.

## License

[MIT](https://github.com/kantemiir/RoR2-HighCommand/blob/master/LICENSE) © kantemiir. Risk of Rain 2 belongs to Hopoo Games and Gearbox. This is a fan-made mod and is not affiliated
with them.
