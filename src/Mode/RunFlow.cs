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
        private bool autoReviveFailed;
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
            autoReviveFailed = false;
            if (run != null && run.Army.Count > 0)
            {
                StartCoroutine(ArriveOnStage(stage));
                BeginRelocateRally();
            }
        }

        /// <summary>
        /// The rally point of the old stage means nothing here: it moves to the player spawn at once (a revival before
        /// the army has landed would otherwise put the hero at the old map's coordinates, often high in the air).
        /// On the scenes where the stock game brings no allied AI along (SceneDef.suppressNpcEntry: arena = Void Fields,
        /// bazaar, the DLC intermissions; see hc_dump_scenes) the heroes are brought in here, or the army would have no
        /// bodies and lose the run.
        /// </summary>
        private IEnumerator ArriveOnStage(Stage stage)
        {
            float waited = 0f;
            while (SpawnPoint.readOnlyInstancesList.Count == 0 && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!stage || run == null) yield break;

            Transform spawn = stage.GetPlayerSpawnTransform();
            if (spawn) run.RallyPoint = Formation.SnapToGround(spawn.position);

            if (!stage.sceneDef || !stage.sceneDef.suppressNpcEntry) yield break;
            int brought = 0;
            foreach (Hero h in run.Army.Heroes)
            {
                CharacterMaster master = h.Master;
                if (!master || master.GetBodyObject()) continue;
                Vector3 where = Formation.GroundNear(run.RallyPoint, 2f, 8f, run.Army.Heroes);
                if (master.Respawn(where, spawn ? spawn.rotation : Quaternion.identity)) brought++;
            }
            Plugin.Log.LogInfo($"RunFlow: {stage.sceneDef.cachedName} brings no allies along: {brought} hero(es) brought in by the mode.");
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
            bool autoRevive = false;
            bool settled = Time.time - stageStart > StageSettleSeconds;
            if (run.Army.Count > 0 && run.Army.AliveCount == 0 && settled)
            {
                noHeroSeconds += dt;
                if (canRevive && Settings.AutoRevive.Value && TryAutoRevive(out bool revived))
                {
                    // the army can pay: the first fallen hero comes back by itself after a short pause
                    autoRevive = true;
                    defeatSeconds = revived ? -1f : Mathf.Max(0f, Settings.AutoReviveDelay.Value - noHeroSeconds);
                    if (revived) noHeroSeconds = 0f;
                }
                else
                {
                    float limit = canRevive ? Settings.DefeatGraceRevive.Value : Settings.DefeatGraceBroke.Value;
                    defeatSeconds = Mathf.Max(0f, limit - noHeroSeconds);
                    if (noHeroSeconds >= limit)
                    {
                        Plugin.Log.LogInfo("RunFlow: every hero is dead and nobody can come back in time: the run is lost.");
                        vanilla.BeginGameOver(RoR2Content.GameEndings.StandardLoss);
                        return;
                    }
                }
            }
            else
            {
                noHeroSeconds = 0f;
                autoReviveFailed = false;
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
            state.ServerSetFlow(defeatSeconds, canRevive, autoRevive, tele, charge, telePos, teleRadius);

            KeepArmyInZone(tp);
            if (SyncHeroFlags()) run.PublishRoster();
        }

        /// <summary>
        /// Every hero is down and a revival is affordable: once the delay has passed, buy it (the same purchase as the
        /// Revive key). Returns false when there is nobody to revive (the normal defeat countdown applies then).
        /// </summary>
        private bool TryAutoRevive(out bool revived)
        {
            revived = false;
            if (autoReviveFailed) return false;
            Hero fallen = ArmyService.FirstFallen(run.Army.Heroes);
            if (fallen == null) return false;
            if (noHeroSeconds < Settings.AutoReviveDelay.Value) return true;

            string error = ArmyService.ExecRevive(fallen);
            if (error != null)
            {
                // once per wipe: the manual countdown takes over (no warning every tick)
                autoReviveFailed = true;
                Plugin.Log.LogWarning($"RunFlow: automatic revival of {fallen} failed: {error}");
                return false;
            }
            revived = true;
            Plugin.Log.LogInfo($"RunFlow: every hero had fallen, {fallen} was revived automatically.");
            return true;
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
                Vector3 point = order != null ? order.Destination() : Vector3.zero;
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

        // While a zone charges, idle heroes stand inside it (each at its own spot) and fight from there.
        private void KeepArmyInZone(TeleporterInteraction tp)
        {
            HoldoutZoneController zone = FindActiveZone(tp);
            Vector3 center = zone ? zone.transform.position : Vector3.zero;
            float radius = zone ? Mathf.Max(8f, zone.currentRadius) : 0f;

            foreach (Hero h in run.Army.Heroes)
            {
                HeroBrain brain = OrderService.BrainOf(h);
                if (!brain) continue;
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
