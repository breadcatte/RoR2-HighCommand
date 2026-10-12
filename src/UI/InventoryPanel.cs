using System;
using System.Collections.Generic;
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
    /// "Who carries what": one row per hero with their items and equipment. Drag an item (Shift: the whole stack) onto
    /// another hero's row to hand it over; drag equipment to swap; the footer shows spare equipment and a button that
    /// deals everything out again by role. Reads the heroes' inventories, which the game replicates to every machine.
    /// </summary>
    internal sealed class InventoryPanel
    {
        private const float PanelW = 1240f;
        private const float HeaderH = 78f;
        private const float FooterH = 104f;
        private const float MaxPanelH = 980f;
        private const float RowGap = 6f;
        private const float LeftBlockW = 250f;
        private const float Margin = 18f;
        private static readonly float[] CellSizes = { 46f, 40f, 34f, 28f };

        private sealed class Cell
        {
            internal RectTransform root;
            internal Image frame;
            internal Image fill;
            internal RawImage icon;
            internal TextMeshProUGUI count;
            internal ItemIndex item = ItemIndex.None;
            internal EquipmentIndex equipment = EquipmentIndex.None;
            internal int spareIndex = -1;
            internal int slot;
            internal Hero owner;
        }

        private sealed class Row
        {
            internal Hero hero;
            internal RectTransform root;
            internal Image frame;
            internal RawImage portrait;
            internal TextMeshProUGUI name;
            internal TextMeshProUGUI role;
            internal TextMeshProUGUI info;
            internal TextMeshProUGUI more;
            internal TextMeshProUGUI empty;
            internal readonly List<Cell> equipCells = new List<Cell>();
            internal readonly List<Cell> cells = new List<Cell>();
            internal readonly List<KeyValuePair<ItemIndex, int>> items = new List<KeyValuePair<ItemIndex, int>>();
            internal float height;
        }

        private readonly RectTransform root;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI hint;
        private readonly RectTransform rowsHost;
        private readonly RectTransform footer;
        private readonly TextMeshProUGUI spareLabel;
        private readonly RectTransform autoButton;
        private readonly Image autoBack;
        private readonly TextMeshProUGUI autoLabel;
        private readonly RectTransform ghost;
        private readonly RawImage ghostIcon;
        private readonly TextMeshProUGUI ghostCount;

        private static readonly IReadOnlyList<Hero> NoHeroes = new List<Hero>();
        private static readonly IReadOnlyList<EquipmentIndex> NoEquipment = new List<EquipmentIndex>();

        private readonly List<Row> rows = new List<Row>();
        private readonly List<Cell> spareCells = new List<Cell>();
        private int lastSignature = int.MinValue;

        // drag state
        private bool dragging;
        private ItemIndex dragItem = ItemIndex.None;
        private EquipmentIndex dragEquipment = EquipmentIndex.None;
        private int dragSlot;
        private int dragSpareIndex = -1;
        private Hero dragFrom;
        private int dragCount;

        internal bool Visible => root && root.gameObject.activeSelf;

        internal InventoryPanel(Transform parent)
        {
            root = UiKit.NewPanel("Inventory", parent, UiKit.PanelDark);
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(PanelW, 600f));

            title = UiKit.NewText("Title", root, "", 32f, UiKit.Accent, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -12f), new Vector2(PanelW - 2f * Margin, 40f));

            hint = UiKit.NewText("Hint", root, "", 18f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -52f), new Vector2(PanelW - 2f * Margin, 24f));

            rowsHost = UiKit.NewRect("Rows", root);
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -HeaderH), new Vector2(PanelW - 2f * Margin, 400f));

            footer = UiKit.NewRect("Footer", root);
            UiKit.Place(footer, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, 10f), new Vector2(PanelW - 2f * Margin, FooterH - 14f));

            spareLabel = UiKit.NewText("SpareLabel", footer, "", 18f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(spareLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -6f), new Vector2(760f, 26f));

            autoButton = UiKit.NewRect("AutoButton", footer);
            UiKit.Place(autoButton, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(260f, 52f));
            autoBack = autoButton.gameObject.AddComponent<Image>();
            UiKit.StyleOutline(autoBack, 8f);
            autoBack.color = new Color(0.16f, 0.2f, 0.27f, 1f);
            autoBack.raycastTarget = false;
            autoLabel = UiKit.NewText("Label", autoButton, "", 21f, UiKit.Text, TextAlignmentOptions.Center);
            UiKit.Stretch(autoLabel.rectTransform);

            ghost = UiKit.NewRect("DragGhost", root);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0f, 0f);
            ghost.pivot = new Vector2(0.5f, 0.5f);
            ghost.sizeDelta = new Vector2(52f, 52f);
            ghostIcon = UiKit.NewRawImage("Icon", ghost, null, new Color(1f, 1f, 1f, 0.9f));
            UiKit.Stretch(ghostIcon.rectTransform);
            ghostCount = UiKit.NewText("Count", ghost, "", 20f, UiKit.Gold, TextAlignmentOptions.BottomRight);
            UiKit.Stretch(ghostCount.rectTransform);
            ghost.gameObject.SetActive(false);

            ClientInput.RegisterUiHitTest(Contains);
            root.gameObject.SetActive(false);
        }

        internal bool Contains(Vector2 screenPoint) => Visible && UiKit.Contains(root, screenPoint);

        internal void Toggle()
        {
            if (!root) return;
            bool show = !root.gameObject.activeSelf;
            if (show)
            {
                lastSignature = int.MinValue;
                root.SetAsLastSibling();
            }
            root.gameObject.SetActive(show);
            if (!show) CancelDrag();
        }

        internal void Hide()
        {
            if (!root) return;
            CancelDrag();
            root.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) UnityEngine.Object.Destroy(root.gameObject);
        }

        // ---- data helpers ---------------------------------------------------------------------------------

        /// <summary>The host's sharing mode (replicated), not this machine's own config value.</summary>
        private static bool IsMirror(HcState state)
        {
            return state != null ? state.ItemMirror : LobbyRules.ItemMirror;
        }

        private static PickupDef PickupOf(ItemIndex item)
        {
            PickupIndex index = PickupCatalog.FindPickupIndex(item);
            return index.isValid ? PickupCatalog.GetPickupDef(index) : null;
        }

        private static PickupDef PickupOf(EquipmentIndex equipment)
        {
            PickupIndex index = PickupCatalog.FindPickupIndex(equipment);
            return index.isValid ? PickupCatalog.GetPickupDef(index) : null;
        }

        private static void CollectItems(Hero hero, List<KeyValuePair<ItemIndex, int>> into)
        {
            into.Clear();
            CharacterMaster master = hero.Master;
            Inventory inv = master ? master.inventory : null;
            if (!inv) return;

            foreach (ItemIndex idx in inv.itemAcquisitionOrder)
            {
                int count = inv.GetItemCountPermanent(idx);
                if (count <= 0) continue;
                ItemDef def = ItemCatalog.GetItemDef(idx);
                if (def == null || def.hidden || def.tier == ItemTier.NoTier) continue;
                into.Add(new KeyValuePair<ItemIndex, int>(idx, count));
            }
            // rarest first; equal tiers keep the order the items were picked up in
            var order = new Dictionary<ItemIndex, int>();
            for (int i = 0; i < into.Count; i++) order[into[i].Key] = i;
            into.Sort((a, b) =>
            {
                int ta = ItemDistributor.TierRank(ItemCatalog.GetItemDef(a.Key).tier);
                int tb = ItemDistributor.TierRank(ItemCatalog.GetItemDef(b.Key).tier);
                return ta != tb ? ta.CompareTo(tb) : order[a.Key].CompareTo(order[b.Key]);
            });
        }

        private static int Signature(IReadOnlyList<Hero> heroes, IReadOnlyList<EquipmentIndex> spare)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < heroes.Count; i++)
                {
                    Hero hero = heroes[i];
                    h = h * 31 + hero.Id;
                    h = h * 31 + (hero.IsAlive ? 1 : 0);
                    CharacterMaster m = hero.Master;
                    Inventory inv = m ? m.inventory : null;
                    if (!inv) continue;
                    int slots = HeroEquipment.SlotCount(inv);
                    for (int s = 0; s < slots; s++) h = h * 31 + (int)HeroEquipment.InSlot(inv, s);
                    foreach (ItemIndex idx in inv.itemAcquisitionOrder)
                        h = h * 31 + ((int)idx * 7 + inv.GetItemCountPermanent(idx));
                }
                for (int i = 0; i < spare.Count; i++) h = h * 31 + (int)spare[i] + 3;
                return h;
            }
        }

        // ---- building ----------------------------------------------------------------------------------------

        private Cell NewCell(Transform parent, string name)
        {
            var c = new Cell();
            c.root = UiKit.NewRect(name, parent);
            c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 1f);
            c.root.pivot = new Vector2(0f, 1f);
            c.frame = c.root.gameObject.AddComponent<Image>();
            c.frame.sprite = UiKit.White;
            c.frame.color = UiKit.Border;
            c.frame.raycastTarget = false;
            c.fill = UiKit.NewImage("Fill", c.root, UiKit.White, new Color(0.03f, 0.04f, 0.06f, 1f));
            UiKit.Stretch(c.fill.rectTransform, 2, 2, 2, 2);
            c.icon = UiKit.NewRawImage("Icon", c.root, null, Color.white);
            UiKit.Stretch(c.icon.rectTransform, 3, 3, 3, 3);
            c.count = UiKit.NewText("Count", c.root, "", 16f, UiKit.Text, TextAlignmentOptions.BottomRight);
            UiKit.Stretch(c.count.rectTransform, 0, 1, 3, 0);
            c.count.enableAutoSizing = true;
            c.count.fontSizeMin = 10f;
            c.count.fontSizeMax = 18f;
            return c;
        }

        private Row NewRow(Hero hero)
        {
            var row = new Row { hero = hero };
            row.root = UiKit.NewRect("Row", rowsHost);
            row.root.anchorMin = row.root.anchorMax = new Vector2(0f, 1f);
            row.root.pivot = new Vector2(0f, 1f);

            row.frame = row.root.gameObject.AddComponent<Image>();
            UiKit.StyleOutline(row.frame, 9f);
            row.frame.color = UiKit.Border;
            row.frame.raycastTarget = false;
            UiKit.NewFill("Fill", row.root, new Color(0.07f, 0.09f, 0.13f, 0.96f), 9f);

            row.portrait = UiKit.NewRawImage("Portrait", row.root, null, Color.white);
            UiKit.Place(row.portrait.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(60f, 60f));
            if (hero.Survivor && hero.Survivor.bodyPrefab)
            {
                CharacterBody b = hero.Survivor.bodyPrefab.GetComponent<CharacterBody>();
                if (b) row.portrait.texture = b.portraitIcon;
            }

            row.name = UiKit.NewText("Name", row.root, hero.DisplayName, 22f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(row.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -8f), new Vector2(LeftBlockW - 84f, 28f));
            row.name.enableAutoSizing = true;
            row.name.fontSizeMin = 12f;
            row.name.fontSizeMax = 22f;

            string roleToken = HeroRoles.RoleToken(hero.Survivor ? hero.Survivor.cachedName : "");
            row.role = UiKit.NewText("Role", row.root, Loc.Get(roleToken), 16f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(row.role.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -36f), new Vector2(LeftBlockW - 84f, 22f));

            row.info = UiKit.NewText("Info", row.root, "", 16f, UiKit.Gold, TextAlignmentOptions.Left);
            UiKit.Place(row.info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -58f), new Vector2(LeftBlockW - 84f, 22f));

            row.empty = UiKit.NewText("Empty", row.root, Loc.Get("HIGHCOMMAND_INV_EMPTY"), 18f, UiKit.TextDim, TextAlignmentOptions.Left);
            row.more = UiKit.NewText("More", row.root, "", 18f, UiKit.Accent, TextAlignmentOptions.Right);
            return row;
        }

        private void SyncRows(IReadOnlyList<Hero> heroes)
        {
            // drop rows of heroes that are gone, add rows for new ones, keep the roster's order
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                bool stillThere = false;
                for (int k = 0; k < heroes.Count; k++) if (heroes[k] == rows[i].hero) { stillThere = true; break; }
                if (stillThere) continue;
                if (rows[i].root) UnityEngine.Object.Destroy(rows[i].root.gameObject);
                rows.RemoveAt(i);
            }
            for (int k = 0; k < heroes.Count; k++)
            {
                bool exists = false;
                for (int i = 0; i < rows.Count; i++) if (rows[i].hero == heroes[k]) { exists = true; break; }
                if (!exists) rows.Add(NewRow(heroes[k]));
            }
            rows.Sort((a, b) => a.hero.JoinOrder.CompareTo(b.hero.JoinOrder));
        }

        private void Rebuild(IReadOnlyList<Hero> heroes, IReadOnlyList<EquipmentIndex> spare, HcState state)
        {
            SyncRows(heroes);
            foreach (Row r in rows) CollectItems(r.hero, r.items);

            // equipment places per hero (MUL-T has two): the item grid starts after the widest of them
            const float EquipSize = 50f, EquipPitch = 54f;
            int maxSlots = 1;
            foreach (Row r in rows)
            {
                CharacterMaster mm = r.hero.Master;
                maxSlots = Mathf.Max(maxSlots, HeroEquipment.SlotCount(mm ? mm.inventory : null));
            }

            // pick the biggest icon size at which everything fits
            float gridX = LeftBlockW + maxSlots * EquipPitch + 2f;
            float gridW = PanelW - 2f * Margin - gridX - 12f;
            float avail = MaxPanelH - HeaderH - FooterH;
            float size = CellSizes[CellSizes.Length - 1];
            int maxLines = int.MaxValue;
            foreach (float candidate in CellSizes)
            {
                int perLine = Mathf.Max(1, Mathf.FloorToInt(gridW / (candidate + 4f)));
                float total = 0f;
                foreach (Row r in rows)
                {
                    int lines = Mathf.Max(1, (r.items.Count + perLine - 1) / perLine);
                    total += Mathf.Max(76f, lines * (candidate + 4f) + 14f) + RowGap;
                }
                size = candidate;
                if (total <= avail) { maxLines = int.MaxValue; break; }
                // last candidate: cap the lines instead
                float per = rows.Count > 0 ? avail / rows.Count - RowGap : 80f;
                maxLines = Mathf.Max(1, Mathf.FloorToInt((per - 14f) / (candidate + 4f)));
            }

            int perLineFinal = Mathf.Max(1, Mathf.FloorToInt(gridW / (size + 4f)));
            float y = 0f;
            int totalItems = 0;
            foreach (Row r in rows)
            {
                int count = r.items.Count;
                foreach (var kv in r.items) totalItems += kv.Value;

                int capacity = maxLines == int.MaxValue ? count : Mathf.Min(count, maxLines * perLineFinal);
                int hidden = count - capacity;
                int lines = Mathf.Max(1, (capacity + perLineFinal - 1) / perLineFinal);
                r.height = Mathf.Max(76f, lines * (size + 4f) + 14f);

                r.root.anchoredPosition = new Vector2(0f, -y);
                r.root.sizeDelta = new Vector2(PanelW - 2f * Margin, r.height);
                y += r.height + RowGap;

                // equipment cells, one per place
                CharacterMaster m = r.hero.Master;
                Inventory heroInv = m ? m.inventory : null;
                int slots = HeroEquipment.SlotCount(heroInv);
                while (r.equipCells.Count < slots) r.equipCells.Add(NewCell(r.root, "Equipment"));
                for (int s = 0; s < r.equipCells.Count; s++)
                {
                    Cell ec = r.equipCells[s];
                    if (s >= slots) { ec.root.gameObject.SetActive(false); continue; }
                    ec.root.gameObject.SetActive(true);
                    ec.owner = r.hero;
                    ec.slot = s;
                    ec.item = ItemIndex.None;
                    ec.equipment = heroInv ? HeroEquipment.InSlot(heroInv, s) : EquipmentIndex.None;
                    ec.root.anchoredPosition = new Vector2(LeftBlockW + s * EquipPitch, -8f);
                    ec.root.sizeDelta = new Vector2(EquipSize, EquipSize);

                    PickupDef eqDef = ec.equipment != EquipmentIndex.None ? PickupOf(ec.equipment) : null;
                    ec.icon.color = PickupIcons.Apply(ec.icon, eqDef) ? Color.white : new Color(1f, 1f, 1f, 0f);
                    // an empty place is a dim outline, not a black square
                    ec.frame.color = eqDef != null ? eqDef.baseColor : new Color(0.55f, 0.62f, 0.72f, 0.3f);
                    ec.fill.color = eqDef != null ? new Color(0.03f, 0.04f, 0.06f, 1f) : new Color(0.03f, 0.04f, 0.06f, 0.55f);
                    ec.count.text = "";
                }

                // item cells
                while (r.cells.Count < capacity) r.cells.Add(NewCell(r.root, "Item"));
                for (int i = 0; i < r.cells.Count; i++)
                {
                    Cell c = r.cells[i];
                    if (i >= capacity) { c.root.gameObject.SetActive(false); continue; }
                    c.root.gameObject.SetActive(true);
                    KeyValuePair<ItemIndex, int> kv = r.items[i];
                    c.owner = r.hero;
                    c.item = kv.Key;
                    c.equipment = EquipmentIndex.None;
                    int col = i % perLineFinal, line = i / perLineFinal;
                    c.root.anchoredPosition = new Vector2(gridX + col * (size + 4f), -(7f + line * (size + 4f)));
                    c.root.sizeDelta = new Vector2(size, size);
                    PickupDef def = PickupOf(kv.Key);
                    c.icon.color = PickupIcons.Apply(c.icon, def) ? Color.white : (def != null ? def.baseColor : Color.gray);
                    c.frame.color = def != null ? def.baseColor : UiKit.Border;
                    c.count.text = kv.Value > 1 ? kv.Value.ToString() : "";
                }

                r.empty.gameObject.SetActive(count == 0);
                UiKit.Place(r.empty.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(gridX, -26f), new Vector2(400f, 26f));
                r.more.gameObject.SetActive(hidden > 0);
                if (hidden > 0)
                {
                    r.more.text = Loc.Format("HIGHCOMMAND_INV_MORE", hidden);
                    UiKit.Place(r.more.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 6f), new Vector2(160f, 24f));
                }
            }

            // spare equipment
            while (spareCells.Count < spare.Count) spareCells.Add(NewCell(footer, "Spare"));
            for (int i = 0; i < spareCells.Count; i++)
            {
                Cell c = spareCells[i];
                if (i >= spare.Count) { c.root.gameObject.SetActive(false); continue; }
                c.root.gameObject.SetActive(true);
                c.owner = null;
                c.item = ItemIndex.None;
                c.equipment = spare[i];
                c.spareIndex = i;
                c.root.anchoredPosition = new Vector2(i * 50f, -36f);
                c.root.sizeDelta = new Vector2(46f, 46f);
                PickupDef def = PickupOf(spare[i]);
                c.icon.color = PickupIcons.Apply(c.icon, def) ? Color.white : Color.gray;
                c.frame.color = def != null ? def.baseColor : UiKit.Border;
                c.count.text = "";
            }
            spareLabel.text = spare.Count > 0 ? Loc.Get("HIGHCOMMAND_INV_SPARE") : "";

            float panelH = HeaderH + Mathf.Max(0f, y - RowGap) + FooterH;
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(PanelW, panelH));
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -HeaderH), new Vector2(PanelW - 2f * Margin, Mathf.Max(0f, y)));

            title.text = Loc.Format("HIGHCOMMAND_INV_TITLE", totalItems);
            bool mirror = IsMirror(state);
            hint.text = Loc.Get(mirror ? "HIGHCOMMAND_INV_HINT_MIRROR" : "HIGHCOMMAND_INV_HINT_SPLIT");
            autoLabel.text = Loc.Get("HIGHCOMMAND_INV_AUTO");
            autoButton.gameObject.SetActive(!mirror);
        }

        // ---- per-frame ---------------------------------------------------------------------------------------

        internal void Tick()
        {
            if (!Visible) return;

            // a pause screen or the console in the middle of a drag cancels it (nothing is handed over)
            if (dragging && ClientInput.MouseBlocked) CancelDrag();

            HcState state = HcState.Instance;
            IReadOnlyList<Hero> heroes = state != null ? state.Heroes : NoHeroes;
            IReadOnlyList<EquipmentIndex> spare = state != null ? state.SpareEquipment : NoEquipment;

            int signature = Signature(heroes, spare) * 31 + (IsMirror(state) ? 1 : 0);
            if (signature != lastSignature)
            {
                lastSignature = signature;
                try { Rebuild(heroes, spare, state); }
                catch (Exception e) { Plugin.Log.LogError("InventoryPanel rebuild failed: " + e); }
            }

            Vector2 mouse = ClientInput.Mouse;

            // row highlight + dead state
            foreach (Row r in rows)
            {
                bool over = UiKit.Contains(r.root, mouse);
                bool target = dragging && over && r.hero != dragFrom;
                r.frame.color = target ? UiKit.Accent : UiKit.Border;
                UiKit.SetText(r.info, r.hero.IsAlive ? "" : Loc.Get("HIGHCOMMAND_INV_DEAD"));
                r.info.color = UiKit.Bad;
            }

            Cell hovered = HoveredCell(mouse);

            if (hovered != null && !dragging) ShowCellTooltip(hovered);

            // press -> start dragging
            if (!dragging && hovered != null && ClientInput.MouseDown(0)) BeginDrag(hovered);

            if (dragging)
            {
                PlaceGhost(mouse);
                if (!ClientInput.MouseHeld(0) || ClientInput.MouseUp(0)) EndDrag(mouse);
            }

            // auto-distribute button
            if (autoButton.gameObject.activeSelf)
            {
                bool over = UiKit.Contains(autoButton, mouse);
                autoBack.color = over ? new Color(0.26f, 0.34f, 0.45f, 1f) : new Color(0.16f, 0.2f, 0.27f, 1f);
                if (over && !dragging && ClientInput.MouseDown(0))
                {
                    string error = ArmyService.RedistributeItems();
                    if (error != Net.CommandChannel.Relayed) Toast.Show(error ?? Loc.Get("HIGHCOMMAND_INV_AUTO_DONE"));
                    lastSignature = int.MinValue;
                }
            }
        }

        // ---- test hooks (used by the hc_inv_* console commands) -----------------------------------------------

        private static Vector2 CenterOf(RectTransform rt)
        {
            Vector3 w = rt.TransformPoint(rt.rect.center); // overlay canvas: world units are screen pixels
            return new Vector2(w.x, w.y);
        }

        /// <summary>Screen position of an item cell (<paramref name="item"/>), a hero's equipment slot or a spare piece.</summary>
        internal bool TryFindCell(Hero owner, ItemIndex item, bool equipment, int spareIndex, out Vector2 center, int equipSlot = 0)
        {
            center = Vector2.zero;
            if (!Visible) return false;

            if (spareIndex >= 0)
            {
                if (spareIndex >= spareCells.Count || !spareCells[spareIndex].root.gameObject.activeSelf) return false;
                center = CenterOf(spareCells[spareIndex].root);
                return true;
            }
            foreach (Row r in rows)
            {
                if (r.hero != owner) continue;
                if (equipment)
                {
                    if (equipSlot < 0 || equipSlot >= r.equipCells.Count || !r.equipCells[equipSlot].root.gameObject.activeSelf) return false;
                    center = CenterOf(r.equipCells[equipSlot].root);
                    return true;
                }
                foreach (Cell c in r.cells)
                {
                    if (!c.root.gameObject.activeSelf || c.item != item) continue;
                    center = CenterOf(c.root);
                    return true;
                }
            }
            return false;
        }

        internal bool TryFindRow(Hero hero, out Vector2 center)
        {
            center = Vector2.zero;
            if (!Visible) return false;
            foreach (Row r in rows)
            {
                if (r.hero != hero) continue;
                // the free part of the row, away from the item cells
                Vector2 c = CenterOf(r.root);
                center = new Vector2(c.x + 300f, c.y);
                return true;
            }
            return false;
        }

        private Cell HoveredCell(Vector2 mouse)
        {
            foreach (Row r in rows)
            {
                foreach (Cell ec in r.equipCells)
                    if (ec.root.gameObject.activeSelf && UiKit.Contains(ec.root, mouse)) return ec;
                foreach (Cell c in r.cells)
                    if (c.root.gameObject.activeSelf && UiKit.Contains(c.root, mouse)) return c;
            }
            foreach (Cell c in spareCells)
                if (c.root.gameObject.activeSelf && UiKit.Contains(c.root, mouse)) return c;
            return null;
        }

        private void ShowCellTooltip(Cell c)
        {
            if (c.item == ItemIndex.None && c.equipment == EquipmentIndex.None)
            {
                Tooltip.Show(Loc.Get("HIGHCOMMAND_INV_EQUIPMENT"), Loc.Get("HIGHCOMMAND_INV_NOEQUIP"));
                return;
            }

            PickupDef def = c.item != ItemIndex.None ? PickupOf(c.item) : PickupOf(c.equipment);
            if (def == null) return;

            string body = Language.GetString(def.pickupToken);
            if (c.item != ItemIndex.None && c.owner != null && c.owner.Master && c.owner.Master.inventory)
            {
                int n = c.owner.Master.inventory.GetItemCountPermanent(c.item);
                body += "\n\n" + Loc.Format("HIGHCOMMAND_INV_COUNT", n);
            }
            Tooltip.Show(Language.GetString(def.nameToken), body, def.baseColor);
        }

        private void BeginDrag(Cell c)
        {
            if (c.item == ItemIndex.None && c.equipment == EquipmentIndex.None) return; // empty equipment slot
            if (IsMirror(HcState.Instance) && c.item != ItemIndex.None) return;

            dragging = true;
            dragItem = c.item;
            dragEquipment = c.item == ItemIndex.None ? c.equipment : EquipmentIndex.None;
            dragSpareIndex = c.spareIndex;
            dragSlot = c.slot;
            dragFrom = c.owner;

            dragCount = 1;
            if (dragItem != ItemIndex.None && ClientInput.Shift && dragFrom != null && dragFrom.Master && dragFrom.Master.inventory)
                dragCount = Mathf.Max(1, dragFrom.Master.inventory.GetItemCountPermanent(dragItem));

            PickupDef def = dragItem != ItemIndex.None ? PickupOf(dragItem) : PickupOf(dragEquipment);
            PickupIcons.Apply(ghostIcon, def);
            ghostCount.text = dragCount > 1 ? "x" + dragCount : "";
            ghost.gameObject.SetActive(true);
            ghost.SetAsLastSibling();
            PlaceGhost(ClientInput.Mouse);
        }

        private void PlaceGhost(Vector2 mouse)
        {
            // the ghost is a child of the panel (pivot and anchors at the centre): screen point -> panel space
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, mouse, null, out Vector2 local);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.anchoredPosition = local;
        }

        private void CancelDrag()
        {
            dragging = false;
            if (ghost) ghost.gameObject.SetActive(false);
        }

        private void EndDrag(Vector2 mouse)
        {
            Hero target = null;
            foreach (Row r in rows)
                if (UiKit.Contains(r.root, mouse)) { target = r.hero; break; }

            string error = null;
            string what = null;
            if (target != null && target != dragFrom)
            {
                if (dragItem != ItemIndex.None && dragFrom != null)
                {
                    error = ArmyService.TryMoveItem(dragFrom, target, dragItem, dragCount);
                    PickupDef def = PickupOf(dragItem);
                    what = (def != null ? Language.GetString(def.nameToken) : "?") + (dragCount > 1 ? " x" + dragCount : "");
                }
                else if (dragEquipment != EquipmentIndex.None)
                {
                    if (dragFrom != null) error = ArmyService.TryMoveEquipment(dragFrom, dragSlot, target);
                    else error = ArmyService.TryEquipFromPool(dragSpareIndex, target);
                    PickupDef def = PickupOf(dragEquipment);
                    what = def != null ? Language.GetString(def.nameToken) : "?";
                }

                if (error == Net.CommandChannel.Relayed) lastSignature = int.MinValue; // the server answers with a toast
                else if (error != null) Toast.Error(error);
                else if (what != null)
                {
                    string from = dragFrom != null ? dragFrom.DisplayName : Loc.Get("HIGHCOMMAND_INV_EQUIPMENT");
                    Toast.Show(Loc.Format("HIGHCOMMAND_INV_MOVED", from, target.DisplayName, what));
                    lastSignature = int.MinValue;
                }
            }
            CancelDrag();
        }
    }
}
