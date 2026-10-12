using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace HighCommand.Orders
{
    /// <summary>
    /// What a hero can see: hostile bodies near a point, with line-of-sight tests done lazily for the few best
    /// candidates only. Cheap enough to run for a dozen heroes several times a second.
    /// </summary>
    internal static class Perception
    {
        private static readonly TeamIndex[] HostileTeams = { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void };

        private struct Candidate
        {
            internal CharacterBody body;
            internal float score;
            internal float distance;
        }

        private static readonly List<Candidate> Buffer = new List<Candidate>(64);

        /// <summary>Counts hostile, alive, targetable bodies within a radius of a point.</summary>
        internal static int CountEnemies(Vector3 center, float radius, TeamIndex ownTeam = TeamIndex.Player)
        {
            float r2 = radius * radius;
            int n = 0;
            foreach (TeamIndex team in HostileTeams)
            {
                var members = TeamComponent.GetTeamMembers(team);
                for (int i = 0; i < members.Count; i++)
                {
                    TeamComponent tc = members[i];
                    if (!tc) continue;
                    CharacterBody body = tc.body;
                    if (!body) continue;
                    // the distance first: most bodies are far away, and the targetability tests cost more
                    if ((body.corePosition - center).sqrMagnitude > r2) continue;
                    if (IsTargetable(body, ownTeam)) n++;
                }
            }
            return n;
        }

        internal static bool IsTargetable(CharacterBody enemy, TeamIndex viewerTeam)
        {
            if (!enemy || !enemy.healthComponent || !enemy.healthComponent.alive) return false;
            if (enemy.HasBuff(RoR2Content.Buffs.Intangible) || enemy.HasBuff(RoR2Content.Buffs.HiddenInvincibility)) return false;
            return enemy.GetVisibilityLevel(viewerTeam) != VisibilityLevel.Untargetable;
        }

        /// <summary>
        /// The most attractive enemy for <paramref name="self"/> inside <paramref name="maxDistance"/> of
        /// <paramref name="searchCenter"/>. Closer is better; bosses, elites and wounded enemies are preferred;
        /// <paramref name="current"/> gets a bonus so targets do not flip every tick.
        /// </summary>
        internal static CharacterBody FindBestEnemy(CharacterBody self, Vector3 searchCenter, float maxDistance, bool requireLineOfSight,
            CharacterBody current = null, float leashFromPoint = float.PositiveInfinity, Vector3? leashPoint = null)
        {
            Buffer.Clear();
            float max2 = maxDistance * maxDistance;
            Vector3 origin = self.corePosition;

            foreach (TeamIndex team in HostileTeams)
            {
                var members = TeamComponent.GetTeamMembers(team);
                for (int i = 0; i < members.Count; i++)
                {
                    TeamComponent tc = members[i];
                    if (!tc || !tc.body) continue;
                    CharacterBody b = tc.body;

                    Vector3 p = b.corePosition;
                    float d2 = (p - searchCenter).sqrMagnitude;
                    if (d2 > max2) continue;
                    if (leashPoint.HasValue && (p - leashPoint.Value).sqrMagnitude > leashFromPoint * leashFromPoint) continue;
                    if (!IsTargetable(b, self.teamComponent.teamIndex)) continue;

                    float dist = Mathf.Sqrt((p - origin).sqrMagnitude);
                    float score = dist;
                    if (b.isChampion || b.isBoss) score -= 12f;
                    if (b.isElite) score -= 5f;
                    HealthComponent hc = b.healthComponent;
                    if (hc.fullCombinedHealth > 0f)
                    {
                        float frac = hc.combinedHealth / hc.fullCombinedHealth;
                        score -= (1f - frac) * 6f; // finish off the wounded
                    }
                    if (b == current) score -= 8f; // stickiness

                    Buffer.Add(new Candidate { body = b, score = score, distance = dist });
                }
            }
            if (Buffer.Count == 0) return null;

            Buffer.Sort((a, b) => a.score.CompareTo(b.score));
            if (!requireLineOfSight) return Buffer[0].body;

            // line of sight only for the best few
            Vector3 eye = self.inputBank ? self.inputBank.aimOrigin : self.corePosition;
            int tests = Mathf.Min(4, Buffer.Count);
            for (int i = 0; i < tests; i++)
            {
                if (HasLineOfSight(eye, Buffer[i].body.corePosition)) return Buffer[i].body;
            }
            return null;
        }

        internal static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.5f) return true;
            return !Physics.Raycast(from, delta / dist, dist, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }
    }
}
