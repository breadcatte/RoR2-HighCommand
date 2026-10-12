using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.UI;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Turns mouse and keyboard into selection changes: click, box, shift, double click, Ctrl+A, control groups,
    /// Tab cycling. Also tells the overlay what is under the cursor.
    /// </summary>
    internal sealed class SelectionController
    {
        private const float DragThreshold = 6f;
        private const float DoubleClickTime = 0.32f;

        private readonly Selection selection;
        private readonly WorldOverlay overlay;
        private readonly List<Hero> scratch = new List<Hero>(32);

        private bool pressed;
        private bool dragging;
        private Vector2 pressPoint;
        private float lastClickTime = -10f;
        private Hero lastClickHero;

        internal SelectionController(Selection selection, WorldOverlay overlay)
        {
            this.selection = selection;
            this.overlay = overlay;
        }

        /// <summary>True while the player is dragging a selection box (other mouse handling should stay out of the way).</summary>
        internal bool IsDragging => dragging;

        internal static readonly KeyCode[] GroupKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
        };

        internal void Tick(IReadOnlyList<Hero> heroes, RtsCamera camera, bool inputAllowed)
        {
            Vector2 mouse = ClientInput.Mouse;
            bool overUi = ClientInput.PointerOverUi;

            // what is under the cursor
            overlay.HoveredHero = null;
            overlay.HoveredEnemy = null;
            if (inputAllowed && !overUi && ClientInput.PointerInWindow && !dragging)
            {
                overlay.HoveredHero = WorldPicker.PickHero(mouse, heroes);
                if (overlay.HoveredHero == null) overlay.HoveredEnemy = WorldPicker.PickEnemy(mouse);
            }

            if (inputAllowed) HandleMouse(heroes, mouse, overUi);
            else CancelDrag();

            if (inputAllowed && !ClientInput.KeyboardBlocked) HandleKeys(heroes, camera);
        }

        // ---- mouse -------------------------------------------------------------------------------

        private void HandleMouse(IReadOnlyList<Hero> heroes, Vector2 mouse, bool overUi)
        {
            if (ClientInput.MouseDown(0) && !overUi && ClientInput.PointerInWindow && !OrderState.WantsLeftClick)
            {
                pressed = true;
                dragging = false;
                pressPoint = mouse;
            }

            if (pressed && ClientInput.MouseHeld(0))
            {
                if (!dragging && (mouse - pressPoint).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;
                if (dragging) overlay.DragRect = MakeRect(pressPoint, mouse);
            }

            if (pressed && ClientInput.MouseUp(0))
            {
                if (dragging) FinishBox(heroes, MakeRect(pressPoint, mouse));
                else FinishClick(heroes, mouse);
                CancelDrag();
            }

            if (pressed && !ClientInput.MouseHeld(0) && !ClientInput.MouseUp(0)) CancelDrag();
        }

        private void CancelDrag()
        {
            pressed = false;
            dragging = false;
            overlay.DragRect = null;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private void FinishBox(IReadOnlyList<Hero> heroes, Rect rect)
        {
            scratch.Clear();
            WorldPicker.HeroesInRect(rect, heroes, scratch);
            if (ClientInput.Shift) selection.Add(scratch);
            else selection.Set(scratch);
        }

        private void FinishClick(IReadOnlyList<Hero> heroes, Vector2 mouse)
        {
            Hero hero = WorldPicker.PickHero(mouse, heroes);
            float now = Time.unscaledTime;

            if (hero == null)
            {
                if (!ClientInput.Shift) selection.Clear();
                lastClickHero = null;
                return;
            }

            bool doubleClick = hero == lastClickHero && now - lastClickTime < DoubleClickTime;
            lastClickHero = hero;
            lastClickTime = now;

            if (doubleClick)
            {
                // all heroes of the same kind (same survivor), on screen or not
                scratch.Clear();
                for (int i = 0; i < heroes.Count; i++)
                    if (heroes[i].IsAlive && heroes[i].BodyIndex == hero.BodyIndex) scratch.Add(heroes[i]);
                if (ClientInput.Shift) selection.Add(scratch); else selection.Set(scratch);
                return;
            }

            if (ClientInput.Shift) selection.Toggle(hero);
            else selection.SetSingle(hero);
        }

        // ---- keyboard ----------------------------------------------------------------------------

        private void HandleKeys(IReadOnlyList<Hero> heroes, RtsCamera camera)
        {
            if (ClientInput.Ctrl && ClientInput.RawKeyDown(KeyCode.A))
            {
                scratch.Clear();
                for (int i = 0; i < heroes.Count; i++) if (heroes[i].IsAlive) scratch.Add(heroes[i]);
                selection.Set(scratch);
            }

            if (ClientInput.RawKeyDown(KeyCode.Tab)) CycleSelection(heroes, ClientInput.Shift ? -1 : 1);

            for (int g = 0; g < GroupKeys.Length; g++)
            {
                if (!ClientInput.RawKeyDown(GroupKeys[g])) continue;

                if (ClientInput.Ctrl) selection.AssignGroup(g);
                else if (ClientInput.Shift) selection.AddToGroup(g);
                else
                {
                    bool twice = selection.RecallGroup(g, Time.unscaledTime);
                    if (twice && camera)
                    {
                        Vector3? c = selection.Center();
                        if (c.HasValue) camera.FocusOn(c.Value);
                    }
                }
            }

            if (ClientInput.RawKeyDown(KeyCode.Escape) && selection.Count > 0 && !OrderState.WantsLeftClick) { /* Esc is the pause key: keep the selection */ }
        }

        private void CycleSelection(IReadOnlyList<Hero> heroes, int direction)
        {
            scratch.Clear();
            for (int i = 0; i < heroes.Count; i++) if (heroes[i].IsAlive) scratch.Add(heroes[i]);
            if (scratch.Count == 0) return;

            int index = -1;
            if (selection.Count > 0) index = scratch.IndexOf(selection.Heroes[0]);
            index = (index + direction + scratch.Count) % scratch.Count;
            selection.SetSingle(scratch[index]);
        }
    }

    /// <summary>
    /// Mode flags shared between input handlers (e.g. "the next left click picks a target for an order").
    /// Filled in properly by the orders module.
    /// </summary>
    internal static class OrderState
    {
        internal static bool WantsLeftClick;
    }
}
