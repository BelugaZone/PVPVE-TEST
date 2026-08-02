using UnityEditor;
using UnityEngine;
using System.IO;

public class ItemRegistrySetup : Editor
{
    [MenuItem("Tools/Inventory/Update Item Registry")]
    public static void UpdateRegistry()
    {
        string registryPath = "Assets/Data/ItemRegistry.asset";
        
        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }

        ItemRegistry registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(registryPath);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<ItemRegistry>();
            AssetDatabase.CreateAsset(registry, registryPath);
        }

        registry.allItems.Clear();
        registry.allWeapons.Clear();

        // Find all ItemData
        string[] itemGuids = AssetDatabase.FindAssets("t:ItemData");
        foreach (string guid in itemGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null) registry.allItems.Add(item);
        }

        // Find all Weapon_Data
        string[] weaponGuids = AssetDatabase.FindAssets("t:Weapon_Data");
        foreach (string guid in weaponGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Weapon_Data weapon = AssetDatabase.LoadAssetAtPath<Weapon_Data>(path);
            if (weapon != null) registry.allWeapons.Add(weapon);
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"[ItemRegistry] Updated successfully. Found {registry.allItems.Count} items and {registry.allWeapons.Count} weapons. Located at {registryPath}");
    }
}
