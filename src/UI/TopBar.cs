using HighCommand.Army;
using HighCommand.Client;
using HighCommand.Mode;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// The top strip, panels of one height: the stage (name, time, difficulty) on the left; in the middle the army's
    /// items (a click opens the item panel), the purse, the army and the price of the next hero (a click opens the
    /// barracks); on the right the teleporter with its progress (a click starts / gathers / leaves). The red defeat
    /// line comes below them.
    /// </summary>
    internal sealed class TopBar
    {
        private const float Height = 60f;
        private const float Top = 14f;
        private const float StageW = 400f, EconomyW = 580f, TeleW = 340f, ItemsW = 190f;

        private readonly RectTransform stagePanel;
        private readonly TextMeshProUGUI stageText;
        private readonly TextMeshProUGUI metaText;

        private readonly RectTransform economyPanel;
        private readonly TextMeshProUGUI goldText;
        private readonly TextMeshProUGUI incomeText;
        private readonly TextMeshProUGUI armyText;
        private readonly TextMeshProUGUI armySubText;
        private readonly TextMeshProUGUI nextLabel;
        private readonly TextMeshProUGUI nextText;
        private readonly TextMeshProUGUI keyText;

        private readonly RectTransform teleporterPanel;
        private readonly Image teleporterGlyph;
        private readonly TextMeshProUGUI teleporterText;
        private readonly RectTransform teleporterBarBack;
        private readonly Image teleporterBar;

        private readonly RectTransform itemsPanel;
        private readonly Image itemsFrame;
        private readonly TextMeshProUGUI itemsText;
        private readonly TextMeshProUGUI itemsSubText;
        private readonly TextMeshProUGUI itemsKey;

        private readonly TextMeshProUGUI defeatText;
        private readonly Image defeatPlate;

        private float shownGold;

        internal TopBar(Transform parent)
        {
            // --- stage ---
            stagePanel = UiKit.NewPanel("StagePanel", parent, UiKit.PanelColor);
            UiKit.Place(stagePanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -Top), new Vector2(StageW, Height));
            stageText = UiKit.NewText("Stage", stagePanel, "", 22f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(stageText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -6f), new Vector2(StageW - 28f, 28f));
            stageText.enableAutoSizing = true;
            stageText.fontSizeMin = 13f;
            stageText.fontSizeMax = 22f;

            Image clock = UiKit.NewImage("Clock", stagePanel, UiKit.GlyphSprite(Glyph.Clock), UiKit.TextDim);
            UiKit.Place(clock.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(16f, 15f), new Vector2(15f, 15f));
            metaText = UiKit.NewText("Meta", stagePanel, "", 17f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(metaText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(38f, 5f), new Vector2(StageW - 52f, 22f));

            // --- purse, army, next hero ---
            economyPanel = UiKit.NewPanel("EconomyPanel", parent, UiKit.PanelColor);
            UiKit.Place(economyPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Top), new Vector2(EconomyW, Height));

            // the purse
            Image coin = UiKit.NewImage("Coin", economyPanel, UiKit.GlyphSprite(Glyph.Coin), UiKit.Gold);
            UiKit.Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 5f), new Vector2(26f, 26f));
            goldText = UiKit.NewText("Gold", economyPanel, "", 32f, UiKit.Gold, TextAlignmentOptions.Left);
            UiKit.Place(goldText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(50f, 6f), new Vector2(170f, 40f));
            incomeText = UiKit.NewText("Income", economyPanel, "", 14f, UiKit.Good, TextAlignmentOptions.Left);
            UiKit.Place(incomeText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(52f, 4f), new Vector2(160f, 18f));
            Divider(economyPanel, 224f);

            // the army
            Image person = UiKit.NewImage("Person", economyPanel, UiKit.GlyphSprite(Glyph.Person), UiKit.TextDim);
            UiKit.Place(person.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(240f, 5f), new Vector2(24f, 24f));
            armyText = UiKit.NewText("Army", economyPanel, "", 28f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(armyText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(272f, 6f), new Vector2(90f, 36f));
            armySubText = UiKit.NewText("ArmySub", economyPanel, "", 14f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(armySubText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(242f, 4f), new Vector2(120f, 18f));
            Divider(economyPanel, 372f);

            // the next hero and the key of the barracks
            nextLabel = UiKit.NewText("NextLabel", economyPanel, "", 14f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(nextLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(388f, -7f), new Vector2(150f, 18f));
            nextLabel.enableAutoSizing = true;
            nextLabel.fontSizeMin = 10f;
            nextLabel.fontSizeMax = 14f;
            nextText = UiKit.NewText("Next", economyPanel, "", 24f, UiKit.Gold, TextAlignmentOptions.Left);
            UiKit.Place(nextText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(388f, 5f), new Vector2(110f, 30f));

            Image cap = UiKit.NewImage("KeyCap", economyPanel, UiKit.White, new Color(0.02f, 0.03f, 0.05f, 0.85f));
            UiKit.Place(cap.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(26f, 24f));
            keyText = UiKit.NewText("Key", cap.rectTransform, "", 15f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.Stretch(keyText.rectTransform, 0, 0, 0, 0);

            // --- teleporter ---
            teleporterPanel = UiKit.NewPanel("TeleporterPanel", parent, UiKit.PanelColor);
            UiKit.Place(teleporterPanel, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(EconomyW * 0.5f + 8f, -Top), new Vector2(TeleW, Height));
            teleporterGlyph = UiKit.NewImage("Glyph", teleporterPanel, UiKit.GlyphSprite(Glyph.Teleporter), UiKit.TextDim);
            UiKit.Place(teleporterGlyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(28f, 28f));
            teleporterText = UiKit.NewText("Text", teleporterPanel, "", 17f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(teleporterText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, -8f), new Vector2(TeleW - 68f, 24f));
            teleporterText.enableAutoSizing = true;
            teleporterText.fontSizeMin = 10f;
            teleporterText.fontSizeMax = 17f;
            teleporterBarBack = UiKit.NewImage("BarBack", teleporterPanel, UiKit.White, new Color(0.1f, 0.12f, 0.16f, 1f)).rectTransform;
            UiKit.Place(teleporterBarBack, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(54f, 11f), new Vector2(TeleW - 68f, 9f));
            teleporterBar = UiKit.NewImage("Bar", teleporterBarBack, UiKit.White, new Color(0.45f, 0.75f, 1f, 1f));
            teleporterBar.rectTransform.anchorMin = Vector2.zero;
            teleporterBar.rectTransform.anchorMax = new Vector2(0f, 1f);
            teleporterBar.rectTransform.offsetMin = teleporterBar.rectTransform.offsetMax = Vector2.zero;

            // --- the army's items: left of the purse, a click opens the item panel (same as its key) ---
            itemsPanel = UiKit.NewPanel("ItemsPanel", parent, UiKit.PanelColor);
            UiKit.Place(itemsPanel, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-EconomyW * 0.5f - 8f, -Top), new Vector2(ItemsW, Height));
            itemsFrame = UiKit.NewImage("Open", itemsPanel, UiKit.White, UiKit.Good);
            UiKit.StyleOutline(itemsFrame, 7f);
            UiKit.Stretch(itemsFrame.rectTransform, 0, 0, 0, 0);
            itemsFrame.raycastTarget = false;
            itemsFrame.enabled = false;
            Image chest = UiKit.NewImage("Chest", itemsPanel, UiKit.GlyphSprite(Glyph.Chest), UiKit.Gold);
            UiKit.Place(chest.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(26f, 26f));
            itemsText = UiKit.NewText("Items", itemsPanel, "", 19f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(itemsText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -7f), new Vector2(ItemsW - 94f, 26f));
            itemsText.enableAutoSizing = true;
            itemsText.fontSizeMin = 12f;
            itemsText.fontSizeMax = 19f;
            itemsSubText = UiKit.NewText("ItemsSub", itemsPanel, "", 14f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(itemsSubText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 5f), new Vector2(ItemsW - 94f, 18f));
            Image itemsCap = UiKit.NewImage("KeyCap", itemsPanel, UiKit.White, new Color(0.02f, 0.03f, 0.05f, 0.85f));
            UiKit.Place(itemsCap.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(26f, 24f));
            itemsKey = UiKit.NewText("Key", itemsCap.rectTransform, "", 15f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.Stretch(itemsKey.rectTransform, 0, 0, 0, 0);

            // --- defeat line: a dark plate under the red text, so it reads over any ground ---
            defeatPlate = UiKit.NewImage("DefeatPlate", parent, UiKit.White, new Color(0.09f, 0.02f, 0.03f, 0.84f));
            UiKit.StyleOutline(defeatPlate, 10f);
            UiKit.Place(defeatPlate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -144f), new Vector2(900f, 56f));
            defeatPlate.enabled = false;
            defeatText = UiKit.NewText("Defeat", parent, "", 32f, UiKit.Bad, TextAlignmentOptions.Center);
            UiKit.Place(defeatText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1500f, 44f));
            defeatText.enabled = false;

            ClientInput.RegisterUiHitTest(Contains);
        }

        private static void Divider(RectTransform panel, float x)
        {
            Image line = UiKit.NewImage("Divider", panel, UiKit.White, new Color(0.6f, 0.7f, 0.82f, 0.22f));
            UiKit.Place(line.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(1f, Height - 22f));
        }

        internal bool Contains(Vector2 screenPoint) =>
            UiKit.Contains(stagePanel, screenPoint) || UiKit.Contains(economyPanel, screenPoint) || UiKit.Contains(teleporterPanel, screenPoint)
            || ItemsPanelContains(screenPoint) || DefeatContains(screenPoint);

        /// <summary>The army's items: a click opens the item panel.</summary>
        internal bool ItemsPanelContains(Vector2 screenPoint) => itemsPanel.gameObject.activeSelf && UiKit.Contains(itemsPanel, screenPoint);

        /// <summary>The purse / army panel: a click opens the barracks.</summary>
        internal bool GoldPanelContains(Vector2 screenPoint) => UiKit.Contains(economyPanel, screenPoint);

        /// <summary>The red "all heroes have fallen" strip: shown only while the defeat countdown runs; a click revives.</summary>
        internal bool DefeatContains(Vector2 screenPoint) => defeatPlate.enabled && UiKit.Contains(defeatPlate.rectTransform, screenPoint);

        internal bool TeleporterPanelContains(Vector2 screenPoint) => UiKit.Contains(teleporterPanel, screenPoint);

        private bool visibleFlag;

        internal void SetVisible(bool visible)
        {
            visibleFlag = visible;
            if (stagePanel.gameObject.activeSelf != visible) stagePanel.gameObject.SetActive(visible);
            if (economyPanel.gameObject.activeSelf != visible) economyPanel.gameObject.SetActive(visible);
            if (itemsPanel.gameObject.activeSelf != visible) itemsPanel.gameObject.SetActive(visible);
            if (!visible)
            {
                if (teleporterPanel.gameObject.activeSelf) teleporterPanel.gameObject.SetActive(false);
                defeatText.enabled = false;
                defeatPlate.enabled = false;
            }
        }

        /// <param name="itemsOpen">The item panel is open (its button in the strip is framed).</param>
        internal void Tick(float dt, bool itemsOpen)
        {
            Run run = Run.instance;
            if (!run || !visibleFlag) return;

            // --- stage (rebuilt when stage, second or difficulty changes) ---
            SceneInfo info = SceneInfo.instance;
            SceneDef scene = info && info.sceneDef ? info.sceneDef : null;
            int seconds = (int)run.GetRunStopwatch();
            int cleared = run.stageClearCount;
            DifficultyIndex difficultyIndex = run.selectedDifficulty;
            if (scene != infoScene || seconds != infoSeconds || cleared != infoCleared || difficultyIndex != infoDifficulty)
            {
                infoScene = scene;
                infoSeconds = seconds;
                infoCleared = cleared;
                infoDifficulty = difficultyIndex;

                string stage = scene ? Language.GetString(scene.nameToken) : "";
                string time = $"{seconds / 60:00}:{seconds % 60:00}";
                string difficulty = "";
                DifficultyDef dd = DifficultyCatalog.GetDifficultyDef(difficultyIndex);
                if (dd != null) difficulty = Language.GetString(dd.nameToken);
                UiKit.SetText(stageText, $"{Loc.Format("HIGHCOMMAND_TOP_STAGE", cleared + 1)}  <color=#A6B3C7>{stage}</color>");
                // the chests of the stage, opened / all: a reason to look round the whole map
                Army.Interactables.CountChests(out int opened, out int chests);
                string chestText = chests > 0 ? "    <color=#E8C060>" + Loc.Format("HIGHCOMMAND_TOP_CHESTS", opened, chests) + "</color>" : "";
                UiKit.SetText(metaText, $"{time}    {difficulty}{chestText}");
            }

            // --- purse ---
            HcState state = HcState.Instance;
            if (state == null)
            {
                UiKit.SetText(goldText, "");
                UiKit.SetText(armyText, "");
                UiKit.SetText(nextText, "");
                return;
            }

            shownGold = Mathf.MoveTowards(shownGold, state.Gold, Mathf.Max(8f, Mathf.Abs(state.Gold - shownGold) * 6f) * dt);
            int gold = Mathf.RoundToInt(shownGold);
            if (gold != shownGoldInt || goldText.text.Length == 0)
            {
                shownGoldInt = gold;
                UiKit.SetText(goldText, "$" + gold);
            }

            // --- army and the next hero ---
            var heroList = state.Heroes;
            int heroes = heroList.Count;
            int alive = 0;
            for (int i = 0; i < heroes; i++) if (heroList[i].IsAlive) alive++;
            int income = Mathf.RoundToInt(Economy.IncomePerMinute());
            int limit = state.ArmyLimit;
            int price = state.NextHeroPrice();
            bool affordable = state.Gold >= (uint)price;
            bool full = heroes >= limit;
            if (alive != subAlive || heroes != subHeroes || limit != subLimit || income != subIncome || price != subPrice
                || affordable != subAffordable || armyText.text.Length == 0)
            {
                subAlive = alive;
                subHeroes = heroes;
                subLimit = limit;
                subIncome = income;
                subPrice = price;
                subAffordable = affordable;

                UiKit.SetText(incomeText, income > 0 ? Loc.Format("HIGHCOMMAND_TOP_INCOME", income) : "");
                UiKit.SetText(armyText, alive + "/" + heroes);
                armyText.color = alive < heroes ? UiKit.Bad : UiKit.Text;
                UiKit.SetText(armySubText, Loc.Format("HIGHCOMMAND_TOP_ARMYLIMIT", limit));
                UiKit.SetText(nextLabel, Loc.Get("HIGHCOMMAND_TOP_NEXTHERO"));
                UiKit.SetText(nextText, "$" + price);
                nextText.color = full ? UiKit.TextDim : affordable ? UiKit.Good : UiKit.Gold;
                UiKit.SetText(keyText, Settings.KeyBarracks.Value.ToString());
            }

            if (UiKit.Contains(economyPanel, ClientInput.Mouse) && !ClientInput.MouseBlocked)
            {
                // the texts are built when the price changes, not every frame the pointer rests here
                if (tipPrice != price || tipTitle == null)
                {
                    tipPrice = price;
                    tipTitle = Loc.Get("HIGHCOMMAND_TOP_NEXTHERO") + ": $" + price;
                    tipBody = Loc.Get("HIGHCOMMAND_TOP_BARRACKS_TIP") + "\n\n[" + Settings.KeyBarracks.Value + "]";
                }
                Tooltip.Show(tipTitle, tipBody);
            }

            TickTeleporter(state);
            TickDefeat(state);
            TickItems(itemsOpen);
        }

        private int itemsCount = -1;
        private string itemsTipTitle, itemsTipBody;

        private void TickItems(bool open)
        {
            if (itemsFrame.enabled != open) itemsFrame.enabled = open;

            // every item the army owns, whoever carries it (the pool is the army's inventory)
            int count = 0;
            Inventory pool = LootService.Pool;
            if (pool)
            {
                var order = pool.itemAcquisitionOrder;
                for (int i = 0; i < order.Count; i++) count += pool.GetItemCountPermanent(order[i]);
            }
            if (count != itemsCount || itemsText.text.Length == 0)
            {
                itemsCount = count;
                UiKit.SetText(itemsText, Loc.Get("HIGHCOMMAND_TOP_ITEMS"));
                UiKit.SetText(itemsSubText, Loc.Format("HIGHCOMMAND_TOP_ITEMS_COUNT", count));
                UiKit.SetText(itemsKey, Settings.KeyInventory.Value.ToString());
                itemsTipTitle = Loc.Get("HIGHCOMMAND_TOP_ITEMS");
                itemsTipBody = Loc.Get("HIGHCOMMAND_HELP_INVENTORY") + "\n\n[" + Settings.KeyInventory.Value + "]";
            }
            if (ItemsPanelContains(ClientInput.Mouse) && !ClientInput.MouseBlocked) Tooltip.Show(itemsTipTitle, itemsTipBody);
        }

        // what the texts show now
        private SceneDef infoScene;
        private int infoSeconds = -1, infoCleared = -1;
        private DifficultyIndex infoDifficulty = DifficultyIndex.Invalid;
        private int shownGoldInt = int.MinValue;
        private int subAlive = -1, subHeroes = -1, subLimit = -1, subIncome = -1, subPrice = -1;
        private bool subAffordable;
        private int tipPrice = -1;
        private string tipTitle, tipBody;
        private int teleporterKey = int.MinValue;
        private int defeatKey = int.MinValue;

        private void TickTeleporter(HcState state)
        {
            bool show = state.Teleporter != HcState.TeleState.None;
            if (teleporterPanel.gameObject.activeSelf != show) teleporterPanel.gameObject.SetActive(show);
            if (!show) return;

            Color barColor = new Color(0.45f, 0.75f, 1f, 1f);
            float fraction = state.TeleCharge;
            int percent = Mathf.FloorToInt(Mathf.Clamp01(fraction) * 100f);
            switch (state.Teleporter)
            {
                case HcState.TeleState.Idle:
                    fraction = 0f;
                    percent = 0;
                    break;
                case HcState.TeleState.Charging:
                    break;
                case HcState.TeleState.BossAlive:
                    barColor = new Color(1f, 0.4f, 0.3f, 1f);
                    fraction = 1f;
                    percent = 0;
                    break;
                default: // charged / leaving
                    barColor = UiKit.Good;
                    fraction = 1f;
                    percent = 0;
                    break;
            }

            // the text depends on the state and, while charging, on the whole percent only
            int key = (int)state.Teleporter * 1000 + percent;
            if (key != teleporterKey)
            {
                teleporterKey = key;
                string text;
                switch (state.Teleporter)
                {
                    case HcState.TeleState.Idle: text = Loc.Format("HIGHCOMMAND_TP_STATE_IDLE", Settings.KeyTeleporter.Value); break;
                    case HcState.TeleState.Charging: text = Loc.Format("HIGHCOMMAND_TP_STATE_CHARGING", percent); break;
                    case HcState.TeleState.BossAlive: text = Loc.Get("HIGHCOMMAND_TP_STATE_BOSS"); break;
                    case HcState.TeleState.Charged: text = Loc.Format("HIGHCOMMAND_TP_STATE_CHARGED", Settings.KeyTeleporter.Value); break;
                    default: text = Loc.Get("HIGHCOMMAND_TP_STATE_LEAVING"); break;
                }
                UiKit.SetText(teleporterText, text);
            }
            teleporterBar.color = barColor;
            Color glyphColor = state.Teleporter == HcState.TeleState.Idle ? UiKit.TextDim : barColor;
            if (teleporterGlyph.color != glyphColor) teleporterGlyph.color = glyphColor;
            float anchor = Mathf.Clamp01(fraction);
            if (Mathf.Abs(teleporterBar.rectTransform.anchorMax.x - anchor) > 0.0005f)
                teleporterBar.rectTransform.anchorMax = new Vector2(anchor, 1f);
        }

        private void TickDefeat(HcState state)
        {
            bool show = state.DefeatSeconds >= 0f;
            defeatText.enabled = show;
            defeatPlate.enabled = show;
            if (!show) return;

            int seconds = Mathf.CeilToInt(state.DefeatSeconds);
            int key = seconds * 4 + (state.DefeatCanRevive ? 1 : 0) + (state.DefeatAutoRevive ? 2 : 0);
            if (key != defeatKey)
            {
                defeatKey = key;
                string text;
                if (state.DefeatAutoRevive)
                    text = Loc.Format("HIGHCOMMAND_DEFEAT_AUTO_REVIVE", seconds, state.RevivePrice(), Settings.KeyRevive.Value);
                else if (state.DefeatCanRevive)
                    text = Loc.Format("HIGHCOMMAND_DEFEAT_CAN_REVIVE", seconds, Settings.KeyRevive.Value);
                else
                    text = Loc.Format("HIGHCOMMAND_DEFEAT_BROKE", seconds);
                UiKit.SetText(defeatText, text);
                float width = Mathf.Clamp(defeatText.GetPreferredValues(text).x + 70f, 360f, 1500f);
                defeatPlate.rectTransform.sizeDelta = new Vector2(width, 56f);
            }
            // pulse a little so it cannot be missed
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
            Color c = UiKit.Bad;
            c.a = pulse;
            defeatText.color = c;
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (stagePanel) Object.Destroy(stagePanel.gameObject);
            if (economyPanel) Object.Destroy(economyPanel.gameObject);
            if (itemsPanel) Object.Destroy(itemsPanel.gameObject);
            if (teleporterPanel) Object.Destroy(teleporterPanel.gameObject);
            if (defeatText) Object.Destroy(defeatText.gameObject);
            if (defeatPlate) Object.Destroy(defeatPlate.gameObject);
        }
    }
}
