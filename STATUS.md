# HighCommand — статус по этапам

Обновляется после каждого этапа. «Проверено» = проверено в игре скриптом (`tools\run-game.ps1` + `HighCommand.autoexec.txt`)
или глазами; «не проверено» = собрано, но в игре не смотрели.

| Этап | Состояние | Как проверить |
|---|---|---|
| M0 каркас | ✅ проверено | лог `HighCommand 0.1.0 loaded`, консоль `hc_ping` |
| M1 режим | ✅ проверено скриптом (меню → кнопка → лобби → забег `HighCommandRun(Clone)`) | Главное меню → «Дополнительные режимы» → «Верховное командование»; либо консоль `hc_start` |
| M2 стартовый герой под ИИ | ✅ проверено скриптом: герой из лоадаута, капсула открывается, дерётся, переходит между стадиями | консоль `hc_status`, `hc_next_stage` |
| M3 камера/ввод/мини-карта | в работе | |
| M4–M13 | не начаты | |

## Известные факты об игре (проверено)
* Игра 1.5.0, Unity 2021.3.45. Каталог режимов: ClassicRun, EclipseRun, WeeklyRun, BaseDefenseRun, InfiniteTowerRun + наш.
* У всех 21 ванильного выжившего есть `…MonsterMaster` с ИИ (`MasterCatalog.FindAiMasterIndexForBody`).
* `[ConCommand]` в моде находится только если сборка помечена `[assembly: SearchableAttribute.OptIn]`.
  Флаг `ExecuteOnServer` пересылает команду серверу — в главном меню сервера нет, команда молча пропадает.
* `Run` и `RunArtifactManager` требуют друг друга (RequireComponent) — компонент `Run` заменить нельзя, поэтому
  префаб режима = клон ClassicRun + наш маркер `HighCommandRun`.
* `Stage.RespawnAllNPCs` сам возрождает мастера с `SetDontDestroyOnLoad` у точки старта на новой стадии.
* Капсула: ИИ не жмёт «interact», поэтому выход из капсулы запрашиваем сами (`Interactor.AttemptInteraction(seat)`).

## Команды консоли (Ctrl+Alt+~)
`hc_ping` · `hc_start` · `hc_status` · `hc_next_stage` · `hc_kill_enemies` · `hc_dump_survivors` ·
`hc_dump_gamemodes` · `hc_dump_run` · `hc_dump_ui <имя> [глубина]` · `hc_menu_extra [click]`

## Разработка
* Сборка и установка: `.\tools\build.ps1` (игра должна быть закрыта).
* Скриптовый прогон: положить сценарий в `BepInEx\config\HighCommand.autoexec.txt` и `.\tools\run-game.ps1`.
  Директивы: `@wait n`, `@scene имя`, `@run`, `@norun`, `@log текст`, `@quit`; остальные строки — консольные команды.
