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
