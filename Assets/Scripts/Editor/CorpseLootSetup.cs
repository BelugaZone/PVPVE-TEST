using UnityEngine;
using UnityEditor;

/// <summary>
/// One-shot Editor utility that:
///  1. Adds Enemy_LootContainer to Enemy_Range.prefab  (drops shotgun)
///  2. Adds Enemy_LootContainer to Enemy_Melee.prefab  (drops grenade)
///
/// Run via   Tools > Setup Corpse Loot
/// The script wires up the ItemRegistry reference automatically by finding the
/// first ItemRegistry asset in the project.
/// </summary>
public static class CorpseLootSetup
{
    private const string RangePrefabPath = "Assets/Prefab/Enemy_Range.prefab";
    private const string MeleePrefabPath = "Assets/Prefab/Enemy_Melee.prefab";
    private const string RegistryFilter  = "t:ItemRegistry";

    [MenuItem("Tools/Setup Corpse Loot")]
    public static void Run()
    {
        // ── Find ItemRegistry asset ──────────────────────────────────────
        string[] guids = AssetDatabase.FindAssets(RegistryFilter);
        if (guids.Length == 0)
        {
            Debug.LogError("[CorpseLootSetup] No ItemRegistry asset found in project.");
            return;
        }
        var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(
                            AssetDatabase.GUIDToAssetPath(guids[0]));
        Debug.Log($"[CorpseLootSetup] Using registry: {AssetDatabase.GetAssetPath(registry)}");

        // ── Patch Enemy_Range ────────────────────────────────────────────
        SetupPrefab(
            RangePrefabPath,
            registry,
            new Enemy_LootContainer.LootEntry[]
            {
                new Enemy_LootContainer.LootEntry
                {
                    itemId     = "Shotgun G7",     // Weapon_Data.weaponName for player shotgun
                    count      = 1,
                    dropChance = 1.0f              // always drops
                }
            },
            triggerRadius: 0.8f);

        // ── Patch Enemy_Melee ─────────────────────────────────────────────
        SetupPrefab(
            MeleePrefabPath,
            registry,
            new Enemy_LootContainer.LootEntry[]
            {
                new Enemy_LootContainer.LootEntry
                {
                    itemId     = "Grenade",        // GrenadeWeaponData.weaponName
                    count      = 1,
                    dropChance = 1.0f
                }
            },
            triggerRadius: 0.8f);

        Debug.Log("[CorpseLootSetup] Done! Both enemy prefabs now have Enemy_LootContainer.");
    }

    private static void SetupPrefab(
        string prefabPath,
        ItemRegistry registry,
        Enemy_LootContainer.LootEntry[] loot,
        float triggerRadius)
    {
        // Load & open prefab for editing
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError($"[CorpseLootSetup] Prefab not found: {prefabPath}");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var root = scope.prefabContentsRoot;

            // Remove any existing Enemy_LootContainer to avoid duplicates
            var existing = root.GetComponent<Enemy_LootContainer>();
            if (existing != null)
                Object.DestroyImmediate(existing);

            // Remove any existing trigger collider added by us previously.
            // We only remove SphereColliders — don't touch capsules/boxes used by AI.
            var existingSphere = root.GetComponent<SphereCollider>();
            if (existingSphere != null && existingSphere.isTrigger)
                Object.DestroyImmediate(existingSphere);

            // Add the SphereCollider trigger (required by Enemy_LootContainer)
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius    = triggerRadius;
            col.enabled   = false;  // will be enabled in EnableLootTrigger()

            // Add & configure the loot container
            var container = root.AddComponent<Enemy_LootContainer>();

            // Use serialized property to write the private [SerializeField] fields
            var so = new SerializedObject(container);

            var propLoot = so.FindProperty("possibleLoot");
            propLoot.arraySize = loot.Length;
            for (int i = 0; i < loot.Length; i++)
            {
                var elem = propLoot.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("itemId").stringValue     = loot[i].itemId;
                elem.FindPropertyRelative("count").intValue         = loot[i].count;
                elem.FindPropertyRelative("dropChance").floatValue  = loot[i].dropChance;
            }

            var propReg = so.FindProperty("itemRegistry");
            propReg.objectReferenceValue = registry;

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[CorpseLootSetup] Patched: {prefabPath}");
        }
    }
}
