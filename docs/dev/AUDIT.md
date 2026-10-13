# AUDIT — QA of High Command 0.9.0 (offline, single player)

Scope: single-player only. Network code (host/client, sync, NetworkBehaviour logic) was NOT touched or checked.
Rollback point: git tag `pre-qa-0.9.0`.

## Stage 1 — code audit

Method: every source file read; build with ALL warning suppressions removed (only CS0436 stays: type-forward noise
from the game's own assemblies); BepInEx manifest / dependencies compared with the code.

| # | Found | Where | Fix |
|---|-------|-------|-----|
| A1 | The camera override object stayed registered on the stock camera rigs when the mode's camera was destroyed (the rig would call a dead provider) | `Client/RtsCamera.cs` OnDisable | `ReleaseRigs()` hands every rig back (`SetOverrideCam(null)`) |
| A3 | Camera obstruction sphere-cast buffer of 24 hits could miss the nearest wall on busy maps (NonAlloc results are unsorted) | `Client/RtsCamera.cs` | buffer 24 -> 64 |
| B2 | Dead statement: "Esc keeps the selection" did nothing | `Client/SelectionController.cs` | removed |
| C5 | Help sheet (F1) had ~30 lines in a 450 px column: text overflowed the panel | `UI/HelpOverlay.cs` | rewritten: two columns, one row object per line, auto-shrink, panel height computed |
| N1 | A manual cast cut short by another order left its press timer behind; the next cast skipped the 0.2 s aim wait | `Orders/HeroAIState.cs` | timer and frame counter reset when the mode is not Cast |
| N2 | `?.` used on Unity objects (bypasses Unity's destroyed-object check) | `Army/LootService.cs`, `Mode/HighCommandRun.cs` | explicit `if (obj)` |
| W1 | CS0628 `protected` member in a sealed class | `Mode/HcState.cs` InvokeRpcItemFeed | `private` (the delegate is registered in the static ctor; behaviour unchanged) |
| W2 | CS0618 obsolete `GlobalEventManager.onCharacterDeathGlobal` (x2) | `Army/RunStats.cs` | `onCharacterDeathServer` (system-level "something died" event) |
| W3 | CS0618 obsolete `SliderConfig.formatString` | `Core/OptionsIntegration.cs` | `FormatString` |
| W4 | CS0618 obsolete `TeleportHelper.TeleportBody(body, pos)` | `Orders/HeroAIState.cs` | 3-argument overload, same behaviour |
| W5 | CS0649 never-assigned field `selectedMark` | `UI/RosterPanel.cs` | removed |
| W6 | Unused helper `PreviousPress` | `Orders/HeroAIState.cs` | removed |
| M1 | `NoWarn` hid 5 warning classes | `HighCommand.csproj` | only CS0436 left; build = 0 warnings, 0 errors |

Checked and found correct: every `+=` event subscription has its `-=` (RunStats, LootFeed, HeroAIState,
HighCommandRun, ClientRoot, RunFlow); manifest (name, version 0.9.0 = `Plugin.Version`, dependency
RoR2BepInExPack, icon) and the soft dependency on RiskOfOptions; each Harmony patch class is applied separately and a
failure is logged, not fatal.

Noted for Stage 3 (allocations / per-frame work, not bugs): RosterPanel copies and sorts the hero list every frame;
CommandCard / TopBar rebuild strings every frame; HeroBrain builds debug strings and a TeamIndex array on every
decision; RunFlow does GetComponent per hero per tick; `Run.participatingPlayerCount` postfix reads the lobby rule
on every call.

## Stage 2 — live single-player test (scripts in `tools\qa\`, run with `tools\qa-run.ps1`)

| Scenario | Result |
|----------|--------|
| Menu load, game mode registered, lobby opens | OK |
| Run start, drop pod, commander without body, RTS camera takes the stock rig | OK |
| Selection: click, box, Ctrl+1 / 1 groups, F1 help | OK |
| Orders: move, attack-move, hold, stop, retreat, stance, Shift queue, manual cast, Alt+Z auto toggle | OK (`hc_status`) |
| Recruit up to the army limit, gold and price growth | OK |
| Stage change (`hc_next_stage`): army follows, rally point, new map picture | OK |
| Hero death and revival for gold | OK |
| Teleporter: nearest hero activates, army gathers | OK up to the walk (a full charge was verified in M9) |
| Total defeat: countdown, `BeginGameOver`, army statistics on the end screen | OK (kills 80, deaths 6, gold 196, purchases 5 shown) |
| Restart a run in the same session, exit to menu | OK |

Errors left in the log are the game's / other mods' own (verified with a vanilla ClassicRun in the same session):
`A connection has already been set as ready`, `GenericPool: ReleasePools called on non-empty pool`,
`TeamManager.SetTeamExperience NullReferenceException` (second run in a session), `MusicController.ScheduleIntensityCalculation`
(vanilla run without a player body), `Unable to set gate state for village`, `Cannot set the parent of 'Sparks, Trail'`
(Artificer's lightning bomb ghost). No exception or error comes from HighCommand code.

Fixed in this stage:

| # | Found | Fix |
|---|-------|-----|
| S2-1 | `ActivityManager::CurrentGameMode property unable to determine GameMode` logged at every run start (platform activities know only stock modes) | `RunPatches.ActivityGameMode` prefix returns the stock fallback quietly for a High Command run |
| S2-2 | A manual cast reported "not ready" while its own button was still held (the stock is spent on the first frame) | the readiness check is made at the start of the cast only |
| S2-3 | Help sheet bled the command card through (translucent) | opaque panel |
| S2-4 | `hc_cfg` test values were saved to the .cfg on exit and survived into the next test | `qa-run.ps1` resets the config before every scenario |

## Stage 3 — optimisation (6 heroes, 0-6 enemies, 144 fps cap lifted with `fps_max -1`)

Measured with `tools\qa\perf.txt` (`hc_perf` counters; `hc_ui off` hides the whole interface).

| | Before | After |
|-|--------|-------|
| Interface CPU per second (`ui`) | 43.5 ms | 19.9 ms |
| Frame time, interface shown | 6.01 ms (166 fps) | 5.51 ms (182 fps) |
| Cost of drawing the interface (shown vs hidden) | +1.0 ms / frame | within noise |
| Gen-0 garbage collections in 48 s | 4 | 2 |
| Roster panel | 9.0 ms/s | 2.0 ms/s |
| Command card | 13.6 ms/s | 6.3 ms/s |
| Top bar | 7.2 ms/s | 1.3 ms/s |

Changes: world overlay on its own nested canvas and bars written only when their picture changes; roster cards, command
card, top bar, selection strip, loot feed, tooltip and minimap view box write texts / rects only when the value changed
(`UiKit.SetText`, per-card caches); skill icon / name / description looked up when the skill changes (was: a regex and two
language lookups per button per frame); no per-frame list copy and sort; hover picking rejects far bodies with one
projection; `HeroBrain` caches `BaseAI`, uses constant debug strings and a static team array; the hurt-hero scan runs only
for a hero that is hurt; `Perception` tests the distance before the costly targetability checks; the brain is cached on
`Hero` (no `GetComponent` per order / tick).
With 60 extra enemies the frame time is dominated by the game itself (57-100 fps at 24-60 enemies); the mod's share there is
about 20 ms per second in total.
