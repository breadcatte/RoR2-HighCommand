using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Navigation;
using UnityEngine;

namespace HighCommand.Orders
{
    public enum BrainMode : byte
    {
        Idle,   // stand where you are
        Fight,  // fight the engaged enemy using the hero's own skill rules; movement scaled by MoveScale
        Go,     // travel to Goal; no skills
        Cast    // aim at CastPoint / CastTarget and use CastSlot once
    }

    /// <summary>
    /// The decision layer of one hero (server only): holds the order queue and the stance, looks around a few times
    /// a second and publishes what the body should do (Mode, Goal, Enemy ...). The actual button pressing and
    /// walking is done by <see cref="HeroAIState"/> every physics frame.
    /// </summary>
    public sealed class HeroBrain : MonoBehaviour
    {
        // ---- tunables ---------------------------------------------------------------------------
        private const float ThinkInterval = 0.12f;
        private const float AcquireAggressive = 55f;
        private const float AcquireDefensive = 32f;
        private const float AcquireAttackMove = 42f;
        private const float AcquireHold = 36f;
        private const float LeashAggressive = 95f;
        private const float LeashDefensive = 42f;
        private const float ArriveDistance = 3.2f;
        private const float StuckSeconds = 2.8f;

        // ---- state ------------------------------------------------------------------------------
        public Hero Hero { get; internal set; }

        public Stance Stance = Stance.Aggressive;

        /// <summary>Auto-use switches: skill slots 0..3 and the equipment (4). Off = the AI never presses it.</summary>
        public readonly bool[] AutoSlot = { true, true, true, true, true };

        /// <summary>Where the hero stands when it has nothing to do.</summary>
        public Vector3 Anchor;

        /// <summary>
        /// While a holdout zone (teleporter, pillar ...) charges, RunFlow sets its centre and radius here: an idle hero
        /// then stands inside it, at its own spot, and fights from there. Radius 0 = no zone.
        /// </summary>
        public Vector3 ZoneCenter;
        public float ZoneRadius;

        private Vector3 zoneSpot;
        private Vector3 zoneSpotKey;
        private float zoneSpotRadius;
        private CharacterBody lastBody;

        public Order Current { get; private set; }

        public int QueuedCount => queue.Count;

        // ---- output (read by HeroAIState) --------------------------------------------------------
        public BrainMode Mode { get; private set; } = BrainMode.Idle;
        public Vector3 Goal { get; private set; }
        public bool Sprint { get; private set; }
        public float MoveScale { get; private set; } = 1f;
        public bool AllowSkills { get; private set; } = true;
        public CharacterBody Enemy { get; private set; }
        public int CastSlot { get; private set; } = -1;
        public Vector3 CastPoint { get; private set; }
        public CharacterBody CastTarget { get; private set; }
        public string DebugState { get; private set; } = "";
        public bool Stuck { get; private set; }

        // ---- profile, fleeing and the context the skill rules look at -------------------------------------

        private HeroProfile profile;

        /// <summary>This hero's data-driven behaviour (ranges, flee threshold, per-slot skill rules).</summary>
        public HeroProfile Profile
        {
            get
            {
                if (profile == null)
                    profile = ProfileStore.For(Hero != null && Hero.Survivor ? Hero.Survivor.cachedName : null);
                return profile;
            }
        }

        /// <summary>True while the hero runs from a fight it is losing.</summary>
        public bool Fleeing { get; private set; }

        private float fleeSince;
        private float lastThreatTime;
        private int enemiesNear;
        private bool targetIsBig;
        private float targetDistance = float.MaxValue;
        private bool lowHealth;

        private static readonly TeamIndex[] HostileTeams = { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void };

        private readonly List<Order> queue = new List<Order>(4);
        private BaseAI cachedAi;
        private float thinkTimer;
        private Vector3 progressPos;
        private float progressTime;
        private float castStarted;
        private bool initialised;

        private void Awake()
        {
            thinkTimer = Random.Range(0f, ThinkInterval);
        }

        // ---- commands -------------------------------------------------------------------------------

        public void Issue(Order order, bool queued)
        {
            if (order == null) return;
            order.Issued = Time.time;
            if (!queued)
            {
                queue.Clear();
                Current = null; // dropped without ceremony; the new order starts next tick
            }
            queue.Add(order);
            if (Current == null) thinkTimer = 0f; // react immediately
        }

        public void ClearOrders(bool setAnchorHere)
        {
            queue.Clear();
            Current = null;
            Enemy = null;
            if (setAnchorHere && Hero != null && Hero.Body) Anchor = Hero.Body.footPosition;
            thinkTimer = 0f;
        }

        public void SetStance(Stance s) => Stance = s;

        internal void NotifyCastPressed()
        {
            if (Current != null && Current.Kind == OrderKind.Cast) Finish();
        }

        // ---- loop -------------------------------------------------------------------------------------

        private void FixedUpdate()
        {
            thinkTimer -= Time.fixedDeltaTime;
            if (thinkTimer > 0f) return;
            thinkTimer = ThinkInterval;
            long started = Dev.Perf.Begin();
            try { Think(); }
            catch (System.Exception e) { Plugin.Log.LogError($"HeroBrain.Think failed for {Hero}: {e}"); Mode = BrainMode.Idle; }
            Dev.Perf.End(ref Dev.Perf.Brain, started);
        }

        private void Think()
        {
            CharacterBody self = Hero != null ? Hero.Body : null;
            if (!self || !self.healthComponent || !self.healthComponent.alive)
            {
                Mode = BrainMode.Idle;
                Enemy = null;
                DebugState = "dead";
                return;
            }

            // A different body (a new stage, a revival): old orders and the old anchor belong to the old place.
            if (self != lastBody)
            {
                lastBody = self;
                initialised = false;
                queue.Clear();
                Current = null;
                Enemy = null;
            }

            Vector3 pos = self.footPosition;
            if (!initialised)
            {
                // Wait until the hero stands on its own feet (a passenger of the drop pod is still in the air).
                if (self.currentVehicle) { Mode = BrainMode.Idle; DebugState = "in pod"; return; }
                initialised = true;
                Anchor = pos;
                progressPos = pos;
                progressTime = Time.time;
                if (Settings.VerboseLog.Value) Plugin.Log.LogInfo($"{Hero}: anchor set at {pos}");
            }

            if (!cachedAi && Hero.Master) cachedAi = Hero.Master.GetComponent<BaseAI>();
            BaseAI ai = cachedAi;

            // defaults for this tick
            MoveScale = 1f;
            AllowSkills = true;
            Sprint = false;
            CastSlot = -1;

            if (Current == null && queue.Count > 0)
            {
                Current = queue[0];
                queue.RemoveAt(0);
                Current.startedAt = Time.time;
                if (Current.Kind == OrderKind.Stop)
                {
                    Anchor = pos;
                    Finish();
                }
                else if (Current.Kind == OrderKind.Hold)
                {
                    Anchor = pos;
                }
                if (Current != null) castStarted = Time.time;
            }

            UpdateContext(self, pos);

            Order o = Current;
            if (UpdateFleeing(self, pos, o))
            {
                ThinkFlee(self, pos);
            }
            else if (o == null) ThinkIdle(self, ai, pos);
            else ThinkOrder(o, self, ai, pos);

            TrackProgress(pos);
            PushEnemyToAi(ai);
        }

        // ---- context for the skill rules and for fleeing ----------------------------------------------

        private void UpdateContext(CharacterBody self, Vector3 pos)
        {
            HealthComponent hc = self.healthComponent;
            float frac = hc && hc.fullCombinedHealth > 0f ? hc.combinedHealth / hc.fullCombinedHealth : 1f;
            lowHealth = frac < Profile.retreatHealth * 1.6f;

            enemiesNear = Perception.CountEnemies(pos, 20f, self.teamComponent.teamIndex);
            if (enemiesNear > 0) lastThreatTime = Time.time;

            targetIsBig = Enemy && (Enemy.isBoss || Enemy.isChampion);
            targetDistance = Enemy ? (Enemy.corePosition - self.corePosition).magnitude : float.MaxValue;
        }

        /// <summary>The per-slot usage rule from the profile (0 primary .. 3 special, 4 equipment).</summary>
        public bool RuleAllows(int slot)
        {
            switch (Profile.RuleFor(slot))
            {
                case "never": return false;
                case "escape": return lowHealth || Fleeing;
                case "engage": return Enemy && targetDistance >= Profile.engageDistance;
                case "burst": return enemiesNear >= Profile.burstEnemies || targetIsBig;
                default: return true;
            }
        }

        // Decides whether the hero should be running away right now.
        private bool UpdateFleeing(CharacterBody self, Vector3 pos, Order order)
        {
            if (!Settings.AutoRetreat.Value)
            {
                Fleeing = false;
                return false;
            }

            HealthComponent hc = self.healthComponent;
            float frac = hc && hc.fullCombinedHealth > 0f ? hc.combinedHealth / hc.fullCombinedHealth : 1f;

            bool fleeable = order == null || order.Kind == OrderKind.AttackMove || order.Kind == OrderKind.Attack
                            || order.Kind == OrderKind.Patrol || order.Kind == OrderKind.Follow;

            if (!Fleeing)
            {
                // the (costly) look around is made only for a hero that is hurt enough to care
                if (fleeable && frac < Profile.retreatHealth
                    && Perception.CountEnemies(pos, 38f, self.teamComponent.teamIndex) > 0)
                {
                    Fleeing = true;
                    fleeSince = Time.time;
                    fleeGoalAt = -100f; // a fresh place to run to
                    if (Settings.VerboseLog.Value) Plugin.Log.LogInfo($"{Hero}: hurt ({frac:0.00}), fleeing");
                }
                return Fleeing;
            }

            // stop fleeing: recovered, nothing around any more, or the order changed to something explicit
            bool threatened = Perception.CountEnemies(pos, 38f, self.teamComponent.teamIndex) > 0;
            bool calm = Time.time - lastThreatTime > 3f && !threatened;
            if (frac >= Profile.recoverHealth || calm || !fleeable || Time.time - fleeSince > 25f)
            {
                Fleeing = false;
                Enemy = null;
            }
            return Fleeing;
        }

        private void ThinkFlee(CharacterBody self, Vector3 pos)
        {
            Enemy = null;
            Mode = BrainMode.Go;
            Goal = SafePoint(self, pos);
            Sprint = true;
            AllowSkills = false;
            DebugState = "fleeing";
        }

        /// <summary>Hostile bodies within 20 m (refreshed every think); the executor uses it to time an escape skill.</summary>
        public int EnemiesNear => enemiesNear;

        private Vector3 fleeGoal;
        private float fleeGoalAt = -100f;
        private static readonly List<NodeGraph.NodeIndex> NodeBuffer = new List<NodeGraph.NodeIndex>(256);

        // The place to run to is chosen again only every 1.5 s or when reached: a goal that changes every tick makes
        // the hero zig-zag on the spot.
        private Vector3 SafePoint(CharacterBody self, Vector3 pos)
        {
            Vector3 left = fleeGoal - pos;
            left.y = 0f;
            if (Time.time - fleeGoalAt < 1.5f && left.sqrMagnitude > 4f * 4f) return fleeGoal;
            fleeGoalAt = Time.time;
            fleeGoal = ChooseSafePoint(pos);
            if (Settings.VerboseLog.Value) Plugin.Log.LogInfo($"{Hero}: flee goal {fleeGoal:0} (from {pos:0})");
            return fleeGoal;
        }

        // To the nearest healthy friend; alone: a ground node on about the same level, away from the enemies. A point
        // far below (the foot of a cliff) is never chosen: a hero walking there falls and lands at 1 HP among enemies.
        private Vector3 ChooseSafePoint(Vector3 pos)
        {
            HighCommandRun run = HighCommandRun.Instance;
            Hero friend = null;
            float friendDist = 120f * 120f;
            if (run != null)
            {
                foreach (Hero h in run.Army.Heroes)
                {
                    if (h == Hero || !h.IsAlive) continue;
                    CharacterBody b = h.Body;
                    HealthComponent hc = b ? b.healthComponent : null;
                    if (!hc || hc.fullCombinedHealth <= 0f || hc.combinedHealth / hc.fullCombinedHealth < 0.4f) continue;
                    float d = (b.footPosition - pos).sqrMagnitude;
                    if (d < friendDist) { friendDist = d; friend = h; }
                }
            }
            if (friend != null) return friend.Body.footPosition;

            Vector3 enemies = Vector3.zero;
            int e = 0;
            foreach (TeamIndex team in HostileTeams)
            {
                var members = TeamComponent.GetTeamMembers(team);
                for (int i = 0; i < members.Count; i++)
                {
                    TeamComponent tc = members[i];
                    if (!tc || !tc.body || (tc.body.corePosition - pos).sqrMagnitude > 40f * 40f) continue;
                    enemies += tc.body.corePosition;
                    e++;
                }
            }
            Vector3 rally = run != null ? run.RallyPoint : pos;
            if (e == 0) return rally;
            Vector3 threat = enemies / e;

            SceneInfo info = SceneInfo.instance;
            NodeGraph graph = info ? info.groundNodes : null;
            if (graph)
            {
                NodeBuffer.Clear();
                graph.FindNodesInRange(pos, 10f, 34f, HullMask.Human, NodeBuffer);
                int step = Mathf.Max(1, NodeBuffer.Count / 120); // a big open map has hundreds: a sample is enough
                bool found = false;
                float bestScore = float.MinValue;
                Vector3 best = pos;
                for (int i = 0; i < NodeBuffer.Count; i += step)
                {
                    if (!graph.GetNodePosition(NodeBuffer[i], out Vector3 np)) continue;
                    float dy = np.y - pos.y;
                    if (dy < -5f || dy > 8f) continue; // stay on this level: no cliffs down, no walls up
                    Vector3 fromThreat = np - threat;
                    fromThreat.y = 0f;
                    Vector3 toRally = rally - np;
                    toRally.y = 0f;
                    float score = fromThreat.magnitude - 1.5f * Mathf.Abs(dy) - 0.15f * toRally.magnitude;
                    if (score > bestScore) { bestScore = score; best = np; found = true; }
                }
                NodeBuffer.Clear();
                if (found) return best;
            }

            // no node graph (or nothing on this level): straight away, but only if the ground there is not far below
            Vector3 away = pos - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            Vector3 p = Formation.SnapToGround(pos + away.normalized * 20f);
            return p.y - pos.y < -5f ? pos : p;
        }

        // ---- idle / stance ---------------------------------------------------------------------------

        private void ThinkIdle(CharacterBody self, BaseAI ai, Vector3 pos)
        {
            float acquire = Stance == Stance.Aggressive ? Profile.acquireAggressive : Profile.acquireDefensive;
            float leash = Stance == Stance.Aggressive ? Profile.leashAggressive : Profile.leashDefensive;

            if (ZoneRadius > 0f)
            {
                // a charging zone: hold a personal spot inside it and do not chase beyond its edge
                Anchor = ZoneSpot();
                leash = Mathf.Min(leash, ZoneRadius * 0.85f + 6f);
            }

            CharacterBody enemy = null;
            if (Stance != Stance.Passive)
            {
                enemy = Perception.FindBestEnemy(self, pos, acquire, requireLineOfSight: true, current: Enemy,
                    leashFromPoint: leash, leashPoint: Anchor);
            }
            else if (!self.outOfDanger && ai != null && ai.currentEnemy != null && ai.currentEnemy.gameObject)
            {
                // passive: only answer whoever is hitting us
                CharacterBody attacker = ai.currentEnemy.characterBody;
                if (attacker && Perception.IsTargetable(attacker, self.teamComponent.teamIndex)) enemy = attacker;
            }

            if (enemy)
            {
                Enemy = enemy;
                Mode = BrainMode.Fight;
                DebugState = "idle: fighting";
                return;
            }

            Enemy = null;
            float away = (pos - Anchor).magnitude;
            if (away > ArriveDistance + 1f)
            {
                Mode = BrainMode.Go;
                Goal = Anchor;
                Sprint = away > 22f;
                AllowSkills = false;
                DebugState = "idle: returning to anchor";
            }
            else
            {
                Mode = BrainMode.Idle;
                DebugState = "idle";
            }
        }

        // A stable place for this hero inside the zone (heroes must not all stack on the centre).
        private Vector3 ZoneSpot()
        {
            if ((ZoneCenter - zoneSpotKey).sqrMagnitude > 1f || Mathf.Abs(ZoneRadius - zoneSpotRadius) > 1.5f)
            {
                zoneSpotKey = ZoneCenter;
                zoneSpotRadius = ZoneRadius;
                int id = Hero != null ? Hero.Id : 0;
                float angle = id * 2.399963f; // golden angle: neighbours spread evenly
                float radius = ZoneRadius * (0.22f + 0.34f * (((id * 7) % 5) / 4f));
                Vector3 p = ZoneCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                zoneSpot = Formation.SnapToGround(p);
            }
            return zoneSpot;
        }

        // ---- orders ------------------------------------------------------------------------------------

        private void ThinkOrder(Order o, CharacterBody self, BaseAI ai, Vector3 pos)
        {
            switch (o.Kind)
            {
                case OrderKind.Move:
                    GoTo(o.Point, pos, sprintFar: true, skills: false);
                    if (Arrived(pos, o.Point)) { Anchor = o.Point; Finish(); }
                    DebugState = "move";
                    break;

                case OrderKind.Retreat:
                    GoTo(o.Point, pos, sprintFar: true, skills: false);
                    Sprint = true;
                    if (Arrived(pos, o.Point)) { Anchor = o.Point; Finish(); }
                    DebugState = "retreat";
                    break;

                case OrderKind.AttackMove:
                    ThinkAttackMove(o, o.Point, self, pos);
                    break;

                case OrderKind.Patrol:
                {
                    Vector3 end = o.patrolToSecond ? o.Point2 : o.Point;
                    ThinkAttackMove(o, end, self, pos, patrol: true);
                    break;
                }

                case OrderKind.Attack:
                {
                    CharacterBody target = o.Target ? o.Target.GetComponent<CharacterBody>() : null;
                    if (!target || !Perception.IsTargetable(target, self.teamComponent.teamIndex))
                    {
                        // the target is gone (or cloaked): the order is done
                        Finish();
                        DebugState = "attack done";
                        break;
                    }
                    Enemy = target;
                    Mode = BrainMode.Fight;
                    DebugState = "attack";
                    break;
                }

                case OrderKind.Hold:
                {
                    MoveScale = 0f;
                    CharacterBody enemy = Perception.FindBestEnemy(self, pos, AcquireHold, requireLineOfSight: true, current: Enemy);
                    Enemy = enemy;
                    Mode = enemy ? BrainMode.Fight : BrainMode.Idle;
                    DebugState = "hold";
                    break;
                }

                case OrderKind.Follow:
                    ThinkFollow(o, self, pos);
                    break;

                case OrderKind.Interact:
                    ThinkInteract(o, self, pos);
                    break;

                case OrderKind.Cast:
                    Mode = BrainMode.Cast;
                    CastSlot = o.Slot;
                    CastPoint = o.Point;
                    CastTarget = o.Target ? o.Target.GetComponent<CharacterBody>() : null;
                    MoveScale = 0f;
                    DebugState = "cast";
                    if (Time.time - castStarted > 3f) Finish(); // could not be cast: do not block the queue
                    break;

                default:
                    Finish();
                    break;
            }
        }

        private void ThinkAttackMove(Order o, Vector3 destination, CharacterBody self, Vector3 pos, bool patrol = false)
        {
            // an explicit attack-move overrides a passive stance
            CharacterBody enemy = Perception.FindBestEnemy(self, pos, AcquireAttackMove, requireLineOfSight: false, current: Enemy);

            if (enemy)
            {
                Enemy = enemy;
                Mode = BrainMode.Fight;
                DebugState = patrol ? "patrol: fighting" : "attack-move: fighting";
                return;
            }

            Enemy = null;
            GoTo(destination, pos, sprintFar: false, skills: true);
            DebugState = patrol ? "patrol" : "attack-move";
            if (Arrived(pos, destination))
            {
                if (patrol) o.patrolToSecond = !o.patrolToSecond;
                else { Anchor = destination; Finish(); }
            }
        }

        private void ThinkFollow(Order o, CharacterBody self, Vector3 pos)
        {
            CharacterBody leader = o.Target ? o.Target.GetComponent<CharacterBody>() : null;
            if (!leader || !leader.healthComponent || !leader.healthComponent.alive)
            {
                Finish();
                return;
            }
            Vector3 lp = leader.footPosition;
            float dist = (lp - pos).magnitude;

            CharacterBody enemy = Perception.FindBestEnemy(self, lp, 28f, requireLineOfSight: true, current: Enemy,
                leashFromPoint: 26f, leashPoint: lp);
            if (enemy && dist < 22f)
            {
                Enemy = enemy;
                Mode = BrainMode.Fight;
                DebugState = "follow: fighting";
                return;
            }

            Enemy = null;
            if (dist > 7.5f)
            {
                Mode = BrainMode.Go;
                Goal = lp;
                Sprint = dist > 20f;
                AllowSkills = false;
                DebugState = "follow";
            }
            else
            {
                Mode = BrainMode.Idle;
                DebugState = "follow: close";
            }
        }

        private void ThinkInteract(Order o, CharacterBody self, Vector3 pos)
        {
            if (!o.Target)
            {
                Finish();
                return;
            }
            Vector3 tp = o.Target.transform.position;
            float dist = (new Vector3(tp.x, pos.y, tp.z) - pos).magnitude;
            if (dist > 3.5f && Time.time - o.startedAt < 40f)
            {
                GoTo(tp, pos, sprintFar: true, skills: false);
                DebugState = "interact: walking";
                return;
            }

            var interactor = self.GetComponent<Interactor>();
            if (interactor) interactor.AttemptInteraction(o.Target);
            DebugState = "interact: used";
            Finish();
        }

        // ---- helpers -----------------------------------------------------------------------------------

        private void GoTo(Vector3 destination, Vector3 pos, bool sprintFar, bool skills)
        {
            Mode = BrainMode.Go;
            Goal = destination;
            AllowSkills = skills;
            Sprint = sprintFar && (destination - pos).sqrMagnitude > 10f * 10f;
        }

        private static bool Arrived(Vector3 pos, Vector3 point)
        {
            Vector3 d = point - pos;
            d.y *= 0.4f; // vertical slack: the goal point is often a few meters above the node
            return d.sqrMagnitude <= ArriveDistance * ArriveDistance;
        }

        private void Finish()
        {
            Current = null;
            Enemy = null;
            thinkTimer = 0f;
        }

        // Watches for a hero that tries to go somewhere but does not get there.
        private void TrackProgress(Vector3 pos)
        {
            bool travelling = Mode == BrainMode.Go;
            if (!travelling || (pos - progressPos).sqrMagnitude > 1.2f * 1.2f)
            {
                progressPos = pos;
                progressTime = Time.time;
                Stuck = false;
                return;
            }
            Stuck = Time.time - progressTime > StuckSeconds;
        }

        private void PushEnemyToAi(BaseAI ai)
        {
            if (ai == null) return;

            if (Mode == BrainMode.Fight && Enemy && Enemy.healthComponent && Enemy.healthComponent.alive)
            {
                if (ai.currentEnemy.gameObject != Enemy.gameObject)
                {
                    ai.currentEnemy.Reset();
                    ai.currentEnemy.gameObject = Enemy.gameObject;
                    ai.currentEnemy.bestHurtBox = ai.GetBestHurtBox(Enemy.gameObject);
                }
                ai.enemyAttention = ai.enemyAttentionDuration;
            }
            else if (Mode == BrainMode.Go || Mode == BrainMode.Cast)
            {
                // while travelling the AI must not be dragged along by its own target search
                if (ai.currentEnemy.gameObject) ai.currentEnemy.Reset();
            }
        }
    }
}
