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
        private readonly TextMeshProUGUI text;
        private float shownUntil;
        private float fadeStart;

        internal ToastView(Transform parent)
        {
            text = UiKit.NewText("Toast", parent, "", 28f, UiKit.Text, TextAlignmentOptions.Center);
            UiKit.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1200f, 44f));
            text.enabled = false;
        }

        internal void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= shownUntil && Toast.TryTake(out string next))
            {
                text.text = next;
                text.enabled = true;
                fadeStart = now + 1.4f;
                shownUntil = now + 1.9f;
            }

            if (!text.enabled) return;
            if (now >= shownUntil) { text.enabled = false; return; }
            float alpha = now < fadeStart ? 1f : Mathf.Clamp01(1f - (now - fadeStart) / (shownUntil - fadeStart));
            Color c = UiKit.Text;
            c.a = alpha;
            text.color = c;
        }

        internal void Destroy()
        {
            if (text) Object.Destroy(text.gameObject);
        }
    }
}
