using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// One soldier of the army: an AI-controlled survivor. Wraps the hero's CharacterMaster (which persists across
    /// stages and respawns its body) so the rest of the mod never has to care whether a body currently exists.
    /// </summary>
    public sealed class Hero
    {
        public int Id;
        public SurvivorDef Survivor;

        /// <summary>Network id of the master; lets a client find the hero's master once it has been spawned.</summary>
        public NetworkInstanceId MasterId = NetworkInstanceId.Invalid;

        private CharacterMaster master;

        /// <summary>The hero's CharacterMaster (resolved lazily on clients).</summary>
        public CharacterMaster Master
        {
            get
            {
                if (!master && MasterId != NetworkInstanceId.Invalid)
                {
                    GameObject go = Util.FindNetworkObject(MasterId);
                    if (go) master = go.GetComponent<CharacterMaster>();
                }
                return master;
            }
            set
            {
                master = value;
                if (master)
                {
                    NetworkIdentity id = master.GetComponent<NetworkIdentity>();
                    if (id) MasterId = id.netId;
                }
            }
        }
        public BodyIndex BodyIndex = BodyIndex.None;

        /// <summary>Order in which the hero joined the army (the first one is the starting survivor).</summary>
        public int JoinOrder;

        // ---- what the UI needs to know about the hero's orders ------------------------------------------
        // The HeroBrain lives on the server only; RunFlow copies these into the Hero and the roster replicates them,
        // so the host and every client draw the same stance, auto-use switches and order lines.

        /// <summary>(byte)Orders.Stance.</summary>
        public byte NetStance;

        /// <summary>Bit i = automatic use of slot i is on (0 primary .. 3 special, 4 equipment).</summary>
        public byte NetAutoMask = 0x1F;

        /// <summary>(byte)Orders.OrderKind of the current order, 0 = none.</summary>
        public byte NetOrderKind;

        /// <summary>Where the current order leads (target position for attack / follow / interact).</summary>
        public Vector3 NetOrderPoint;

        public byte NetQueued;

        /// <summary>Seconds left (rounded up, 0 = ready) on the secondary, utility and special skills.</summary>
        public readonly byte[] NetSkillCd = new byte[3];

        /// <summary>Enemies killed by the hero and its minions (host).</summary>
        public int Kills;

        /// <summary>Veteran rank 0..3 earned by kills (see ArmyUpgrades.RankKills).</summary>
        public int Rank;

        /// <summary>The hero's decision layer (server only; set when the hero is created, null on clients).</summary>
        public HighCommand.Orders.HeroBrain Brain;

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
