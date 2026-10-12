using System;
using System.Collections.Generic;
using HarmonyLib;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// The army's shared purse (server side). Every source of money in the stock game that would go to a player's
    /// master is redirected here: kill rewards (TeamManager.GiveTeamMoney) and direct gifts (CharacterMaster.GiveMoney).
    /// </summary>
    internal static class Economy
    {
        /// <summary>Gold gained: amount and where it came from (for the floating "+$" text). Host-side only.</summary>
        internal static event Action<uint, Vector3?> OnGoldGained;

        private static readonly Queue<KeyValuePair<float, uint>> Income = new Queue<KeyValuePair<float, uint>>();

        private static HcState State => HighCommandRun.Instance != null ? HighCommandRun.Instance.State : null;

        internal static uint Gold => State ? State.Gold : 0u;

        internal static void ResetForNewRun()
        {
            Income.Clear();
            HcState s = State;
            if (s != null && NetworkServer.active)
            {
                s.ServerSetGold((uint)Mathf.Max(0, LobbyRules.StartingGold));
                s.ServerSetBought(0);
                s.ServerPublishSettings();
            }
        }

        /// <summary>Server: adds gold (after the gold multiplier). Returns the amount actually added.</summary>
        internal static uint Add(uint amount, Vector3? at = null, bool applyMultiplier = true)
        {
            HcState s = State;
            if (!NetworkServer.active || s == null || amount == 0) return 0;

            uint gained = amount;
            if (applyMultiplier)
                gained = (uint)Mathf.Max(0, Mathf.RoundToInt(amount * Settings.GoldMultiplier.Value));
            if (gained == 0) return 0;

            ulong sum = (ulong)s.Gold + gained;
            s.ServerSetGold(sum > uint.MaxValue ? uint.MaxValue : (uint)sum);

            Income.Enqueue(new KeyValuePair<float, uint>(Time.time, gained));
            if (applyMultiplier) RunStats.AddGold(gained); // gifts and refunds are not earnings
            OnGoldGained?.Invoke(gained, at);
            return gained;
        }

        /// <summary>Server: removes gold if there is enough.</summary>
        internal static bool TrySpend(uint amount)
        {
            HcState s = State;
            if (!NetworkServer.active || s == null) return false;
            if (s.Gold < amount) return false;
            s.ServerSetGold(s.Gold - amount);
            return true;
        }

        /// <summary>Gold per minute over the last 60 seconds (for the HUD).</summary>
        internal static float IncomePerMinute()
        {
            float now = Time.time;
            while (Income.Count > 0 && now - Income.Peek().Key > 60f) Income.Dequeue();
            float sum = 0f;
            foreach (var e in Income) sum += e.Value;
            return sum;
        }
    }

    /// <summary>Redirects the stock game's player gold into the shared purse while a High Command run is live.</summary>
    internal static class EconomyPatches
    {
        [HarmonyPatch(typeof(TeamManager), nameof(TeamManager.GiveTeamMoney), typeof(TeamIndex), typeof(uint), typeof(bool))]
        internal static class KillGold
        {
            private static bool Prefix(TeamIndex teamIndex, uint money)
            {
                if (!HighCommandRun.IsActive) return true;
                if (teamIndex == TeamIndex.Player) Economy.Add(money, LastVictimPosition);
                return false;
            }
        }

        // The position of the enemy that just died: DeathRewards computes the gold right before it calls
        // GiveTeamMoney, so a prefix on DeathRewards.OnKilledServer tells the popup where to appear.
        internal static Vector3? LastVictimPosition;

        [HarmonyPatch(typeof(DeathRewards), nameof(DeathRewards.OnKilledServer))]
        internal static class RememberVictim
        {
            private static void Prefix(DeathRewards __instance)
            {
                if (!HighCommandRun.IsActive) return;
                CharacterBody body = __instance.GetComponent<CharacterBody>();
                LastVictimPosition = body ? body.corePosition : (Vector3?)null;
            }

            private static void Finalizer()
            {
                LastVictimPosition = null;
            }
        }

        /// <summary>Items such as "Roll of Pennies" give gold straight to a master: it goes to the purse instead.</summary>
        [HarmonyPatch(typeof(CharacterMaster), nameof(CharacterMaster.GiveMoney), typeof(uint), typeof(bool))]
        internal static class DirectGold
        {
            private static bool Prefix(CharacterMaster __instance, uint amount)
            {
                if (!HighCommandRun.IsActive || !NetworkServer.active) return true;
                if (__instance.teamIndex != TeamIndex.Player) return true;
                Economy.Add(amount, __instance.GetBody() ? __instance.GetBody().corePosition : (Vector3?)null);
                return false;
            }
        }
    }
}
