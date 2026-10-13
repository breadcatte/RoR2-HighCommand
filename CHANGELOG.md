# Changelog

## 0.11.0 - loot from the map, a real strategy camera, first public release

Public release
* The source is on GitHub: https://github.com/kantemiir/RoR2-HighCommand (MIT license), with a new README (English and
  Russian) and screenshots, issue templates and CONTRIBUTING.md. Internal working notes moved to `docs/dev/`.
* The English name of the Void Fiend is printed properly (the game's own text reads "V??oid Fiend").
* The Items button's open frame no longer covers its text.
* Dev: `hc_cam yaw <deg>` / `hc_cam pitch <deg>`; scenario `tools\qa\showcase.txt` takes the README's screenshots
  (it switches the game to English for the shots and back).

Camera height
* The camera only follows ground a hero can stand on (the stock node graph): a rock spire, a roof or a decoration high
  above the play area no longer lifts it into the sky, and its height never leaves the range of the map's walkable
  ground.
* Standing still above lower ground (after crossing a chasm at its edge's level, beside a path over a void) the camera
  comes down onto that ground after 1.5 s instead of hanging in the air for good.
* The camera never moves in by itself any more: the distance is only what the wheel set (it used to be pulled in front
  of obstacles, which put it under roofs and rocks, inside the level, and kept the wheel from zooming out).
* The camera can be laid almost on the ground: the tilt (middle mouse up / down, PageUp / PageDown) now goes down to
  4 degrees instead of 25. It tilts up by itself only as far as it must to stay above the ground (a low view on a
  slope), never to see past a hill.
* A small piece of the level (a rock, a roof, a tree, a bridge, up to 60 m) is hidden only while it hides a hero on
  screen (the hero walked behind it, or the camera was turned behind it) and comes back half a second later. The floor
  a hero stands on, big ground and the game's objects are never hidden.
* What other strategy games do, now here too:
  * zoom to the cursor (Company of Heroes, Total War): the wheel zooms towards the point under the mouse, which stays
    under the mouse (`Camera/ZoomToCursor`, on);
  * zoom tilt (Company of Heroes, Total War): coming close also lowers the view, down to a side view of the heroes at
    the nearest zoom; zooming out brings the player's tilt back (`Camera/ZoomTilt`, on). The nearest zoom is 6 m
    instead of 10 (an untouched old value is updated once);
  * Home resets the camera's turn, tilt and zoom to the start view (Age of Empires IV / Company of Heroes);
  * Space twice locks the camera on the selected heroes (or the army) until it is moved by hand or Space is pressed
    again (the follow camera of Warcraft III / StarCraft II);
  * a hero hidden behind a hill or a cliff (too big to hide) is drawn as a flat silhouette where it stands (StarCraft II,
    Torchlight).
* Dev: `hc_cam audit` also tries the lowest tilt at the nearest distance on every point and counts where the camera
  had to tilt up and where it would stay in the ground; scenario `tools\qa\camvillage.txt`.
* Dev: `hc_cam audit` pans along 32 lines across the map the old and the new way and counts the places where the camera
  climbs or hangs above walkable ground; scenario `tools\qa\camheight.txt` runs it on ten stages.

The map is the source of items
* Items no longer drop from killed enemies. Chests, shrines, multishops, broken drones, printers, lunar pods and the
  rest are placed on the stages as in the stock game again, and the army's purse pays for them (gold; lunar coins come
  from the player). Bosses and the teleporter give items as before.
* A right click on such an object sends ONE hero: the nearest of the selected ones by the walk along the ground (not as
  the crow flies), or of the whole army when nobody is selected. The others carry on. Afterwards the hero takes up what
  it was doing (walks back to its hold spot, goes on patrolling ...) or returns to its post.
* Shift + right click on several objects: one hero's route through all of them, drawn on the ground.
* The item flies out of the chest as in the stock game and joins the pool where it lands (the loot feed shows it).
* Price tags over the priced objects in view (gold when the purse can pay, red when not); the pointer badge says what
  a click does and what it costs ("Open chest  $25"). Not enough gold: the click is refused with a message, nobody walks.
* The minimap marks found objects (chests, shops, shrines, drones, printers, portals, the teleporter); an object is
  found when a hero comes near it or the camera looks at it. The top strip counts the stage's chests (opened / all).
* Idle heroes open barrels (free gold) near their post by themselves (`Loot/AutoOpenBarrels`).
* Idle heroes also open a chest near their post (within 30 m of it, 35 m of the hero) by themselves when no enemy is
  near, they have not been hit lately, there is no zone to hold, and the purse can pay; the gold other heroes are
  already walking to spend is kept for them, and a chest that turns out too dear on arrival is left without a message
  (`Loot/AutoOpenChests`, `hc_cfg autochests`). Only chests: shops, shrines, printers and drones wait for an order.
* A hero that cannot get to an object or pay at the last moment comes back and says why.
* Config: `Loot/ItemDropsEnabled` and `Loot/RemovePaidInteractables` now default to false; an existing config file is
  switched once (`Dev/ConfigVersion` = 2). Setting them back gives the old way.

Audit of the interface rework (fixes)
* A click beside the open "Orders" list only closes it (it used to deselect the army or send it somewhere).
* The pointer badge and the right click ask one shared function: a selected hero under the pointer showed "Select"
  while the click moved the army.
* Camera: a ridge or a tree between the camera and the focus first tilts the view further down (up to 30 degrees);
  ground right behind the focus no longer pulls the camera down onto it. Stacked surfaces of one mesh (a ramp over a
  passage) are found now.
* The top strip's tooltip text is no longer rebuilt every frame.
* Help sheet (F1): both columns fit inside the panel (the right one ran past its edge), short key names ("СКМ вбок / [ ]",
  "PgUp PgDn"), a line for the right click on a chest; the sheet shrinks on a big interface scale.
* Commander ability buttons: a smaller picture, the names ("Удар с орбиты", "Боевой клич") read on two lines.

Dev
* `hc_find <kind:Chest|name>`, `hc_click_found [button] [shift] [hover]`, `hc_move_found <hero>`, `hc_loot`,
  `hc_invulnerable`, `hc_gold set <n>`, `hc_cfg autobarrels`; scenario `tools\qa\loot.txt`.
* `hc_mouse_hero` / `hc_mouse_enemy` keep the pointer on the moving unit (the cursor tests were flaky: units walked
  out from under a fixed pixel).

Interface
* A "Cancel" button at the right end of the command card's title: while a target is being chosen it stops choosing
  (like Esc); otherwise the selected heroes drop their orders (and the queue) and stand where they are (like Stop).
* An "Items" button in the top strip, left of the purse: how many items the army has; a click opens the item panel
  (the same as I), and it is framed while the panel is open.
* The strip of selected-hero cards at the bottom centre is gone (the roster on the left already marks the selected
  heroes, and the command card's title names them); the commander's abilities moved into its place, so the bottom
  right holds only the orders card.
* The red "all heroes have fallen" strip is clickable: a click revives the first fallen hero (the text says so).
* The teleporter strip no longer claims "Teleporter ready" while the teleporter has not been started: "Teleporter found:
  click or press [key] to start".
* Roster cards show the health of a living hero as numbers (current/max) above the bar; hidden for a fallen hero and on
  the compact cards (15+ heroes).
* The selection box is a faint tint with a hairline border and corner brackets (was a flat green rectangle). Heroes the
  box would take light up while it is dragged.
* A selected hero no longer disappears under a green circle: only the front arc of a thin ring lies on the ground in
  front of its feet, and a small triangle sits above its health bar. Hover marks of heroes and enemies are arcs too.
* Camera height: the camera no longer dives into a chasm when panned over it (it keeps the level of the edge; only a
  long stay over the void takes it down), it stays on its own level where a ramp lies over a passage or a bridge over a
  pit (the nearest surface to the current level is taken, not the first one below a point 14 m up), and its height moves
  smoothly at a limited speed. The pull-in in front of walls and ramps is smoothed too (quick in, slow out).
  Flying to a hero, an alert or a minimap click still goes straight to the height of the target.

* One look for the whole interface: panels, cards and buttons have two cut corners and a thin steel outline, the big
  panels carry a short orange tick, the titles of the command card have an orange bar and a hairline.
* Command card, made short (about 160 px instead of 290): the selected hero's skills, one "Orders" button that opens a
  list (attack-move, focus, stop, hold, retreat, patrol, rally point, formation: each with a picture, its name and the
  hotkey; a click on a row or Esc closes it), a three-way stance switch and, above, the commander's abilities. The
  teleporter button is gone: the strip at the top is clickable and the hotkey stays. The hotkey sits on a small key cap,
  the cooldown is a radial sweep with the seconds in the middle. The pictures are drawn in code.
* The pointer shows what a right click would do: a small badge with a picture and a word next to it - Move, Attack,
  Follow, Use, Select ("(queued)" with Shift). While a targeting mode waits for a click it shows that mode.
* A line runs from every selected hero (the first six) to what is under the pointer: dashes that flow towards the
  target on a soft glow, an arrowhead and a breathing ring, in the colour of the action. It shows only over an enemy,
  a friend or an object, or over bare ground while Shift is held (planning a queue); over plain ground the badge alone
  is shown. Slanted lines are drawn with soft edges now, so they no longer break into stair steps.
* The game's own "remote operation" drone picker (a big dark panel that a stray key of the spectator controls opened in
  the middle of the screen) can no longer appear while the mode runs.
* Camera tilt: the middle mouse button already turned the view sideways, now dragging it up / down tilts it (up = flatter,
  down = more from above; 25-85 degrees), also PageUp / PageDown (`Keys/TiltUp`, `Keys/TiltDown`), speed
  `Camera/TiltSpeed`, direction `Camera/InvertTilt`. `Camera/Pitch` is the starting tilt. The minimap's view box and the
  marks on the ground follow the tilt. The screen edge no longer pans the camera while the middle button is held.
* The top strip is three panels of one height with pictures: stage (name, clock, difficulty), purse with the income,
  army (alive / total, limit), the next hero's price (green when affordable) with the barracks key, and the
  teleporter with its own picture and bar. The teleporter texts are shorter so that they are not shrunk to tiny type.
* Minimap: a proper frame (cut corners, clipped contents), the picture is now a soft relief of the walkable ground
  (colour by height, haze, light edge, faint grid) instead of a cloud of dots, the stage name and "N" in the corners,
  dots have a dark rim, selected heroes get a ring, the camera box is a view finder (tint, hairlines, corner brackets).
* The red defeat strip and the toast messages sit on a dark plate; toasts are above an open barracks / item panel.
* Selected-hero cards: the level is a line above the portrait instead of lying over it.
* The Void Fiend's name is printed properly ("Исчадие Пустоты"): the game's own Russian text for it is damaged.

Dev
* `hc_cam sweep <dx> <dz> <seconds>` pans along a line through the keyboard pan's code and logs the heights (with what
  the old code would have picked); `hc_drag ... hold` keeps the mouse button down until `hc_input_end`.
* Scenarios `tools\qa\camera.txt`, `tools\qa\select.txt`, `tools\qa\uishot.txt` (a screenshot per interface state) and
  `tools\qa\cursor.txt` (pointer badge, preview line, orders list) and `tools\qa\tilt.txt` (camera tilt; `hc_drag ... mid`
  drags with the middle button, `hc_cam info` prints the pitch).
* `hc_mouse <x> <y> [shift]`, `hc_mouse_hero <id>`, `hc_mouse_enemy` move the synthetic pointer without clicking;
  `hc_cursor` prints what the badge shows.
* `tools\glyph-preview` renders every command-card picture into one PNG without the game
  (`dotnet run --project tools\glyph-preview -- <file.png>`).

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
