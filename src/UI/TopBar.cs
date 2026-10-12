using HighCommand.Army;
using HighCommand.Client;
using HighCommand.Mode;
using RoR2;
using TMPro;
using UnityEngine;

namespace HighCommand.UI
{
    /// <summary>
    /// Top strip: stage / time / difficulty on the left; gold, income, army size and the price of the next hero in the
    /// middle. Clicking the gold block opens the barracks.
    /// </summary>
    internal sealed class TopBar
    {
        private readonly RectTransform infoPanel;
        private readonly TextMeshProUGUI infoText;
        private readonly RectTransform goldPanel;
        private readonly TextMeshProUGUI goldText;
        private readonly TextMeshProUGUI subText;

        private readonly RectTransform teleporterPanel;
        private readonly TextMeshProUGUI teleporterText;
        private readonly RectTransform teleporterBarBack;
        private readonly UnityEngine.UI.Image teleporterBar;
        private readonly TextMeshProUGUI defeatText;

        private float shownGold;

        internal TopBar(Transform parent)
        {
            teleporterPanel = UiKit.NewPanel("TeleporterPanel", parent, UiKit.PanelColor);
            UiKit.Place(teleporterPanel, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(276f, -16f), new Vector2(330f, 72f));
            teleporterText = UiKit.NewText("Text", teleporterPanel, "", 20f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(teleporterText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(304f, 28f));
            teleporterText.enableAutoSizing = true;
            teleporterText.fontSizeMin = 11f;
            teleporterText.fontSizeMax = 20f;
            teleporterBarBack = UiKit.NewImage("BarBack", teleporterPanel, UiKit.White, new Color(0.14f, 0.15f, 0.18f, 1f)).rectTransform;
            UiKit.Place(teleporterBarBack, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(302f, 14f));
            teleporterBar = UiKit.NewImage("Bar", teleporterBarBack, UiKit.White, new Color(0.45f, 0.75f, 1f, 1f));
            teleporterBar.rectTransform.anchorMin = Vector2.zero;
            teleporterBar.rectTransform.anchorMax = new Vector2(0f, 1f);
            teleporterBar.rectTransform.offsetMin = teleporterBar.rectTransform.offsetMax = Vector2.zero;

            defeatText = UiKit.NewText("Defeat", parent, "", 32f, UiKit.Bad, TextAlignmentOptions.Center);
            UiKit.Place(defeatText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1500f, 44f));
            defeatText.enabled = false;

            infoPanel = UiKit.NewPanel("InfoPanel", parent, UiKit.PanelColor);
            UiKit.Place(infoPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(430f, 64f));
            infoText = UiKit.NewText("Info", infoPanel, "", 22f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Stretch(infoText.rectTransform, 14, 4, 10, 4);
            infoText.enableWordWrapping = false;

            goldPanel = UiKit.NewPanel("GoldPanel", parent, UiKit.PanelColor);
            UiKit.Place(goldPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(520f, 72f));
            goldText = UiKit.NewText("Gold", goldPanel, "", 34f, UiKit.Gold, TextAlignmentOptions.Left);
            UiKit.Place(goldText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 6f), new Vector2(240f, 44f));
            subText = UiKit.NewText("Sub", goldPanel, "", 19f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(subText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, -22f), new Vector2(490f, 26f));
            // big numbers (a rich army, a high income) must shrink the line, not run out of the panel
            subText.enableAutoSizing = true;
            subText.fontSizeMin = 12f;
            subText.fontSizeMax = 19f;

            ClientInput.RegisterUiHitTest(Contains);
        }

        internal bool Contains(Vector2 screenPoint) =>
            UiKit.Contains(infoPanel, screenPoint) || UiKit.Contains(goldPanel, screenPoint) || UiKit.Contains(teleporterPanel, screenPoint)
            || DefeatContains(screenPoint);

        internal bool GoldPanelContains(Vector2 screenPoint) => UiKit.Contains(goldPanel, screenPoint);

        /// <summary>The red "all heroes have fallen" strip: shown only while the defeat countdown runs; a click revives.</summary>
        internal bool DefeatContains(Vector2 screenPoint) => defeatText.enabled && UiKit.Contains(defeatText.rectTransform, screenPoint);

        internal bool TeleporterPanelContains(Vector2 screenPoint) => UiKit.Contains(teleporterPanel, screenPoint);

        private bool visibleFlag;

        internal void SetVisible(bool visible)
        {
            visibleFlag = visible;
            if (infoPanel.gameObject.activeSelf != visible) infoPanel.gameObject.SetActive(visible);
            if (goldPanel.gameObject.activeSelf != visible) goldPanel.gameObject.SetActive(visible);
            if (!visible)
            {
                if (teleporterPanel.gameObject.activeSelf) teleporterPanel.gameObject.SetActive(false);
                defeatText.enabled = false;
            }
        }

        internal void Tick(float dt)
        {
            Run run = Run.instance;
            if (!run || !visibleFlag) return;

            // --- info (rebuilt when stage, second or difficulty changes) ---
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
                UiKit.SetText(infoText, $"{Loc.Format("HIGHCOMMAND_TOP_STAGE", cleared + 1)}  {stage}\n<size=80%><color=#A6B3C7>{time}   {difficulty}</color></size>");
            }

            // --- gold ---
            HcState state = HcState.Instance;
            if (state == null) { UiKit.SetText(goldText, ""); UiKit.SetText(subText, ""); return; }

            shownGold = Mathf.MoveTowards(shownGold, state.Gold, Mathf.Max(8f, Mathf.Abs(state.Gold - shownGold) * 6f) * dt);
            int gold = Mathf.RoundToInt(shownGold);
            if (gold != shownGoldInt || goldText.text.Length == 0)
            {
                shownGoldInt = gold;
                UiKit.SetText(goldText, "$" + gold);
            }

            var heroList = state.Heroes;
            int heroes = heroList.Count;
            int alive = 0;
            for (int i = 0; i < heroes; i++) if (heroList[i].IsAlive) alive++;
            int income = Mathf.RoundToInt(Economy.IncomePerMinute());
            int limit = state.ArmyLimit;
            int price = state.NextHeroPrice();
            if (alive != subAlive || heroes != subHeroes || limit != subLimit || income != subIncome || price != subPrice
                || subText.text.Length == 0)
            {
                subAlive = alive;
                subHeroes = heroes;
                subLimit = limit;
                subIncome = income;
                subPrice = price;
                string incomeText = income > 0 ? $"  +{income}/{Loc.Get("HIGHCOMMAND_TOP_PERMIN")}" : "";
                UiKit.SetText(subText, Loc.Format("HIGHCOMMAND_TOP_ARMY", alive, heroes, limit)
                                       + "   " + Loc.Format("HIGHCOMMAND_TOP_NEXT", price) + incomeText
                                       + "   [" + Settings.KeyBarracks.Value + "]");
            }

            TickTeleporter(state);
            TickDefeat(state);
        }

        // what the texts show now
        private SceneDef infoScene;
        private int infoSeconds = -1, infoCleared = -1;
        private DifficultyIndex infoDifficulty = DifficultyIndex.Invalid;
        private int shownGoldInt = int.MinValue;
        private int subAlive = -1, subHeroes = -1, subLimit = -1, subIncome = -1, subPrice = -1;
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
            float anchor = Mathf.Clamp01(fraction);
            if (Mathf.Abs(teleporterBar.rectTransform.anchorMax.x - anchor) > 0.0005f)
                teleporterBar.rectTransform.anchorMax = new Vector2(anchor, 1f);
        }

        private void TickDefeat(HcState state)
        {
            bool show = state.DefeatSeconds >= 0f;
            defeatText.enabled = show;
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
            if (infoPanel) Object.Destroy(infoPanel.gameObject);
            if (goldPanel) Object.Destroy(goldPanel.gameObject);
            if (teleporterPanel) Object.Destroy(teleporterPanel.gameObject);
            if (defeatText) Object.Destroy(defeatText.gameObject);
        }
    }
}
