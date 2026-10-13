using System.Collections.Generic;
using HarmonyLib;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>What a map object is, as far as the commander cares (the marker on the map and who may use it).</summary>
    internal enum InteractKind : byte
    {
        Other,
        Chest,       // chests, equipment barrels, adaptive chests
        Shop,        // multishop terminals
        Shrine,      // shrines (chance, blood, combat, mountain, woods, order ...)
        Drone,       // broken drones and turrets: repaired, they join the hero who paid
        Printer,     // 3D printers, cauldrons: pay with an item of the hero who goes
        Lunar,       // lunar pods and the like: paid with the player's lunar coins
        Barrel,      // free gold
        Portal,
        Teleporter,
        Unsupported  // needs a choice made in a window a hero cannot open (scrapper, command cube ...)
    }

    /// <summary>
    /// The stage's usable objects as the mode sees them: kind, price, whether the army can pay, whether it is used up.
    /// Plain reads of the game objects (single player: the host has them all).
    /// </summary>
    internal static class Interactables
    {
        private static readonly Dictionary<int, InteractKind> Kinds = new Dictionary<int, InteractKind>(256);

        internal static void ResetForStage() => Kinds.Clear();

        internal static InteractKind KindOf(GameObject entity)
        {
            if (!entity) return InteractKind.Other;
            int id = entity.GetInstanceID();
            if (Kinds.TryGetValue(id, out InteractKind known)) return known;
            InteractKind kind = Classify(entity);
            Kinds[id] = kind;
            return kind;
        }

        private static InteractKind Classify(GameObject e)
        {
            if (e.GetComponent<TeleporterInteraction>()) return InteractKind.Teleporter;
            if (e.GetComponent<ScrapperController>()) return InteractKind.Unsupported;
            if (e.GetComponent<BarrelInteraction>()) return InteractKind.Barrel;
            if (e.GetComponent<SceneExitController>()) return InteractKind.Portal;

            // (chests carry a picker component of their own in this version: only a picker without a price is a
            // choice window - a command cube, a void potential)
            var p = e.GetComponent<PurchaseInteraction>();
            if (!p) return e.GetComponent<PickupPickerController>() ? InteractKind.Unsupported : InteractKind.Other;
            switch (p.costType)
            {
                case CostTypeIndex.WhiteItem:
                case CostTypeIndex.GreenItem:
                case CostTypeIndex.RedItem:
                case CostTypeIndex.BossItem:
                case CostTypeIndex.LunarItemOrEquipment:
                case CostTypeIndex.Equipment:
                    return InteractKind.Printer;
                case CostTypeIndex.LunarCoin:
                    return InteractKind.Lunar;
            }
            if (p.isShrine) return InteractKind.Shrine;
            if (p.isDrone || e.GetComponent<SummonMasterBehavior>()) return InteractKind.Drone;
            if (e.GetComponent<ChestBehavior>() || e.GetComponent<RouletteChestController>() || e.GetComponent<OptionChestBehavior>())
                return InteractKind.Chest;
            if (e.GetComponent<ShopTerminalBehavior>()) return InteractKind.Shop;
            return InteractKind.Other;
        }

        /// <summary>A hero can walk up to it and use it (a choice window, a used-up object: no).</summary>
        internal static bool Usable(GameObject entity) => entity && KindOf(entity) != InteractKind.Unsupported && !IsUsed(entity);

        /// <summary>Opened, bought, spent: nothing more to get here.</summary>
        internal static bool IsUsed(GameObject entity)
        {
            if (!entity) return true;
            var p = entity.GetComponent<PurchaseInteraction>();
            if (p) return !p.available;
            var barrel = entity.GetComponent<BarrelInteraction>();
            if (barrel) return barrel.Networkopened;
            return false;
        }

        /// <summary>The price as the game shows it; false when the object costs nothing.</summary>
        internal static bool TryGetPrice(GameObject entity, out CostTypeIndex type, out int cost)
        {
            type = CostTypeIndex.None;
            cost = 0;
            var p = entity ? entity.GetComponent<PurchaseInteraction>() : null;
            if (!p || p.costType == CostTypeIndex.None) return false;
            type = p.costType;
            cost = p.cost;
            return cost > 0 || type != CostTypeIndex.Money;
        }

        /// <summary>
        /// Whether the army can pay right now. Gold and lunar coins are checked; an item price depends on the hero who
        /// goes (it is checked when the hero gets there), health always can be paid.
        /// </summary>
        internal static bool Affordable(GameObject entity)
        {
            if (!TryGetPrice(entity, out CostTypeIndex type, out int cost)) return true;
            switch (type)
            {
                case CostTypeIndex.Money: return Economy.Gold >= (uint)cost;
                case CostTypeIndex.LunarCoin: return Purse.LunarCoins() >= (uint)cost;
                default: return true;
            }
        }

        /// <summary>The short price text: "$25", "1 lunar", "50% HP", "1 item".</summary>
        internal static string PriceText(CostTypeIndex type, int cost)
        {
            switch (type)
            {
                case CostTypeIndex.Money: return "$" + cost;
                case CostTypeIndex.LunarCoin: return Loc.Format("HIGHCOMMAND_PRICE_LUNAR", cost);
                case CostTypeIndex.PercentHealth: return Loc.Format("HIGHCOMMAND_PRICE_HEALTH", cost);
                case CostTypeIndex.WhiteItem:
                case CostTypeIndex.GreenItem:
                case CostTypeIndex.RedItem:
                case CostTypeIndex.BossItem:
                case CostTypeIndex.LunarItemOrEquipment:
                case CostTypeIndex.Equipment:
                    return Loc.Format("HIGHCOMMAND_PRICE_ITEM", cost);
                default: return "";
            }
        }

        /// <summary>The game's own verb for it ("Open chest", "Repair drone"), or the plain "Use".</summary>
        internal static string ActionText(GameObject entity)
        {
            string token = null;
            var p = entity ? entity.GetComponent<PurchaseInteraction>() : null;
            if (p) token = p.contextToken;
            else
            {
                var barrel = entity ? entity.GetComponent<BarrelInteraction>() : null;
                if (barrel) token = barrel.contextToken;
            }
            string text = string.IsNullOrEmpty(token) ? null : Language.GetString(token);
            return string.IsNullOrEmpty(text) || text == token ? Loc.Get("HIGHCOMMAND_CURSOR_INTERACT") : text;
        }

        /// <summary>Chests on this stage: how many there are and how many the army has opened.</summary>
        internal static void CountChests(out int opened, out int total)
        {
            opened = total = 0;
            var list = InstanceTracker.GetInstancesList<PurchaseInteraction>();
            for (int i = 0; i < list.Count; i++)
            {
                PurchaseInteraction p = list[i];
                if (!p || KindOf(p.gameObject) != InteractKind.Chest) continue;
                total++;
                if (!p.available) opened++;
            }
        }

        /// <summary>
        /// The place a hero walks to for an object: the point of its colliders nearest to <paramref name="from"/> (a big
        /// chest or a shop is wider than the reach around its pivot).
        /// </summary>
        internal static Vector3 ReachPoint(GameObject entity, Vector3 from)
        {
            Vector3 pivot = entity.transform.position;
            var colliders = entity.GetComponentsInChildren<Collider>();
            bool any = false;
            Bounds b = default;
            foreach (Collider c in colliders)
            {
                if (!c || !c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) return pivot;
            Vector3 p = b.ClosestPoint(new Vector3(from.x, b.center.y, from.z));
            return new Vector3(p.x, pivot.y, p.z);
        }
    }

    /// <summary>
    /// The army's purse pays for what a hero buys: the stock game takes gold from the buyer's own master (a hero has
    /// none) and lunar coins from the buyer's player (a hero is not a player).
    /// </summary>
    internal static class Purse
    {
        internal static bool IsHero(Interactor activator)
        {
            if (!activator || !HighCommandRun.IsActive) return false;
            var body = activator.GetComponent<CharacterBody>();
            return body && HeroMarker.Of(body) != null;
        }

        private static NetworkUser Player()
        {
            foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
                if (user && user.isParticipating) return user;
            return NetworkUser.readOnlyLocalPlayersList.Count > 0 ? NetworkUser.readOnlyLocalPlayersList[0] : null;
        }

        internal static uint LunarCoins()
        {
            NetworkUser user = Player();
            return user ? user.lunarCoins : 0u;
        }

        [HarmonyPatch(typeof(CostTypeDef), nameof(CostTypeDef.IsAffordable))]
        internal static class Affordable
        {
            private static bool Prefix(CostTypeDef __instance, int cost, Interactor activator, ref bool __result)
            {
                if (!IsHero(activator)) return true;
                if (__instance == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.Money))
                {
                    __result = Economy.Gold >= (uint)Mathf.Max(0, cost);
                    return false;
                }
                if (__instance == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.LunarCoin))
                {
                    __result = LunarCoins() >= (uint)Mathf.Max(0, cost);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CostTypeDef), nameof(CostTypeDef.PayCost), typeof(CostTypeDef.PayCostContext), typeof(CostTypeDef.PayCostResults))]
        internal static class Pay
        {
            private static bool Prefix(CostTypeDef __instance, CostTypeDef.PayCostContext context)
            {
                if (context == null || !IsHero(context.activator) || !NetworkServer.active) return true;
                if (__instance == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.Money))
                {
                    Economy.TrySpend((uint)Mathf.Max(0, context.cost));
                    return false;
                }
                if (__instance == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.LunarCoin))
                {
                    NetworkUser user = Player();
                    if (user) user.DeductLunarCoins((uint)Mathf.Max(0, context.cost));
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// A printer or a cauldron took items from the hero who used it: the pool (the army's record of what it owns)
        /// loses them too, so the distributor and the item panel stay right.
        /// </summary>
        internal static void OnPurchase(CostTypeDef.PayCostContext context, CostTypeDef.PayCostResults result)
        {
            if (!NetworkServer.active || context == null || result == null || !IsHero(context.activator)) return;
            HighCommandRun run = HighCommandRun.Instance;
            Inventory pool = LootService.Pool;
            if (run == null || !pool) return;

            Hero payer = HeroMarker.Of(context.activatorBody);
            bool changed = false;
            foreach (Inventory.ItemAndStackValues taken in result.itemStacksTaken)
            {
                int n = taken.stackValues.totalStacks;
                if (n <= 0) continue;
                int inPool = pool.GetItemCountPermanent(taken.itemIndex);
                int remove = Mathf.Min(n, inPool);
                if (remove <= 0) continue;
                pool.RemoveItemPermanent(taken.itemIndex, remove);
                run.Items.Forget(taken.itemIndex, remove, payer);
                changed = true;
            }
            if (changed) run.OnPoolChanged();
        }
    }
}
