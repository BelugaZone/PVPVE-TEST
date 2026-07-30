using UnityEditor;
using UnityEngine;
using FishNet.Object;
using FishNet.Component.Transforming;
using FishNet.Component.Animating;

public class FishNetMigrationSetup : EditorWindow
{
    [MenuItem("FishNet/Setup Phase 1")]
    public static void SetupPhase1()
    {
        // 1. Add FishNet components to Player Prefab
        string playerPrefabPath = "Assets/Prefab/Player.prefab"; 
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab != null)
        {
            if (playerPrefab.GetComponent<NetworkObject>() == null)
                playerPrefab.AddComponent<NetworkObject>();
                
            if (playerPrefab.GetComponent<NetworkTransform>() == null)
            {
                var nt = playerPrefab.AddComponent<NetworkTransform>();
            }
            
            if (playerPrefab.GetComponent<NetworkAnimator>() == null)
                playerPrefab.AddComponent<NetworkAnimator>();
                
            EditorUtility.SetDirty(playerPrefab);
            PrefabUtility.SavePrefabAsset(playerPrefab);
            Debug.Log("Added FishNet components to Player Prefab.");
        }
        else
        {
            Debug.LogError($"[FishNet Setup] 找不到玩家预制体 ({playerPrefabPath})！请确保您已经在场景中将玩家拖拽到了 Assets/Prefab 文件夹下保存为名为 'Player' 的预制体。");
        }

        Debug.Log("Phase 1 Script Setup Complete! \nNext steps:\n1. Duplicate your Main Scene.\n2. Remove the local Player from the duplicated scene.\n3. Drag the FishNet 'NetworkManager' prefab into the new scene to test.");
    }

    [MenuItem("FishNet/Fix Prefab Hash Errors")]
    public static void FixPrefabHashes()
    {
        string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" });
        int fixedCount = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var netObj = prefab.GetComponentInChildren<FishNet.Object.NetworkObject>(true);
                if (netObj != null)
                {
                    // If it's an enemy, remove the NetworkObject entirely for Phase 2
                    if (prefab.name.Contains("Enemy"))
                    {
                        GameObject.DestroyImmediate(netObj, true);
                        EditorUtility.SetDirty(prefab);
                        PrefabUtility.SavePrefabAsset(prefab);
                        Debug.Log($"Removed NetworkObject from {prefab.name} (Not needed until Phase 3)");
                        fixedCount++;
                    }
                    else
                    {
                        // Otherwise, just dirty it to force hash regeneration
                        EditorUtility.SetDirty(prefab);
                        PrefabUtility.SavePrefabAsset(prefab);
                        fixedCount++;
                    }
                }
            }
        }

        string[] soGuids = AssetDatabase.FindAssets("t:DefaultPrefabObjects");
        foreach (var guid in soGuids)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null)
            {
                EditorUtility.SetDirty(so);
            }
        }
        
        AssetDatabase.SaveAssets();
        Debug.Log($"[FishNet Fix] 成功处理了 {fixedCount} 个预制体并清理了缓存！请再次尝试运行游戏。");
    }

    [MenuItem("FishNet/Setup Phase 3")]
    public static void SetupPhase3()
    {
        string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" });
        int fixedCount = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.name.Contains("Enemy_") && prefab.GetComponent<Enemy>() != null)
            {
                bool modified = false;

                if (prefab.GetComponent<NetworkObject>() == null)
                {
                    prefab.AddComponent<NetworkObject>();
                    modified = true;
                }

                if (prefab.GetComponent<NetworkTransform>() == null)
                {
                    prefab.AddComponent<NetworkTransform>();
                    modified = true;
                }

                if (prefab.GetComponent<NetworkAnimator>() == null)
                {
                    var netAnim = prefab.AddComponent<NetworkAnimator>();
                    netAnim.SetAnimator(prefab.GetComponentInChildren<Animator>());
                    modified = true;
                }

                if (modified)
                {
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                    Debug.Log($"[Phase 3] Added Network components to {prefab.name}");
                    fixedCount++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FishNet Phase 3 Setup] 成功为 {fixedCount} 个敌人预制体添加了网络组件！");
    }
}
