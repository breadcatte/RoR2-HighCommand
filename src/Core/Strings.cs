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

            // orders
            Loc.Add("HIGHCOMMAND_KEY_RMB", "Right click", "ПКМ");
            Loc.Add("HIGHCOMMAND_HELP_RMB", "Move / attack / follow / use", "Идти / атаковать / следовать / использовать");
            Loc.Add("HIGHCOMMAND_HELP_QUEUE", "Queue the order", "Добавить приказ в очередь");
            Loc.Add("HIGHCOMMAND_HELP_ATTACKMOVE", "Attack-move to a point", "Идти в точку, сражаясь");
            Loc.Add("HIGHCOMMAND_HELP_FOCUSFIRE", "Focus fire on the enemy under the cursor", "Фокус на враге под курсором");
            Loc.Add("HIGHCOMMAND_HELP_STOPHOLD", "Stop / hold position", "Стоп / держать позицию");
            Loc.Add("HIGHCOMMAND_HELP_RETREAT", "Retreat / patrol / rally point", "Отступить / патруль / точка сбора");
            Loc.Add("HIGHCOMMAND_HELP_CAST", "Use secondary / utility / special / equipment", "Применить: вторичный / рывок / особый / экипировку");
            Loc.Add("HIGHCOMMAND_HELP_AUTO", "Switch a skill's auto-use", "Вкл/выкл автоприменение умения");
            Loc.Add("HIGHCOMMAND_HELP_STANCE", "Stance: aggressive / defensive / passive", "Стойка: агрессивная / защитная / пассивная");
            Loc.Add("HIGHCOMMAND_HELP_FORMATION", "Formation: blob / line / column", "Построение: кучка / линия / колонна");
            Loc.Add("HIGHCOMMAND_HINT_ATTACKMOVE", "Attack-move: left click a point (right click cancels)", "Атака-движение: ЛКМ по точке (ПКМ — отмена)");
            Loc.Add("HIGHCOMMAND_HINT_FOCUS", "Focus fire: left click an enemy", "Фокус: ЛКМ по врагу");
            Loc.Add("HIGHCOMMAND_HINT_PATROL", "Patrol: left click the far point", "Патруль: ЛКМ по дальней точке");
            Loc.Add("HIGHCOMMAND_HINT_RALLY", "Rally point: left click", "Точка сбора: ЛКМ");
            Loc.Add("HIGHCOMMAND_HINT_CAST", "Skill: left click a target or ground", "Умение: ЛКМ по цели или земле");
            Loc.Add("HIGHCOMMAND_RALLY_SET", "Rally point set", "Точка сбора задана");
            Loc.Add("HIGHCOMMAND_FORMATION_BLOB", "blob", "кучка");
            Loc.Add("HIGHCOMMAND_FORMATION_LINE", "line", "линия");
            Loc.Add("HIGHCOMMAND_FORMATION_COLUMN", "column", "колонна");
            Loc.Add("HIGHCOMMAND_FORMATION_SET", "Formation: {0}", "Построение: {0}");
            Loc.Add("HIGHCOMMAND_STANCE_AGGRESSIVE", "aggressive", "агрессивная");
            Loc.Add("HIGHCOMMAND_STANCE_DEFENSIVE", "defensive", "защитная");
            Loc.Add("HIGHCOMMAND_STANCE_PASSIVE", "passive", "пассивная");
            Loc.Add("HIGHCOMMAND_STANCE_SET", "Stance: {0}", "Стойка: {0}");
            Loc.Add("HIGHCOMMAND_AUTO_ON", "Auto-use ON: {0}", "Автоприменение ВКЛ: {0}");
            Loc.Add("HIGHCOMMAND_AUTO_OFF", "Auto-use OFF: {0}", "Автоприменение ВЫКЛ: {0}");
            Loc.Add("HIGHCOMMAND_SLOT_PRIMARY", "primary", "основное");
            Loc.Add("HIGHCOMMAND_SLOT_SECONDARY", "secondary", "вторичное");
            Loc.Add("HIGHCOMMAND_SLOT_UTILITY", "utility", "рывок");
            Loc.Add("HIGHCOMMAND_SLOT_SPECIAL", "special", "особое");
            Loc.Add("HIGHCOMMAND_SLOT_EQUIPMENT", "equipment", "экипировка");

            // economy / barracks
            Loc.Add("HIGHCOMMAND_TOP_STAGE", "Stage {0}", "Стадия {0}");
            Loc.Add("HIGHCOMMAND_TOP_ARMY", "Army {0}/{1} (limit {2})", "Армия {0}/{1} (лимит {2})");
            Loc.Add("HIGHCOMMAND_TOP_NEXT", "next hero: ${0}", "след. герой: ${0}");
            Loc.Add("HIGHCOMMAND_TOP_PERMIN", "min", "мин");
            Loc.Add("HIGHCOMMAND_HELP_BARRACKS", "Barracks: recruit heroes", "Казарма: вербовка героев");
            Loc.Add("HIGHCOMMAND_BARRACKS_TITLE", "Barracks - next hero costs ${0}   (gold: ${1})", "Казарма - следующий герой стоит ${0}   (золото: ${1})");
            Loc.Add("HIGHCOMMAND_BARRACKS_HINT", "Click a survivor to recruit them. Esc closes this window.", "Нажмите на выжившего, чтобы нанять. Esc закрывает окно.");
            Loc.Add("HIGHCOMMAND_RECRUIT_BUY", "Recruit - ${0}", "Нанять - ${0}");
            Loc.Add("HIGHCOMMAND_RECRUIT_PRICE", "${0}", "${0}");
            Loc.Add("HIGHCOMMAND_RECRUIT_LOCKED", "Locked", "Закрыт");
            Loc.Add("HIGHCOMMAND_RECRUIT_NOTALLOWED", "Unavailable", "Недоступен");
            Loc.Add("HIGHCOMMAND_RECRUIT_OWNED", "In the army", "Уже в армии");
            Loc.Add("HIGHCOMMAND_RECRUIT_FULL", "Army is full", "Армия заполнена");
            Loc.Add("HIGHCOMMAND_RECRUIT_NEEDGOLD", "Not enough gold: ${0}", "Не хватает золота: ${0}");
            Loc.Add("HIGHCOMMAND_RECRUITED", "Recruited: {0}", "Нанят: {0}");
            Loc.Add("HIGHCOMMAND_ERR_NORUN", "No run in progress", "Забег не идёт");
            Loc.Add("HIGHCOMMAND_ERR_FAILED", "Could not recruit (gold refunded)", "Не удалось нанять (золото возвращено)");
            Loc.Add("HIGHCOMMAND_ERR_NOLINK", "No connection to the host", "Нет связи с хостом");
            Loc.Add("HIGHCOMMAND_ROLE_RANGED", "Ranged damage", "Дальний урон");
            Loc.Add("HIGHCOMMAND_ROLE_ASSASSIN", "Assassin", "Убийца");
            Loc.Add("HIGHCOMMAND_ROLE_TANK", "Tank", "Танк");
            Loc.Add("HIGHCOMMAND_ROLE_MELEE", "Melee", "Ближний бой");
            Loc.Add("HIGHCOMMAND_ROLE_CASTER", "Caster", "Заклинатель");
            Loc.Add("HIGHCOMMAND_ROLE_SNIPER", "Sniper", "Снайпер");
            Loc.Add("HIGHCOMMAND_ROLE_SUPPORT", "Support", "Поддержка");
            Loc.Add("HIGHCOMMAND_ROLE_SUMMONER", "Summoner", "Призыватель");
            Loc.Add("HIGHCOMMAND_ROLE_GENERIC", "Survivor", "Выживший");

            // inventory
            Loc.Add("HIGHCOMMAND_HELP_INVENTORY", "Items: who carries what, drag to give", "Предметы: у кого что, перетащите, чтобы передать");
            Loc.Add("HIGHCOMMAND_INV_TITLE", "Army items ({0} total)", "Предметы армии (всего {0})");
            Loc.Add("HIGHCOMMAND_INV_HINT_SPLIT", "Drag an item onto another hero to give it. Shift = the whole stack. Esc closes.",
                "Перетащите предмет на другого героя, чтобы отдать. Shift - вся стопка. Esc закрывает.");
            Loc.Add("HIGHCOMMAND_INV_HINT_MIRROR", "Mirror mode: every hero has every item, nothing to move. Esc closes.",
                "Режим «зеркало»: у каждого героя есть все предметы, передавать нечего. Esc закрывает.");
            Loc.Add("HIGHCOMMAND_INV_AUTO", "Auto-distribute", "Раздать заново");
            Loc.Add("HIGHCOMMAND_INV_AUTO_DONE", "Items redistributed by role", "Предметы розданы заново по ролям");
            Loc.Add("HIGHCOMMAND_INV_EMPTY", "no items yet", "предметов пока нет");
            Loc.Add("HIGHCOMMAND_INV_MORE", "+{0} more", "ещё +{0}");
            Loc.Add("HIGHCOMMAND_INV_EQUIPMENT", "Equipment", "Снаряжение");
            Loc.Add("HIGHCOMMAND_INV_SPARE", "Spare equipment (drag onto a hero):", "Запасное снаряжение (перетащите на героя):");
            Loc.Add("HIGHCOMMAND_INV_NOEQUIP", "no equipment", "без снаряжения");
            Loc.Add("HIGHCOMMAND_INV_MOVED", "{0} -> {1}: {2}", "{0} -> {1}: {2}");
            Loc.Add("HIGHCOMMAND_INV_COUNT", "Owned: {0}", "В наличии: {0}");
            Loc.Add("HIGHCOMMAND_INV_DEAD", "fallen", "пал");
        }
    }
}
