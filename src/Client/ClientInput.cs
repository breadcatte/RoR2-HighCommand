using System;
using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Single place that decides whether the mode may read keyboard / mouse this frame. The mode reads the keyboard
    /// and mouse directly (UnityEngine.Input), so it must stay quiet while the pause menu, chat or the developer
    /// console owns the input.
    /// </summary>
    internal static class ClientInput
    {
        /// <summary>True while pause / chat / console have the keyboard.</summary>
        internal static bool KeyboardBlocked { get; private set; }

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

        /// <summary>Called once per frame, first.</summary>
        internal static void Refresh()
        {
            Mouse = Input.mousePosition;
            PointerInWindow = Application.isFocused
                              && Mouse.x >= 0f && Mouse.y >= 0f && Mouse.x <= Screen.width - 1 && Mouse.y <= Screen.height - 1;

            bool blocked = PauseManager.isPaused || Util.IsChatWindowOpen() || ConsoleWindow.instance != null;
            LocalUser user = LocalUserManager.GetFirstLocalUser();
            if (user != null && user.pauseScreenController != null) blocked = true;
            KeyboardBlocked = blocked;

            bool over = false;
            for (int i = 0; i < UiHitTests.Count && !over; i++)
            {
                try { over = UiHitTests[i](Mouse); }
                catch (Exception) { over = false; }
            }
            PointerOverUi = over;
        }

        internal static bool Key(KeyCode key) => !KeyboardBlocked && Input.GetKey(key);

        internal static bool KeyDown(KeyCode key) => !KeyboardBlocked && Input.GetKeyDown(key);

        internal static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        internal static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        internal static bool Alt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }
}
