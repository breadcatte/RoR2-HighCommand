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
        }
    }
}
