using System.Collections.Generic;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// Shares the army's item pool (the commander's inventory) between the heroes (server only).
    ///
    /// Model: the pool inventory is the truth of what the army owns. Each unit of an item is held by exactly ONE
    /// hero (like in a normal co-op run, never mirrored by default). When the pool gains units, each new unit goes to
    /// the hero that wants it most (role vs the item's tags, fewest items so far). The commander can move units
    /// between heroes by hand; the distributor never takes items back on its own.
    /// </summary>
    internal sealed class ItemDistributor
    {
        public enum SharingMode { Split, Mirror }

        private readonly Dictionary<ItemIndex, int> distributedTotal = new Dictionary<ItemIndex, int>();
        private bool busy;

        internal void Reset() => distributedTotal.Clear();

        /// <summary>
        /// Units the army no longer owns (a hero paid a printer with them): they are not handed out again. In the mirror
        /// mode every other hero holds a copy of them too, and those copies go as well.
        /// </summary>
        internal void Forget(ItemIndex idx, int count, Hero payer = null)
        {
            if (count <= 0) return;
            distributedTotal.TryGetValue(idx, out int given);
            distributedTotal[idx] = Mathf.Max(0, given - count);

            HighCommand.Mode.HighCommandRun run = HighCommand.Mode.HighCommandRun.Instance;
            if (!HighCommand.Mode.LobbyRules.ItemMirror || run == null) return;
            foreach (Hero h in run.Army.Heroes)
            {
                if (h == payer || !h.Master || !h.Master.inventory) continue;
                int remove = Mathf.Min(count, h.Master.inventory.GetItemCountPermanent(idx));
                if (remove > 0) h.Master.inventory.RemoveItemPermanent(idx, remove);
            }
        }

        /// <summary>Hands out whatever the pool gained since the last call.</summary>
        internal void Sync(ArmyManager army)
        {
            if (busy || !NetworkServer.active || army == null) return;
            Inventory pool = LootService.Pool;
            if (!pool) return;

            busy = true;
            try
            {
                var heroes = new List<Hero>();
                foreach (Hero h in army.Heroes) if (h.Master && h.Master.inventory) heroes.Add(h);
                if (heroes.Count == 0) return;

                bool mirror = HighCommand.Mode.LobbyRules.ItemMirror;

                foreach (ItemIndex idx in ItemCatalog.allItems)
                {
                    int have = pool.GetItemCountPermanent(idx);
                    distributedTotal.TryGetValue(idx, out int given);
                    if (have <= given) continue;

                    ItemDef def = ItemCatalog.GetItemDef(idx);
                    if (def == null) continue;

                    for (int n = given; n < have; n++)
                    {
                        if (mirror)
                        {
                            foreach (Hero h in heroes) h.Master.inventory.GiveItemPermanent(idx, 1);
                        }
                        else
                        {
                            Hero target = BestHero(heroes, def);
                            if (target != null) target.Master.inventory.GiveItemPermanent(idx, 1);
                        }
                    }
                    distributedTotal[idx] = have;
                }

                DistributeEquipment(army, heroes);
            }
            finally
            {
                busy = false;
            }
        }

        private static Hero BestHero(List<Hero> heroes, ItemDef def)
        {
            Hero best = null;
            float bestScore = float.MinValue;
            foreach (Hero h in heroes)
            {
                HeroRole role = HeroRoles.RoleOf(h.Survivor);
                float affinity = 0.6f;
                bool any = false;
                float sum = 0f;
                int count = 0;
                if (def.tags != null)
                {
                    foreach (ItemTag tag in def.tags)
                    {
                        if (tag != ItemTag.Damage && tag != ItemTag.Healing && tag != ItemTag.Utility
                            && tag != ItemTag.SprintRelated && tag != ItemTag.MobilityRelated) continue;
                        sum += HeroRoles.Affinity(role, tag);
                        count++;
                        any = true;
                    }
                }
                if (any) affinity = sum / count;

                int owned = CountItems(h.Master.inventory);
                // 0.16 per item already owned: a role preference can lead by about three items, not more
                float score = affinity - 0.16f * owned + Random.value * 0.05f;
                if (!h.IsAlive) score -= 0.15f; // fallen heroes are the last choice
                if (score > bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        private static int CountItems(Inventory inv)
        {
            int total = 0;
            foreach (ItemIndex idx in inv.itemAcquisitionOrder)
            {
                ItemDef d = ItemCatalog.GetItemDef(idx);
                if (d != null && !d.hidden && d.tier != ItemTier.NoTier) total += inv.GetItemCountPermanent(idx);
            }
            return total;
        }

        private static void DistributeEquipment(ArmyManager army, List<Hero> heroes)
        {
            if (army.EquipmentPool.Count == 0) return;
            foreach (Hero h in heroes)
            {
                if (army.EquipmentPool.Count == 0) break;
                Inventory inv = h.Master.inventory;
                // only an EMPTY place is used: a hero that carries equipment keeps it (MUL-T has two places)
                EquipmentLocation where = HeroEquipment.TargetFor(inv, out bool empty);
                if (!empty) continue;
                EquipmentIndex eq = army.EquipmentPool[0];
                army.EquipmentPool.RemoveAt(0);
                inv.SetEquipmentIndex(eq, in where);
            }
        }

        // ---- manual moves ----------------------------------------------------------------------------

        /// <summary>Moves units of an item from one hero to another. Returns the number moved.</summary>
        internal int Move(ItemIndex item, Hero from, Hero to, int count)
        {
            if (!NetworkServer.active || from == null || to == null || from == to) return 0;
            if (!from.Master || !to.Master || !from.Master.inventory || !to.Master.inventory) return 0;

            int available = from.Master.inventory.GetItemCountPermanent(item);
            int n = Mathf.Min(count, available);
            if (n <= 0) return 0;
            from.Master.inventory.RemoveItemPermanent(item, n);
            to.Master.inventory.GiveItemPermanent(item, n);
            return n;
        }

        /// <summary>
        /// Hands the piece in <paramref name="fromSlot"/> of one hero to another: into an empty place when the receiver
        /// has one, otherwise it swaps with the receiver's active piece.
        /// </summary>
        internal bool MoveEquipment(Hero from, int fromSlot, Hero to)
        {
            if (!NetworkServer.active || from == null || to == null || from == to) return false;
            if (!from.Master || !to.Master || !from.Master.inventory || !to.Master.inventory) return false;

            Inventory a = from.Master.inventory, b = to.Master.inventory;
            EquipmentIndex piece = HeroEquipment.InSlot(a, fromSlot);
            if (piece == EquipmentIndex.None) return false;

            EquipmentLocation source = HeroEquipment.LocationOfSlot(a, fromSlot);
            EquipmentLocation target = HeroEquipment.TargetFor(b, out bool _);
            EquipmentIndex displaced = b.GetEquipmentIndex(in target);

            a.SetEquipmentIndex(displaced, in source);
            b.SetEquipmentIndex(piece, in target);
            return true;
        }

        /// <summary>Gives a hero a piece of spare equipment from the pool; what the hero held goes back to the pool.</summary>
        internal bool EquipFromPool(ArmyManager army, int poolIndex, Hero to)
        {
            if (!NetworkServer.active || army == null || to == null || !to.Master || !to.Master.inventory) return false;
            if (poolIndex < 0 || poolIndex >= army.EquipmentPool.Count) return false;

            Inventory inv = to.Master.inventory;
            // the same place a picked-up piece would go to; whatever stood there goes back to the pool
            EquipmentLocation where = HeroEquipment.TargetFor(inv, out bool _);
            EquipmentIndex old = inv.GetEquipmentIndex(in where);
            EquipmentIndex next = army.EquipmentPool[poolIndex];
            army.EquipmentPool.RemoveAt(poolIndex);
            if (old != EquipmentIndex.None) army.EquipmentPool.Add(old);

            inv.SetEquipmentIndex(next, in where);
            return true;
        }

        /// <summary>
        /// Takes back every pooled item from the heroes and deals them out again by role, rarest first. Items a hero
        /// owns for other reasons (more units than the pool knows about) are left alone.
        /// </summary>
        internal void RedistributeAll(ArmyManager army)
        {
            if (!NetworkServer.active || army == null || HighCommand.Mode.LobbyRules.ItemMirror) return;
            Inventory pool = LootService.Pool;
            if (!pool) return;

            var heroes = new List<Hero>();
            foreach (Hero h in army.Heroes) if (h.Master && h.Master.inventory) heroes.Add(h);
            if (heroes.Count == 0) return;

            var units = new List<ItemDef>();
            foreach (ItemIndex idx in ItemCatalog.allItems)
            {
                int want = pool.GetItemCountPermanent(idx);
                if (want <= 0) continue;
                ItemDef def = ItemCatalog.GetItemDef(idx);
                if (def == null) continue;

                foreach (Hero h in heroes)
                {
                    if (want <= 0) break;
                    int take = Mathf.Min(want, h.Master.inventory.GetItemCountPermanent(idx));
                    if (take <= 0) continue;
                    h.Master.inventory.RemoveItemPermanent(idx, take);
                    want -= take;
                    for (int n = 0; n < take; n++) units.Add(def);
                }
            }

            units.Sort((x, y) => TierRank(x.tier).CompareTo(TierRank(y.tier)));
            foreach (ItemDef def in units)
            {
                Hero target = BestHero(heroes, def);
                if (target != null) target.Master.inventory.GiveItemPermanent(def.itemIndex, 1);
            }
        }

        /// <summary>Lower = rarer / more important. Shared by the distributor and the inventory panel.</summary>
        internal static int TierRank(ItemTier tier)
        {
            switch (tier)
            {
                case ItemTier.Tier3: return 0;
                case ItemTier.Boss: return 1;
                case ItemTier.Lunar: return 2;
                case ItemTier.VoidTier3:
                case ItemTier.VoidBoss: return 3;
                case ItemTier.Tier2: return 4;
                case ItemTier.VoidTier2: return 5;
                case ItemTier.Tier1: return 6;
                case ItemTier.VoidTier1: return 7;
                default: return 8;
            }
        }

        /// <summary>Gives a newly recruited hero a fair share taken from the best-equipped heroes.</summary>
        internal void CatchUp(ArmyManager army, Hero newcomer)
        {
            if (!NetworkServer.active || newcomer == null || !newcomer.Master || !newcomer.Master.inventory) return;

            if (HighCommand.Mode.LobbyRules.ItemMirror)
            {
                // everybody owns everything: the newcomer gets a copy of the whole pool
                Inventory pool = LootService.Pool;
                if (!pool) return;
                foreach (ItemIndex idx in ItemCatalog.allItems)
                {
                    int missing = pool.GetItemCountPermanent(idx) - newcomer.Master.inventory.GetItemCountPermanent(idx);
                    if (missing > 0) newcomer.Master.inventory.GiveItemPermanent(idx, missing);
                }
                return;
            }
            if (!Settings.RebalanceOnRecruit.Value) return;

            var heroes = new List<Hero>();
            int total = 0;
            foreach (Hero h in army.Heroes)
            {
                if (!h.Master || !h.Master.inventory) continue;
                heroes.Add(h);
                total += CountItems(h.Master.inventory);
            }
            if (heroes.Count < 2) return;

            int target = total / heroes.Count;
            int guard = 200;
            while (CountItems(newcomer.Master.inventory) < target && guard-- > 0)
            {
                Hero donor = null;
                int best = 0;
                foreach (Hero h in heroes)
                {
                    if (h == newcomer) continue;
                    int c = CountItems(h.Master.inventory);
                    if (c > best) { best = c; donor = h; }
                }
                if (donor == null || best <= target) break;

                // take a unit of the donor's most plentiful item (it hurts them least)
                ItemIndex pick = ItemIndex.None;
                int most = 0;
                foreach (ItemIndex idx in donor.Master.inventory.itemAcquisitionOrder)
                {
                    ItemDef d = ItemCatalog.GetItemDef(idx);
                    if (d == null || d.hidden || d.tier == ItemTier.NoTier) continue;
                    int c = donor.Master.inventory.GetItemCountPermanent(idx);
                    if (c > most) { most = c; pick = idx; }
                }
                if (pick == ItemIndex.None) break;
                Move(pick, donor, newcomer, 1);
            }
        }
    }
}
