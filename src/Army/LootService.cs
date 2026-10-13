using System.Collections.Generic;
using HarmonyLib;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// Items come from the map as in the stock game: chests, shrines, bosses and the teleporter drop them, they fly out
    /// and land, and where they land they go into the army's pool (the commander's inventory) instead of lying on the
    /// ground; the HUD feed announces them. Optionally (Loot/ItemDropsEnabled, the mode's old way) enemies drop items
    /// too, with a chance tied to the gold they give.
    /// </summary>
    internal static class LootService
    {
        // vanilla small chest price at the start of a run, in gold: the unit the item price is built from
        private const float ChestBasePrice = 25f;

        /// <summary>The commander's master: its inventory is the army's item pool.</summary>
        internal static CharacterMaster PoolMaster
        {
            get
            {
                var players = PlayerCharacterMasterController.instances;
                for (int i = 0; i < players.Count; i++)
                {
                    CharacterMaster m = players[i].master;
                    if (m) return m;
                }
                return null;
            }
        }

        internal static Inventory Pool
        {
            get
            {
                CharacterMaster m = PoolMaster;
                return m ? m.inventory : null;
            }
        }

        // ---- enemy kills -----------------------------------------------------------------------------

        /// <summary>Server: an enemy died; maybe it drops an item.</summary>
        internal static void OnEnemyKilled(CharacterBody victim, uint goldReward)
        {
            if (!Settings.ItemDropsEnabled.Value || !NetworkServer.active || !victim || !Run.instance) return;

            float price = ChestBasePrice * Settings.ItemGoldFactor.Value * Mathf.Pow(Mathf.Max(1f, Run.instance.difficultyCoefficient), 1.25f);
            float chance = goldReward / Mathf.Max(1f, price);

            if (victim.isElite) chance *= 1.8f;
            if (victim.isChampion || victim.isBoss) chance = Mathf.Max(chance * 4f, 0.6f);
            chance = Mathf.Clamp01(chance * LobbyRules.LootMultiplier);

            CharacterMaster luckSource = PoolMaster;
            if (!Util.CheckRoll(chance * 100f, luckSource)) return;

            PickupIndex pick = RollPickup(victim);
            if (!pick.isValid) return;
            Grant(new UniquePickup(pick), victim.corePosition);
        }

        /// <summary>Picks what drops: tier by the enemy's weight, then a random item of that tier from the run's pool.</summary>
        private static PickupIndex RollPickup(CharacterBody victim)
        {
            Run run = Run.instance;
            Xoroshiro128Plus rng = run.treasureRng;

            // weights: white, green, red, equipment, boss-tier
            float white = 70f, green = 26f, red = 3f, equipment = 4f, bossTier = 0f;
            if (victim.isElite) { white = 35f; green = 50f; red = 9f; equipment = 6f; }
            if (victim.isChampion || victim.isBoss) { white = 0f; green = 50f; red = 38f; equipment = 4f; bossTier = 8f; }

            // deeper into the run, better things become likelier
            float depth = Mathf.Min(run.stageClearCount, 10) * 0.04f;
            white *= 1f - depth;
            green *= 1f + depth;
            red *= 1f + depth * 2f;

            List<PickupIndex> list = null;
            float total = white + green + red + equipment + bossTier;
            float roll = rng.nextNormalizedFloat * total;
            if ((roll -= white) < 0f) list = run.availableTier1DropList;
            else if ((roll -= green) < 0f) list = run.availableTier2DropList;
            else if ((roll -= red) < 0f) list = run.availableTier3DropList;
            else if ((roll -= equipment) < 0f) list = run.availableEquipmentDropList;
            else list = run.availableBossDropList;

            if (list == null || list.Count == 0) list = run.availableTier1DropList;
            if (list == null || list.Count == 0) return PickupIndex.none;
            return rng.NextElementUniform(list);
        }

        // ---- granting ----------------------------------------------------------------------------------

        /// <summary>Server: puts a pickup into the army's pool and tells the clients.</summary>
        internal static bool Grant(UniquePickup pickup, Vector3? at)
        {
            if (!NetworkServer.active || !pickup.isValid) return false;
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            if (def == null) return false;

            CharacterMaster pool = PoolMaster;
            if (!pool || !pool.inventory) return false;

            bool given = false;
            if (def.itemIndex != ItemIndex.None)
            {
                // temporary (decaying) items would stay in the pool forever and never reach a hero: make them permanent
                pool.inventory.GiveItemPermanent(def.itemIndex, 1);
                given = true;
            }
            else if (def.equipmentIndex != EquipmentIndex.None)
            {
                HighCommandRun run = HighCommandRun.Instance;
                if (run != null) run.Army.EquipmentPool.Add(def.equipmentIndex);
                given = true;
            }
            else if (def.miscPickupIndex != MiscPickupIndex.None)
            {
                // lunar coins and the like go to the player's profile
                foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
                {
                    if (user && user.isParticipating) { user.AwardLunarCoins(1); break; }
                }
                given = true;
            }
            if (!given) return false;

            RunStats.AddItem();
            HighCommandRun hc = HighCommandRun.Instance;
            if (hc != null)
            {
                HcState feed = hc.State;
                if (feed) feed.CallRpcItemFeed((int)pickup.pickupIndex.value, 1);
                hc.OnPoolChanged();
            }
            return true;
        }
    }

    internal static class LootPatches
    {
        /// <summary>
        /// An item flying out of a chest (or a boss, a shrine ...) is seen on its way, as in the stock game; it joins the
        /// pool where it lands (see <see cref="WorldPickups"/>). One that never lands (it falls off the map, the stage
        /// ends while it flies) is granted when it disappears.
        /// </summary>
        [HarmonyPatch(typeof(PickupDropletController), "OnDisable")]
        internal static class LostDroplets
        {
            private static readonly AccessTools.FieldRef<PickupDropletController, bool> Alive =
                AccessTools.FieldRefAccess<PickupDropletController, bool>("alive");

            private static void Postfix(PickupDropletController __instance)
            {
                if (!HighCommandRun.IsActive || !NetworkServer.active || !Alive(__instance)) return;
                Alive(__instance) = false;
                LootService.Grant(__instance.pickupState, __instance.transform.position);
            }
        }

        /// <summary>Every item that would lie on the ground (a landed droplet, a boss reward ...) goes into the pool.</summary>
        [HarmonyPatch(typeof(GenericPickupController), nameof(GenericPickupController.CreatePickup))]
        internal static class WorldPickups
        {
            private static bool Prefix(ref GenericPickupController.CreatePickupInfo createPickupInfo, ref GenericPickupController __result)
            {
                if (!HighCommandRun.IsActive || !NetworkServer.active) return true;
                if (LootService.Grant(createPickupInfo.pickup, createPickupInfo.position))
                {
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        /// <summary>Kills by the army drop items.</summary>
        [HarmonyPatch(typeof(DeathRewards), nameof(DeathRewards.OnKilledServer))]
        internal static class KillDrops
        {
            private static void Postfix(DeathRewards __instance, DamageReport damageReport)
            {
                if (!HighCommandRun.IsActive || !NetworkServer.active) return;
                CharacterBody victim = damageReport.victimBody;
                if (!victim || !victim.teamComponent) return;
                TeamIndex team = victim.teamComponent.teamIndex;
                if (team != TeamIndex.Monster && team != TeamIndex.Lunar && team != TeamIndex.Void) return;
                if (damageReport.attackerTeamIndex != TeamIndex.Player) return;

                LootService.OnEnemyKilled(victim, __instance.goldReward);
            }
        }

        /// <summary>
        /// No chests: interactables that cost something (chests, printers, shrines of chance, broken drones ...) are
        /// never placed. Free ones (teleporter, portals, barrels, the mountain shrine ...) stay.
        /// </summary>
        [HarmonyPatch(typeof(DirectorCore), nameof(DirectorCore.TrySpawnObject))]
        internal static class NoPaidInteractables
        {
            private static bool Prefix(DirectorSpawnRequest directorSpawnRequest, ref GameObject __result)
            {
                if (!HighCommandRun.IsActive || !Settings.RemovePaidInteractables.Value) return true;
                var card = directorSpawnRequest?.spawnCard as InteractableSpawnCard;
                if (card == null || !card.prefab) return true;

                if (!IsPaid(card.prefab)) return true;
                string name = card.prefab.name;
                foreach (string keep in Settings.KeptInteractables())
                    if (name.IndexOf(keep, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

                __result = null;
                return false;
            }

            private static bool IsPaid(GameObject prefab)
            {
                if (prefab.GetComponent<ChestBehavior>()) return true;
                if (prefab.GetComponent<ShopTerminalBehavior>()) return true;
                var purchase = prefab.GetComponent<PurchaseInteraction>();
                if (purchase && (purchase.costType != CostTypeIndex.None && purchase.cost > 0)) return true;
                return false;
            }
        }
    }
}
