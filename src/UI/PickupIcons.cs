using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Item and equipment icons. In this game version the direct icon fields of a PickupDef are empty and the sprite
    /// has to be loaded through its Addressables reference; loaded sprites are kept for the whole session.
    /// </summary>
    internal static class PickupIcons
    {
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();
        private static readonly HashSet<int> Failed = new HashSet<int>();
        private static readonly List<object> KeepAlive = new List<object>();

        internal static Sprite SpriteOf(PickupDef def)
        {
            if (def == null) return null;
            int key = (int)def.pickupIndex.value;
            if (Sprites.TryGetValue(key, out Sprite cached) && cached) return cached;
            if (Failed.Contains(key)) return null;

            Sprite sprite = def.iconSprite;
            if (!sprite && def.iconSpriteReference != null)
            {
                try
                {
                    if (def.iconSpriteReference.RuntimeKeyIsValid())
                    {
                        var holder = def.GetIconAODR(true);
                        if (holder.loadHandle.IsValid() && !holder.loadHandle.IsDone) holder.loadHandle.WaitForCompletion();
                        sprite = holder.Result;
                        KeepAlive.Add(holder);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"PickupIcons: could not load the icon of {def.internalName}: {e.Message}");
                }
            }

            if (sprite) Sprites[key] = sprite;
            else Failed.Add(key);
            return sprite;
        }

        /// <summary>Shows the pickup's icon in the image. Returns false (image cleared) when there is none.</summary>
        internal static bool Apply(RawImage image, PickupDef def)
        {
            if (!image) return false;
            image.uvRect = new Rect(0f, 0f, 1f, 1f);

            Sprite sprite = SpriteOf(def);
            if (sprite && sprite.texture)
            {
                Texture tex = sprite.texture;
                image.texture = tex;
                try
                {
                    Rect r = sprite.textureRect;
                    image.uvRect = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
                }
                catch (Exception) { /* not an atlas sprite: the whole texture is right */ }
                return true;
            }

            if (def != null && def.iconTexture)
            {
                image.texture = def.iconTexture;
                return true;
            }
            image.texture = null;
            return false;
        }
    }
}
