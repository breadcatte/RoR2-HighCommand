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

        private float shownGold;

        internal TopBar(Transform parent)
        {
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

        internal bool Contains(Vector2 screenPoint) => UiKit.Contains(infoPanel, screenPoint) || UiKit.Contains(goldPanel, screenPoint);

        internal bool GoldPanelContains(Vector2 screenPoint) => UiKit.Contains(goldPanel, screenPoint);

        internal void SetVisible(bool visible)
        {
            if (infoPanel.gameObject.activeSelf != visible) infoPanel.gameObject.SetActive(visible);
            if (goldPanel.gameObject.activeSelf != visible) goldPanel.gameObject.SetActive(visible);
        }

        internal void Tick(float dt)
        {
            Run run = Run.instance;
            if (!run) return;

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
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (infoPanel) Object.Destroy(infoPanel.gameObject);
            if (goldPanel) Object.Destroy(goldPanel.gameObject);
        }
    }
}
