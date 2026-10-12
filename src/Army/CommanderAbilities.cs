using System.Collections;
using System.Collections.Generic;
using HighCommand.Mode;
using HighCommand.Orders;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// What a bodiless commander can do on its own (server side): four abilities with cooldowns, shared by the whole
    /// army. They are deliberately small: the real fighting is done by the heroes.
    /// </summary>
    internal static class CommanderAbilities
    {
        internal const int Count = 4;

        internal const int Strike = 0;   // damage in an area (needs a point)
        internal const int Medic = 1;    // heal every hero
        internal const int WarCry = 2;   // attack speed / movement buff for every hero
        internal const int Mark = 3;     // mark an enemy: it takes more damage and everybody focuses it (needs an enemy)

        internal static readonly float[] Cooldown = { 55f, 70f, 80f, 22f };

        internal static bool NeedsPoint(int index) => index == Strike;
        internal static bool NeedsTarget(int index) => index == Mark;

        internal static string NameToken(int index) => "HIGHCOMMAND_ABILITY_" + index + "_NAME";
        internal static string DescToken(int index) => "HIGHCOMMAND_ABILITY_" + index + "_DESC";

        /// <summary>Seconds until the ability is ready (0 = ready). Works on every machine.</summary>
        internal static float Remaining(HcState state, int index)
        {
            if (state == null || !Run.instance || index < 0 || index >= Count) return 0f;
            return Mathf.Max(0f, state.AbilityReadyAt[index] - Run.instance.GetRunStopwatch());
        }

        /// <summary>Server: uses an ability. Returns null on success, else the text to show the commander.</summary>
        internal static string Execute(int index, Vector3 point, NetworkInstanceId target)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null || !Run.instance) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            if (index < 0 || index >= Count) return Loc.Get("HIGHCOMMAND_ERR_FAILED");

            HcState state = run.State;
            float left = Remaining(state, index);
            if (left > 0.05f) return Loc.Format("HIGHCOMMAND_ABILITY_COOLDOWN", Mathf.CeilToInt(left));

            var alive = new List<Hero>();
            foreach (Hero h in run.Army.Heroes) if (h.IsAlive && h.Body) alive.Add(h);
            if (alive.Count == 0) return Loc.Get("HIGHCOMMAND_ABILITY_NOHEROES");

            string error = null;
            switch (index)
            {
                case Strike: error = DoStrike(run, alive, point); break;
                case Medic: DoMedic(alive); break;
                case WarCry: DoWarCry(alive); break;
                case Mark: error = DoMark(alive, target); break;
            }
            if (error != null) return error;

            state.ServerSetAbilityReady(index, Run.instance.GetRunStopwatch() + Cooldown[index]);
            return null;
        }

        // ---- the abilities ---------------------------------------------------------------------------------------

        private static string DoStrike(HighCommandRun run, List<Hero> alive, Vector3 point)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            run.StartCoroutine(StrikeRoutine(alive[0].Body.gameObject, point));
            return null;
        }

        private static IEnumerator StrikeRoutine(GameObject attacker, Vector3 center)
        {
            // a short warning, then three blasts in a row
            yield return new WaitForSeconds(1.1f);

            float level = Run.instance ? Mathf.Max(1f, Run.instance.ambientLevel) : 1f;
            float damage = (12f + 2.4f * (level - 1f)) * 10f;
            GameObject vfx = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/ExplosionVFX");

            for (int i = 0; i < 3; i++)
            {
                Vector2 jitter = i == 0 ? Vector2.zero : Random.insideUnitCircle * 6f;
                Vector3 at = center + new Vector3(jitter.x, 0f, jitter.y);

                new BlastAttack
                {
                    attacker = attacker,
                    inflictor = attacker,
                    teamIndex = TeamIndex.Player,
                    position = at,
                    radius = 13f,
                    baseDamage = damage,
                    baseForce = 1800f,
                    bonusForce = Vector3.up * 400f,
                    crit = false,
                    damageType = DamageType.Generic,
                    falloffModel = BlastAttack.FalloffModel.None,
                    procCoefficient = 0.2f,
                    damageColorIndex = DamageColorIndex.Item
                }.Fire();

                if (vfx)
                    EffectManager.SpawnEffect(vfx, new EffectData { origin = at, scale = 13f }, transmit: true);
                yield return new WaitForSeconds(0.3f);
            }
        }

        private static void DoMedic(List<Hero> alive)
        {
            foreach (Hero h in alive)
            {
                HealthComponent hc = h.Body.healthComponent;
                if (!hc || !hc.alive) continue;
                hc.HealFraction(0.4f, default(ProcChainMask));
                hc.AddBarrier(hc.fullCombinedHealth * 0.1f);
            }
        }

        private static void DoWarCry(List<Hero> alive)
        {
            foreach (Hero h in alive)
                h.Body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 12f);
        }

        private static string DoMark(List<Hero> alive, NetworkInstanceId target)
        {
            GameObject enemyObject = target != NetworkInstanceId.Invalid ? NetworkServer.FindLocalObject(target) : null;
            CharacterBody enemy = enemyObject ? enemyObject.GetComponent<CharacterBody>() : null;
            if (!enemy || !enemy.healthComponent || !enemy.healthComponent.alive) return Loc.Get("HIGHCOMMAND_ABILITY_NOTARGET");

            enemy.AddTimedBuff(RoR2Content.Buffs.DeathMark, 10f);

            var ids = new int[alive.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = alive[i].Id;
            OrderService.Execute(new OrderCommand
            {
                HeroIds = ids,
                Kind = OrderKind.Attack,
                TargetId = target,
                Focus = true,
                Point = enemy.corePosition
            });
            return null;
        }
    }
}
