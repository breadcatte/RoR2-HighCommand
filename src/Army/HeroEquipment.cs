using RoR2;
using UnityEngine;

namespace HighCommand.Army
{
    /// <summary>
    /// Where a hero keeps equipment. Most survivors have one slot, but some (MUL-T) have two and switch the active
    /// one every time the equipment is used, so "the" equipment of a hero is the set of pieces in all of its slots.
    /// Every operation here works on the active set of a slot, like the game's own pickup code does.
    /// </summary>
    internal static class HeroEquipment
    {
        /// <summary>How many equipment slots the UI shows for this inventory (1..3).</summary>
        internal static int SlotCount(Inventory inv) => inv ? Mathf.Clamp(inv.equipmentSlotCount, 1, 3) : 1;

        internal static EquipmentLocation LocationOfSlot(Inventory inv, int slot)
        {
            var location = new EquipmentLocation { slot = (uint)slot, set = inv.GetActiveEquipmentSetInSlot((uint)slot) };
            return location;
        }

        /// <summary>The piece shown in a slot (its active set), or None.</summary>
        internal static EquipmentIndex InSlot(Inventory inv, int slot)
        {
            if (!inv || slot < 0 || slot >= inv.equipmentSlotCount) return EquipmentIndex.None;
            EquipmentLocation location = LocationOfSlot(inv, slot);
            return inv.GetEquipmentIndex(in location);
        }

        /// <summary>
        /// Where a new piece goes: an empty place in any slot, otherwise the active place (whatever is there is replaced,
        /// exactly as picking up equipment does). <paramref name="empty"/> says which of the two it was.
        /// </summary>
        internal static EquipmentLocation TargetFor(Inventory inv, out bool empty)
        {
            var query = new Inventory.EquipmentLocationQueryArgs
            {
                allowEmpty = true,
                searchAllSlots = true,
                searchCurrentSetOnly = true
            };
            if (inv.TryFindBestEquipmentLocation(in query, out EquipmentLocation location))
            {
                empty = true;
                return location;
            }
            empty = false;
            return inv.activeEquipmentLocation;
        }
    }
}
