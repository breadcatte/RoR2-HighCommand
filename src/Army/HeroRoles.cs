using RoR2;

namespace HighCommand.Army
{
    public enum HeroRole : byte
    {
        Generic,
        Ranged,
        Assassin,
        Tank,
        Melee,
        Caster,
        Sniper,
        Support,
        Summoner
    }

    /// <summary>What each known survivor is good at. Used by the barracks labels and by the item distributor.</summary>
    internal static class HeroRoles
    {
        internal static HeroRole RoleOf(string survivorName)
        {
            switch (survivorName)
            {
                case "Commando":
                case "Huntress":
                case "Chef": return HeroRole.Ranged;
                case "Bandit2":
                case "Merc": return HeroRole.Assassin;
                case "Toolbot":
                case "FalseSon":
                case "Enforcer2": return HeroRole.Tank;
                case "Croco":
                case "Loader":
                case "Drifter":
                case "Acolyte": return HeroRole.Melee;
                case "Mage":
                case "VoidSurvivor": return HeroRole.Caster;
                case "Railgunner": return HeroRole.Sniper;
                case "Treebot":
                case "Seeker":
                case "Captain": return HeroRole.Support;
                case "Engi":
                case "DroneTech": return HeroRole.Summoner;
                default: return HeroRole.Generic;
            }
        }

        internal static HeroRole RoleOf(SurvivorDef def) => def ? RoleOf(def.cachedName) : HeroRole.Generic;

        internal static string RoleToken(string survivorName)
        {
            switch (RoleOf(survivorName))
            {
                case HeroRole.Ranged: return "HIGHCOMMAND_ROLE_RANGED";
                case HeroRole.Assassin: return "HIGHCOMMAND_ROLE_ASSASSIN";
                case HeroRole.Tank: return "HIGHCOMMAND_ROLE_TANK";
                case HeroRole.Melee: return "HIGHCOMMAND_ROLE_MELEE";
                case HeroRole.Caster: return "HIGHCOMMAND_ROLE_CASTER";
                case HeroRole.Sniper: return "HIGHCOMMAND_ROLE_SNIPER";
                case HeroRole.Support: return "HIGHCOMMAND_ROLE_SUPPORT";
                case HeroRole.Summoner: return "HIGHCOMMAND_ROLE_SUMMONER";
                default: return "HIGHCOMMAND_ROLE_GENERIC";
            }
        }

        /// <summary>How much a hero of this role wants an item with the given tag (0..1).</summary>
        internal static float Affinity(HeroRole role, ItemTag tag)
        {
            switch (tag)
            {
                case ItemTag.Damage:
                    switch (role)
                    {
                        case HeroRole.Ranged:
                        case HeroRole.Assassin:
                        case HeroRole.Caster:
                        case HeroRole.Sniper: return 1.0f;
                        case HeroRole.Melee: return 0.9f;
                        case HeroRole.Summoner: return 0.8f;
                        case HeroRole.Tank: return 0.55f;
                        case HeroRole.Support: return 0.5f;
                        default: return 0.7f;
                    }
                case ItemTag.Healing:
                    switch (role)
                    {
                        case HeroRole.Tank:
                        case HeroRole.Melee: return 1.0f;
                        case HeroRole.Assassin: return 0.8f;
                        case HeroRole.Support: return 0.7f;
                        default: return 0.6f;
                    }
                case ItemTag.Utility:
                case ItemTag.SprintRelated:
                case ItemTag.MobilityRelated:
                    switch (role)
                    {
                        case HeroRole.Support:
                        case HeroRole.Summoner: return 1.0f;
                        case HeroRole.Assassin:
                        case HeroRole.Melee: return 0.85f;
                        default: return 0.7f;
                    }
                default:
                    return 0.6f;
            }
        }
    }
}
