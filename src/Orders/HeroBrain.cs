using System.Collections.Generic;
using HighCommand.Army;
using RoR2;
using RoR2.CharacterAI;
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

        private readonly List<Order> queue = new List<Order>(4);
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
            try { Think(); }
            catch (System.Exception e) { Plugin.Log.LogError($"HeroBrain.Think failed for {Hero}: {e}"); Mode = BrainMode.Idle; }
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

            BaseAI ai = Hero.Master ? Hero.Master.GetComponent<BaseAI>() : null;

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

            Order o = Current;
            if (o == null) ThinkIdle(self, ai, pos);
            else ThinkOrder(o, self, ai, pos);

            TrackProgress(pos);
            PushEnemyToAi(ai);
        }

        // ---- idle / stance ---------------------------------------------------------------------------

        private void ThinkIdle(CharacterBody self, BaseAI ai, Vector3 pos)
        {
            float acquire = Stance == Stance.Aggressive ? AcquireAggressive : AcquireDefensive;
            float leash = Stance == Stance.Aggressive ? LeashAggressive : LeashDefensive;

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
                DebugState = "idle: fighting " + enemy.name;
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
                    DebugState = "attack " + target.name;
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
                    DebugState = "cast slot " + o.Slot;
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
                DebugState = (patrol ? "patrol: fighting " : "attack-move: fighting ") + enemy.name;
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
