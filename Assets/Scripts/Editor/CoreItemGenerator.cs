using UnityEngine;
using UnityEditor;

public class CoreItemGenerator
{
    [MenuItem("Tools/Generate Core Item")]
    public static void GenerateCoreItem()
    {
        string path = "Assets/Data/Items/core.asset"; // Ensure directory exists or we might get error
        
        // Ensure directory
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder("Assets/Data/Items"))
            AssetDatabase.CreateFolder("Assets/Data", "Items");

        ItemData existingItem = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (existingItem == null)
        {
            ItemData coreItem = ScriptableObject.CreateInstance<ItemData>();
            coreItem.itemId = "core";
            coreItem.itemName = "Core (Extraction)";
            coreItem.itemType = ItemType.Consumable; // Just something non-weapon
            coreItem.maxStack = 1;

            AssetDatabase.CreateAsset(coreItem, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created Core Item at {path}. Please add it to your ItemRegistry manually!");
        }
        else
        {
            Debug.Log("Core Item already exists at " + path);
        }
    }
}
