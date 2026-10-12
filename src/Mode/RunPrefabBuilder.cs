using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HighCommand.Mode
{
    /// <summary>
    /// Builds the HighCommandRun game-mode prefab at load time, without a Unity Editor: a clone of the stock
    /// ClassicRun prefab with another name, our marker component, our game-mode token and a fresh network asset id
    /// (otherwise the client prefab table would mix it up with ClassicRun).
    /// </summary>
    internal static class RunPrefabBuilder
    {
        private const string SourceKey = "RoR2/Base/ClassicRun/ClassicRun.prefab";

        internal static GameObject Prefab { get; private set; }

        private static GameObject holder;

        internal static IEnumerator Build(ContentPack pack)
        {
            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(SourceKey);
            while (!handle.IsDone) yield return null;

            if (handle.Status != AsyncOperationStatus.Succeeded || !handle.Result)
            {
                Plugin.Log.LogError($"RunPrefabBuilder: cannot load '{SourceKey}' ({handle.Status}). The mode will not be available.");
                yield break;
            }

            try
            {
                Prefab = Clone(handle.Result);
                pack.gameModePrefabs.Add(new[] { Prefab });
                Plugin.Log.LogInfo($"RunPrefabBuilder: registered game mode prefab '{Prefab.name}'.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("RunPrefabBuilder: failed to build the run prefab: " + e);
            }
        }

        private static GameObject Clone(GameObject source)
        {
            // Objects under an inactive parent never run Awake/Start: this is how a prefab "asset" is faked at runtime.
            holder = new GameObject("HighCommand.PrefabHolder");
            holder.SetActive(false);
            holder.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(holder);

            GameObject clone = UnityEngine.Object.Instantiate(source, holder.transform);
            clone.name = HighCommandRun.PrefabName;

            Run run = clone.GetComponent<Run>();
            if (!run) throw new InvalidOperationException("ClassicRun prefab has no Run component.");
            run.nameToken = HighCommandRun.NameToken;
            run.userPickable = true;

            clone.AddComponent<HighCommandRun>();

            NetworkIdentity identity = clone.GetComponent<NetworkIdentity>();
            if (!identity) throw new InvalidOperationException("ClassicRun prefab has no NetworkIdentity.");
            AssignAssetId(identity, "HighCommand:" + HighCommandRun.PrefabName);

            return clone;
        }

        private static void AssignAssetId(NetworkIdentity identity, string key)
        {
            byte[] hash;
            using (MD5 md5 = MD5.Create()) hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(32);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));

            NetworkHash128 id = NetworkHash128.Parse(sb.ToString());
            FieldInfo field = typeof(NetworkIdentity).GetField("m_AssetId", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException("NetworkIdentity.m_AssetId not found.");
            field.SetValue(identity, id);
            Plugin.Log.LogInfo($"RunPrefabBuilder: network asset id {id}");
        }
    }
}
