using System;
using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Single place that reads keyboard / mouse for the mode and decides whether it may do so this frame. The mode
    /// reads the keyboard and mouse directly (UnityEngine.Input), so it must stay quiet while the pause menu, chat or
    /// the developer console owns the input.
    ///
    /// For testing, every read goes through this class so console commands (hc_click, hc_drag, hc_key) can inject
    /// synthetic input: the same code path runs as for a real mouse.
    /// </summary>
    internal static class ClientInput
    {
        // ---- gate ----------------------------------------------------------------------------------

        /// <summary>True while pause / chat / console have the keyboard.</summary>
        internal static bool KeyboardBlocked { get; private set; }

        /// <summary>True while the pause screen or the console is up: the mode ignores clicks and the wheel then.</summary>
        internal static bool MouseBlocked { get; private set; }

        /// <summary>True while the mouse is over one of the mode's own UI panels.</summary>
        internal static bool PointerOverUi { get; private set; }

        /// <summary>Mouse position in screen pixels.</summary>
        internal static Vector2 Mouse { get; private set; }

        /// <summary>The window has focus and the pointer is inside it.</summary>
        internal static bool PointerInWindow { get; private set; }

        private static readonly List<Func<Vector2, bool>> UiHitTests = new List<Func<Vector2, bool>>();

        /// <summary>Registers a callback that says whether a screen point is on top of a UI element of the mode.</summary>
        internal static void RegisterUiHitTest(Func<Vector2, bool> hitTest)
        {
            if (hitTest != null && !UiHitTests.Contains(hitTest)) UiHitTests.Add(hitTest);
        }

        internal static void UnregisterUiHitTest(Func<Vector2, bool> hitTest) => UiHitTests.Remove(hitTest);

        // ---- synthetic input -------------------------------------------------------------------------

        private struct Step
        {
            internal Vector2? mouse;
            internal int buttonDown;   // -1 none
            internal int buttonUp;     // -1 none
            internal KeyCode keyDown;  // None = nothing
            internal KeyCode keyUp;
            internal bool ctrl, shift, alt;
            internal bool keepModifiers;
        }

        private static readonly Queue<Step> Script = new Queue<Step>();
        private static Vector2? fakeMouse;
        private static bool fakeButton0, fakeButton1, fakeButton2;
        private static int fakeDown = -1, fakeUp = -1;
        private static KeyCode fakeKeyDown, fakeKeyHeld;
        private static bool fakeCtrl, fakeShift, fakeAlt;
        private static float fakeScroll;

        /// <summary>Is synthetic input active (the real mouse position is ignored).</summary>
        internal static bool Synthetic => fakeMouse.HasValue;

        internal static void InjectClick(Vector2 at, int button = 0, bool shift = false, bool ctrl = false)
        {
            Script.Enqueue(new Step { mouse = at, buttonDown = -1, buttonUp = -1 });
            Script.Enqueue(new Step { mouse = at, buttonDown = button, buttonUp = -1, shift = shift, ctrl = ctrl, keepModifiers = true });
            Script.Enqueue(new Step { mouse = at, buttonDown = -1, buttonUp = button, shift = shift, ctrl = ctrl, keepModifiers = true });
            Script.Enqueue(new Step { mouse = at, buttonDown = -1, buttonUp = -1 });
        }

        /// <param name="release">False keeps the button down after the move (testing the picture of a box being dragged).</param>
        internal static void InjectDrag(Vector2 from, Vector2 to, int button = 0, bool shift = false, bool release = true)
        {
            Script.Enqueue(new Step { mouse = from, buttonDown = -1, buttonUp = -1 });
            Script.Enqueue(new Step { mouse = from, buttonDown = button, buttonUp = -1, shift = shift, keepModifiers = true });
            for (int i = 1; i <= 4; i++)
                Script.Enqueue(new Step { mouse = Vector2.Lerp(from, to, i / 4f), buttonDown = -1, buttonUp = -1, shift = shift, keepModifiers = true });
            if (!release) return;
            Script.Enqueue(new Step { mouse = to, buttonDown = -1, buttonUp = button, shift = shift, keepModifiers = true });
            Script.Enqueue(new Step { mouse = to, buttonDown = -1, buttonUp = -1 });
        }

        /// <summary>Puts the (synthetic) pointer somewhere and leaves it there until <see cref="EndSynthetic"/>.</summary>
        internal static void InjectMove(Vector2 at, bool shift = false)
        {
            Script.Enqueue(new Step { mouse = at, buttonDown = -1, buttonUp = -1, shift = shift, keepModifiers = true });
        }

        internal static void InjectKey(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Script.Enqueue(new Step { keyDown = key, buttonDown = -1, buttonUp = -1, ctrl = ctrl, shift = shift, alt = alt, keepModifiers = true });
            Script.Enqueue(new Step { keyUp = key, buttonDown = -1, buttonUp = -1 });
        }

        internal static void InjectScroll(float amount) => fakeScroll += amount;

        /// <summary>Gives control back to the real mouse.</summary>
        internal static void EndSynthetic()
        {
            Script.Clear();
            fakeMouse = null;
            fakeButton0 = fakeButton1 = fakeButton2 = false;
            fakeKeyHeld = KeyCode.None;
            fakeCtrl = fakeShift = fakeAlt = false;
        }

        private static void AdvanceScript()
        {
            fakeDown = -1;
            fakeUp = -1;
            fakeKeyDown = KeyCode.None;
            fakeScroll = 0f;

            if (Script.Count == 0) return;
            Step s = Script.Dequeue();
            if (s.mouse.HasValue) fakeMouse = s.mouse;
            if (s.buttonDown >= 0)
            {
                fakeDown = s.buttonDown;
                SetFakeButton(s.buttonDown, true);
            }
            if (s.buttonUp >= 0)
            {
                fakeUp = s.buttonUp;
                SetFakeButton(s.buttonUp, false);
            }
            if (s.keyDown != KeyCode.None)
            {
                fakeKeyDown = s.keyDown;
                fakeKeyHeld = s.keyDown;
            }
            if (s.keyUp != KeyCode.None && fakeKeyHeld == s.keyUp) fakeKeyHeld = KeyCode.None;
            if (s.keepModifiers || s.ctrl || s.shift || s.alt)
            {
                fakeCtrl = s.ctrl;
                fakeShift = s.shift;
                fakeAlt = s.alt;
            }
            else
            {
                fakeCtrl = fakeShift = fakeAlt = false;
            }
        }

        private static void SetFakeButton(int b, bool value)
        {
            if (b == 0) fakeButton0 = value;
            else if (b == 1) fakeButton1 = value;
            else if (b == 2) fakeButton2 = value;
        }

        // ---- per-frame refresh ------------------------------------------------------------------------

        /// <summary>Called once per frame, first.</summary>
        internal static void Refresh()
        {
            AdvanceScript();

            Mouse = fakeMouse ?? (Vector2)Input.mousePosition;
            PointerInWindow = fakeMouse.HasValue || (Application.isFocused
                              && Mouse.x >= 0f && Mouse.y >= 0f && Mouse.x <= Screen.width - 1 && Mouse.y <= Screen.height - 1);

            // The pause screen and the console own both devices; the chat window owns the keyboard only.
            bool overlay = PauseManager.isPaused || ConsoleWindow.instance != null;
            LocalUser user = LocalUserManager.GetFirstLocalUser();
            if (user != null && user.pauseScreenController != null) overlay = true;
            MouseBlocked = overlay;
            KeyboardBlocked = overlay || Util.IsChatWindowOpen();

            bool over = false;
            for (int i = 0; i < UiHitTests.Count && !over; i++)
            {
                try { over = UiHitTests[i](Mouse); }
                catch (Exception) { over = false; }
            }
            PointerOverUi = over;
        }

        // ---- reads --------------------------------------------------------------------------------

        internal static bool MouseDown(int button)
        {
            if (MouseBlocked) return false;
            if (fakeMouse.HasValue) return fakeDown == button;
            return Input.GetMouseButtonDown(button);
        }

        internal static bool MouseUp(int button)
        {
            if (MouseBlocked) return false;
            if (fakeMouse.HasValue) return fakeUp == button;
            return Input.GetMouseButtonUp(button);
        }

        internal static bool MouseHeld(int button)
        {
            if (MouseBlocked) return false;
            if (fakeMouse.HasValue)
                return button == 0 ? fakeButton0 : button == 1 ? fakeButton1 : fakeButton2;
            return Input.GetMouseButton(button);
        }

        internal static float Scroll => MouseBlocked ? 0f : fakeMouse.HasValue ? fakeScroll : Input.mouseScrollDelta.y;

        /// <summary>Raw key state, ignoring the blocked gate (callers that need the gate use <see cref="Key"/>).</summary>
        internal static bool RawKey(KeyCode key)
        {
            if (fakeKeyHeld == key && key != KeyCode.None) return true;
            return Input.GetKey(key);
        }

        internal static bool RawKeyDown(KeyCode key)
        {
            if (fakeKeyDown == key && key != KeyCode.None) return true;
            return Input.GetKeyDown(key);
        }

        internal static bool Key(KeyCode key) => !KeyboardBlocked && RawKey(key);

        internal static bool KeyDown(KeyCode key) => !KeyboardBlocked && RawKeyDown(key);

        internal static bool Ctrl => fakeCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        internal static bool Shift => fakeShift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        internal static bool Alt => fakeAlt || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }
}
