using EntityStates.AI;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Navigation;
using RoR2.Skills;
using UnityEngine;

namespace HighCommand.Orders
{
    /// <summary>
    /// The body-level executor of a hero (the game's own AI state machine runs it instead of the monster
    /// "Wander/Combat" states). Every physics frame it writes the same inputs a player would: move vector, aim,
    /// skill buttons, sprint, jump, equipment.
    ///
    /// Decisions come from <see cref="HeroBrain"/> (orders, stance, target). Which skill to press and how to move while
    /// fighting comes from the AISkillDriver rules the game ships for every survivor, evaluated by BaseAI; this state
    /// only reads the winning driver. Orders can override the movement (hold position, go to a point) and the
    /// per-skill auto-use switches can veto a skill.
    /// </summary>
    public class HeroAIState : BaseAIState
    {
        private const float UpdateInterval = 0.1f;

        private HeroBrain brain;
        private float updateTimer;
        private Vector3 footPosition;

        private float strafeDirection;
        private float strafeTimer;

        private AISkillDriver dominantDriver;
        private bool driverMeetsConditions;
        private SkillSlot driverSlot = SkillSlot.None;
        private bool aimConfirmationNeeded;
        private bool hasSkillButtonBeenUnheld;
        private bool hasSkillButtonBeenHeld;
        private BaseAI.OnBeginSkillDriverDelegate onBeginDriver;
        private bool freshDriver;

        // casting
        private float castPressTime = -1f;
        private int castFrames;

        // stuck recovery
        private float stuckSince = -1f;
        private int stuckStage;

        public override void OnEnter()
        {
            base.OnEnter();
            brain = characterMaster ? characterMaster.GetComponent<HeroBrain>() : null;
            if (ai)
            {
                ai.broadNavigationAgent.InvalidatePath();
                onBeginDriver = OnAiBeginSkillDriver;
                ai.onBeginSkillDriver += onBeginDriver;
            }
            strafeDirection = Random.value < 0.5f ? -1f : 1f;
        }

        public override void OnExit()
        {
            if (ai != null && onBeginDriver != null) ai.onBeginSkillDriver -= onBeginDriver;
            base.OnExit();
        }

        private void OnAiBeginSkillDriver(in BaseAI.SkillDriverEvaluation evaluation)
        {
            freshDriver = true;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!ai || !body) return;

            float dt = GetDeltaTime();
            updateTimer -= dt;
            strafeTimer -= dt;

            footPosition = body.temporaryPathfindingFootpositionDoNotUseWillBePatchedOut;
            BroadNavigationSystem.Agent agent = ai.broadNavigationAgent;
            agent.currentPosition = footPosition;

            if (updateTimer <= 0f)
            {
                updateTimer = UpdateInterval;
                long started = Dev.Perf.Begin();
                UpdateAI(UpdateInterval);
                Dev.Perf.End(ref Dev.Perf.Executor, started);
            }
        }

        // ---------------------------------------------------------------------------------------------

        private BrainMode CurrentMode => brain ? brain.Mode : BrainMode.Fight;

        private void UpdateAI(float dt)
        {
            bodyInputs.moveVector = Vector3.zero;
            dominantDriver = null;
            driverMeetsConditions = false;
            driverSlot = SkillSlot.None;
            aimConfirmationNeeded = false;

            if (!body || !bodyInputBank) return;

            BrainMode mode = CurrentMode;
            switch (mode)
            {
                case BrainMode.Go:
                    UpdateGo(dt);
                    break;
                case BrainMode.Cast:
                    UpdateCast();
                    break;
                case BrainMode.Idle:
                    // standing: still lets the skill rules shoot at what is in range, but never moves
                    UpdateDrivers(dt, movementScale: 0f);
                    break;
                default:
                    UpdateDrivers(dt, brain ? brain.MoveScale : 1f);
                    break;
            }

            UpdateStuckRecovery(mode);
        }

        // ---- Go: travel to a point -----------------------------------------------------------------

        private void UpdateGo(float dt)
        {
            Vector3 goal = brain.Goal;
            ai.SetGoalPosition(goal);

            // A fleeing hero never steps off an edge: a fall leaves it at 1 HP, usually among the enemies it ran from.
            // An ordered move may (the path to a clicked point can lead down a ledge).
            bool fleeing = brain.Fleeing;

            BroadNavigationSystem.AgentOutput output = ai.broadNavigationAgent.output;
            Vector3 next = output.nextPosition ?? footPosition;
            // unreachable goal: head straight at it, the local navigator stops at walls
            if (!fleeing && !output.nextPosition.HasValue && !output.targetReachable) next = goal;

            Vector3 target = next + (bodyTransform.position - footPosition);
            ai.localNavigator.targetPosition = target;
            ai.localNavigator.allowWalkOffCliff = !fleeing;
            ai.localNavigator.Update(dt);
            bodyInputs.moveVector = ai.localNavigator.moveVector;
        }

        // ---- Cast: aim and press one skill ----------------------------------------------------------

        private void UpdateCast()
        {
            // stand still; the press itself happens in GenerateBodyInputs once the aim has settled
            bodyInputs.moveVector = Vector3.zero;
        }

        // ---- Fight / Idle: follow the skill-driver rules ----------------------------------------------

        private void UpdateDrivers(float dt, float movementScale)
        {
            BaseAI.SkillDriverEvaluation eval = ai.skillDriverEvaluation;
            dominantDriver = eval.dominantSkillDriver;
            BaseAI.Target target = eval.target;

            if (freshDriver)
            {
                freshDriver = false;
                hasSkillButtonBeenUnheld = false;
                hasSkillButtonBeenHeld = false;
            }

            if (!dominantDriver)
            {
                ai.aimVectorDampTimeOverride = -1f;
                ai.aimVectorMaxSpeedOverride = -1f;
                return;
            }

            driverSlot = dominantDriver.skillSlot;
            AISkillDriver.MovementType movement = dominantDriver.movementType;
            ai.aimVectorDampTimeOverride = dominantDriver.aimVectorDampTimeOverride;
            ai.aimVectorMaxSpeedOverride = dominantDriver.aimVectorMaxSpeedOverride;

            if (target == null || !target.gameObject) return;

            target.GetBullseyePosition(out Vector3 targetPos);
            Vector3 targetGround = targetPos;

            // The broad path leads to where the target stands, unless the driver wants a straight line.
            if (target.characterBody) ai.SetGoalPosition(target);
            else ai.SetGoalPosition(target.gameObject.transform.position);

            BroadNavigationSystem.AgentOutput output = ai.broadNavigationAgent.output;
            bool straight = dominantDriver.ignoreNodeGraph;
            Vector3 next = straight ? (body.isFlying ? targetPos : targetGround) : (output.nextPosition ?? footPosition);
            Vector3 toward = (next - footPosition).normalized * 10f;
            Vector3 side = Vector3.Cross(Vector3.up, toward);

            Vector3 desired = bodyTransform.position;
            bool allowWalkOffCliff = true;
            switch (movement)
            {
                case AISkillDriver.MovementType.ChaseMoveTarget:
                    desired = next + (bodyTransform.position - footPosition);
                    break;
                case AISkillDriver.MovementType.FleeMoveTarget:
                    desired -= toward;
                    break;
                case AISkillDriver.MovementType.StrafeMovetarget:
                    if (strafeTimer <= 0f)
                    {
                        strafeTimer = 0.25f;
                    }
                    desired += side * strafeDirection;
                    allowWalkOffCliff = false;
                    break;
                case AISkillDriver.MovementType.FollowMoveTarget:
                    desired = next + ai.followPattern.targetOffset;
                    break;
                case AISkillDriver.MovementType.CircleMoveTargetCW:
                    desired -= side;
                    allowWalkOffCliff = false;
                    break;
                case AISkillDriver.MovementType.CircleMoveTargetCCW:
                    desired += side;
                    allowWalkOffCliff = false;
                    break;
                case AISkillDriver.MovementType.Stop:
                default:
                    break;
            }

            // Ranged heroes keep their distance (the stock survivor rules always close in, see HeroProfile.standoff).
            bool kiting = false;
            float standoff = brain ? brain.Profile.standoff : 0f;
            if (standoff > 0f && target.characterBody && movementScale > 0f)
            {
                Vector3 fromTarget = footPosition - targetPos;
                fromTarget.y = 0f;
                float distance = fromTarget.magnitude;
                Vector3 awayDir = distance > 0.1f ? fromTarget / distance : -bodyTransform.forward;
                if (distance < standoff * 0.65f)
                {
                    desired = bodyTransform.position + awayDir * 10f + side * (0.3f * strafeDirection);
                    allowWalkOffCliff = false;
                    kiting = true;
                }
                else if (distance < standoff && (movement == AISkillDriver.MovementType.ChaseMoveTarget
                                                  || movement == AISkillDriver.MovementType.FollowMoveTarget))
                {
                    desired = bodyTransform.position + side * strafeDirection;
                    allowWalkOffCliff = false;
                    kiting = true;
                }
            }

            ai.localNavigator.targetPosition = desired;
            ai.localNavigator.allowWalkOffCliff = allowWalkOffCliff;
            ai.localNavigator.Update(dt);
            if (ai.localNavigator.wasObstructedLastUpdate) strafeDirection = -strafeDirection;

            Vector3 move = ai.localNavigator.moveVector * (kiting ? 1f : dominantDriver.moveInputScale);
            bodyInputs.moveVector = movement == AISkillDriver.MovementType.Stop && !kiting ? Vector3.zero : move * movementScale;

            // activation conditions (line of sight is tested right now, not from the cached value)
            bool flagTargetLos = dominantDriver.activationRequiresTargetLoS;
            bool flagAimLos = dominantDriver.activationRequiresAimTargetLoS;
            aimConfirmationNeeded = dominantDriver.activationRequiresAimConfirmation;
            if (target == eval.aimTarget && flagTargetLos && flagAimLos) flagAimLos = false;

            bool ok = true;
            if (ok && flagTargetLos) ok = target.TestLOSNow();
            if (ok && flagAimLos && eval.aimTarget != null) ok = eval.aimTarget.TestLOSNow();
            driverMeetsConditions = ok;
        }

        // ---- inputs ---------------------------------------------------------------------------------------

        public override BaseAI.BodyInputs GenerateBodyInputs(in BaseAI.BodyInputs previous)
        {
            BrainMode mode = CurrentMode;

            // a cast that was cut short by another order must not leak its timer into the next one
            if (mode != BrainMode.Cast && (castPressTime >= 0f || castFrames != 0))
            {
                castPressTime = -1f;
                castFrames = 0;
            }
            if (mode != BrainMode.Go) escapeFrames = 0;

            bodyInputs.pressSkill1 = false;
            bodyInputs.pressSkill2 = false;
            bodyInputs.pressSkill3 = false;
            bodyInputs.pressSkill4 = false;
            bodyInputs.pressSprint = false;
            bodyInputs.pressActivateEquipment = false;
            bodyInputs.desiredAimDirection = Vector3.zero;

            if (mode == BrainMode.Cast) GenerateCastInputs(previous);
            else if (mode == BrainMode.Go) GenerateTravelInputs();
            else GenerateFightInputs(previous);

            ModifyInputsForJumpIfNeccessary(ref bodyInputs);
            if (stuckStage == 1 && stuckSince >= 0f) bodyInputs.pressJump = true;
            return bodyInputs;
        }

        private void GenerateTravelInputs()
        {
            bodyInputs.pressSprint = brain && brain.Sprint;
            // look where we are going
            if (bodyInputs.moveVector != Vector3.zero) AimInDirection(ref bodyInputs, bodyInputs.moveVector);
            TryEscapeSkill();
        }

        private int escapeFrames;

        // A fleeing hero with enemies close by uses its "get away" skill (profile fleeSlot): the move vector and the aim
        // both point away (dashes follow one or the other, depending on the survivor), held for a few frames.
        private void TryEscapeSkill()
        {
            if (brain == null) { escapeFrames = 0; return; }
            int slot = brain.Profile.fleeSlot;
            if (slot < 0 || slot > 3 || !brain.AutoSlot[slot]) { escapeFrames = 0; return; }

            if (escapeFrames > 0)
            {
                escapeFrames--;
                SetPress(slot, true);
                return;
            }
            if (!brain.Fleeing || brain.EnemiesNear == 0 || bodyInputs.moveVector == Vector3.zero) return;

            GenericSkill skill = GetSkill(slot);
            if (!skill || !skill.IsReady()) return;

            escapeFrames = 3;
            SetPress(slot, true);
            if (Settings.VerboseLog.Value)
                Plugin.Log.LogInfo($"{characterMaster.name}: escape skill {skill.skillDef.skillName} ({brain.EnemiesNear} enemies near)");
        }

        private void GenerateFightInputs(in BaseAI.BodyInputs previous)
        {
            bool previousHeld = false;
            switch (driverSlot)
            {
                case SkillSlot.Primary: previousHeld = previous.pressSkill1; break;
                case SkillSlot.Secondary: previousHeld = previous.pressSkill2; break;
                case SkillSlot.Utility: previousHeld = previous.pressSkill3; break;
                case SkillSlot.Special: previousHeld = previous.pressSkill4; break;
            }
            hasSkillButtonBeenUnheld |= !previousHeld;

            bool press = false;
            if (dominantDriver)
            {
                press = driverMeetsConditions && SlotAllowed(driverSlot) && (brain == null || brain.AllowSkills || brain.Mode == BrainMode.Fight);
                if (aimConfirmationNeeded && !ai.hasAimConfirmation) press = false;

                switch (dominantDriver.buttonPressType)
                {
                    case AISkillDriver.ButtonPressType.Abstain:
                        press = false;
                        break;
                    case AISkillDriver.ButtonPressType.TapContinuous:
                        press = press && !previousHeld;
                        break;
                    case AISkillDriver.ButtonPressType.PressOnce:
                        press &= !hasSkillButtonBeenHeld;
                        break;
                }
                if (dominantDriver.buttonEnsureReleasedForOneSample) press &= hasSkillButtonBeenUnheld;
            }
            hasSkillButtonBeenHeld |= press;

            switch (driverSlot)
            {
                case SkillSlot.Primary: bodyInputs.pressSkill1 = press; break;
                case SkillSlot.Secondary: bodyInputs.pressSkill2 = press; break;
                case SkillSlot.Utility: bodyInputs.pressSkill3 = press; break;
                case SkillSlot.Special: bodyInputs.pressSkill4 = press; break;
            }

            if (dominantDriver)
            {
                bodyInputs.pressSprint = dominantDriver.shouldSprint;
                bodyInputs.pressActivateEquipment = dominantDriver.shouldFireEquipment && AutoEquipment && !previous.pressActivateEquipment;
                bodyInputs.pressJump |= dominantDriver.shouldJump && !previous.pressJump;

                AISkillDriver.AimType aimType = dominantDriver.aimType;
                BaseAI.Target aimTarget = ai.skillDriverEvaluation.aimTarget;
                if (aimType == AISkillDriver.AimType.MoveDirection) AimInDirection(ref bodyInputs, bodyInputs.moveVector);
                if (aimTarget != null) AimAt(ref bodyInputs, aimTarget);
            }
        }

        private void GenerateCastInputs(in BaseAI.BodyInputs previous)
        {
            if (brain == null) return;

            // aim at the enemy or the ground point
            Vector3 aimPoint = brain.CastTarget ? brain.CastTarget.corePosition : brain.CastPoint;
            Vector3 origin = bodyInputBank.aimOrigin;
            Vector3 direction = aimPoint - origin;
            if (direction.sqrMagnitude > 0.01f) bodyInputs.desiredAimDirection = direction.normalized;

            int slot = brain.CastSlot;
            if (slot == 4)
            {
                // equipment: press once
                bodyInputs.pressActivateEquipment = !previous.pressActivateEquipment;
                brain.NotifyCastPressed();
                return;
            }

            GenericSkill skill = GetSkill(slot);
            if (!skill)
            {
                if (Settings.VerboseLog.Value) Plugin.Log.LogInfo($"{characterMaster.name}: cast slot {slot}: no such skill");
                brain.NotifyCastPressed();
                return;
            }

            // wait a moment so the aim has swung round, then hold the button for a few frames
            if (castPressTime < 0f) castPressTime = Time.time + 0.2f;
            if (Time.time < castPressTime) return;

            // The check is for the start only: pressing the button spends the stock at once, so a skill that is
            // already being cast reports "not ready" while the button is still held.
            if (castFrames == 0 && !skill.IsReady())
            {
                // on cooldown / out of stock: nothing to cast, give up
                if (Settings.VerboseLog.Value)
                    Plugin.Log.LogInfo($"{characterMaster.name}: cast slot {slot}: not ready (stock {skill.stock}, cooldown {skill.cooldownRemaining:0.0}, " +
                                       $"def {skill.skillDef.GetType().Name}/{skill.skillDef.skillName}, requiredStock {skill.skillDef.requiredStock}, canExecute {skill.CanExecute()})");
                castPressTime = -1f;
                castFrames = 0;
                brain.NotifyCastPressed();
                return;
            }
            if (castFrames == 0 && Settings.VerboseLog.Value)
                Plugin.Log.LogInfo($"{characterMaster.name}: casting slot {slot} ({skill.skillDef.skillName}) at {aimPoint:0}");

            SetPress(slot, true);
            castFrames++;
            if (castFrames >= 4)
            {
                castPressTime = -1f;
                castFrames = 0;
                brain.NotifyCastPressed();
            }
        }

        private void SetPress(int slot, bool value)
        {
            switch (slot)
            {
                case 0: bodyInputs.pressSkill1 = value; break;
                case 1: bodyInputs.pressSkill2 = value; break;
                case 2: bodyInputs.pressSkill3 = value; break;
                default: bodyInputs.pressSkill4 = value; break;
            }
        }

        private GenericSkill GetSkill(int slot)
        {
            if (!bodySkillLocator) return null;
            switch (slot)
            {
                case 0: return bodySkillLocator.primary;
                case 1: return bodySkillLocator.secondary;
                case 2: return bodySkillLocator.utility;
                case 3: return bodySkillLocator.special;
                default: return null;
            }
        }

        private bool SlotAllowed(SkillSlot slot)
        {
            if (brain == null) return true;
            int index;
            switch (slot)
            {
                case SkillSlot.Primary: index = 0; break;
                case SkillSlot.Secondary: index = 1; break;
                case SkillSlot.Utility: index = 2; break;
                case SkillSlot.Special: index = 3; break;
                default: return true;
            }
            if (!brain.AutoSlot[index] || !brain.RuleAllows(index)) return false;
            // an "escape" skill must not be spent by a rule that runs at the enemy (Huntress' BlinkToCloseGap at low HP)
            if (dominantDriver && dominantDriver.movementType == AISkillDriver.MovementType.ChaseMoveTarget
                && brain.Profile.RuleFor(index) == "escape") return false;
            return true;
        }

        private bool AutoEquipment => brain == null || (brain.AutoSlot[4] && brain.RuleAllows(4));

        // ---- stuck recovery ---------------------------------------------------------------------------

        private void UpdateStuckRecovery(BrainMode mode)
        {
            if (brain == null || mode != BrainMode.Go || !brain.Stuck)
            {
                stuckSince = -1f;
                stuckStage = 0;
                return;
            }

            if (stuckSince < 0f)
            {
                stuckSince = Time.time;
                stuckStage = 1; // first: jump and re-plan
                ai.broadNavigationAgent.InvalidatePath();
                return;
            }

            float stuckFor = Time.time - stuckSince;
            if (stuckStage == 1 && stuckFor > 2.5f) stuckStage = 2;
            if (stuckStage == 2 && stuckFor > 5f)
            {
                // last resort: hop to the nearest safe spot towards the goal
                TeleportTowardsGoal();
                stuckSince = Time.time;
                stuckStage = 1;
            }
        }

        private void TeleportTowardsGoal()
        {
            if (!Run.instance || !body) return;
            Vector3 toward = brain.Goal - footPosition;
            Vector3 aimPoint = footPosition + Vector3.ClampMagnitude(toward, 14f);
            Vector3 safe = Run.instance.FindSafeTeleportPositionSimplified(body.hullClassification, aimPoint);
            TeleportHelper.TeleportBody(body, safe, false);
            ai.broadNavigationAgent.InvalidatePath();
            if (Settings.VerboseLog.Value) Plugin.Log.LogInfo($"{characterMaster.name}: was stuck, teleported to {safe}");
        }
    }
}
