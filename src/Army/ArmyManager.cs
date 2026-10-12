using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HighCommand.Orders;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// Server-side roster of the army. Creates heroes (AI survivor masters), keeps them registered, and answers
    /// "who is alive" for the rest of the mod. One instance per run, owned by <see cref="Mode.HighCommandRun"/>.
    /// </summary>
    public sealed class ArmyManager
    {
        private readonly List<Hero> heroes = new List<Hero>();
        private int nextId = 1;

        public IReadOnlyList<Hero> Heroes => heroes;

        /// <summary>Equipment the army owns but no hero holds yet.</summary>
        public readonly List<EquipmentIndex> EquipmentPool = new List<EquipmentIndex>();

        public int Count => heroes.Count;

        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < heroes.Count; i++) if (heroes[i].IsAlive) n++;
                return n;
            }
        }

        public Hero Find(CharacterMaster master)
        {
            for (int i = 0; i < heroes.Count; i++) if (heroes[i].Master == master) return heroes[i];
            return null;
        }

        public Hero Find(CharacterBody body) => body ? Find(body.master) : null;

        public bool Contains(SurvivorDef survivor)
        {
            for (int i = 0; i < heroes.Count; i++) if (heroes[i].Survivor == survivor) return true;
            return false;
        }

        /// <summary>Living hero bodies, for systems that need "a player to target" (director spawn target, etc.).</summary>
        public void CollectLivingBodies(List<CharacterBody> into)
        {
            for (int i = 0; i < heroes.Count; i++)
            {
                if (!heroes[i].IsAlive) continue;
                CharacterBody b = heroes[i].Body;
                if (b) into.Add(b);
            }
        }

        public void Clear()
        {
            EquipmentPool.Clear();
            heroes.Clear();
            nextId = 1;
        }

        /// <summary>The master prefab used to drive a survivor: the game's own AI master for that body when it has one.</summary>
        internal static GameObject FindMasterPrefab(BodyIndex bodyIndex, out bool exact)
        {
            exact = false;
            MasterCatalog.MasterIndex index = MasterCatalog.FindAiMasterIndexForBody(bodyIndex);
            if (index.isValid)
            {
                GameObject prefab = MasterCatalog.GetMasterPrefab(index);
                if (prefab) { exact = true; return prefab; }
            }
            // Unknown (modded) survivor: any survivor AI master as a shell, body prefab swapped after spawn.
            return MasterCatalog.FindMasterPrefab("CommandoMonsterMaster");
        }

        /// <summary>
        /// Creates a hero for <paramref name="survivor"/> at the given place. Server only.
        /// Returns null (and logs why) when it cannot be created.
        /// </summary>
        public Hero Recruit(SurvivorDef survivor, Loadout loadout, Vector3 position, Quaternion rotation, bool withPod = false)
        {
            if (!NetworkServer.active) { Plugin.Log.LogWarning("ArmyManager.Recruit called on a client."); return null; }
            if (!survivor || !survivor.bodyPrefab) { Plugin.Log.LogWarning("ArmyManager.Recruit: no survivor."); return null; }

            CharacterBody bodyPrefabComponent = survivor.bodyPrefab.GetComponent<CharacterBody>();
            if (!bodyPrefabComponent) { Plugin.Log.LogWarning($"ArmyManager.Recruit: {survivor.cachedName} has no CharacterBody."); return null; }

            BodyIndex bodyIndex = bodyPrefabComponent.bodyIndex;
            GameObject masterPrefab = FindMasterPrefab(bodyIndex, out bool exact);
            if (!masterPrefab) { Plugin.Log.LogError("ArmyManager.Recruit: no AI master prefab available."); return null; }

            var hero = new Hero
            {
                Id = nextId++,
                Survivor = survivor,
                BodyIndex = bodyIndex,
                JoinOrder = heroes.Count
            };

            var summon = new MasterSummon
            {
                masterPrefab = masterPrefab,
                position = position,
                rotation = rotation,
                teamIndexOverride = TeamIndex.Player,
                ignoreTeamMemberLimit = true,
                loadout = loadout,
                useAmbientLevel = false,
                preSpawnSetupCallback = master => PrepareMaster(master, hero, survivor, exact)
            };

            CharacterMaster created = summon.Perform();
            if (!created)
            {
                Plugin.Log.LogError($"ArmyManager.Recruit: MasterSummon failed for {survivor.cachedName}.");
                nextId--;
                return null;
            }

            hero.Master = created;
            heroes.Add(hero);

            CharacterBody body = created.GetBody();
            if (withPod && body && Run.instance)
            {
                try { Run.instance.HandlePlayerFirstEntryAnimation(body, position, rotation); }
                catch (Exception e) { Plugin.Log.LogWarning("Hero pod entry failed: " + e.Message); }
            }

            Plugin.Log.LogInfo($"Recruited {hero} master={created.name} body={(body ? body.name : "none")} exactAiMaster={exact} at {position}");
            return hero;
        }

        // Runs before the master is spawned on the network, so everything here is in place from the first frame.
        private static void PrepareMaster(CharacterMaster master, Hero hero, SurvivorDef survivor, bool exactMaster)
        {
            master.name = "HC_" + survivor.cachedName + "Master";
            master.destroyOnBodyDeath = false;

            if (!exactMaster)
                master.SetBodyPrefabSilently(survivor.bodyPrefab);

            // The master outlives the stage: the stock Stage.RespawnAllNPCs brings it back at the next spawn point,
            // and SceneExitController gives it the teleport-out effect when the stage ends.
            if (!master.GetComponent<SetDontDestroyOnLoad>())
                master.gameObject.AddComponent<SetDontDestroyOnLoad>();

            HeroMarker marker = master.gameObject.AddComponent<HeroMarker>();
            marker.Hero = hero;

            // The decision layer and its body executor replace the monsters' wander/combat states.
            HeroBrain brain = master.gameObject.AddComponent<HeroBrain>();
            brain.Hero = hero;
            BaseAI ai = master.GetComponent<BaseAI>();
            if (ai)
            {
                // BaseAI starts its state machine from scanState, but the machine itself also falls back to its own
                // initial / main state types (Wander): all three have to point at the hero state.
                var heroState = new EntityStates.SerializableEntityStateType(typeof(HeroAIState));
                ai.scanState = heroState;
                EntityStateMachine machine = ai.stateMachine ? ai.stateMachine : EntityStateMachine.FindByCustomName(master.gameObject, "AI");
                if (machine)
                {
                    machine.initialStateType = heroState;
                    machine.mainStateType = heroState;
                }
            }
        }
    }
}
