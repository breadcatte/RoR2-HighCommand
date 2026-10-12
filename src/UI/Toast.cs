using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HighCommand.UI
{
    /// <summary>Short messages near the top of the screen ("Stance: defensive", "Not enough gold" ...).</summary>
    internal static class Toast
    {
        private static readonly Queue<string> Pending = new Queue<string>();

        internal static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Pending.Enqueue(message);
            while (Pending.Count > 4) Pending.Dequeue();
        }

        /// <summary>A message about something that did not work: same toast, plus the game's "cannot afford" sound.</summary>
        internal static void Error(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Show(message);
            try { RoR2.Util.PlaySound("Play_UI_insufficient_funds", RoR2.RoR2Application.instance ? RoR2.RoR2Application.instance.gameObject : null); }
            catch (System.Exception) { /* sounds are a nicety */ }
        }

        internal static bool TryTake(out string message)
        {
            if (Pending.Count > 0) { message = Pending.Dequeue(); return true; }
            message = null;
            return false;
        }

        internal static void Clear() => Pending.Clear();
    }

    /// <summary>Draws the toast messages: one at a time, fading out.</summary>
    internal sealed class ToastView
    {
        private static readonly Color PlateColor = new Color(0.04f, 0.05f, 0.07f, 0.86f);

        private readonly RectTransform plate;
        private readonly UnityEngine.UI.Image plateImage;
        private readonly UnityEngine.UI.Image bar;
        private readonly TextMeshProUGUI text;
        private float shownUntil;
        private float fadeStart;
        private float lastAlpha = -1f;

        internal ToastView(Transform parent)
        {
            plate = UiKit.NewRect("Toast", parent);
            UiKit.Place(plate, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(300f, 44f));
            plateImage = plate.gameObject.AddComponent<UnityEngine.UI.Image>();
            UiKit.StyleOutline(plateImage, 9f);
            plateImage.color = PlateColor;
            plateImage.raycastTarget = false;

            bar = UiKit.NewImage("Bar", plate, UiKit.White, UiKit.Accent);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(3f, 22f));

            text = UiKit.NewText("Text", plate, "", 25f, UiKit.Text, TextAlignmentOptions.Center);
            UiKit.Stretch(text.rectTransform, 22, 0, 14, 0);
            plate.gameObject.SetActive(false);
        }

        internal void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= shownUntil && Toast.TryTake(out string next))
            {
                text.text = next;
                float width = Mathf.Clamp(text.GetPreferredValues(next).x + 52f, 180f, 1400f);
                plate.sizeDelta = new Vector2(width, 44f);
                plate.gameObject.SetActive(true);
                plate.SetAsLastSibling(); // above an open barracks / item panel
                fadeStart = now + 1.4f;
                shownUntil = now + 1.9f;
                lastAlpha = -1f;
            }

            if (!plate.gameObject.activeSelf) return;
            if (now >= shownUntil) { plate.gameObject.SetActive(false); return; }
            float alpha = now < fadeStart ? 1f : Mathf.Clamp01(1f - (now - fadeStart) / (shownUntil - fadeStart));
            if (Mathf.Abs(alpha - lastAlpha) < 0.01f) return;
            lastAlpha = alpha;

            Color c = PlateColor;
            c.a *= alpha;
            plateImage.color = c;
            Color a = UiKit.Accent;
            a.a = alpha;
            bar.color = a;
            Color t = UiKit.Text;
            t.a = alpha;
            text.color = t;
        }

        internal void Destroy()
        {
            if (plate) Object.Destroy(plate.gameObject);
        }
    }
}
