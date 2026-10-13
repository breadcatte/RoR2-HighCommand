# CLAUDE.md — High Command (RTS-режим для Risk of Rain 2)

Рабочая памятка для сессий Claude. Обновлять по ходу работы. Отвечать пользователю по-русски, коротко.

## Что это
BepInEx-плагин `kantemiir.HighCommand`: отдельный RTS-режим поверх нетронутой игры. Бестелесный командир с камерой
сверху, армия = ванильные выжившие под ИИ, покупка героев за золото, приказы группам, предметы с врагов сразу в общий пул.
**Только одиночная игра.** Решение пользователя (2026-10-12): кооп не нужен, время на сеть не тратить — ни на
проверку, ни на репликацию новых данных. Новые фичи делаются для хоста напрямую (UI может читать серверные объекты:
`Hero.Brain`, `ArmyUpgrades.Levels`). Существующий сетевой код (`Net\CommandChannel`, сериализация `HcState`) не
удалять и не развивать.

## Пути и инструменты
| Что | Где |
|---|---|
| Репозиторий | `C:\Users\user\Documents\RoR2Strategy` (ветка `master`) |
| Git (не в PATH) | `_tools\mingit\cmd\git.exe` |
| Игра | `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2` (RoR2 1.5.0, Unity 2021.3.45) |
| Установленный мод | `<игра>\BepInEx\plugins\local-HighCommand\` (DLL + `profiles\hero_profiles.json`) |
| Лог BepInEx | `<игра>\BepInEx\LogOutput.log` (без временных меток, пишется лениво — читать после закрытия игры) |
| Лог Unity | `%USERPROFILE%\AppData\LocalLow\Hopoo Games, LLC\Risk of Rain 2\Player.log` (+ `Player-prev.log`) |
| Конфиг мода | `<игра>\BepInEx\config\kantemiir.HighCommand.cfg` (пользовательские клавиши! не стирать без бэкапа) |
| Декомпиляция игры | `_ref\` (gitignored), декомпилятор `_tools\ilspycmd.exe` |
| .NET SDK | `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` (target `netstandard2.1`) |

## Команды
```powershell
.\tools\build.ps1              # сборка + установка DLL (игра должна быть закрыта)
.\tools\build.ps1 -NoInstall   # только сборка
.\tools\package.ps1            # zip в dist\ (Plugin.Version == package\manifest.json version_number)
.\tools\qa-run.ps1 <smoke|full|menu|ui|alerts|perf> [-NoBuild]   # сценарий в игре без человека
```
* Сценарии: `tools\qa\*.txt` (консольные команды + `@wait n`, `@scene x`, `@run`, `@norun`, `@log`, `@quit`).
  Исполняются через `BepInEx\config\HighCommand.autoexec.txt` (если файл остался — выполнится при следующем запуске!).
* `qa-run.ps1` **удаляет cfg пользователя** перед прогоном — сохранять и возвращать его (см. CHANGELOG/задачи).
* Консоль в игре: `Ctrl+Alt+~`; `hc_status`, `hc_flow`, `hc_perf`, `hc_cfg verbose true` и др. (`src\Dev\DevCommands.cs`).
* Проверка UI/ввода — только «настоящим путём» (клик/клавиша через `hc_click`/`hc_key`/`hc_menu_extra real`),
  а не прямым вызовом метода: так уже пропустили неработающую кнопку меню.

## Оформление интерфейса (0.10.x)
* Единый стиль живёт в `src\UI\UiKit.cs`: `NewPanel` (скошенные углы + оранжевые метки), `Shape`/`StyleOutline`/`NewFill`
  (рамка и заливка со скошенными углами), `Dot`, `GlyphSprite`. Новые панели и карточки строить через них, а не
  через `UiKit.White` с рамкой 2 px.
* Картинки кнопок приказов рисуются кодом в `src\UI\GlyphArt.cs` (без файлов). Посмотреть без игры:
  `dotnet run --project tools\glyph-preview -- <файл.png>` (SDK: `%LOCALAPPDATA%\dotnet-sdk`).
* Вид интерфейса проверять сценарием `uishot` (`qa-run.ps1 uishot`) и скриншотами; «до/после» — `docs\ui-rework\`.
* Высота камеры (`RtsCamera.FocusHeight`) берётся только с земли, где есть узлы графа героев (`MapInfo.WalkableHeightsNear`);
  стоящая камера над более низкой землёй оседает через 1.5 с (`Settle`). Проверка — `tools\qa\camheight.txt`
  (`hc_cam audit`, ждать «0 climbed», «0 of N stops left hanging»; сценарий длинный — `qa-run -TimeoutMin 30`).
  Решения пользователя (2026-10-13), не возвращать: камера САМА НЕ ПРИБЛИЖАЕТСЯ (расстояние — только колёсико) и сама
  не наклоняется, чтобы заглянуть за холм; наклон до 4° («положить камеру к земле»), подъём только чтобы не уйти под
  землю (`GroundLift`/`InGround`). Мелкий кусок уровня (≤60 м) скрывается `forceRenderingOff` ТОЛЬКО если закрывает
  героя на экране (`CollectHeroCovers`/`HideOccluders`); пол под героем, большая земля и объекты игры — никогда.
  Проверка — `camvillage.txt` («0 still in the ground»). По образцу RTS (2026-10-13): зум к курсору (`ZoomToCursor`),
  опускание вида при приближении (`ZoomTilt`, `ViewPitch`), Home — сброс вида, двойной Пробел — слежение,
  силуэт героя за большой землёй (`IsBehindGround` → `WorldOverlay.DrawGhosts`).
* Картинка миникарты строится в `MapInfo.BuildTexture` (покрытие + рельеф), рамка камеры и точки — в `UI\Minimap.cs`.

## Структура `src\`
* `Plugin.cs` — вход: ContentPack, Harmony-патчи по одному (`TryPatch`), Risk of Options, AutoExec.
* `Mode\` — режим: `HighCommandRun` (маркер + серверный старт армии), `HcState` (реплицируемое состояние),
  `RunFlow` (тик 0.25 с: поражение, телепорт, ростер), `RunPatches` (патчи под бестелесного командира),
  `RunPrefabBuilder` (клон ClassicRun), `LobbyRules`/`LobbyPatches`/`MenuButton`.
* `Army\` — сервер: `ArmyManager` (найм через MasterSummon, подмена ИИ), `ArmyService` (найм/воскрешение/предметы/телепорт),
  `Economy`, `LootService`, `ItemDistributor`, `CommanderAbilities`, `RunStats`.
* `Orders\` — логика приказов и ИИ без знания UI: `HeroBrain` (решения, тик 0.12 с), `HeroAIState` (ввод тела, 0.1 с),
  `Perception`, `Formation`, `HeroProfile` (+ `package\profiles\hero_profiles.json`), `Order`/`OrderService`.
* `Client\` — камера (`RtsCamera`), единый шлюз ввода (`ClientInput`), выделение, приказы с ввода, оповещения.
* `UI\` — панели uGUI, строятся кодом. `Net\CommandChannel` — клиент→сервер (заморожено, не проверено).
* `Core\` — настройки, локализация (ru/en без R2API), MiniJson. `Dev\` — консольные команды, AutoExec, Perf.

## Ключевые решения (не менять без причины)
* Run-префаб = клон `ClassicRun` + `HighCommandRun` + `HcState` (Run/RunArtifactManager нельзя заменить).
* Герои = ИИ-мастера ванильных выживших; ИИ-состояние подменено на `HeroAIState`, решения в `HeroBrain`.
* Все патчи проверяют `HighCommandRun.IsActive` → обычные забеги не затронуты. Ванильную игру не ломать.
* Логика приказов/армии/экономики не знает про UI (не вызывает панели напрямую, а поднимает события); ввод — только
  через `ClientInput`. UI в соло-режиме может читать серверное состояние.

## Правила работы
* Сначала план и оценка, крупные изменения — только после подтверждения пользователя. Мелкие очевидные баги — сразу.
* Одна проблема за раз: правка → сборка → проверка в игре (сценарий или скриншот). Не говорить «работает» без проверки.
* Перед прогонами сохранять `kantemiir.HighCommand.cfg` и возвращать после; после тестов проверить, что
  `HighCommand.autoexec.txt` не остался в `config`.
* Вести `CHANGELOG.md`; коммит на каждую законченную правку с трейлером `Co-Authored-By`.
* Пользователь не программист: сам запускает игру и оценивает; ответы короткие, по-русски.

## Распределение работы Opus / Sonnet (действует всегда)
Opus дороже. По каждой задаче заранее решать исполнителя:
* **Opus (себе):** корневые причины сложных багов, архитектура, изменения на стыке систем (камера, ввод, ИИ героев,
  экономика, UI), дизайн механик, всё дорогое в ошибке или трудно откатываемое. Сомневаешься — бери себе и поясни.
* **Sonnet 5.5:** простое и чётко очерченное: константы/значения, переименования, косметика UI, тексты и подсказки,
  однотипные правки по шаблону, логи, мелкий рефакторинг без смены поведения, документация, правки скриптов `tools\`.
* Для каждой задачи Sonnet — самодостаточный промт: что сделать, в каких файлах, как проверить, чего не трогать.
* В конце каждого ответа с работой — блок «Для Sonnet» (или «нет»).
* Результат Sonnet проверять критично (diff + сборка + прогон), прежде чем закрывать задачу.

## Лут с карты (с 2026-10-13)
* Предметы — из сундуков/святынь/дронов на карте (ванильная расстановка), платит общая казна: префиксы на
  `CostTypeDef.IsAffordable/PayCost` для героя-покупателя (`Army\Interactables.cs`, класс `Purse`). Убийства лут не дают
  (`Loot/ItemDropsEnabled`=false, миграция `Dev/ConfigVersion`).
* ПКМ по объекту → `OrderService.SendRunner`: один герой (путь по графу `Orders\PathCost`), `HeroBrain.Errand` ставит
  поход перед собственным приказом героя и потом возвращает его (Hold → Move+Hold). Телепорт — старый путь.
* Предмет летит дроплетом и попадает в пул при приземлении (`WorldPickups`); не приземлившийся — в `LostDroplets`.
* В этой версии игры у сундуков есть `PickupPickerController`: «неподдерживаемым» считать только пикер без цены.
* Что что-то видно: `Client\Scouting` (метки миникарты), ценники — `WorldOverlay.DrawPriceTags`.
* Сами (в покое, `HeroBrain.ThinkIdle`): бочки (`TryFetchBarrel`) и ТОЛЬКО сундуки (`TryFetchChest`, если хватает
  денег с учётом чужих походов) — решение пользователя; магазины/святыни/принтеры/дроны — только по приказу.
* Проверка: `qa-run.ps1 loot` (+ `hc_loot`, `hc_find kind:Chest`, `hc_click_found`).

## Известное состояние (обновлять)
* 0.10.0 (2026-10-12): этап 3 частично — улучшения армии и ранги (`Army\ArmyUpgrades.cs`, бонусы постфиксом на
  `CharacterBody.RecalculateStats`), 2 стартовых героя, цены по «соло»-сложности (`HcState.SoloDifficulty`), маршрут
  очереди. Проверка без приказов: 3/3 забега оба героя живы 165 с. Не сделано: события на карте (спасение героя).
* 0.9.2: этапы 1–2 пройдены. HANDOFF.md писал Sonnet — местами неточен; верить коду и логам.
* Корень «герой сам умирает»: ванильные AISkillDriver выживших (`_ref\ai_drivers.txt`) всегда сближаются с целью
  (стиль Умбры). Исправлено полями профиля `standoff`/`fleeSlot` + автовоскрешение. Одинокий герой всё ещё хрупок —
  это баланс/ИИ (этап 3), а не баг.
* Сцены без союзного ИИ (`SceneDef.suppressNpcEntry`: arena, bazaar, sad1, soul1, surv2, катсцены) — мод сам высаживает
  героев (`RunFlow.ArriveOnStage`). У Базара сетка-заглушка из 6 узлов: путь не ищется (`HeroAIState.DropStubNodeGraph`).
* Проверки в игре удобно делать сценариями с `hc_status` каждые N секунд; `hc_next_stage <scene>` для конкретной карты.
* Профили героев: недостающие в JSON поля берутся из встроенного профиля выжившего (`ProfileStore.BuiltIn`).
* Шум в логе, который НЕ наш: «connection already set as ready», «GenericPool ReleasePools», «Unable to set gate state»,
  «Failed to confine the cursor» (окно без фокуса при автопрогоне), AkGameObjManager Scene Camera, «multiple NetworkIdentity».
