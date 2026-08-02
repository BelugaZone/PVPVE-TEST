using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

// Run once via menu: Tools > Setup Player UI System
// Generates placeholder sprites, starter ItemData assets, the HUD + panel prefabs,
// and patches Player.prefab to add PlayerInventory + HUD canvas + wired prefab refs.
// Idempotent: each step skips creation when the asset/component already exists.
public class PlayerUISetupTool : EditorWindow
{
    private const string PLAYER_PREFAB_PATH = "Assets/Prefab/Player.prefab";
    private const string ICONS_DIR = "Assets/UI/Icons";
    private const string INVENTORY_DATA_DIR = "Assets/Data/Inventory";
    private const string HUD_PREFAB_PATH = "Assets/Prefab/UI/PlayerHUD.prefab";
    private const string PANEL_PREFAB_PATH = "Assets/Prefab/UI/PlayerInventoryPanel.prefab";

    // References to existing Weapon_Data assets (by filename) for starter items.
    private const string PISTOL_DATA_GUID = ""; // left blank; resolved by filename below

    [MenuItem("Tools/Setup Player UI System")]
    public static void Setup()
    {
        EnsureDir(ICONS_DIR);
        EnsureDir(INVENTORY_DATA_DIR);
        EnsureDir("Assets/Prefab/UI");

        CreatePlaceholderSprites();
        CreateStarterItemData();
        GameObject hudPrefab = CreateHudPrefab();
        GameObject panelPrefab = CreatePanelPrefab();
        PatchPlayerPrefab(hudPrefab, panelPrefab);

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(hudPrefab);
        EditorUtility.SetDirty(panelPrefab);
        Debug.Log("Player UI System setup complete. Add PlayerHUD to the scene or it spawns with the Player.");
    }

    // --- 1. Placeholder sprites (single white + a few type colors) ---
    private static void CreatePlaceholderSprites()
    {
        CreateColorTexture(ICONS_DIR + "/White.png", Color.white);
        CreateColorTexture(ICONS_DIR + "/Gun.png", new Color(0.9f, 0.5f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Pistol.png", new Color(0.9f, 0.8f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Melee.png", new Color(0.5f, 0.35f, 0.2f));
        CreateColorTexture(ICONS_DIR + "/Grenade.png", new Color(0.3f, 0.7f, 0.3f));
        CreateColorTexture(ICONS_DIR + "/Ammo.png", new Color(0.9f, 0.85f, 0.3f));
        CreateColorTexture(ICONS_DIR + "/Med.png", new Color(0.3f, 0.8f, 0.6f));
    }

    private static void CreateColorTexture(string path, Color color)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) return;
        Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color[] px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    // --- 2. Starter ItemData assets (spare pistol + ammo box + med) ---
    private static void CreateStarterItemData()
    {
        Weapon_Data pistolData = FindWeaponData("Weapon_Pistol_D");
        CreateItemData(INVENTORY_DATA_DIR + "/Item_Pistol.asset",
            "pistol_spare", "Pistol (spare)", ItemType.Weapon, pistolData);

        Weapon_Data rifleData = FindWeaponData("Weapon_Rifle_D");
        CreateItemData(INVENTORY_DATA_DIR + "/Item_RifleAmmo.asset",
            "rifle_ammo", "Rifle Ammo", ItemType.Ammo, rifleData != null ? rifleData : pistolData);

        CreateItemData(INVENTORY_DATA_DIR + "/Item_MedKit.asset",
            "medkit", "Med Kit", ItemType.Consumable, null);
    }

    private static ItemData CreateItemData(string path, string id, string name, ItemType type, Weapon_Data wd)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (existing != null) return existing;
        var asset = ScriptableObject.CreateInstance<ItemData>();
        asset.itemId = id;
        asset.itemName = name;
        asset.itemType = type;
        asset.maxStack = type == ItemType.Ammo ? 100 : 1;
        if (type == ItemType.Weapon) asset.weaponData = wd;
        if (type == ItemType.Ammo) { asset.ammoForWeaponType = wd != null ? wd.weaponType : WeaponType.Rifle; asset.ammoAmount = 60; }
        if (type == ItemType.Consumable) asset.healAmount = 25;
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static Weapon_Data FindWeaponData(string assetFileName)
    {
        string[] guids = AssetDatabase.FindAssets("t:Weapon_Data");
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith("/" + assetFileName + ".asset") || p.EndsWith(assetFileName + ".asset"))
                return AssetDatabase.LoadAssetAtPath<Weapon_Data>(p);
        }
        return null;
    }

    // --- 3. HUD prefab: a Canvas with UI_HudRoot (panel prefab is built at runtime by UI_InventoryPanel) ---
    private static GameObject CreateHudPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HUD_PREFAB_PATH);
        if (existing != null) return existing;

        GameObject root = new GameObject("PlayerHUD");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<UI_HudRoot>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, HUD_PREFAB_PATH);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // --- 3b. Panel prefab: a bare RectTransform + UI_InventoryPanel root ---
    private static GameObject CreatePanelPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PANEL_PREFAB_PATH);
        if (existing != null) return existing;

        GameObject root = new GameObject("PlayerInventoryPanel");
        root.AddComponent<RectTransform>();
        root.AddComponent<UI_InventoryPanel>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PANEL_PREFAB_PATH);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // --- 4. Patch Player.prefab: add PlayerInventory (if missing), wire prefab refs,
    //         assign starter backpack items, instantiate HUD prefab as child ---
    private static void PatchPlayerPrefab(GameObject hudPrefab, GameObject panelPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PLAYER_PREFAB_PATH);
        try
        {
            // Add PlayerInventory if missing
            var inv = root.GetComponent<PlayerInventory>();
            if (inv == null) inv = root.AddComponent<PlayerInventory>();

            // Assign starter backpack items
            var starters = new System.Collections.Generic.List<ItemData>
            {
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_Pistol.asset"),
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_RifleAmmo.asset"),
                AssetDatabase.LoadAssetAtPath<ItemData>(INVENTORY_DATA_DIR + "/Item_MedKit.asset"),
            };
            var so = new SerializedObject(inv);
            var prop = so.FindProperty("starterBackpackItems");
            prop.ClearArray();
            for (int i = 0; i < starters.Count; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = starters[i];
            }

            // Assign hudPrefab + panelPrefab references on PlayerInventory (same SerializedObject).
            so.FindProperty("hudPrefab").objectReferenceValue = hudPrefab;
            so.FindProperty("panelPrefab").objectReferenceValue = panelPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Add HUD child if missing
            Transform existingHud = root.transform.Find("PlayerHUD_Canvas");
            if (existingHud == null)
            {
                // Instantiate the HUD prefab as a child. UI_HudRoot.Build runs at runtime via PlayerInventory.
                var hudInstance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
                hudInstance.transform.SetParent(root.transform, false);
                hudInstance.name = "PlayerHUD_Canvas";
                hudInstance.SetActive(false); // enabled on owner by PlayerInventory
            }

            PrefabUtility.SaveAsPrefabAsset(root, PLAYER_PREFAB_PATH);
            Debug.Log("Player.prefab patched with PlayerInventory + HUD canvas.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureDir(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
