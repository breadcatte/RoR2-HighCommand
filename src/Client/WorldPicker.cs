using System.Collections.Generic;
using HighCommand.Army;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Mouse picking in the 3D world: which hero / enemy is under the cursor, and where the cursor touches the ground.
    /// Units are picked by screen distance to their body (forgiving, like in an RTS) rather than by collider.
    /// </summary>
    internal static class WorldPicker
    {
        // a body whose centre is farther than this (pixels) from the cursor cannot be the one picked
        private const float FarFromCursor = 400f;

        private static Camera Cam
        {
            get
            {
                RtsCamera rts = RtsCamera.Instance;
                return rts ? rts.SceneCam : null;
            }
        }

        /// <summary>Screen position and on-screen radius (pixels) of a world sphere; false when behind the camera.</summary>
        internal static bool Project(Vector3 world, float worldRadius, out Vector2 screen, out float pixelRadius)
        {
            screen = default;
            pixelRadius = 0f;
            Camera cam = Cam;
            if (!cam) return false;

            Vector3 p = cam.WorldToScreenPoint(world);
            if (p.z <= 0.1f) return false;
            screen = new Vector2(p.x, p.y);

            Vector3 edge = cam.WorldToScreenPoint(world + cam.transform.right * worldRadius);
            pixelRadius = Mathf.Abs(edge.x - p.x);
            return true;
        }

        /// <summary>Screen position of a world point for an explicit camera (no radius): one projection, no property lookups.</summary>
        internal static bool ProjectPoint(Camera cam, Vector3 world, out Vector2 screen)
        {
            Vector3 p = cam.WorldToScreenPoint(world);
            screen = new Vector2(p.x, p.y);
            return p.z > 0.1f;
        }

        internal static bool IsOnScreen(Vector2 s, float margin = 40f)
        {
            return s.x > -margin && s.y > -margin && s.x < Screen.width + margin && s.y < Screen.height + margin;
        }

        /// <summary>The ground point under a screen position; falls back to the focus plane.</summary>
        internal static bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            point = default;
            Camera cam = Cam;
            if (!cam) return false;

            Ray ray = cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out RaycastHit hit, 1500f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }

            RtsCamera rts = RtsCamera.Instance;
            float y = rts ? rts.Focus.y : 0f;
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        /// <summary>The nearest hero whose body is within reach of the cursor, or null.</summary>
        internal static Hero PickHero(Vector2 screen, IReadOnlyList<Hero> heroes)
        {
            Hero best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < heroes.Count; i++)
            {
                Hero h = heroes[i];
                CharacterBody b = h.Body;
                if (!b || !h.IsAlive) continue;

                Vector3 center = b.corePosition;
                if (!Project(center, Mathf.Max(0.6f, b.radius * 1.25f), out Vector2 s, out float r)) continue;
                float reach = Mathf.Max(18f, r + 8f);
                float d = (s - screen).magnitude;
                if (d <= reach && d < bestScore)
                {
                    bestScore = d;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>The hostile body nearest to the cursor, or null.</summary>
        internal static CharacterBody PickEnemy(Vector2 screen)
        {
            CharacterBody best = null;
            float bestScore = float.MaxValue;
            Camera cam = Cam;
            if (!cam) return null;
            float far = FarFromCursor * Mathf.Max(1f, Screen.height / 1080f);
            float farSqr = far * far;
            var bodies = CharacterBody.readOnlyInstancesList;
            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody b = bodies[i];
                if (!b || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent) continue;
                if (!IsHostileToPlayer(b.teamComponent.teamIndex)) continue;

                // one cheap projection first: bodies far from the cursor never need the size measured
                Vector3 core = b.corePosition;
                if (!ProjectPoint(cam, core, out Vector2 rough)) continue;
                if ((rough - screen).sqrMagnitude > farSqr) continue;

                if (!Project(core, Mathf.Max(0.6f, b.radius * 1.2f), out Vector2 s, out float r)) continue;
                float reach = Mathf.Max(16f, r + 6f);
                float d = (s - screen).magnitude;
                if (d <= reach && d < bestScore)
                {
                    bestScore = d;
                    best = b;
                }
            }
            return best;
        }

        /// <summary>An interactable object (teleporter, shrine, portal, chest ...) under the cursor, or null.</summary>
        internal static GameObject PickInteractable(Vector2 screen)
        {
            Camera cam = Cam;
            if (!cam) return null;

            Ray ray = cam.ScreenPointToRay(screen);
            int mask = LayerIndex.CommonMasks.interactable;
            if (!Physics.Raycast(ray, out RaycastHit hit, 1200f, mask, QueryTriggerInteraction.Collide)) return null;

            if (!EntityLocator.HasEntityLocator(hit.collider.gameObject, out EntityLocator locator)) return null;
            GameObject entity = locator.entity;
            if (!entity) return null;
            if (!entity.TryGetComponent<IInteractable>(out IInteractable interactable)) return null;
            if (!((MonoBehaviour)interactable).isActiveAndEnabled) return null;
            return entity;
        }

        internal static bool IsHostileToPlayer(TeamIndex team)
        {
            return team == TeamIndex.Monster || team == TeamIndex.Lunar || team == TeamIndex.Void;
        }

        /// <summary>Heroes whose body is inside the screen rectangle.</summary>
        internal static void HeroesInRect(Rect screenRect, IReadOnlyList<Hero> heroes, List<Hero> into)
        {
            for (int i = 0; i < heroes.Count; i++)
            {
                Hero h = heroes[i];
                CharacterBody b = h.Body;
                if (!b || !h.IsAlive) continue;
                if (!Project(b.corePosition, b.radius, out Vector2 s, out float r)) continue;
                // a unit counts when the middle of its body is in the box
                if (screenRect.Contains(s)) into.Add(h);
            }
        }
    }
}
