using System.Collections.Generic;
using HighCommand.Army;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Mode
{
    /// <summary>
    /// The part of the army's state every machine needs to draw the UI: the shared gold, how many heroes were bought,
    /// the price settings and the roster (which master belongs to which hero). Lives on the run object; the server
    /// writes it, clients read it. Serialised by hand (no UNET weaver is available to a mod).
    /// </summary>
    public sealed class HcState : NetworkBehaviour
    {
        private const uint DirtyAll = 1u;

        public static HcState Instance { get; private set; }

        // ---- replicated state --------------------------------------------------------------------
        public uint Gold { get; private set; }
        public int Bought { get; private set; }
        public int ArmyLimit { get; private set; } = 6;
        public float PriceBase { get; private set; } = 250f;
        public float PriceGrowth { get; private set; } = 1.4f;
        public float PriceMultiplier { get; private set; } = 1f;
        public float ReviveFraction { get; private set; } = 0.4f;

        /// <summary>The host's item sharing mode: true = every hero owns a copy of every item (nothing to hand over).</summary>
        public bool ItemMirror { get; private set; }

        // ---- run flow (written by RunFlow on the server) ----------------------------------------------
        public enum TeleState : byte { None = 0, Idle = 1, Charging = 2, BossAlive = 3, Charged = 4, Leaving = 5 }

        /// <summary>Seconds left before the run is lost because no hero is alive (&lt; 0 = nothing is counting down).</summary>
        public float DefeatSeconds { get; private set; } = -1f;

        /// <summary>While the defeat countdown runs: can the army still pay for a revival?</summary>
        public bool DefeatCanRevive { get; private set; }

        /// <summary>While the countdown runs: a revival will be bought automatically when it reaches 0 (no defeat).</summary>
        public bool DefeatAutoRevive { get; private set; }

        public TeleState Teleporter { get; private set; }
        public float TeleCharge { get; private set; }
        public Vector3 TelePosition { get; private set; }
        public float TeleRadius { get; private set; }

        /// <summary>Run-stopwatch time at which each commander ability is ready again.</summary>
        public readonly float[] AbilityReadyAt = new float[4];

        public void ServerSetAbilityReady(int index, float runTime)
        {
            if (!NetworkServer.active || index < 0 || index >= AbilityReadyAt.Length) return;
            AbilityReadyAt[index] = runTime;
            SetDirtyBit(DirtyAll);
        }

        /// <summary>Server: publishes the run-flow values; only a real change makes the network send anything.</summary>
        public void ServerSetFlow(float defeatSeconds, bool canRevive, bool autoRevive, TeleState tele, float charge, Vector3 position, float radius)
        {
            if (!NetworkServer.active) return;
            bool changed = false;
            if (Mathf.Abs(defeatSeconds - DefeatSeconds) > 0.45f || (defeatSeconds < 0f) != (DefeatSeconds < 0f)) { DefeatSeconds = defeatSeconds; changed = true; }
            if (canRevive != DefeatCanRevive) { DefeatCanRevive = canRevive; changed = true; }
            if (autoRevive != DefeatAutoRevive) { DefeatAutoRevive = autoRevive; changed = true; }
            if (tele != Teleporter) { Teleporter = tele; changed = true; }
            if (Mathf.Abs(charge - TeleCharge) > 0.004f) { TeleCharge = charge; changed = true; }
            if ((position - TelePosition).sqrMagnitude > 0.25f) { TelePosition = position; changed = true; }
            if (Mathf.Abs(radius - TeleRadius) > 0.25f) { TeleRadius = radius; changed = true; }
            if (changed) SetDirtyBit(DirtyAll);
        }

        private struct RosterEntry
        {
            internal int id;
            internal NetworkInstanceId masterId;
            internal int survivorIndex;
            internal int joinOrder;
            internal byte stance;
            internal byte autoMask;
            internal byte orderKind;
            internal byte queued;
            internal Vector3 orderPoint;
            internal byte cd0, cd1, cd2;
        }

        private readonly List<RosterEntry> roster = new List<RosterEntry>();
        private readonly List<Hero> clientHeroes = new List<Hero>();
        private bool clientHeroesDirty = true;

        /// <summary>
        /// The heroes as this machine sees them: the authoritative objects on the server, reconstructed ones on clients.
        /// </summary>
        public IReadOnlyList<Hero> Heroes
        {
            get
            {
                HighCommandRun run = HighCommandRun.Instance;
                if (NetworkServer.active && run != null) return run.Army.Heroes;
                RebuildClientHeroes();
                return clientHeroes;
            }
        }

        private readonly List<EquipmentIndex> spareEquipment = new List<EquipmentIndex>();

        /// <summary>Equipment the army owns that no hero holds right now.</summary>
        public IReadOnlyList<EquipmentIndex> SpareEquipment
        {
            get
            {
                HighCommandRun run = HighCommandRun.Instance;
                if (NetworkServer.active && run != null) return run.Army.EquipmentPool;
                return spareEquipment;
            }
        }

        // ---- RPC: item feed (server -> every machine) -------------------------------------------------
        // Written by hand, exactly as the UNET weaver would generate a [ClientRpc].

        private static readonly int kRpcItemFeed = 0x48430A01;

        /// <summary>Raised on every machine when the army gains a pickup: (pickup index, count).</summary>
        public static event System.Action<PickupIndex, int> ItemGained;

        static HcState()
        {
            RegisterRpcDelegate(typeof(HcState), kRpcItemFeed, InvokeRpcItemFeed);
            NetworkCRC.RegisterBehaviour("HcState", 0);
        }

        private static void InvokeRpcItemFeed(NetworkBehaviour obj, NetworkReader reader)
        {
            if (!NetworkClient.active) return;
            int pickup = (int)reader.ReadPackedUInt32() - 1;
            int count = (int)reader.ReadPackedUInt32();
            ItemGained?.Invoke(new PickupIndex(pickup), count);
        }

        /// <summary>Server: announces a gained pickup to every machine (including the host's own client).</summary>
        public void CallRpcItemFeed(int pickupIndex, int count)
        {
            if (!NetworkServer.active) return;
            var writer = new NetworkWriter();
            writer.Write((short)0);
            writer.Write((short)2);
            writer.WritePackedUInt32((uint)kRpcItemFeed);
            writer.Write(GetComponent<NetworkIdentity>().netId);
            writer.WritePackedUInt32((uint)(pickupIndex + 1));
            writer.WritePackedUInt32((uint)count);
            SendRPCInternal(writer, 0, "RpcItemFeed");
        }

        // ---- lifecycle ------------------------------------------------------------------------------

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        // ---- server writes ----------------------------------------------------------------------------

        public void ServerSetGold(uint gold)
        {
            if (!NetworkServer.active) return;
            Gold = gold;
            SetDirtyBit(DirtyAll);
        }

        public void ServerSetBought(int bought)
        {
            if (!NetworkServer.active) return;
            Bought = bought;
            SetDirtyBit(DirtyAll);
        }

        /// <summary>Copies the economy settings (host config) into the replicated state.</summary>
        public void ServerPublishSettings()
        {
            if (!NetworkServer.active) return;
            // the lobby rules (when chosen) win over the config file
            ArmyLimit = LobbyRules.ArmyLimit;
            PriceBase = Settings.HeroBasePrice.Value;
            PriceGrowth = Settings.HeroPriceGrowth.Value;
            PriceMultiplier = LobbyRules.PriceMultiplier;
            ReviveFraction = Settings.ReviveCostFraction.Value;
            ItemMirror = LobbyRules.ItemMirror;
            SetDirtyBit(DirtyAll);
        }

        /// <summary>Refreshes the replicated roster from the server's army.</summary>
        public void ServerPublishRoster(ArmyManager army)
        {
            if (!NetworkServer.active || army == null) return;
            roster.Clear();
            foreach (Hero h in army.Heroes)
            {
                roster.Add(new RosterEntry
                {
                    id = h.Id,
                    masterId = h.MasterId,
                    survivorIndex = h.Survivor ? (int)h.Survivor.survivorIndex : -1,
                    joinOrder = h.JoinOrder,
                    stance = h.NetStance,
                    autoMask = h.NetAutoMask,
                    orderKind = h.NetOrderKind,
                    queued = h.NetQueued,
                    orderPoint = h.NetOrderPoint,
                    cd0 = h.NetSkillCd[0],
                    cd1 = h.NetSkillCd[1],
                    cd2 = h.NetSkillCd[2]
                });
            }
            spareEquipment.Clear();
            spareEquipment.AddRange(army.EquipmentPool);
            SetDirtyBit(DirtyAll);
        }

        // ---- prices (same formula on every machine) ---------------------------------------------------

        /// <summary>
        /// The run's difficulty coefficient as if one player played it (the stock formula with a player count of 1).
        /// Prices follow time, stages and the chosen difficulty only: the stock coefficient also grows with the player
        /// count, and the army counts as players, so every recruit made the next one dearer twice (growth^bought and
        /// a higher coefficient).
        /// </summary>
        public static float SoloDifficulty()
        {
            Run run = Run.instance;
            if (!run) return 1f;
            DifficultyDef def = DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty);
            float scaling = def != null ? def.scalingValue : 2f;
            float minutes = Mathf.Floor(run.GetEffectiveRunStopwatch() / 60f);
            return (1f + 0.0506f * scaling * minutes) * Mathf.Pow(1.15f, run.effectiveStageClearCount);
        }

        internal float PriceScale() => Mathf.Pow(Mathf.Max(1f, SoloDifficulty()), 1.25f) * PriceMultiplier; // the chest price curve

        /// <summary>Price of the next recruit: base * growth^bought * difficulty scaling * the multiplier.</summary>
        public int NextHeroPrice()
        {
            float price = PriceBase * Mathf.Pow(PriceGrowth, Bought) * PriceScale();
            return Mathf.Max(1, Mathf.RoundToInt(price));
        }

        /// <summary>A revival costs a fraction of a FIRST recruit's price: it does not grow with the number of heroes bought.</summary>
        public int RevivePrice()
        {
            return Mathf.Max(1, Mathf.RoundToInt(PriceBase * PriceScale() * ReviveFraction));
        }

        // ---- network ------------------------------------------------------------------------------------

        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            if (!initialState && syncVarDirtyBits == 0u) return false;

            writer.Write(Gold);
            writer.WritePackedUInt32((uint)Bought);
            writer.WritePackedUInt32((uint)ArmyLimit);
            writer.Write(PriceBase);
            writer.Write(PriceGrowth);
            writer.Write(PriceMultiplier);
            writer.Write(ReviveFraction);
            writer.Write(ItemMirror);
            writer.Write(DefeatSeconds);
            writer.Write(DefeatCanRevive);
            writer.Write(DefeatAutoRevive);
            writer.Write((byte)Teleporter);
            writer.Write(TeleCharge);
            writer.Write(TelePosition);
            writer.Write(TeleRadius);
            for (int i = 0; i < AbilityReadyAt.Length; i++) writer.Write(AbilityReadyAt[i]);

            writer.WritePackedUInt32((uint)roster.Count);
            foreach (RosterEntry e in roster)
            {
                writer.WritePackedUInt32((uint)e.id);
                writer.Write(e.masterId);
                writer.WritePackedUInt32((uint)(e.survivorIndex + 1));
                writer.WritePackedUInt32((uint)e.joinOrder);
                writer.Write(e.stance);
                writer.Write(e.autoMask);
                writer.Write(e.orderKind);
                writer.Write(e.queued);
                writer.Write(e.orderPoint);
                writer.Write(e.cd0);
                writer.Write(e.cd1);
                writer.Write(e.cd2);
            }

            writer.WritePackedUInt32((uint)spareEquipment.Count);
            foreach (EquipmentIndex eq in spareEquipment) writer.WritePackedUInt32((uint)((int)eq + 1));
            return true;
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            Gold = reader.ReadUInt32();
            Bought = (int)reader.ReadPackedUInt32();
            ArmyLimit = (int)reader.ReadPackedUInt32();
            PriceBase = reader.ReadSingle();
            PriceGrowth = reader.ReadSingle();
            PriceMultiplier = reader.ReadSingle();
            ReviveFraction = reader.ReadSingle();
            ItemMirror = reader.ReadBoolean();
            DefeatSeconds = reader.ReadSingle();
            DefeatCanRevive = reader.ReadBoolean();
            DefeatAutoRevive = reader.ReadBoolean();
            Teleporter = (TeleState)reader.ReadByte();
            TeleCharge = reader.ReadSingle();
            TelePosition = reader.ReadVector3();
            TeleRadius = reader.ReadSingle();
            for (int i = 0; i < AbilityReadyAt.Length; i++) AbilityReadyAt[i] = reader.ReadSingle();

            int count = (int)reader.ReadPackedUInt32();
            roster.Clear();
            for (int i = 0; i < count; i++)
            {
                roster.Add(new RosterEntry
                {
                    id = (int)reader.ReadPackedUInt32(),
                    masterId = reader.ReadNetworkId(),
                    survivorIndex = (int)reader.ReadPackedUInt32() - 1,
                    joinOrder = (int)reader.ReadPackedUInt32(),
                    stance = reader.ReadByte(),
                    autoMask = reader.ReadByte(),
                    orderKind = reader.ReadByte(),
                    queued = reader.ReadByte(),
                    orderPoint = reader.ReadVector3(),
                    cd0 = reader.ReadByte(),
                    cd1 = reader.ReadByte(),
                    cd2 = reader.ReadByte()
                });
            }

            int spare = (int)reader.ReadPackedUInt32();
            spareEquipment.Clear();
            for (int i = 0; i < spare; i++) spareEquipment.Add((EquipmentIndex)((int)reader.ReadPackedUInt32() - 1));
            clientHeroesDirty = true;
        }

        private void RebuildClientHeroes()
        {
            if (!clientHeroesDirty) return;
            clientHeroesDirty = false;

            var old = new Dictionary<int, Hero>();
            foreach (Hero h in clientHeroes) old[h.Id] = h;
            clientHeroes.Clear();

            foreach (RosterEntry e in roster)
            {
                if (!old.TryGetValue(e.id, out Hero hero)) hero = new Hero { Id = e.id };
                hero.MasterId = e.masterId;
                hero.JoinOrder = e.joinOrder;
                hero.NetStance = e.stance;
                hero.NetAutoMask = e.autoMask;
                hero.NetOrderKind = e.orderKind;
                hero.NetQueued = e.queued;
                hero.NetOrderPoint = e.orderPoint;
                hero.NetSkillCd[0] = e.cd0;
                hero.NetSkillCd[1] = e.cd1;
                hero.NetSkillCd[2] = e.cd2;
                hero.Survivor = e.survivorIndex >= 0 ? SurvivorCatalog.GetSurvivorDef((SurvivorIndex)e.survivorIndex) : null;
                if (hero.Survivor && hero.Survivor.bodyPrefab)
                {
                    CharacterBody pb = hero.Survivor.bodyPrefab.GetComponent<CharacterBody>();
                    if (pb) hero.BodyIndex = pb.bodyIndex;
                }
                clientHeroes.Add(hero);
            }
        }
    }
}
