using System.Collections.Generic;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Mode
{
    /// <summary>
    /// Server: monsters hunt the army. A stock monster picks a target only when it sees one (a ray and a 90 degree
    /// cone). In a normal run the director spawns everything 25-40 m from the one player, so nearly every monster sees
    /// it and runs at it from all sides. The army is spread over the map and moves on its own, so many monsters never
    /// look its way and just stand there. A monster with no target gets the nearest living hero, as if it had heard the
    /// fight, and walks to it the stock way (its own path finding and skill drivers).
    /// </summary>
    internal sealed class EnemyHunt : MonoBehaviour
    {
        private const float Interval = 1f;
        private static readonly TeamIndex[] EnemyTeams = { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void };

        private readonly List<CharacterBody> heroes = new List<CharacterBody>();
        private float timer;

        /// <summary>Monsters given a hero by the hunt since the run started (tests).</summary>
        internal static int Acquired { get; private set; }

        private void OnEnable() => Acquired = 0;

        private void Update()
        {
            if (!NetworkServer.active) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Interval;

            HighCommandRun run = HighCommandRun.Instance;
            if (run == null || !Settings.EnemiesHunt.Value || !Run.instance || Run.instance.isGameOverServer) return;

            long started = Dev.Perf.Begin();
            try { Tick(run); }
            catch (System.Exception e) { Plugin.Log.LogError("EnemyHunt failed: " + e); }
            Dev.Perf.End(ref Dev.Perf.Flow, started);
        }

        private void Tick(HighCommandRun run)
        {
            heroes.Clear();
            run.Army.CollectLivingBodies(heroes);
            if (heroes.Count == 0) return;

            float range = Settings.EnemyHuntRange.Value;
            float rangeSqr = range * range;
            int given = 0;
            foreach (TeamIndex team in EnemyTeams)
            {
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(team))
                {
                    CharacterBody body = tc ? tc.body : null;
                    if (!body || !body.master || !body.healthComponent || !body.healthComponent.alive) continue;
                    BaseAI[] ais = body.master.AiComponents;
                    if (ais == null) continue;
                    for (int i = 0; i < ais.Length; i++)
                    {
                        BaseAI ai = ais[i];
                        if (!ai || !ai.enabled || ai.currentEnemy.gameObject) continue;
                        CharacterBody prey = Nearest(body, rangeSqr);
                        if (!prey) break;
                        ai.currentEnemy.gameObject = prey.gameObject;
                        ai.enemyAttention = ai.enemyAttentionDuration;
                        given++;
                    }
                }
            }
            Acquired += given;
            if (given > 0 && Settings.VerboseLog.Value) Plugin.Log.LogInfo($"EnemyHunt: {given} monster(s) set on the army.");
        }

        private CharacterBody Nearest(CharacterBody hunter, float rangeSqr)
        {
            Vector3 from = hunter.corePosition;
            CharacterBody best = null;
            float bestSqr = rangeSqr;
            for (int i = 0; i < heroes.Count; i++)
            {
                CharacterBody hero = heroes[i];
                if (!hero || hero.GetVisibilityLevel(hunter) == VisibilityLevel.Untargetable) continue;
                float d = (hero.corePosition - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = hero; }
            }
            return best;
        }

        /// <summary>Monsters of the enemy teams, those with a target, and the distance of the farthest idle one (tests).</summary>
        internal static string Describe()
        {
            int total = 0, hunting = 0, idle = 0;
            float idleNearest = float.PositiveInfinity, huntingDistance = 0f;
            var bodies = new List<CharacterBody>();
            HighCommandRun run = HighCommandRun.Instance;
            if (run != null) run.Army.CollectLivingBodies(bodies);
            foreach (TeamIndex team in EnemyTeams)
            {
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(team))
                {
                    CharacterBody body = tc ? tc.body : null;
                    if (!body || !body.master || body.master.AiComponents == null || body.master.AiComponents.Length == 0) continue;
                    if (!body.healthComponent || !body.healthComponent.alive) continue;
                    total++;
                    float toArmy = float.PositiveInfinity;
                    foreach (CharacterBody h in bodies)
                        if (h) toArmy = Mathf.Min(toArmy, Vector3.Distance(h.corePosition, body.corePosition));
                    if (body.master.AiComponents[0].currentEnemy.gameObject)
                    {
                        hunting++;
                        if (bodies.Count > 0) huntingDistance += toArmy;
                        continue;
                    }
                    idle++;
                    idleNearest = Mathf.Min(idleNearest, toArmy);
                }
            }
            string avg = hunting > 0 && bodies.Count > 0 ? (huntingDistance / hunting).ToString("0") : "-";
            return $"enemies={total} hunting={hunting} (avg {avg} m from the army) idle={idle} " +
                   $"closestIdle={(idle > 0 && bodies.Count > 0 ? idleNearest.ToString("0") : "-")} " +
                   $"setByHunt={Acquired} on={Settings.EnemiesHunt.Value} range={Settings.EnemyHuntRange.Value:0}";
        }
    }
}
