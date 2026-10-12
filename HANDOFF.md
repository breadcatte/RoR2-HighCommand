# HANDOFF — передача проекта «Верховное командование» (High Command) другой сессии

Дата написания: 2026-10-12. Версия мода: **0.9.1**. Последний коммит: `0454958`. Тег отката до QA-прохода: `pre-qa-0.9.0`.
Файл написан по реальному состоянию репозитория, исходников и свежего лога игры. Где что-то НЕ проверено — так и сказано.
Остальные документы: `DESIGN.md` (архитектура), `STATUS.md` (этапы M0–M13), `AUDIT.md` (QA-проход), `ROADMAP.md`
(жанровый анализ и план), `CHANGELOG.md`, `README.md`. HANDOFF.md их не заменяет, а собирает главное в одном месте.

---------------------------------------------------------------------------------------------------------------------

## 0. За 60 секунд

| Что | Значение |
|---|---|
| Где лежит проект | `C:\Users\user\Documents\RoR2Strategy` (НЕ в `RoR2Comfort`: там обычно открыта сессия, но это другой мод пользователя) |
| Git | не в PATH. Использовать `C:\Users\user\Documents\RoR2Strategy\_tools\mingit\cmd\git.exe`. Ветка `master`, код без незакоммиченных правок (кроме самого `HANDOFF.md`) |
| Плагин BepInEx | `kantemiir.HighCommand`, `Plugin.Version` = `package\manifest.json` `version_number` = 0.9.1 (`package.ps1` это проверяет) |
| Игра | Risk of Rain 2 1.5.0 (build 1210), Unity 2021.3.45, Steam AppID 632360, папка `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2` |
| Загрузчик | BepInEx 5.4.21 + RoR2BepInExPack **1.43.0** (на Thunderstore есть 1.46.0; в `manifest.json` стоит 1.43.0) |
| Мягкая зависимость | Risk of Options (`com.rune580.riskofoptions`) — окно настроек в игре |
| Сборка | .NET SDK 8.0.425 в `%LOCALAPPDATA%\dotnet-sdk`, target `netstandard2.1`, ссылки на `Risk of Rain 2_Data\Managed\*.dll` |
| Режим работы | **только одиночная игра (офлайн)**. Сеть/мультиплеер не трогать, не проверять, не «чинить» — прямое требование пользователя |
| Пользователь | не программист (мастер по ремонту ПК), пишет по-русски, хочет КОРОТКИЕ простые ответы, чтобы ассистент сам собирал, ставил и проверял, а пользователю оставался только запуск игры и оценка |
| Что запущено сейчас | на момент написания у пользователя **открыта игра** (процесс `Risk of Rain 2` с 18:10) — пока она открыта, `build.ps1` не сможет скопировать DLL |

**Главный урок проекта (уже стоил одной ошибки):** тест, который вызывает метод игры вручную из консоли, НЕ доказывает,
что туда попадает настоящий клик/клавиша. Кнопка режима в меню «не работала» именно так. Любой UI/ввод проверять тем
путём, которым пойдёт пользователь (см. `tools\qa\menu.txt`, `hc_menu_extra real|click`).

---------------------------------------------------------------------------------------------------------------------

## 1. ИДЕЯ МОДА

### 1.1 Что это
Отдельный стратегический режим для Risk of Rain 2 поверх нетронутой игры. Игрок — **бестелесный командир** с RTS-камерой
(вид сверху). Армия — настоящие выжившие игры под управлением ИИ. Стартовый герой берётся из лобби (выжившего и
лоадаут выбирает игрок как обычно), остальных **покупают за золото**, которое падает с убитых врагов. Приказы отдаются
сразу группам героев (двигаться, атаковать, держать позицию…), а умения герои применяют сами. **Предметы с врагов
попадают сразу в общий пул армии** и автоматически распределяются между героями — без сундуков и подбора руками.

Название в игре: «Верховное командование» (en: High Command). Внутреннее имя Run-префаба: `HighCommandRun`.
Кнопка в главном меню: «Меняющиеся режимы игры» (токен `TITLE_EXTRAGAMEMODE`) → «Верховное командование», между
«Затмение» и «Имитация». Также режим есть в списках режимов сетевой / локальной игры, и запускается консолью `hc_start`.

### 1.2 Что реально реализовано и проверено скриптовыми прогонами в игре

| Идея из задания | Статус | Как сделано |
|---|---|---|
| Отдельный игровой режим | ✅ | Префаб = клон `ClassicRun` + маркер `HighCommandRun` + реплицируемый `HcState`; регистрируется через `ContentPack.gameModePrefabs` |
| Вид сверху | ✅ | `RtsCamera` через `CameraRigController.SetOverrideCam` (ванильные постобработка/туман остаются), ванильный HUD скрыт, аудио-слушатель на земле под фокусом |
| Командир без тела | ✅ | `Stage.RespawnCharacter` для мастера игрока пропускается; его мастер = инвентарь-склад армии |
| Герои = настоящие выжившие под ИИ | ✅ | `MasterSummon` из `…MonsterMaster` выжившего (у всех 21 ванильного он есть), поверх — `HeroBrain` + `HeroAIState` |
| Общие приказы | ✅ | Move, Attack-Move, Attack/Focus, Stop, Hold, Follow, Retreat, Patrol, Interact (телепорт и др.), Cast; очередь (Shift); стойки (агрессивная/оборонительная/пассивная); построения (куча/линия/колонна); точка сбора; группы Ctrl+цифра; рамка выделения; Tab; Ctrl+A |
| Покупка героев за золото с врагов | ✅ | Общий кошелёк (`Economy`); перехват золота с убийств и прямых выдач; цена = `база × рост^куплено × сложность^1.25 × множитель`; панель найма `B` |
| Автоприменение скиллов | ✅ (качество среднее) | Штатные правила `AISkillDriver` каждого выжившего + профили `hero_profiles.json` (always / never / escape / engage / burst) + переключатель авто по каждому слоту (Alt+Z/X/C/Y) |
| Предметы с врагов сразу в инвентарь, без сундуков | ✅ с оговоркой | Шанс падения привязан к золоту за врага и сложности; предмет идёт в пул (инвентарь командира), `ItemDistributor` раздаёт героям по ролям. Платные интерактивы (сундуки, принтеры…) не ставятся. Оговорка: предмет не «в инвентарь героя, убившего врага», а в общий пул |
| Воскрешение павших | ✅ | За золото (`U`), цена = 0.4 × цена следующего героя; бесплатно при смене стадии (ванильный `Stage.RespawnAllNPCs`) |
| Ход забега | ✅ | Телепорт: один герой жмёт, армия собирается в зоне, босс, уход на следующую стадию; поражение по таймеру, когда все мертвы и нет золота на воскрешение; экран конца забега со статистикой армии |
| Способности командира (добавлено сверх задания) | ✅ | 4 способности: орбитальный удар, полевой медик, боевой клич, метка (F5–F8) |
| Правила в лобби | ✅ | Категория «Верховное командование» (стартовое золото, цена героев, предел армии, сложность за героя, раздача, частота дропа) — транспилер на `RuleCatalog.Init` |
| Интерфейс | ✅ | TopBar, RosterPanel, CommandCard, SelectionPanel, Minimap, BarracksPanel, InventoryPanel (drag-and-drop предметов), LootFeed, HelpOverlay (F1), Tooltip, Toast, оповещения о гибели |
| Настройки | ✅ | `BepInEx\config\kantemiir.HighCommand.cfg` + Risk of Options |
| Локализация | ✅ | ru/en в коде (`Core/Strings.cs`, `Core/Loc.cs`) без R2API |

### 1.3 Что только задумано / сделано частично
См. разделы 4 и 6. Коротко: сетевой кооператив написан, но не проверялся и сознательно заморожен; финальные стадии
(Лунная, Пустотные, побег) не проверялись; из плана ROADMAP не сделано: рисование всего маршрута очереди, «выбрать
простаивающих», тактическая пауза, авто-воскрешение, контекстные курсоры, растягивание строя мышью, пинги на миникарте,
пресеты осторожности, улучшения за золото.

---------------------------------------------------------------------------------------------------------------------

## 2. АРХИТЕКТУРА

### 2.1 Дерево проекта

```
RoR2Strategy/
├─ HighCommand.csproj          netstandard2.1, EnableDefaultCompileItems=false, Compile src\**\*.cs; ссылки на Managed\*.dll игры
├─ package/                    manifest.json, icon.png, profiles/hero_profiles.json  (то, что идёт в zip)
├─ tools/                      build.ps1, package.ps1, run-game.ps1, qa-run.ps1, make-icon.ps1, crop.ps1, qa/*.txt
├─ src/
│  ├─ Plugin.cs                точка входа: ContentPack, Harmony-патчи по одному (TryPatch), Risk of Options, AutoExec
│  ├─ AssemblyInfo.cs          [assembly: SearchableAttribute.OptIn] — без него игра не находит [ConCommand] в моде
│  ├─ Mode/                    режим как игровая сущность
│  │  ├─ HighCommandRun.cs     маркер режима + серверный «мозг»: стартовая армия, найм, капсула, создание клиентской части
│  │  ├─ HcState.cs            NetworkBehaviour: реплицируемое состояние (золото, цены, ростер, телепорт, таймер поражения)
│  │  ├─ RunPrefabBuilder.cs   клонирует ClassicRun.prefab из Addressables, ставит новый NetworkIdentity.assetId
│  │  ├─ RunFlow.cs            серверный тик 0.25 с: поражение, телепорт/зоны, сбор армии, копирование состояния героев в ростер
│  │  ├─ RunPatches.cs         патчи, чтобы ванильный забег жил с безтелесным командиром (см. 2.3)
│  │  ├─ LobbyPatches.cs       режим в каруселях меню сетевой/локальной игры
│  │  ├─ LobbyRules.cs         свои правила лобби (транспилер + рефлексия по приватным методам RuleCatalog)
│  │  └─ MenuButton.cs         кнопка в «Меняющиеся режимы игры» (клон кнопки Eclipse; хук на SetDesiredMenuScreen)
│  ├─ Army/                    армия, экономика, лут (сервер)
│  │  ├─ ArmyManager.cs        состав армии; `Recruit` = MasterSummon + PrepareMaster (подмена AI-состояния на HeroAIState)
│  │  ├─ Hero.cs               `Hero` (обёртка мастера + реплицируемые поля Net*), `HeroMarker`
│  │  ├─ ArmyService.cs        кто доступен, цена, найм/воскрешение/перенос предметов (Try* — для UI, Exec* — серверная работа)
│  │  ├─ Economy.cs            общий кошелёк + патчи KillGold / DirectGold / RememberVictim
│  │  ├─ LootService.cs        шанс/выбор дропа, `Grant` в пул, патчи Droplets / WorldPickups / KillDrops / NoPaidInteractables
│  │  ├─ ItemDistributor.cs    раздача пула героям (роль × теги), Split / Mirror, ручной перенос, снаряжение
│  │  ├─ HeroEquipment.cs      слоты снаряжения героя (в т.ч. МУЛ-Т)
│  │  ├─ HeroRoles.cs          роль выжившего и сродство к тегам предметов
│  │  ├─ CommanderAbilities.cs 4 способности командира с общими перезарядками
│  │  └─ RunStats.cs           статистика армии для экрана конца забега + патч Run.BeginGameOver
│  ├─ Orders/                  логика приказов и ИИ (сервер, БЕЗ знания про UI/ввод)
│  │  ├─ Order.cs              `OrderKind`, `Stance`, `Order`, `OrderCommand` (сериализуемая команда)
│  │  ├─ OrderService.cs       применение `OrderCommand` к героям (хост напрямую; клиент → CommandChannel)
│  │  ├─ HeroBrain.cs          слой решений героя: очередь, стойка, цель, отступление, зона телепорта (тик 0.12 с)
│  │  ├─ HeroAIState.cs        исполнитель на уровне тела: пишет InputBankTest как игрок (шаг 0.1 с)
│  │  ├─ HeroProfile.cs        профили поведения выживших из JSON (+ запасной профиль "*")
│  │  ├─ Perception.cs         поиск целей (сначала расстояние, потом дорогие проверки)
│  │  └─ Formation.cs          раскладка точек строя, `SnapToGround`
│  ├─ Client/                  всё, что работает на машине игрока
│  │  ├─ ClientRoot.cs         корень клиента: холст, панели, камера, Esc-логика, оповещения, масштаб UI
│  │  ├─ RtsCamera.cs          камера, края экрана, вращение, обход препятствий
│  │  ├─ ClientInput.cs        ЕДИНЫЙ шлюз чтения мыши/клавиатуры + синтетический ввод для тестов + блок при паузе/консоли
│  │  ├─ Selection.cs / SelectionController.cs   выбор героев (клик, рамка, группы, Tab)
│  │  ├─ OrderController.cs    превращает ввод в OrderCommand (режимы наведения, горячие клавиши)
│  │  ├─ WorldPicker.cs        луч по миру: герой / враг / интерактив под курсором
│  │  ├─ MapInfo.cs            картинка миникарты из графа узлов навигации
│  │  └─ AlertService.cs       «герой пал / при смерти» (сообщение, звук, метка, Backspace)
│  ├─ UI/                      панели и виджеты (uGUI/TMP, строится кодом на одном overlay-канвасе)
│  │  └─ TopBar, RosterPanel, CommandCard, SelectionPanel, BarracksPanel, InventoryPanel, LootFeed, Minimap,
│  │     WorldOverlay, HelpOverlay, Tooltip, Toast, PickupIcons, UiKit, UiButton
│  ├─ Net/CommandChannel.cs    команды клиент → сервер (два UNET-сообщения 0x4843 / 0x4844) — НЕ ПРОВЕРЯЛСЯ на реальном клиенте
│  ├─ Core/                    Settings (BepInEx config), Strings/Loc (локализация), MiniJson, OptionsIntegration
│  └─ Dev/                     DevCommands (51 консольная команда hc_*), AutoExec (скрипты), Perf (счётчики)
├─ AUDIT.md  ROADMAP.md  STATUS.md  DESIGN.md  CHANGELOG.md  README.md  HANDOFF.md
├─ dist/                       HighCommand-0.9.0.zip, HighCommand-0.9.1.zip (в .gitignore)
├─ _ref/                       декомпиляция игры для справки (в .gitignore, нужна для чтения API: RoR2, hlapi, ...)
└─ _tools/                     ilspycmd.exe (декомпилятор), mingit (git)  (в .gitignore)
```

### 2.2 Как всё связано (поток данных)

```
 мышь/клавиатура ──► ClientInput (шлюз, блок при паузе/консоли/чате)
                         │
                SelectionController / OrderController  (Client/)
                         │  OrderCommand (сериализуемая)
                         ▼
        хост:   OrderService / ArmyService.Exec*          клиент: CommandChannel (UNET-сообщение)
                         │                                          │
                         └────────────── сервер ◄───────────────────┘
                                     HeroBrain.Issue(order)         (HeroBrain живёт на мастере героя, ТОЛЬКО сервер)
                                          │ Mode/Goal/Enemy/CastSlot
                                          ▼
                                   HeroAIState (EntityState на мастере героя)
                                          │ пишет в InputBankTest и BaseAI.BodyInputs
                                          ▼
                                    тело выжившего (ванильные скиллы/движение)

 сервер ─ RunFlow копирует приказ/стойку/перезарядки героя в Hero.Net* ─► HcState.ServerPublishRoster ─► SetDirtyBit
 клиент ─ HcState.OnDeserialize ─► Hero.Net* ─► UI панели рисуют (UI читает ТОЛЬКО реплицируемое состояние)
```

Принцип, который нужно сохранять: **логика приказов/героев/экономики/лута не знает про UI и ввод**; UI читает только
`HcState` и поля `Hero.Net*`; единственное место чтения ввода — `ClientInput`.

Жизненный цикл: `HighCommandRun.OnEnable` (сервер: подписки, `Economy.ResetForNewRun`, `RunFlow`, `CommandChannel.RegisterServer`;
клиент: `ClientRoot`) → `OnServerStageBegin` → `SpawnStartingArmy` (ждёт точку спавна ≤5 с, создаёт героя из лоадаута
командира; если командира нет — `SurvivorCatalog.defaultSurvivor`) → `RunFlow.RelocateRally` (ждёт посадки, ставит точку
сбора и разводит армию) → тик.

### 2.3 Какие патчи (Harmony) и почему именно они

Плагин НЕ использует R2API: свой `ContentPack`, свой Harmony, свои UNET-сообщения, свой `MiniJson`, свои токены языка.
Каждый класс патча применяется отдельно через `Plugin.TryPatch` — сбой одного логируется (`Patch FAILED (...)`) и не
роняет остальное. Все патчи сначала проверяют `HighCommandRun.IsActive`, поэтому обычные забеги не затронуты.

| Цель (класс.метод) | Тип | Зачем | Файл |
|---|---|---|---|
| `Language.GetLocalizedStringByToken`, `TokenIsRegistered` | Prefix | отдаёт наши токены ru/en без R2API (вызывается на КАЖДЫЙ текст игры → проверка очень дешёвая) | Core/Loc.cs |
| `MultiplayerMenuController.BuildGameModeChoices`, `LocalMultiplayerMenuController.BuildGameModeChoices` | Prefix | меню сетевой/локальной игры оставляет только режимы, чей токен есть в карусели префаба меню → добавляем наш | Mode/LobbyPatches.cs |
| `Run.OverrideRuleChoices` | Postfix | форсирует `Misc.AllowDropIn.Off` (у командира нет тела, «вливаться» нельзя) | Mode/RunPatches.cs |
| `MainMenuController.SetDesiredMenuScreen` | Prefix | при открытии экрана «Меняющиеся режимы игры» клонирует кнопку Eclipse, меняет `m_OnClick` и текст. **Хук именно здесь**, потому что настоящая кнопка меню вызывает этот метод напрямую, а не `TitleMenuController.OnExtraGameModeButtonPressed` | Mode/MenuButton.cs |
| `RuleCatalog.Init` | Transpiler | вставляет вызов `LobbyRules.Inject` после последнего `AddRule` (каталог правил строится в приватном инициализаторе); внутри — рефлексия по приватным `AddCategory`(8 параметров)/`AddRule` | Mode/LobbyRules.cs |
| `Stage.RespawnCharacter` | Prefix | командир не получает тела | RunPatches.NoCommanderBody |
| `PlayerCharacterMasterController.GetPlayersWithBodiesCount` | Postfix | «живых игроков» = живые герои (масштаб HP врагов, бюджеты и т.п.) | RunPatches.LivingPlayerCount |
| `Run.participatingPlayerCount` (getter) | Postfix | размер армии → «эквивалент игроков» `1+(героев−1)×ThreatPerExtraHero` для сложности/бюджета/лута | RunPatches.ParticipatingPlayers |
| `HoldoutZoneController.CountPlayersInRadius`, `CountLivingPlayers` | Prefix (заменяют) | зона телепорта считает только `isPlayerControlled` тела; герои — ИИ-тела → считаем живых героев | RunPatches.ZoneCount* |
| `CombatDirector.PickPlayerAsSpawnTarget` | Prefix (заменяет) | директор спавнит врагов вокруг тел героев, а не безтелесного игрока | RunPatches.DirectorSpawnTarget |
| `ActivityManager.CurrentGameMode` (getter) | Prefix | платформенный менеджер активностей знает только штатные режимы и пишет Error при каждом старте → отдаём `Any` | RunPatches.ActivityGameMode |
| `PauseManager.TogglePauseScreen` | Prefix | Esc сначала закрывает панели режима (наведение/справка/предметы/найм), и лишь потом пауза | RunPatches.EscapeGuard |
| `TeamManager.GiveTeamMoney` | Prefix (заменяет) | золото с убийств → общий кошелёк | Army/Economy.cs |
| `DeathRewards.OnKilledServer` | Prefix + Finalizer (позиция жертвы для «+$»), Postfix (дроп предмета) | всплывающее «+золото» на месте смерти; шанс предмета | Economy.cs, LootService.cs |
| `CharacterMaster.GiveMoney` | Prefix (заменяет) | прямые выдачи денег предметами («Roll of Pennies» и т.п.) → кошелёк | Economy.DirectGold |
| `PickupDropletController.CreatePickupDroplet`, `GenericPickupController.CreatePickup` | Prefix (заменяют) | любой дроп (боссы, святыни…) → в пул, а не на землю | LootPatches.Droplets / WorldPickups |
| `DirectorCore.TrySpawnObject` | Prefix (заменяет) | не ставит платные интерактивы (сундуки, принтеры, святыни шанса…); бесплатные (телепорт, бочки, порталы) остаются; исключения — `Loot/KeepInteractables` | LootPatches.NoPaidInteractables |
| `Run.BeginGameOver` | Prefix | пишет статистику армии в `PlayerStatsComponent` командира перед экраном конца забега | Army/RunStats.cs |

Прочие точки интеграции с игрой (не Harmony): `ContentManager.collectContentPackProviders`; `Stage.onServerStageBegin`,
`Stage.onStageStartGlobal`; `GlobalEventManager.onCharacterDeathServer`; `CameraRigController.SetOverrideCam`;
`MasterSummon.Perform`; `Run.HandlePlayerFirstEntryAnimation` (капсула); `Interactor.AttemptInteraction`;
`TeleportHelper.TeleportBody`; `AISkillDriver`/`BaseAI` (чтение результатов оценки драйверов); консольные `[ConCommand]`.

### 2.4 Почему именно так (коротко)
* **Клон ClassicRun, а не свой наследник `Run`**: `Run` и `RunArtifactManager` требуют друг друга (`RequireComponent`), Unity
  не даёт убрать/заменить ни один; поэтому клон + наш маркер-компонент.
* **Герои — ИИ-мастера ванильных выживших**: у каждого выжившего уже есть набор `AISkillDriver`, не надо писать
  боевой ИИ с нуля; мы управляем «куда идти / кого бить / можно ли жать скилл».
* **Герои переживают стадии**: `SetDontDestroyOnLoad`, `destroyOnBodyDeath=false`; ванильный `Stage.RespawnAllNPCs`
  сам возрождает (в том числе павших) мастеров на новой стадии.
* **UI кодом, без ассетов**: Unity Editor недоступен; спрайты процедурные, шрифты игры.
* **`MiniJson` вместо `JsonUtility`**: `FixPluginTypesSerialization` не работает без сети (см. лог, раздел 4.3), а
  `JsonUtility` не знает типы мод-сборки.
* **Свой канал команд**: у безтелесного командира нет сетевого владельца, UNET `[Command]` недоступны.

---------------------------------------------------------------------------------------------------------------------

## 3. КАК СОБРАТЬ И ЗАПУСТИТЬ

Все команды — из папки `C:\Users\user\Documents\RoR2Strategy` в PowerShell. **Игра должна быть закрыта.**

### 3.1 Сборка и установка
```powershell
.\tools\build.ps1            # Release-сборка + копирование DLL в игру
.\tools\build.ps1 -NoInstall # только собрать (bin\Release\netstandard2.1\HighCommand.dll)
```
* Использует `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` (если нет — `dotnet` из PATH). Целевая папка:
  `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\BepInEx\plugins\local-HighCommand\HighCommand.dll`.
* Отказывается ставить, если процесс `Risk of Rain*` запущен (DLL заблокирована).
* Эквивалент вручную: `dotnet build HighCommand.csproj -c Release` (путь к игре можно переопределить `-p:RoR2Dir="D:\Games\Risk of Rain 2"`).
* Ожидаемый результат сборки: 0 ошибок, 0 предупреждений (в csproj глушится только CS0436 — шум type-forward игровых сборок).
* **Ловушка:** `build.ps1` ищет `profiles\` в КОРНЕ репозитория, а файл лежит в `package\profiles\hero_profiles.json`.
  Поэтому **правки профилей героев сборкой в игру НЕ попадают** — копировать вручную в
  `...\BepInEx\plugins\local-HighCommand\profiles\`. (Сейчас хеши установленного и пакетного файла совпадают.)

### 3.2 Упаковка
```powershell
.\tools\package.ps1          # build + dist\HighCommand-<version>.zip (manifest, icon, README, CHANGELOG, DLL, profiles)
```
Перед упаковкой проверяет, что `Plugin.Version` = `manifest.json version_number`. При смене версии менять ОБА места,
`CHANGELOG.md` и `README.md`.

### 3.3 Ручной запуск игры для проверки
Просто запустить Risk of Rain 2 из Steam. Войти в режим: главное меню → «Меняющиеся режимы игры» → «Верховное
командование» → лобби (выбрать выжившего/сложность/правила «Верховное командование») → старт. Либо консоль
(`Ctrl+Alt+~`): `hc_start`, затем в лобби — `pregame_start_run`.

### 3.4 Автоматические прогоны (без человека)
```powershell
.\tools\qa-run.ps1 full      # полный проход (старт, приказы, найм, стадия, смерть, поражение, рестарт)
.\tools\qa-run.ps1 perf      # нагрузка + счётчики hc_perf
.\tools\qa-run.ps1 ui        # масштаб UI, разрешения, пауза/Esc
.\tools\qa-run.ps1 alerts    # оповещения
.\tools\qa-run.ps1 smoke     # быстрый дым-тест
.\tools\qa-run.ps1 menu      # настоящий путь клика по меню → кнопка → забег
.\tools\qa-run.ps1 full -NoBuild   # без пересборки
```
`qa-run.ps1`: собирает и ставит DLL → **удаляет `kantemiir.HighCommand.cfg`** (настройки и клавиши пользователя будут
сброшены на значения по умолчанию!) → копирует сценарий в `BepInEx\config\HighCommand.autoexec.txt` → запускает игру через
`steam://rungameid/632360` → ждёт выхода (сценарии заканчиваются `@quit`; по таймауту убивает процесс) → печатает
сводку лога → переименовывает сценарий в `HighCommand.autoexec.last.txt`.

Формат сценария (`tools\qa\*.txt`): строка = консольная команда, либо директива `@wait n`, `@scene имя`, `@run`, `@norun`,
`@log текст`, `@quit`; `#` — комментарий. **Если `BepInEx\config\HighCommand.autoexec.txt` остался в папке, он
выполнится при следующем запуске игры (и может закрыть её через `@quit`).**

### 3.5 Логи и артефакты
| Что | Где |
|---|---|
| Лог BepInEx (основной) | `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\BepInEx\LogOutput.log` (пишется лениво; без временных меток) |
| Лог Unity | `C:\Users\user\AppData\LocalLow\Hopoo Games, LLC\Risk of Rain 2\Player.log` (предыдущий запуск — `Player-prev.log`) |
| Конфиг мода | `...\BepInEx\config\kantemiir.HighCommand.cfg` (значения, заданные `hc_cfg`, сохраняются при выходе!) |
| Скриншоты `hc_screenshot <имя>` | `...\BepInEx\plugins\local-HighCommand\screenshots\` |
| Профили героев | `...\BepInEx\plugins\local-HighCommand\profiles\hero_profiles.json` |

Полезные строки лога мода: `HighCommand 0.9.1 loaded`, `Patch applied: ...` / `Patch FAILED (...)`, `MenuButton: High
Command button added...`, `High Command run started`, `Recruited Hero#N(...)`, `Starting army created`, `Alert: ...`,
`RunFlow: every hero is dead ...`. Подробности решений героев — `Dev/VerboseLog = true` в конфиге.

### 3.6 Консоль разработчика (`Ctrl+Alt+~`)
`hc_ping` · `hc_start` · `hc_status` · `hc_flow` · `hc_perf` · `hc_next_stage` · `hc_kill_enemies` · `hc_kill_heroes [id]` ·
`hc_revive [id]` · `hc_tp` · `hc_tp_charge` · `hc_ability <0-3>` · `hc_cfg <имя> <значение>` · `hc_gold` · `hc_recruit` ·
`hc_economy` · `hc_pool` · `hc_drop` · `hc_drop_random` · `hc_interactables` · `hc_inventory` · `hc_items_move` ·
`hc_items_auto` · `hc_sharing` · `hc_rebalance` · `hc_netcmds` · `hc_inv_drag` · `hc_sel` · `hc_select` · `hc_click` ·
`hc_drag` · `hc_key` · `hc_click_hero` · `hc_click_enemy` · `hc_click_teleporter` · `hc_input_end` · `hc_cam` ·
`hc_overlay` · `hc_screenshot <имя>` · `hc_dump_*` · `hc_menu_buttons` · `hc_menu_extra [real|click]` · `hc_spawn` ·
`hc_ui` · `hc_rule` · `hc_name`. Координаты `hc_click`/`hc_drag` — экранные пиксели Unity с началом снизу слева:
`y_unity = 1080 − y_скриншота`. **Эти команды не закрыты читами** (требование только «хост»), т.е. `hc_gold` и т.п. работают
у любого, у кого стоит мод.

### 3.7 Декомпиляция игры (для чтения API)
`_tools\ilspycmd.exe` + готовые выгрузки в `_ref\` (RoR2, Assembly-CSharp, hlapi…), список адресов Addressables
`_ref\addressable_keys.txt`, драйверы ИИ `_ref\ai_drivers.txt`. Для запуска `ilspycmd` может понадобиться
переменная окружения roll-forward (.NET 8).

---------------------------------------------------------------------------------------------------------------------

## 4. ИЗВЕСТНЫЕ ПРОБЛЕМЫ (честно, без приукрашивания)

### 4.1 Самое свежее и самое важное: первая настоящая игра пользователя
12.10.2026 около 18:10–18:13 пользователь впервые зашёл в режим обычным путём (кнопка в меню работает) и играл.
Из `LogOutput.log` этой сессии (всё, что мод написал после старта забега):

```
[Info   :HighCommand] High Command run started (server=True).
[Info   :HighCommand] Recruited Hero#1(Huntress) master=HC_HuntressMaster body=HuntressBody(Clone) exactAiMaster=True at (122.15, 105.40, 117.17)
[Info   :HighCommand] Starting army created: 1 hero(es).
[Info   :HighCommand] RtsCamera: took over camera rig.
[Info   :HighCommand] Hero profiles: 20 survivor profile(s) + fallback loaded.
[Info   :HighCommand] Alert: Hero#1(Huntress) is nearly dead at (149, 106, 57)
[Info   :HighCommand] Alert: Hero#1(Huntress) has fallen at (145, 1, 33)
[Info   :HighCommand] RunFlow: every hero is dead and nobody can come back in time: the run is lost.
```
То есть единственный герой (Охотница) упал, забег проигран. Что известно и чего НЕ известно:
* Не известно, сколько прошло времени (в логе нет меток), сколько было золота и пытался ли пользователь воскресить (`U`).
  Пользователю ещё не задавали эти вопросы — **спросить**: сколько секунд играли, что видели на экране, нажимали ли «воскресить».
* Координаты: «при смерти» — y=106 (рядом с высотой точки появления y=105.4), «пала» — y=1, то есть на ~105 м ниже, в
  пределах ~25 м по горизонтали. Гипотезы (ни одна не проверена): (а) герой ещё был в воздухе/в капсуле, а оповещение
  `AlertService` на только что созданном теле сработало по «низкому здоровью» ложно (у нового тела здоровье может
  читаться неверно в первые кадры; проверить порог `LowHealth=0.22` на свежем теле); (б) герой упал с обрыва —
  `allowWalkOffCliff = true` стоит в `HeroAIState.UpdateGo` (стр. 143) и `UpdateDrivers` (стр. 199), а автоотступление
  раненого — это `BrainMode.Go` со спринтом по прямой к «безопасной точке» (`HeroBrain.SafePoint`), т.е. бег вслепую;
  (в) просто убили враги (одинокий ИИ-герой 1-го уровня на первой стадии).
* **Системный риск, который виден по коду независимо от причины:** с одним героем смерть = поражение за секунды.
  `RunFlow.Tick`: если все мертвы — окно 45 с (`DefeatGraceSecondsCanRevive`), если золота на воскрешение нет — 6 с
  (`DefeatGraceSecondsBroke`). Стартовое золото 100, цена воскрешения = 0.4 × 250 = 100 в начале и **растёт со временем**
  (`difficultyCoefficient^1.25`): через ~минуту 100 золота уже не хватает, и поражение наступает почти мгновенно.
  Автовоскрешения нет. Нужно решить, считать ли это багом баланса (см. раздел 6).

### 4.2 Что НЕ проверено в игре (собрано, но не смотрели)
* **Реальный второй клиент / кооператив.** Проверен только loopback на хосте (`hc_netcmds on`). Сеть заморожена по
  требованию пользователя — но код в моде есть и может содержать ошибки.
* **Настоящая мышь и клавиатура на долгой игре.** Весь ввод проверялся синтетическим вводом (`hc_click`, `hc_key`) и
  одним ручным заходом в меню. Особенно: `Tab` против ванильной таблицы очков (по коду не должна открываться, т.к.
  `targetBodyObject` пуст; живьём не смотрели); настоящая клавиша Esc (проверен только тот же путь через консольную `pause`).
* **Финальные стадии** (Лунная/Commencement, Пустотные, побег на корабле, Mithrix): герои умеют «использовать»
  объекты, но сценарии финалов рассчитаны на живого игрока — не проверялись. Забег без финала идёт по кругу, как
  ванильные петли.
* **Y (снаряжение вручную)** и **построения визуально** — собрано, не смотрели.
* **Два слота снаряжения у МУЛ-Т** — в этой сборке игры не воспроизводились (слот один); ветка по коду готова.
* **Баланс.** Цены героев/предметов, шанс дропа (`ItemGoldFactor=2.5` — «примерно как в ваниле» по формулам), порог
  автоотступления — настраивались по формулам и коротким прогонам, **не по часовым забегам**.
* **Совместимость с модами пользователя**: стоят `local-EngiDrone`, `local-RoR2Comfort` (синяя тонировка и подпись
  «COMFORT ON/35%» на экране — это их мод, не наш), Risk of Options, RoR2BepInExPack. С другими модами выживших и
  контента не проверялось (запасной профиль `"*"` для модовых выживших есть, но не испытан в игре).
  В папке `config` лежат хвосты Starstorm 2 / Moonstorm / ProperSave / ScrollableLobbyUI, но самих плагинов в `plugins` нет.
* Автоприменение умений у отдельных выживших (Rex, Captain, Railgunner, Heretic, Void Fiend…) — по профилям, живьём
  каждого не гоняли.

### 4.3 Ошибки и предупреждения в логе (с текстом) и их происхождение
**Из кода HighCommand в QA-прогонах ошибок нет.** Остальное — игра/другие моды. Проверено ванильным `ClassicRun` в той же сессии.

| Текст в логе | Откуда | Комментарий |
|---|---|---|
| `[Error :FixPluginTypesSerialization] Failed to initialize plugin types serialization fix: ... TLS Support not available.` | окружение (нет TLS в Mono) | из-за этого `JsonUtility` не знает типы мод-сборки → написан `MiniJson` |
| `[Error :RoR2BepInExPack] FixNonLethalOneHP TryGotoNext failed, not applying patch` | пакет 1.43.0 против игры 1.5.0 | пакет старее игры; новее — 1.46.0 |
| `A connection has already been set as ready. There can only be one.` | ванильная игра, при каждом старте забега | не наше |
| `GenericPool: ReleasePools called on non-empty pool GenericPool<RoR2.EffectManagerHelper>` | ванильная игра, при завершении забега | не наше |
| `TeamManager.SetTeamExperience NullReferenceException` | ванильная игра, при **втором** забеге в одной сессии | не наше. Нужно знать: уровни героев идут от командного опыта игры |
| `MusicController.ScheduleIntensityCalculation` NRE | ванильная игра без тела игрока | в QA встречалось; в сессии пользователя 18:10 — не было |
| `Unable to set gate state for village` | игра/другой мод | не наше |
| `Cannot set the parent of 'Sparks, Trail'` | призрак молнии Артифисера (ваниль) | не наше |
| `InvalidKeyException ... Key=RoR2/Base/UI/matUIPaintStreakFlash.mat` и `... Type=UnityEngine.Shader` | Addressables при загрузке (ваниль/другие моды) | не наше |
| `ItemTierDef 'CursedTierDef' / 'HexDef' / 'PageTierDef' has a tier of 'AssignedAtRuntime'` | ваниль | не наше |
| `Large state update of 1567 bytes for netId:110 from script:GameOverController(Clone)` | ваниль | не наше |
| `The prefab '...' has multiple NetworkIdentity components` (десятки) | ванильные префабы | не наше |
| `Parent of RectTransform is being set with parent property. Consider using the SetParent method ...` | **происхождение не установлено**: пришло сразу после экрана конца забега; может быть и ванильный UI, и наш — проверить `SetParent(...)` без `false` в `UiKit`/панелях | предупреждение, не ошибка |
| `Internal: JobTempAlloc has allocations that are more than 4 frames old` / `deleting an allocation that is older...` | Unity, часто при смене сцены | не наше |
| `[AkGameObjManager] Attempting to manage a non-registered object Scene Camera` | Wwise при подмене камеры/слушателя | возможно связано с нашим переносом аудио-слушателя (`ListenerAtFocus`) — не расследовалось |
| `ActivityManager::CurrentGameMode property unable to determine GameMode` | было в 0.9.0 | **исправлено** патчем `ActivityGameMode` в 0.9.1 |

### 4.4 Костыли, заглушки и хрупкие места (где мод держится на внутренностях игры)
1. **Рефлексия по приватным полям/методам:** `NetworkIdentity.m_AssetId` (`RunPrefabBuilder`), `Button.m_OnClick`
   (`MenuButton`), `ContentPack._identifier` (`Plugin.Awake`), `RuleCatalog.AddCategory` с ровно 8 параметрами и
   `AddRule` (`LobbyRules`), `HoldoutZoneController.CountLivingPlayers`, `CombatDirector.PickPlayerAsSpawnTarget`
   (патчи по имени строкой). После обновления игры любое из них может тихо перестать работать: `TryPatch`
   залогирует `Patch FAILED`, а правила лобби просто пропадут (тогда значения берутся из `.cfg`).
2. **`LobbyRules`**: транспилер + `addCategory.Invoke` в приватном инициализаторе. Меняет формат книги правил — это
   делает лобби с модом и без него несовместимыми (для сети важно, офлайн не мешает).
3. **`Assets.RoR2.Scripts.Platform.ActivityManager`** — внутренний пространственный тип игры (`RunPatches.ActivityGameMode`).
4. **Капсула (pod):** герой «выходит» из капсулы потому, что `HighCommandRun.OpenPodWhenLanded` каждые 0.5 с (≤40 с)
   вызывает `Interactor.AttemptInteraction(seat)`; ИИ сам кнопку «использовать» не нажимает. Если на карте капсулы нет
   (`stage.usePod=false`) — путь не используется. Логика ветвления по `timeout < 38f` — эвристика.
5. **Подмена состояния ИИ:** `ArmyManager.PrepareMaster` переписывает `BaseAI.scanState` И `initialStateType` /
   `mainStateType` у машины состояний «AI» — иначе она возвращается в Wander. Если игра поменяет структуру `BaseAI`, герои
   «зависнут».
6. **`HeroAIState`** — частичная копия `BaseAIState`-логики (оценка драйверов читается, движение/кнопки считаются сами).
   Хрупкая. Известные ограничения: нет кайтинга «по-человечески», нет обхода зон босса.
7. **`HeroBrain.PushEnemyToAi`** пишет прямо в `ai.currentEnemy.gameObject`, `bestHurtBox`, `enemyAttention` (внутренности `BaseAI`).
8. **Антизастревание** (`HeroAIState.UpdateStuckRecovery`): прыжок → перепланирование → через 5 с **телепорт** на 14 м к цели.
   Это костыль на случай навигационных ошибок; может переносить героев через препятствия.
9. **`DirectorSpawnTarget`** полностью заменяет метод и при отсутствии живых героев оставляет старый `currentSpawnTarget`
   (может быть уничтоженным) — крайний случай не разбирался.
10. **Предметы в пуле навсегда:** `LootService.Grant` выдаёт `GiveItemPermanent` («временные» предметы становятся постоянными),
    ломающиеся/расходуемые предметы героев (Dio's, Watch) пул «не видит»; `ItemDistributor.distributedTotal` считает только то,
    что выдано, и не учитывает потери у героев. Mirror-режим дублирует каждый предмет каждому герою (намного сильнее).
11. **Лунные монеты:** мисц-подборы (`MiscPickupIndex`) отдаются `AwardLunarCoins(1)` первому «участвующему» пользователю — упрощение.
12. **Профили героев** — JSON рядом с DLL; при ошибке чтения используется запасной профиль. Свой `MiniJson` минимален.
13. **Дефолты конфига «залипают»**: старый `.cfg` хранит прежние значения по умолчанию — при смене дефолтов в коде
    нужно удалять `kantemiir.HighCommand.cfg`.
14. **`hc_cfg` сохраняется на выходе** и может «протечь» в следующий запуск (поэтому `qa-run.ps1` стирает cfg).
15. **Консольные команды в релизной DLL** без защиты читами (см. 3.6).
16. **`Time.timeScale`/пауза:** ввод и UI работают от `unscaledTime`; тактической паузы нет. При паузе/консоли мод
    игнорирует клики и колесо (`ClientInput.MouseBlocked`) и затемняет интерфейс.

### 4.5 Места, где я сомневался в решении
* **Масштаб сложности через `Run.participatingPlayerCount`** (патч getter-а, вызывается много и часто): формула
  `1+(героев−1)×0.65` с округлением — решение «на глаз»; влияет на бюджет директора, число боссов, лут. Не калибровалось
  часовыми прогонами. Побочные эффекты на неизвестные системы не исключены.
* **Живые герои вместо игроков** (`GetPlayersWithBodiesCount`): масштабирует HP врагов по числу живых героев — при гибели
  героев враги «слабеют», при найме «крепнут». Намеренно, но не обкатано.
* **Цена воскрешения** `0.4 × цена следующего героя` и **окно поражения 45 / 6 с** (см. 4.1) — спорный баланс.
* **Шанс предмета** привязан к золоту за врага: `chance = goldReward / (25 × ItemGoldFactor × difficulty^1.25)`, ×1.8 для
  элиты, ×4 (≥0.6) для боссов. Весовые таблицы тиров (70/26/3/4…) — мои, не из игры.
* **Золото без привязки к героям:** `CharacterMaster.GiveMoney` для команды Player всегда в кошелёк — предметы и эффекты,
  которые ждут денег на самом мастере, не увидят их.
* **Раздача «по ролям»** (`HeroRoles`, `ItemDistributor.BestHero`): коэффициент 0.16 за уже имеющийся предмет и случайная
  добавка — эвристика.
* **Hidden-выжившие** (`AllowHiddenSurvivors`, например Heretic) помечены «экспериментально», по умолчанию выключено.
* **Hero level:** `useAmbientLevel=false`, уровни — от командного опыта игры (`TeamManager`); на втором забеге в сессии
  ванильный NRE по опыту — как это влияет на уровни героев, не исследовалось.

### 4.6 Ловушки инструментов (из-за них уже терялось время)
* `git` не в PATH → только `_tools\mingit\cmd\git.exe`.
* .NET-методы работы с файлами в PowerShell не учитывают `Set-Location` → в скриптах использовать абсолютные пути.
* Лог BepInEx пишется лениво — перед чтением дождаться закрытия игры.
* `qa-run.ps1` стирает конфиг пользователя (клавиши!) — после прогонов проверять, что пользовательские настройки не
  потеряны, и не запускать, пока игра открыта.
* Старые тестовые значения `resolution/fps_max/window_mode` могут сохраниться в настройках игры: у пользователя
  `resolution 1920x1080x165`, `window_mode Fullscreen`, `fps_max 144`, `vsync_count 0`.
* Править `profiles` и `manifest` — в `package\`, а не в корне.

---------------------------------------------------------------------------------------------------------------------

## 5. ПРИНЯТЫЕ РЕШЕНИЯ И ОГРАНИЧЕНИЯ

### 5.1 Сознательные упрощения
* **Командир не имеет тела и способностей «героя»**: он только отдаёт приказы и использует 4 общие способности.
* **Армия = ванильные выжившие под ИИ**, а не отдельные юниты: поведение ограничено тем, что умеет их `AISkillDriver`.
* **Один общий кошелёк и один общий пул предметов** на всю армию (на все «командиры» один склад — `LootService.PoolMaster`
  берёт первого игрока-мастера).
* **Нет тумана войны**: миникарта показывает всех врагов (для режима «командир сверху» сознательно проще).
* **Одна общая точка сбора**; построения «куча/линия/колонна» без растягивания мышью.
* **Нет улучшений за золото**, ремонтов, расходников; золото тратится только на найм и воскрешение.
* **Дроп только с убитых врагов + перехват всех остальных дропов**; платные интерактивы вычищены. Бесплатные интерактивы
  (телепорт, бочки, порталы, святыня горы) остаются.
* **Поражение определяет мод** (`RunFlow`), а не ваниль: ванильный game over требует «заспавненного игрока».
* **Статистика экрана конца забега** пишется в `PlayerStatsComponent` командира.
* **Звуки/подсказки** берутся из ванильных событий (`Play_UI_insufficient_funds` и др.).

### 5.2 Только для одиночной игры (мультиплеер НЕ поддерживается)
* Требование пользователя: сеть сейчас вне задачи — «не трогай, не проверяй, не дорабатывай»; но и не блокируй её
  будущее. Поэтому код сети есть, но он **заморожен и непроверен**.
* Что в коде для сети уже есть: `HcState` (ручная `OnSerialize`/`OnDeserialize` и ручной ClientRpc ленты лута),
  `Net/CommandChannel` (сообщения 0x4843/0x4844; типы 48–88 заняты игрой), проверки сервера в `ExecRecruit`/`ExecRevive`/
  `ExecMoveItem`, валидация размера выделения (≤256).
* Чего нет (подробно в `ROADMAP.md` §3): владения героями (сейчас любой клиент командует любым), нескольких командиров
  (пул/золото/способности — по одному на забег), сплит-скрина, проверки на реальном втором клиенте (лаги, смена стадий,
  вход/выход игрока, рассинхрон `Time.time`), жёсткой валидации входных данных команд. Оценка готовности архитектуры
  ~60–70 %. Рекомендованный порядок: владелец героя → явные объекты «кошелёк/склад» → тест с двумя клиентами → только потом фичи.
* В офлайне хост применяет команды напрямую; `CommandChannel.ForceRelay` (`hc_netcmds on`) гоняет их через собственное
  соединение хоста — так проверяется транспорт без второго компьютера.

### 5.3 Что заморожено/согласовано с пользователем
* Не переписывать архитектуру без необходимости; не ломать работающее.
* Настройки — только в конфиг BepInEx (+ Risk of Options); логирование в ключевых точках.
* После каждой группы правок — сборка и проверка запуска в игре.
* Отчитываться по-русски, коротко; не говорить «работает», пока не проверено скриптом или скриншотом.
* Коммиты — с трейлером `Co-Authored-By: ...` (как в истории).

---------------------------------------------------------------------------------------------------------------------

## 6. НЕЗАКОНЧЕННОЕ (что осталось по первоначальному плану)

Порядок — по полезности и срочности, как я бы их брал.

### 6.1 Срочно (по реальной игре пользователя)
1. **Разобраться, почему герой погиб и почему забег проигран за секунды** (раздел 4.1): попросить пользователя
   рассказать, что видел; воспроизвести (`Dev/VerboseLog=true`, `hc_status`, `hc_flow`); проверить ложное срабатывание
   `AlertService` на только что созданном теле в капсуле; проверить падение с высоты (`allowWalkOffCliff`).
2. **Смягчить «одиночный герой умер — конец»**: например, дать больше стартового золота / не растить цену воскрешения
   так быстро / мягче окно поражения / авто-воскрешение (идея №5 ROADMAP: `ArmyService.TryRevive` по таймеру в `RunFlow`).
3. **Выдержать часовой забег с реальной мышью** (до 5–6 стадий): баланс цен/дропа/опасности, утечки, поведение ИИ на
   разных картах и у разных выживших, «застревание» героев.

### 6.2 Из первоначального плана и ROADMAP, ещё не сделано
* Рисовать весь маршрут очереди приказов (сейчас только текущий отрезок; нужно реплицировать точки очереди).
* «Выбрать всех простаивающих» и «всех того же типа на экране» (двойной клик выбирает «где бы ни были»).
* Тактическая пауза (приказы при остановленном времени; опасное место — ванильная пауза/счётчики забега).
* Авто-воскрешение с порогом/приоритетом по роли.
* Контекстные курсоры (меч над врагом, шестерня над телепортом).
* Построение «растянуть линию мышью» (ПКМ-перетаскивание) и поворот строя.
* Пинги на миникарте (вспышка в месте боя/гибели; метка в мире уже есть).
* Профили поведения «осторожно/обычно/агрессивно» одним переключателем (набор множителей к `HeroProfile`).
* Улучшения за золото (армейские бонусы, апгрейд командных способностей) — большая задача: баланс + экономика.
* Звуковые оповещения шире: «враги у телепорта», «найм доступен» и т.п.
* Автоматизация («все простаивающие в бой»).

### 6.3 Проверки, которые обязательно нужно закрыть
* Реальные мышь/клавиатура: Tab, Esc, Alt+Z/X/C/Y, Y (снаряжение), построения.
* Финальные стадии (Лунное/Commencement, Пустотные, побег) — сценарии не рассчитаны на ИИ-героев.
* Совместимость с модами выживших и контента (запасной профиль `"*"`).
* Источник предупреждения `Parent of RectTransform is being set with parent property` (наш он или ванильный).
* Обновить зависимость пакета до `RoR2BepInExPack` актуальной версии (1.46.0) и проверить, не ломается ли что-то.

### 6.4 Отложено сознательно
* **Всё сетевое** (кооператив, владение героями, несколько командиров, тесты с реальным вторым клиентом) — по требованию
  пользователя, отдельным этапом позже.

---------------------------------------------------------------------------------------------------------------------

## 7. Чек-лист для новой сессии (с чего начать)

1. Прочитать этот файл, затем `STATUS.md` и `AUDIT.md`.
2. `git status` (путь из раздела 0): код чистый на коммите `0454958`; единственный новый файл — `HANDOFF.md` (не закоммичен).
3. Убедиться, что игра закрыта, и выполнить `.\tools\build.ps1` — должно быть 0 ошибок, 0 предупреждений.
4. `.\tools\qa-run.ps1 smoke` (потом `full`) — убедиться, что скриптовый прогон проходит. Помнить: он стирает cfg.
5. Взять пункт 6.1.1 и расследовать падение героя по реальному логу.
6. Любую правку UI/ввода проверять настоящим путём (клик по меню, `hc_menu_extra real|click`), а не прямым вызовом метода.
7. Не трогать сеть, пока пользователь не скажет.
