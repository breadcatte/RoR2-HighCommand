using System.Collections.Generic;
using HighCommand.Army;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Which objects of the stage the commander has seen: the minimap marks only those. An object becomes known when a
    /// hero comes near it or when the camera looks at it from close enough, so panning around and sending heroes out
    /// both scout the map. The teleporter is always known (its strip on the top bar shows it anyway).
    /// </summary>
    internal static class Scouting
    {
        private const float HeroSight = 40f;
        private const float Interval = 0.4f;

        private static readonly HashSet<int> Known = new HashSet<int>();
        private static float nextAt;

        internal static int KnownCount => Known.Count;

        internal static void Reset()
        {
            Known.Clear();
            nextAt = 0f;
        }

        internal static bool IsKnown(GameObject thing)
        {
            if (!thing) return false;
            return Known.Contains(thing.GetInstanceID()) || thing.GetComponent<TeleporterInteraction>();
        }

        /// <summary>Marks what the heroes and the camera see now (a few times a second).</summary>
        internal static void Tick(IReadOnlyList<Hero> heroes, RtsCamera camera)
        {
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + Interval;

            Camera cam = camera ? camera.SceneCam : null;
            float cameraSight = camera ? Mathf.Max(30f, camera.Distance * 1.4f) : 0f;
            Vector3 focus = camera ? camera.Focus : Vector3.zero;

            List<IInteractable> all = InterfaceInstanceTracker<IInteractable>.GetInstancesList();
            for (int i = 0; i < all.Count; i++)
            {
                var mb = all[i] as MonoBehaviour;
                if (!mb) continue;
                GameObject thing = mb.gameObject;
                int id = thing.GetInstanceID();
                if (Known.Contains(id)) continue;
                Vector3 at = thing.transform.position;

                bool seen = false;
                if (cam && (at - focus).sqrMagnitude < cameraSight * cameraSight
                    && WorldPicker.ProjectPoint(cam, at, out Vector2 s) && WorldPicker.IsOnScreen(s, -20f))
                    seen = true;
                for (int h = 0; h < heroes.Count && !seen; h++)
                {
                    CharacterBody body = heroes[h].Body;
                    if (body && (body.corePosition - at).sqrMagnitude < HeroSight * HeroSight) seen = true;
                }
                if (seen) Known.Add(id);
            }
        }
    }
}
