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

            ClientInput.RegisterUiHitTest(Contains);
        }

        internal bool Contains(Vector2 screenPoint) =>
            UiKit.Contains(infoPanel, screenPoint) || UiKit.Contains(goldPanel, screenPoint) || UiKit.Contains(teleporterPanel, screenPoint);

        internal bool GoldPanelContains(Vector2 screenPoint) => UiKit.Contains(goldPanel, screenPoint);

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

            // --- info ---
            string stage = "";
            SceneInfo info = SceneInfo.instance;
            if (info && info.sceneDef) stage = Language.GetString(info.sceneDef.nameToken);
            float t = run.GetRunStopwatch();
            string time = $"{(int)(t / 60f):00}:{(int)(t % 60f):00}";
            string difficulty = "";
            DifficultyDef dd = DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty);
            if (dd != null) difficulty = Language.GetString(dd.nameToken);
            infoText.text = $"{Loc.Format("HIGHCOMMAND_TOP_STAGE", run.stageClearCount + 1)}  {stage}\n<size=80%><color=#A6B3C7>{time}   {difficulty}</color></size>";

            // --- gold ---
            HcState state = HcState.Instance;
            if (state == null) { goldText.text = ""; subText.text = ""; return; }

            shownGold = Mathf.MoveTowards(shownGold, state.Gold, Mathf.Max(8f, Mathf.Abs(state.Gold - shownGold) * 6f) * dt);
            goldText.text = "$" + Mathf.RoundToInt(shownGold);

            int heroes = state.Heroes.Count;
            int alive = 0;
            foreach (Hero h in state.Heroes) if (h.IsAlive) alive++;
            float income = Economy.IncomePerMinute();
            string incomeText = income > 0f ? $"  +{Mathf.RoundToInt(income)}/{Loc.Get("HIGHCOMMAND_TOP_PERMIN")}" : "";
            subText.text = Loc.Format("HIGHCOMMAND_TOP_ARMY", alive, heroes, state.ArmyLimit)
                           + "   " + Loc.Format("HIGHCOMMAND_TOP_NEXT", state.NextHeroPrice()) + incomeText
                           + "   [" + Settings.KeyBarracks.Value + "]";

            TickTeleporter(state);
            TickDefeat(state);
        }

        private void TickTeleporter(HcState state)
        {
            bool show = state.Teleporter != HcState.TeleState.None;
            if (teleporterPanel.gameObject.activeSelf != show) teleporterPanel.gameObject.SetActive(show);
            if (!show) return;

            string text;
            Color barColor = new Color(0.45f, 0.75f, 1f, 1f);
            float fraction = state.TeleCharge;
            switch (state.Teleporter)
            {
                case HcState.TeleState.Idle:
                    text = Loc.Format("HIGHCOMMAND_TP_STATE_IDLE", Settings.KeyTeleporter.Value);
                    fraction = 0f;
                    break;
                case HcState.TeleState.Charging:
                    text = Loc.Format("HIGHCOMMAND_TP_STATE_CHARGING", Mathf.FloorToInt(Mathf.Clamp01(fraction) * 100f));
                    break;
                case HcState.TeleState.BossAlive:
                    text = Loc.Get("HIGHCOMMAND_TP_STATE_BOSS");
                    barColor = new Color(1f, 0.4f, 0.3f, 1f);
                    fraction = 1f;
                    break;
                case HcState.TeleState.Charged:
                    text = Loc.Format("HIGHCOMMAND_TP_STATE_CHARGED", Settings.KeyTeleporter.Value);
                    barColor = UiKit.Good;
                    fraction = 1f;
                    break;
                default:
                    text = Loc.Get("HIGHCOMMAND_TP_STATE_LEAVING");
                    barColor = UiKit.Good;
                    fraction = 1f;
                    break;
            }
            teleporterText.text = text;
            teleporterBar.color = barColor;
            teleporterBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        }

        private void TickDefeat(HcState state)
        {
            bool show = state.DefeatSeconds >= 0f;
            defeatText.enabled = show;
            if (!show) return;

            int seconds = Mathf.CeilToInt(state.DefeatSeconds);
            defeatText.text = state.DefeatCanRevive
                ? Loc.Format("HIGHCOMMAND_DEFEAT_CAN_REVIVE", seconds, Settings.KeyRevive.Value)
                : Loc.Format("HIGHCOMMAND_DEFEAT_BROKE", seconds);
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
