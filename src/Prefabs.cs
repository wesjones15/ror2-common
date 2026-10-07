using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    public static class Prefabs
    {
        static GameObject holder;

        /// A prefab copy, kept under an inactive object so it never wakes up in the scene. A
        /// copy with a NetworkIdentity gets its own asset id (from `owner` and `name`), so
        /// clients spawn it rather than the original (what R2API's PrefabAPI.InstantiateClone
        /// does). Register networked copies through a content pack (bodyPrefabs and the like),
        /// which RoR2 hands to ClientScene.RegisterPrefab.
        public static GameObject Clone(GameObject src, string name, string owner)
        {
            if (!holder)
            {
                holder = new GameObject("Ror2CommonPrefabs");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            var copy = Object.Instantiate(src, holder.transform);
            copy.name = name;
            var id = copy.GetComponent<NetworkIdentity>();
            if (id)
            {
                var hash = NetworkHash128.Parse(Md5Hex(owner + "/" + name));
                typeof(NetworkIdentity)
                    .GetField("m_AssetId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(id, hash);
            }
            return copy;
        }

        static string Md5Hex(string s)
        {
            using (var md5 = MD5.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(s))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
