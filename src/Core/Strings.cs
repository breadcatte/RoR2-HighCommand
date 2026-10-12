namespace HighCommand
{
    /// <summary>All user-visible texts of the mod, English and Russian. Tokens start with HIGHCOMMAND_.</summary>
    internal static class Strings
    {
        internal static void Register()
        {
            Loc.Add("HIGHCOMMAND_GAMEMODE_NAME", "High Command", "Верховное командование");
            Loc.Add("HIGHCOMMAND_GAMEMODE_DESC",
                "Command an army of survivors from above. Buy new heroes with gold dropped by enemies.",
                "Командуйте армией выживших сверху. Покупайте новых героев за золото, выпадающее из врагов.");

            // help sheet (F1)
            Loc.Add("HIGHCOMMAND_HELP_TITLE", "High Command - hotkeys", "Верховное командование - горячие клавиши");
            Loc.Add("HIGHCOMMAND_KEY_EDGE", "Screen edge", "Край экрана");
            Loc.Add("HIGHCOMMAND_KEY_WHEEL", "Mouse wheel", "Колесо мыши");
            Loc.Add("HIGHCOMMAND_KEY_MMB", "Middle mouse", "Средняя кнопка");
            Loc.Add("HIGHCOMMAND_KEY_MINIMAP", "Minimap click", "Клик по мини-карте");
            Loc.Add("HIGHCOMMAND_HELP_PAN", "Move the camera", "Двигать камеру");
            Loc.Add("HIGHCOMMAND_HELP_EDGE", "Move the camera", "Двигать камеру");
            Loc.Add("HIGHCOMMAND_HELP_ZOOM", "Zoom in / out", "Приблизить / отдалить");
            Loc.Add("HIGHCOMMAND_HELP_ROTATE", "Rotate the camera", "Повернуть камеру");
            Loc.Add("HIGHCOMMAND_HELP_FOCUS", "Fly to the army", "Перелёт к армии");
            Loc.Add("HIGHCOMMAND_HELP_MINIMAP", "Fly the camera there", "Перелёт камеры в эту точку");
            Loc.Add("HIGHCOMMAND_HELP_HELP", "This sheet", "Эта шпаргалка");
            Loc.Add("HIGHCOMMAND_HELP_PAUSE", "Pause menu", "Меню паузы");
            Loc.Add("HIGHCOMMAND_KEY_LMB", "Left click / drag", "ЛКМ / рамка");
            Loc.Add("HIGHCOMMAND_KEY_DOUBLE", "Double click", "Двойной клик");
            Loc.Add("HIGHCOMMAND_HELP_SELECT", "Select a hero / box select", "Выбрать героя / выделить рамкой");
            Loc.Add("HIGHCOMMAND_HELP_SELECT_ADD", "Add / remove from selection", "Добавить / убрать из выделения");
            Loc.Add("HIGHCOMMAND_HELP_SELECT_TYPE", "Select all of the same kind", "Выбрать всех такого же типа");
            Loc.Add("HIGHCOMMAND_HELP_SELECT_ALL", "Select all heroes", "Выбрать всех героев");
            Loc.Add("HIGHCOMMAND_HELP_GROUP_SET", "Set a control group", "Назначить группу");
            Loc.Add("HIGHCOMMAND_HELP_GROUP_RECALL", "Select group (twice: camera)", "Вызвать группу (дважды: камера)");
            Loc.Add("HIGHCOMMAND_HELP_CYCLE", "Next hero (Shift: previous)", "Следующий герой (Shift: предыдущий)");
            Loc.Add("HIGHCOMMAND_SELECTED_COUNT", "Selected: {0}", "Выбрано: {0}");
            Loc.Add("HIGHCOMMAND_LEVEL_SHORT", "Lv {0}", "Ур. {0}");
        }
    }
}
