using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using HighCommand.Net;
using HighCommand.UI;
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
            if (!CommandChannel.ShouldRelay) { Execute(command); return; }

            string result = CommandChannel.SendOrder(command);
            if (result != CommandChannel.Relayed) Toast.Show(result);
        }

        /// <summary>Server side: applies a command to the army.</summary>
        internal static void Execute(OrderCommand command)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (run == null) return;

            if (command.SetRally)
            {
                run.RallyPoint = command.Point;
                return;
            }

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
                foreach (Hero h in heroes)
                {
                    HeroBrain stanceBrain = BrainOf(h);
                    if (stanceBrain) stanceBrain.SetStance(command.NewStance.Value);
                }
                if (command.Kind == OrderKind.None) return;
            }
            if (command.Kind == OrderKind.None) return;

            GameObject target = command.TargetId != NetworkInstanceId.Invalid ? NetworkServer.FindLocalObject(command.TargetId) : null;

            // An object on the map (a chest, a shrine, a drone ...): one hero goes, the nearest by the walk, and comes
            // back to what it was doing. The teleporter keeps its own routine (ArmyService.ExecTeleporter).
            if (command.Kind == OrderKind.Interact && target && !target.GetComponent<TeleporterInteraction>())
            {
                SendRunner(heroes, target, command);
                return;
            }

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

        /// <summary>The hero sent by the last object order (host only; the interface names it).</summary>
        internal static Hero LastRunner { get; private set; }

        private static void SendRunner(List<Hero> heroes, GameObject target, OrderCommand command)
        {
            Hero previous = LastRunner;
            LastRunner = null;
            Hero best = null;
            float bestCost = float.MaxValue;
            Vector3 goal = target.transform.position;

            // Shift-clicks on several objects make one hero's route: the hero of the last trip takes the next one too
            if (command.Queue && previous != null && previous.IsAlive && previous.Body && heroes.Contains(previous))
            {
                HeroBrain prevBrain = BrainOf(previous);
                if (prevBrain != null && prevBrain.HasErrands)
                {
                    best = previous;
                    bestCost = PathCost.Walk(prevBrain.ErrandsEnd(previous.Body.footPosition), goal);
                }
            }

            // otherwise the hero with the shortest walk (with Shift: from where its planned trips end)
            if (best == null)
            {
                foreach (Hero h in heroes)
                {
                    HeroBrain brain = BrainOf(h);
                    CharacterBody body = h.Body;
                    if (brain == null || !h.IsAlive || !body || body.currentVehicle) continue;

                    Vector3 from = command.Queue ? brain.ErrandsEnd(body.footPosition) : body.footPosition;
                    float cost = PathCost.Walk(from, goal);
                    if (brain.Fleeing) cost += 80f; // hurt and running: the last choice
                    if (cost < bestCost) { bestCost = cost; best = h; }
                }
            }
            if (best == null) return;

            // a long way round takes long: about 4 m/s counted (fights on the way), never less than 45 s
            // (no path found: a short try - the graph may miss a way a hero can still take)
            float budget = bestCost >= 50000f ? 60f : Mathf.Clamp(30f + bestCost / 4f, 45f, 300f);
            var order = new Order { Kind = OrderKind.Interact, Target = target, TargetId = command.TargetId, Point = goal, walkBudget = budget };
            BrainOf(best).Errand(order, command.Queue);
            LastRunner = best;
            if (Settings.VerboseLog.Value)
                Plugin.Log.LogInfo($"Errand {target.name} -> {best} (walk {bestCost:0} m, queued={command.Queue})");
        }

        internal static HeroBrain BrainOf(Hero hero)
        {
            if (hero == null) return null;
            if (hero.Brain) return hero.Brain; // set when the hero is created: no component lookup per call
            return hero.Master ? hero.Master.GetComponent<HeroBrain>() : null;
        }
    }
}
