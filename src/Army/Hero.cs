using RoR2;
using UnityEngine;

namespace HighCommand.Army
{
    /// <summary>
    /// One soldier of the army: an AI-controlled survivor. Wraps the hero's CharacterMaster (which persists across
    /// stages and respawns its body) so the rest of the mod never has to care whether a body currently exists.
    /// </summary>
    public sealed class Hero
    {
        public int Id;
        public CharacterMaster Master;
        public SurvivorDef Survivor;
        public BodyIndex BodyIndex = BodyIndex.None;

        /// <summary>Order in which the hero joined the army (the first one is the starting survivor).</summary>
        public int JoinOrder;

        public CharacterBody Body => Master ? Master.GetBody() : null;

        public GameObject BodyObject => Master ? Master.GetBodyObject() : null;

        public HealthComponent Health
        {
            get
            {
                CharacterBody body = Body;
                return body ? body.healthComponent : null;
            }
        }

        public bool HasBody => Body != null;

        /// <summary>True while the hero has a living body in the world.</summary>
        public bool IsAlive
        {
            get
            {
                HealthComponent hc = Health;
                return hc && hc.alive;
            }
        }

        public bool Exists => Master != null;

        public string DisplayName
        {
            get
            {
                // SurvivorDef.displayNameToken can be an empty string (not null), so fall back to the body's own name.
                if (Survivor && !string.IsNullOrEmpty(Survivor.displayNameToken))
                    return Language.GetString(Survivor.displayNameToken);
                CharacterBody b = Body;
                if (b) return b.GetDisplayName();
                if (Survivor && Survivor.bodyPrefab)
                {
                    var prefabBody = Survivor.bodyPrefab.GetComponent<CharacterBody>();
                    if (prefabBody) return Language.GetString(prefabBody.baseNameToken);
                }
                return "?";
            }
        }

        public Vector3 Position
        {
            get
            {
                CharacterBody b = Body;
                return b ? b.corePosition : (Master ? Master.transform.position : Vector3.zero);
            }
        }

        public override string ToString() => $"Hero#{Id}({(Survivor ? Survivor.cachedName : "?")})";
    }

    /// <summary>Marks a CharacterMaster as an army hero and links back to its <see cref="Hero"/>.</summary>
    public sealed class HeroMarker : MonoBehaviour
    {
        public Hero Hero;

        public static Hero Of(CharacterMaster master)
        {
            if (!master) return null;
            HeroMarker marker = master.GetComponent<HeroMarker>();
            return marker ? marker.Hero : null;
        }

        public static Hero Of(CharacterBody body)
        {
            return body ? Of(body.master) : null;
        }
    }
}
