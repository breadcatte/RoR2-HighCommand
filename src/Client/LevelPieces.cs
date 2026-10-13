using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// What a collider of the world layer is to the camera: not the level at all (the game's objects, bodies), a big
    /// piece of the level (terrain, a cliff: the camera stays out of it) or a small piece (a rock, a roof, a tree, a
    /// bridge: it never moves the camera and is hidden while it covers a hero). Looked up once per collider.
    /// </summary>
    internal static class LevelPieces
    {
        /// <summary>m: a piece of the level up to this size counts as small.</summary>
        internal const float MaxSmallSize = 60f;

        private static readonly Dictionary<int, Renderer[]> Pieces = new Dictionary<int, Renderer[]>();
        private static readonly Renderer[] BigPiece = new Renderer[0];
        private static readonly List<Renderer> Found = new List<Renderer>(16);

        /// <summary>
        /// null = not the level; empty = a big piece of the level; otherwise the renderers of a small piece (the
        /// picture to hide when it is in the way).
        /// </summary>
        internal static Renderer[] Of(Collider collider)
        {
            if (!collider) return null;
            int id = collider.GetInstanceID();
            if (Pieces.TryGetValue(id, out Renderer[] known)) return known;

            Renderer[] result;
            if (collider.GetComponentInParent<UnityEngine.Networking.NetworkIdentity>() || collider.GetComponentInParent<CharacterBody>())
                result = null;
            else
            {
                result = BigPiece;
                Bounds box = collider.bounds;
                if (Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z)) <= MaxSmallSize)
                {
                    // the picture of the piece: renderers on the collider's object, else on its parent (a separate
                    // collision mesh beside the visible one) that lie where the collider is
                    Found.Clear();
                    Collect(collider.transform, box);
                    if (Found.Count == 0 && collider.transform.parent) Collect(collider.transform.parent, box);
                    if (Found.Count > 0) result = Found.ToArray();
                }
            }
            Pieces[id] = result;
            return result;
        }

        /// <summary>A big piece of the level: terrain, a cliff, a mountain (or an unseen wall of the map).</summary>
        internal static bool IsBig(Collider collider)
        {
            Renderer[] parts = Of(collider);
            return parts != null && parts.Length == 0;
        }

        /// <summary>Forgets what was learned (a new stage).</summary>
        internal static void Clear() => Pieces.Clear();

        private static void Collect(Transform root, Bounds near)
        {
            near.Expand(2f);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<UnityEngine.Networking.NetworkIdentity>() || r.GetComponentInParent<CharacterBody>()) continue;
                Bounds b = r.bounds;
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) > MaxSmallSize * 1.5f) continue;
                if (b.Intersects(near)) Found.Add(r);
            }
        }
    }
}
