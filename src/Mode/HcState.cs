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
        public float PriceBase { get; private set; } = 300f;
        public float PriceGrowth { get; private set; } = 1.4f;
        public float PriceMultiplier { get; private set; } = 1f;
        public float ReviveFraction { get; private set; } = 0.4f;

        private struct RosterEntry
        {
            internal int id;
            internal NetworkInstanceId masterId;
            internal int survivorIndex;
            internal int joinOrder;
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

        protected static void InvokeRpcItemFeed(NetworkBehaviour obj, NetworkReader reader)
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
            ArmyLimit = Settings.ArmyLimit.Value;
            PriceBase = Settings.HeroBasePrice.Value;
            PriceGrowth = Settings.HeroPriceGrowth.Value;
            PriceMultiplier = Settings.PriceMultiplier.Value;
            ReviveFraction = Settings.ReviveCostFraction.Value;
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
                    joinOrder = h.JoinOrder
                });
            }
            spareEquipment.Clear();
            spareEquipment.AddRange(army.EquipmentPool);
            SetDirtyBit(DirtyAll);
        }

        // ---- prices (same formula on every machine) ---------------------------------------------------

        /// <summary>Price of the next recruit: base * growth^bought * the run's difficulty scaling * the multiplier.</summary>
        public int NextHeroPrice()
        {
            float coefficient = Run.instance ? Run.instance.difficultyCoefficient : 1f;
            float scale = Mathf.Pow(Mathf.Max(1f, coefficient), 1.25f); // same curve as chest prices
            float price = PriceBase * Mathf.Pow(PriceGrowth, Bought) * scale * PriceMultiplier;
            return Mathf.Max(1, Mathf.RoundToInt(price));
        }

        public int RevivePrice()
        {
            return Mathf.Max(1, Mathf.RoundToInt(NextHeroPrice() * ReviveFraction));
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

            writer.WritePackedUInt32((uint)roster.Count);
            foreach (RosterEntry e in roster)
            {
                writer.WritePackedUInt32((uint)e.id);
                writer.Write(e.masterId);
                writer.WritePackedUInt32((uint)(e.survivorIndex + 1));
                writer.WritePackedUInt32((uint)e.joinOrder);
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

            int count = (int)reader.ReadPackedUInt32();
            roster.Clear();
            for (int i = 0; i < count; i++)
            {
                roster.Add(new RosterEntry
                {
                    id = (int)reader.ReadPackedUInt32(),
                    masterId = reader.ReadNetworkId(),
                    survivorIndex = (int)reader.ReadPackedUInt32() - 1,
                    joinOrder = (int)reader.ReadPackedUInt32()
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
