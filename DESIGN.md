# HighCommand — режим «Верховное командование» для Risk of Rain 2

Рабочее название режима: **High Command / «Верховное командование»** (внутреннее имя Run-префаба: `HighCommandRun`).
Идея: отдельный стратегический режим поверх нетронутого RoR2. Игрок — бестелесный командир с RTS-камерой.
Армия — настоящие выжившие игры под ИИ; стартовый герой выбирается в лобби, остальных покупаем за золото.

Этот файл — архитектура **как она реализована**. Всё, что здесь названо «проверено», сверено с декомпилированным `RoR2.dll`
(игра Unity 2021.3.45, BepInEx 5.4.21; справочная декомпиляция в `_ref/`, она в `.gitignore` и в мод не копируется).
Состояние этапов и список ограничений — в `STATUS.md`.

## 1. Окружение

| Что | Значение |
|---|---|
| Игра | `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2`, AppID 632360 |
| Загрузчик | BepInEx 5.4.21 + RoR2BepInExPack; RiskOfOptions — мягкая зависимость (окно настроек) |
| R2API | не нужен: свой ContentPack + Harmony + свои UNET-сообщения |
| Сборка | .NET SDK 8, target `netstandard2.1`, ссылки на `Managed\*.dll`; `tools\build.ps1`, `tools\package.ps1` |
| Инструменты | `_tools\ilspycmd.exe` (декомпилятор), `_tools\mingit` (git) |

## 2. Архитектура

### 2.1 Режим
* Префаб = клон `RoR2/Base/ClassicRun/ClassicRun.prefab` (Addressables) с нашими компонентами `HighCommandRun` (маркер +
  серверная логика) и `HcState` (реплицируемое состояние). Заменить сам `Run` нельзя (`Run` и `RunArtifactManager` требуют
  друг друга). Новый `NetworkIdentity.assetId` (MD5 имени). Регистрация: `ContentPack.gameModePrefabs`.
* Лобби: Harmony-префиксы добавляют токен режима в списки меню; кнопка в «Дополнительных режимах»; консоль `hc_start`.
* **Правила лобби** (`Mode/LobbyRules.cs`): транспилер на `RuleCatalog.Init` вставляет вызов после последнего штатного
  `AddRule`; категория «Верховное командование» (стартовое золото, цена героев, предел армии, сложность за героя, раздача
  предметов, частота дропа). Первый выбор каждого правила — «из настроек» (значение `.cfg`).

### 2.2 Командир, армия, ход забега
* **Командир** = обычный мастер игрока без тела (`Stage.RespawnCharacter` для него пропускается). Его инвентарь — общий пул
  предметов армии; лобби, лоадаут, камера, отчёт о забеге работают штатно.
* **Герои** = AI-мастера (`MasterSummon` из `…MonsterMaster` выжившего; запасной вариант — мастер Коммандо + подмена тела),
  `SetDontDestroyOnLoad` → ванильный `Stage.RespawnAllNPCs` возрождает их (в том числе павших) на каждой стадии.
* **`RunFlow`** (сервер): поражение (никто не жив и золота на воскрешение нет / таймер на воскрешение вышел →
  `Run.BeginGameOver(StandardLoss)`; ванильная проверка не срабатывает без «заспавненного игрока»), состояние телепорта и
  зоны зарядки, сбор армии у новой точки сбора, копирование приказов/стоек/перезарядок героев в реплицируемый ростер.
* **Телепорт**: `HoldoutZoneController` считает только `isPlayerControlled` — патчим подсчёт на живых героев; кнопка
  «Телепорт» (`H`) = ближайший герой активирует / армия собирается в зоне / уход на следующую стадию.
* **Воскрешение** за золото (`CharacterMaster.Respawn(..., wasRevivedMidStage: true)`), бесплатное — при смене стадии.
* Сложность: `Run.participatingPlayerCount` (getter-патч) = 1 + (героев−1)·`ThreatPerExtraHero`;
  `GetPlayersWithBodiesCount` = живые герои (масштабирует HP врагов, деньги и т. п.).

### 2.3 ИИ героев
* `HeroBrain` (сервер, MonoBehaviour на мастере) — решения раз в 0.12 с: очередь приказов, стойка, поиск цели
  (`Perception`), автоотступление раненого, удержание зоны телепорта. `HeroAIState : BaseAIState` — исполнитель на уровне
  тела: пишет в `InputBankTest` как игрок; какой навык нажать, решают штатные `AISkillDriver` выжившего, поверх них —
  правила профиля (`auto / never / escape / engage / burst`) и переключатели автоприменения.
* **Профили** (`Orders/HeroProfile.cs`): `profiles\hero_profiles.json` рядом с DLL; дальности, пороги отступления,
  правила по слотам; `"*"` — запасной профиль для любых выживших (в том числе модовых). Свой `MiniJson`
  (Unity `JsonUtility` не знает типов модовой сборки).
* Приказы: Move / AttackMove / Attack (Focus) / Stop / Hold / Follow / Retreat / Patrol / Interact / Cast, очередь, стойки,
  построения (`Formation`), точка сбора.

### 2.4 Экономика и лут
* Золото: перехват `TeamManager.GiveTeamMoney` / `CharacterMaster.GiveMoney` → общий кошелёк (`Economy`, `HcState.Gold`).
* Предметы: `DeathRewards.OnKilledServer` постфикс → шанс по золоту за врага, сложности, элите/боссу; предмет сразу в пул
  командира; перехват `PickupDropletController.CreatePickupDroplet` / `GenericPickupController.CreatePickup` направляет
  все прочие дропы (боссы, шрайны) туда же. Платные интерактивы не ставятся (`DirectorCore.TrySpawnObject`).
* `ItemDistributor`: каждая новая единица — герою с лучшим счётом (роль × теги предмета − число уже имеющихся);
  Split / Mirror; новичок забирает долю; ручные переносы и «раздать заново»; снаряжение через `HeroEquipment`.

### 2.5 Камера и UI
* RTS-камера — `ICameraStateProvider` через `CameraRigController.SetOverrideCam` (ванильные постобработка/туман остаются),
  ванильный HUD скрыт (`IsHudAllowed=false`), аудио-слушатель переносится на землю под фокус.
* UI — uGUI/TMP, строится кодом на одном overlay-канвасе, спрайты процедурные, шрифты игры (`HGTextMeshProUGUI`); свой
  hit-testing (`ClientInput`), поддержка синтетического ввода для тестов (`hc_click`, `hc_drag`, `hc_key`).
* Панели: `TopBar` (золото, армия, телепорт, баннер поражения), `RosterPanel`, `CommandCard` (приказы, стойки, умения героя,
  способности командира), `SelectionPanel`, `BarracksPanel`, `InventoryPanel`, `LootFeed`, `Minimap`, `HelpOverlay`,
  `Tooltip`, `Toast`; ru/en через токены `Language` (`Core/Strings.cs`).

### 2.6 Сеть
* Реплицируется `HcState` (ручная `OnSerialize`/`OnDeserialize` и ручной ClientRpc ленты лута): золото, цены, режим раздачи,
  ростер (+приказы, стойки, автоприменение, перезарядки), запасное снаряжение, телепорт, таймер поражения, перезарядки
  способностей командира.
* Команды клиент → сервер: `Net/CommandChannel` (два UNET-сообщения 0x4843 / 0x4844: команда и ответ-тост). Сервер проверяет
  отправителя, id героев, предметы, размеры. Хост применяет команды напрямую (`ForceRelay` отправляет их и хосту — так
  проверяется транспорт без второго компьютера).

### 2.7 Статистика
* `RunStats`: убийства, урон, получено золота, предметы, смерти — пишутся в `PlayerStatsComponent` командира перед
  `Run.BeginGameOver` (экран конца забега показывает результаты армии).

## 3. Модули

```
src/Plugin.cs                  точка входа, патчи, Mod Options
src/Mode/                      HighCommandRun, HcState, RunFlow, RunPatches, LobbyRules, RunPrefabBuilder, меню/лобби
src/Army/                      ArmyManager, Hero, ArmyService, Economy, LootService, ItemDistributor, HeroEquipment,
                               HeroRoles, CommanderAbilities, RunStats
src/Orders/                    HeroBrain, HeroAIState, HeroProfile, Perception, Formation, Order, OrderService
src/Client/                    ClientRoot, RtsCamera, ClientInput, Selection*, OrderController, WorldPicker, MapInfo
src/UI/                        панели и общие виджеты (UiKit, UiButton, Tooltip, PickupIcons ...)
src/Net/CommandChannel.cs      команды клиент → сервер
src/Core/                      Settings, Strings/Loc, MiniJson, OptionsIntegration
src/Dev/                       консольные команды hc_*, AutoExec (сценарии), Perf
```

## 4. Риски (и что с ними сделано)

| Риск | Что сделано |
|---|---|
| Ванильные системы ждут тело у игрока | зона телепорта, поражение, подсчёт игроков, цели директора, золото — патчи под героев (аудит 157 мест `isPlayerControlled` / `PlayerCharacterMasterController.instances`: исправлены существенные для цикла) |
| Клонирование `Run`-префаба без Unity Editor | клон + маркер; проверено в игре, `hc_dump_run` |
| Производительность | редкие тики (0.12 с), поиск целей с ограничением LOS-проверок, счётчики `hc_perf`: 10 героев ≈ 1 мс/с ИИ + ~40 мс/с UI |
| Модовые выжившие | запасной профиль `"*"`, ошибки профиля логируются и не роняют игру |
| Кооператив | свой канал команд; проверен только loopback на хосте |
| Финальные стадии | герои умеют использовать объекты, но сценарии финалов не проверялись |

## 5. Этапы
M0 каркас · M1 режим · M2 стартовый герой · M3 камера+мини-карта · M4 выделение · M5 приказы · M6 боевой ИИ ·
M7 золото/найм · M8 лут · M9 инвентарь · M10 UI/способности командира/правила лобби · M11 ход забега ·
M12 баланс/производительность · M13 полировка/упаковка. Подробности — `STATUS.md`.
