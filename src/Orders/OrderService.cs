using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Orders
{
    /// <summary>
    /// Entry point for commands from the commander's UI. In single player (host) the command is applied right away;
    /// in co-op a client's command would travel to the server first (the OrderCommand is already serialisable).
    /// </summary>
    internal static class OrderService
    {
        internal static void Submit(OrderCommand command)
        {
            if (command == null) return;
            if (NetworkServer.active) Execute(command);
            else Plugin.Log.LogWarning("Order submitted on a client without a server link: co-op transport is not available yet.");
        }

        /// <summary>Server side: applies a command to the army.</summary>
        internal static void Execute(OrderCommand command)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (run == null) return;

            var heroes = new List<Hero>(command.HeroIds.Length);
            foreach (int id in command.HeroIds)
            {
                foreach (Hero h in run.Army.Heroes)
                    if (h.Id == id) { heroes.Add(h); break; }
            }
            if (heroes.Count == 0) return;

            if (command.SetAutoSlot)
            {
                foreach (Hero h in heroes)
                {
                    HeroBrain b = BrainOf(h);
                    if (b != null && command.Slot >= 0 && command.Slot < b.AutoSlot.Length) b.AutoSlot[command.Slot] = command.AutoValue;
                }
                return;
            }

            if (command.NewStance.HasValue)
            {
                foreach (Hero h in heroes) BrainOf(h)?.SetStance(command.NewStance.Value);
                if (command.Kind == OrderKind.None) return;
            }
            if (command.Kind == OrderKind.None) return;

            GameObject target = command.TargetId != NetworkInstanceId.Invalid ? NetworkServer.FindLocalObject(command.TargetId) : null;

            // Stop / Hold use each hero's own position; point orders are spread in a formation.
            bool pointOrder = command.Kind == OrderKind.Move || command.Kind == OrderKind.AttackMove
                              || command.Kind == OrderKind.Retreat || command.Kind == OrderKind.Patrol;

            Vector3[] destinations = null;
            if (pointOrder)
            {
                var positions = new List<Vector3>(heroes.Count);
                foreach (Hero h in heroes) positions.Add(h.Position);
                destinations = Formation.Layout(positions, command.Point, command.Formation);
            }

            for (int i = 0; i < heroes.Count; i++)
            {
                HeroBrain brain = BrainOf(heroes[i]);
                if (brain == null) continue;

                var order = new Order
                {
                    Kind = command.Kind,
                    Point = destinations != null ? destinations[i] : command.Point,
                    Point2 = command.Point2,
                    Target = target,
                    TargetId = command.TargetId,
                    Slot = command.Slot,
                    Focus = command.Focus
                };
                if (command.Kind == OrderKind.Patrol)
                {
                    // patrol between where the hero is now and the clicked point
                    order.Point2 = order.Point;
                    order.Point = heroes[i].Position;
                }
                if (command.Kind == OrderKind.Hold || command.Kind == OrderKind.Stop)
                    order.Point = heroes[i].Position;

                brain.Issue(order, command.Queue);
            }

            if (Settings.VerboseLog.Value)
                Plugin.Log.LogInfo($"Order {command.Kind} -> {heroes.Count} hero(es), queue={command.Queue}");
        }

        internal static HeroBrain BrainOf(Hero hero)
        {
            return hero != null && hero.Master ? hero.Master.GetComponent<HeroBrain>() : null;
        }
    }
}
