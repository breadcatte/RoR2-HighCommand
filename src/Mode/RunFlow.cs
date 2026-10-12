using System.Collections;
using HighCommand.Army;
using HighCommand.Orders;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Mode
{
    /// <summary>
    /// The part of the run the stock game cannot do for a bodiless commander (server only):
    ///  * defeat: the run is lost when no hero is alive and nobody can pay for a revival in time;
    ///  * the teleporter and other holdout zones: the army stays inside the zone while it charges, and the state of the
    ///    teleporter is published for the UI;
    ///  * a new stage: the rally point moves to where the army arrived.
    /// </summary>
    internal sealed class RunFlow : MonoBehaviour
    {
        private const float TickInterval = 0.25f;
        private const float StageSettleSeconds = 9f;

        private HighCommandRun run;
        private float tickTimer;
        private float stageStart;
        private float noHeroSeconds;
        private bool subscribed;

        /// <summary>Cheapest revival the army could buy right now.</summary>
        internal static bool CanAffordRevive()
        {
            HighCommandRun r = HighCommandRun.Instance;
            if (r == null || r.State == null) return false;
            return Economy.Gold >= (uint)r.State.RevivePrice();
        }

        private void OnEnable()
        {
            run = GetComponent<HighCommandRun>();
            stageStart = Time.time;
            if (!subscribed)
            {
                Stage.onServerStageBegin += OnStageBegin;
                subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                Stage.onServerStageBegin -= OnStageBegin;
                subscribed = false;
            }
        }

        private void OnStageBegin(Stage stage)
        {
            stageStart = Time.time;
            noHeroSeconds = 0f;
            if (run != null && run.Army.Count > 0) BeginRelocateRally();
        }

        /// <summary>Waits for the heroes to stand on the ground, then makes that place the rally point.</summary>
        internal void BeginRelocateRally()
        {
            StartCoroutine(RelocateRally());
        }

        // After the stock respawn (or the first pod landing) the heroes stand on their feet: that is the rally point.
        private IEnumerator RelocateRally()
        {
            float waited = 0f;
            while (waited < 40f)
            {
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
                if (run == null) yield break;

                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (Hero h in run.Army.Heroes)
                {
                    if (!h.IsAlive) continue;
                    CharacterBody b = h.Body;
                    if (!b || b.currentVehicle) continue;                     // still in the drop pod
                    CharacterMotor motor = b.characterMotor;
                    if (motor && !motor.isGrounded) continue;                 // still falling
                    sum += h.Position;
                    n++;
                }
                if (n > 0)
                {
                    Vector3 rally = Formation.SnapToGround(sum / n);
                    run.RallyPoint = rally;

                    // the stock respawn scatters the army over ~100 m: bring everybody together around the rally point
                    int slot = 0;
                    foreach (Hero h in run.Army.Heroes)
                    {
                        HeroBrain brain = OrderService.BrainOf(h);
                        if (brain == null || !h.IsAlive) continue;
                        float angle = slot * 2.399963f;
                        float radius = 3f + 2.2f * Mathf.Sqrt(slot);
                        Vector3 spot = rally + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                        brain.ClearOrders(false);
                        brain.Anchor = Formation.SnapToGround(spot);
                        slot++;
                    }
                    yield break;
                }
            }
        }

        private void Update()
        {
            if (!NetworkServer.active || run == null) return;
            tickTimer -= Time.unscaledDeltaTime;
            if (tickTimer > 0f) return;
            tickTimer = TickInterval;

            long started = Dev.Perf.Begin();
            try { Tick(TickInterval); }
            catch (System.Exception e) { Plugin.Log.LogError("RunFlow.Tick failed: " + e); }
            Dev.Perf.End(ref Dev.Perf.Flow, started);
        }

        private void Tick(float dt)
        {
            Run vanilla = Run.instance;
            Stage stage = Stage.instance;
            HcState state = run.State;
            if (!vanilla || !stage || state == null || vanilla.isGameOverServer)
            {
                noHeroSeconds = 0f;
                return;
            }

            // ---- defeat ----
            float defeatSeconds = -1f;
            bool canRevive = CanAffordRevive();
            bool settled = Time.time - stageStart > StageSettleSeconds;
            if (run.Army.Count > 0 && run.Army.AliveCount == 0 && settled)
            {
                noHeroSeconds += dt;
                float limit = canRevive ? Settings.DefeatGraceRevive.Value : Settings.DefeatGraceBroke.Value;
                defeatSeconds = Mathf.Max(0f, limit - noHeroSeconds);
                if (noHeroSeconds >= limit)
                {
                    Plugin.Log.LogInfo("RunFlow: every hero is dead and nobody can come back in time: the run is lost.");
                    vanilla.BeginGameOver(RoR2Content.GameEndings.StandardLoss);
                    return;
                }
            }
            else
            {
                noHeroSeconds = 0f;
            }

            // ---- teleporter and holdout zones ----
            HcState.TeleState tele = HcState.TeleState.None;
            float charge = 0f;
            Vector3 telePos = Vector3.zero;
            float teleRadius = 0f;

            TeleporterInteraction tp = TeleporterInteraction.instance;
            if (tp)
            {
                telePos = tp.transform.position;
                HoldoutZoneController hz = tp.holdoutZoneController;
                charge = hz ? Mathf.Clamp01(hz.charge) : 0f;
                teleRadius = hz ? hz.currentRadius : 0f;
                switch (tp.activationState)
                {
                    case TeleporterInteraction.ActivationState.Idle:
                        tele = HcState.TeleState.Idle;
                        break;
                    case TeleporterInteraction.ActivationState.IdleToCharging:
                    case TeleporterInteraction.ActivationState.Charging:
                        tele = charge >= 0.999f ? HcState.TeleState.BossAlive : HcState.TeleState.Charging;
                        break;
                    case TeleporterInteraction.ActivationState.Charged:
                        tele = HcState.TeleState.Charged;
                        break;
                    default:
                        tele = HcState.TeleState.Leaving;
                        break;
                }
            }
            state.ServerSetFlow(defeatSeconds, canRevive, tele, charge, telePos, teleRadius);

            KeepArmyInZone(tp);
            if (SyncHeroFlags()) run.PublishRoster();
        }

        // Copies stance / auto-use / current order from each server-side brain into the Hero, which the roster replicates.
        private bool SyncHeroFlags()
        {
            bool changed = false;
            foreach (Hero h in run.Army.Heroes)
            {
                HeroBrain brain = OrderService.BrainOf(h);
                if (brain == null) continue;

                byte stance = (byte)brain.Stance;
                byte mask = 0;
                for (int i = 0; i < brain.AutoSlot.Length && i < 8; i++)
                    if (brain.AutoSlot[i]) mask |= (byte)(1 << i);

                Order order = brain.Current;
                byte kind = (byte)(order != null ? order.Kind : OrderKind.None);
                Vector3 point = order != null ? DestinationOf(order) : Vector3.zero;
                byte queued = (byte)Mathf.Min(255, brain.QueuedCount);

                if (h.NetStance != stance) { h.NetStance = stance; changed = true; }
                if (h.NetAutoMask != mask) { h.NetAutoMask = mask; changed = true; }
                if (h.NetOrderKind != kind) { h.NetOrderKind = kind; changed = true; }
                if (h.NetQueued != queued) { h.NetQueued = queued; changed = true; }
                if ((h.NetOrderPoint - point).sqrMagnitude > 0.36f) { h.NetOrderPoint = point; changed = true; }

                // skill cooldowns: only the server's bodies simulate them, so they travel with the roster
                CharacterBody body = h.Body;
                if (body) RunStats.NoteLevel((int)body.level);
                SkillLocator locator = body ? body.skillLocator : null;
                for (int s = 0; s < 3; s++)
                {
                    GenericSkill skill = locator == null ? null : (s == 0 ? locator.secondary : s == 1 ? locator.utility : locator.special);
                    byte cd = 0;
                    if (skill && skill.skillDef && skill.stock <= 0)
                        cd = (byte)Mathf.Clamp(Mathf.CeilToInt(skill.cooldownRemaining), 1, 255);
                    if (h.NetSkillCd[s] != cd) { h.NetSkillCd[s] = cd; changed = true; }
                }
            }
            return changed;
        }

        private static Vector3 DestinationOf(Order o)
        {
            switch (o.Kind)
            {
                case OrderKind.Move:
                case OrderKind.AttackMove:
                case OrderKind.Retreat:
                case OrderKind.Cast:
                    return o.Point;
                case OrderKind.Patrol:
                    return o.patrolToSecond ? o.Point2 : o.Point;
                case OrderKind.Attack:
                case OrderKind.Follow:
                case OrderKind.Interact:
                    return o.Target ? o.Target.transform.position : o.Point;
                default:
                    return Vector3.zero;
            }
        }

        // While a zone charges, idle heroes stand inside it (each at its own spot) and fight from there.
        private void KeepArmyInZone(TeleporterInteraction tp)
        {
            HoldoutZoneController zone = FindActiveZone(tp);
            Vector3 center = zone ? zone.transform.position : Vector3.zero;
            float radius = zone ? Mathf.Max(8f, zone.currentRadius) : 0f;

            foreach (Hero h in run.Army.Heroes)
            {
                if (!h.Master) continue;
                HeroBrain brain = h.Master.GetComponent<HeroBrain>();
                if (brain == null) continue;
                brain.ZoneCenter = center;
                brain.ZoneRadius = radius;
            }
        }

        private static HoldoutZoneController FindActiveZone(TeleporterInteraction tp)
        {
            if (tp && tp.isCharging && tp.holdoutZoneController) return tp.holdoutZoneController;

            foreach (HoldoutZoneController z in InstanceTracker.GetInstancesList<HoldoutZoneController>())
            {
                if (!z || !z.isActiveAndEnabled || z.chargingTeam != TeamIndex.Player) continue;
                if (z.charge >= 1f) continue;
                if (tp && z == tp.holdoutZoneController) continue;
                return z;
            }
            return null;
        }
    }
}
