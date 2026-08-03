#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using FishNet.Object;

public static class StashSetupTool
{
    private const string DefaultLoadoutPath = "Assets/Data/DefaultLoadout.asset";
    private const string LobbyPlayerPrefabPath = "Assets/Prefab/LobbyPlayer.prefab";
    private const string ItemRegistryPath = "Assets/Data/ItemRegistry.asset";

    [MenuItem("Tools/Lobby Setup/Build Stash Assets")]
    public static void BuildStashAssets()
    {
        // 1. DefaultLoadout SO
        var loadout = AssetDatabase.LoadAssetAtPath<DefaultLoadout>(DefaultLoadoutPath);
        if (loadout == null)
        {
            loadout = ScriptableObject.CreateInstance<DefaultLoadout>();
            AssetDatabase.CreateAsset(loadout, DefaultLoadoutPath);
        }
        loadout.entries = new System.Collections.Generic.List<DefaultLoadout.StashEntry>
        {
            new DefaultLoadout.StashEntry { itemId = "Heaven 567", count = 1 },   // pistol (Weapon_Data.weaponName)
            new DefaultLoadout.StashEntry { itemId = "rifle_ammo", count = 2 },   // ammo (ItemData.itemId)
            new DefaultLoadout.StashEntry { itemId = "medkit", count = 2 },       // consumable
        };
        EditorUtility.SetDirty(loadout);

        // 2. LobbyPlayer prefab
        var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(ItemRegistryPath);

        var root = new GameObject("LobbyPlayer");
        var nob = root.AddComponent<NetworkObject>();
        var stash = root.AddComponent<PlayerStash>();
        // Assign itemRegistry via SerializedObject (private serialized field).
        var soStash = new SerializedObject(stash);
        var regProp = soStash.FindProperty("itemRegistry");
        if (regProp != null && registry != null) regProp.objectReferenceValue = registry;
        soStash.ApplyModifiedPropertiesWithoutUndo();

        // UI_StashPanel canvas child (inactive until owner shows it).
        var uiGo = new GameObject("StashPanel");
        uiGo.transform.SetParent(root.transform, false);
        uiGo.AddComponent<UI_StashPanel>();

        DirectoryEnsure(System.IO.Path.GetDirectoryName(LobbyPlayerPrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, LobbyPlayerPrefabPath);
        Object.DestroyImmediate(root);

        // 3. Wire ServerDataManager in the Lobby scene (must be open).
        var sdm = Object.FindObjectOfType<ServerDataManager>();
        if (sdm != null)
        {
            var lobbyPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(LobbyPlayerPrefabPath);
            var so = new SerializedObject(sdm);
            var lpProp = so.FindProperty("lobbyPlayerPrefab");
            if (lpProp != null && lobbyPrefab != null) lpProp.objectReferenceValue = lobbyPrefab;
            var dlProp = so.FindProperty("defaultLoadout");
            if (dlProp != null && loadout != null) dlProp.objectReferenceValue = loadout;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sdm);
            EditorSceneManager_MarkDirty();
            Debug.Log("[StashSetup] Wired ServerDataManager.lobbyPlayerPrefab + defaultLoadout.");
        }
        else
        {
            Debug.LogWarning("[StashSetup] No ServerDataManager in open scene. Open Assets/Scenes/Lobby.unity and re-run to wire.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[StashSetup] Done. DefaultLoadout + LobbyPlayer prefab created.");
    }

    private static void DirectoryEnsure(string dir)
    {
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
    }

    private static void EditorSceneManager_MarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
#endif
