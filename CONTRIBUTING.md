# Contributing to High Command

Thank you for looking! Every kind of help is welcome: bug reports, ideas, balance notes, AI profiles for survivors,
translations, code. Criticism is welcome too. If something about the mode feels wrong, say so.

## Reporting a bug

Open an [issue](https://github.com/kantemiir/RoR2-HighCommand/issues/new/choose) with the *Bug report* template and include:
* what you did, what you expected and what happened;
* the mod version (the first line in the log says `HighCommand x.y.z loaded`);
* `BepInEx\LogOutput.log` from that session (drag the file into the issue);
* other mods you run, if any.

A screenshot or a short clip helps a lot with anything visual (camera, interface).

## Suggesting an idea

Use the *Idea / suggestion* template, or start a thread in
[Discussions](https://github.com/kantemiir/RoR2-HighCommand/discussions). Ideas from other strategy games are welcome:
"in game X this works like Y, and it would fit here because Z" is the perfect format.

## Working on the code

* **Requirements:** Windows, Risk of Rain 2 (Steam) with BepInEx 5, and the .NET SDK (any recent version that can build
  `netstandard2.1`).
* **Build:** `tools\build.ps1` builds `HighCommand.dll` and copies it into the game's plugin folder (close the game first).
  The project finds the game in the default Steam folder. If yours is elsewhere, pass it:
  `dotnet build -c Release -p:RoR2Dir="D:\Games\Risk of Rain 2"`.
* **Test:** `tools\qa-run.ps1 <scenario>` starts the game, plays `tools\qa\<scenario>.txt` and collects the log. Scenarios
  use `hc_*` console commands for synthetic mouse and keys, screenshots and checks. Look at `tools\qa\full.txt` first.
* **Where things live:** `src\Mode` (the game mode and run flow), `src\Army` (heroes, economy, loot), `src\Orders` (orders
  and the heroes' AI), `src\Client` (camera, input, selection), `src\UI` (interface, drawn in code), `src\Dev` (console
  commands and the test runner). Design notes and history: `docs\dev\`, `CHANGELOG.md`.
* **Style:** match the code around you. Comments explain *why*. Every visible text goes through `Core\Strings.cs`
  (English and Russian).
* **Single player:** new features are not replicated over the network for now. Please do not rely on multiplayer.

Small, focused pull requests are the easiest to review. If you plan something big, open an issue first so we can talk
it through.
