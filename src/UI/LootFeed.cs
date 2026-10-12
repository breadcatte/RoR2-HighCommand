using System.Collections.Generic;
using HighCommand.Mode;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Right-hand list of items the army just gained: icon, name in the rarity colour, count. Fills from the server's
    /// item-feed RPC, so it works the same on a host and on a client.
    /// </summary>
    internal sealed class LootFeed
    {
        private const int MaxRows = 7;
        private const float RowHeight = 40f;
        private const float Life = 6.5f;

        private sealed class Row
        {
            internal RectTransform root;
            internal Image back;
            internal RawImage icon;
            internal TextMeshProUGUI label;
            internal PickupIndex pickup;
            internal int count;
            internal float born;
            internal float fade = -1f; // the last fade written (-1 = write the colours again)
            internal int slot = -1;    // the last list position written
        }

        private readonly RectTransform container;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Row> pool = new List<Row>();

        internal LootFeed(Transform parent)
        {
            container = UiKit.NewRect("LootFeed", parent);
            UiKit.Place(container, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -120f), new Vector2(380f, MaxRows * RowHeight));
            HcState.ItemGained += OnItemGained;
        }

        internal void Destroy()
        {
            HcState.ItemGained -= OnItemGained;
            if (container) Object.Destroy(container.gameObject);
        }

        private void OnItemGained(PickupIndex pickup, int count)
        {
            // merge with a recent row of the same item
            foreach (Row r in rows)
            {
                if (r.pickup == pickup && Time.unscaledTime - r.born < Life * 0.6f)
                {
                    r.count += count;
                    r.born = Time.unscaledTime;
                    Refresh(r);
                    return;
                }
            }

            Row row = Acquire();
            row.pickup = pickup;
            row.count = count;
            row.born = Time.unscaledTime;
            Refresh(row);
            rows.Insert(0, row);
            while (rows.Count > MaxRows)
            {
                Row old = rows[rows.Count - 1];
                rows.RemoveAt(rows.Count - 1);
                old.root.gameObject.SetActive(false);
                pool.Add(old);
            }

            try { Util.PlaySound("Play_UI_item_pickup", RoR2Application.instance.gameObject); }
            catch (System.Exception) { /* a missing sound event is not worth an error */ }
        }

        private Row Acquire()
        {
            if (pool.Count > 0)
            {
                Row r = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                r.root.gameObject.SetActive(true);
                return r;
            }

            var row = new Row();
            row.root = UiKit.NewRect("Row", container);
            row.root.anchorMin = row.root.anchorMax = new Vector2(1f, 1f);
            row.root.pivot = new Vector2(1f, 1f);
            row.root.sizeDelta = new Vector2(380f, RowHeight - 4f);

            row.back = row.root.gameObject.AddComponent<Image>();
            row.back.sprite = UiKit.White;
            row.back.color = new Color(0.04f, 0.05f, 0.07f, 0.75f);
            row.back.raycastTarget = false;

            row.icon = UiKit.NewRawImage("Icon", row.root, null, Color.white);
            UiKit.Place(row.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(32f, 32f));

            row.label = UiKit.NewText("Label", row.root, "", 20f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(row.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(330f, 30f));
            row.label.enableAutoSizing = true;
            row.label.fontSizeMin = 12f;
            row.label.fontSizeMax = 20f;
            return row;
        }

        private static void Refresh(Row r)
        {
            r.fade = -1f;
            r.slot = -1;
            PickupDef def = PickupCatalog.GetPickupDef(r.pickup);
            if (def == null) { r.label.text = "?"; return; }
            bool hasIcon = PickupIcons.Apply(r.icon, def);
            r.icon.color = hasIcon ? Color.white : def.baseColor;

            string name = Language.GetString(def.nameToken);
            Color color = def.baseColor;
            string hex = ColorUtility.ToHtmlStringRGB(color);
            r.label.text = r.count > 1 ? $"<color=#{hex}>{name}</color>  x{r.count}" : $"<color=#{hex}>{name}</color>";
        }

        internal void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                Row r = rows[i];
                if (now - r.born > Life)
                {
                    rows.RemoveAt(i);
                    r.root.gameObject.SetActive(false);
                    pool.Add(r);
                    continue;
                }
                // fully visible for most of its life: the colours are written only while it fades
                float fade = Mathf.Clamp01((Life - (now - r.born)) * 1.5f);
                if (r.fade != fade && (fade < 1f || r.fade != 1f))
                {
                    r.fade = fade;
                    r.back.color = new Color(0.04f, 0.05f, 0.07f, 0.75f * fade);
                    r.icon.color = new Color(1f, 1f, 1f, fade);
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].slot == i) continue;
                rows[i].slot = i;
                rows[i].root.anchoredPosition = new Vector2(0f, -i * RowHeight);
            }
        }
    }
}
