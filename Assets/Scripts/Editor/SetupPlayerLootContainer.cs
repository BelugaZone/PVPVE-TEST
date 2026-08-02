using UnityEditor;
using UnityEngine;

public class SetupPlayerLootContainer : Editor
{
    [MenuItem("Tools/Setup Player Loot")]
    public static void SetupPlayerLoot()
    {
        string prefabPath = "Assets/Prefab/Player.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("Could not find Player prefab at " + prefabPath);
            return;
        }

        // We use PrefabUtility to edit the prefab
        using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject playerObj = editingScope.prefabContentsRoot;

            PlayerInventory inv = playerObj.GetComponent<PlayerInventory>();
            if (inv == null)
            {
                Debug.LogError("Player prefab is missing PlayerInventory!");
                return;
            }

            // Check if it already has a LootTrigger
            Transform existingLoot = playerObj.transform.Find("LootTrigger");
            if (existingLoot != null)
            {
                Debug.Log("LootTrigger already exists on Player prefab.");
                return;
            }

            // Create a child object for the trigger
            GameObject triggerObj = new GameObject("LootTrigger");
            triggerObj.transform.SetParent(playerObj.transform);
            triggerObj.transform.localPosition = new Vector3(0, 0.5f, 0); // Roughly center of body
            
            // Add BoxCollider for trigger
            BoxCollider box = triggerObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 1.5f, 1.5f);
            box.enabled = false;

            // Add Enemy_LootContainer
            Enemy_LootContainer container = triggerObj.AddComponent<Enemy_LootContainer>();

            // The user must ensure itemRegistry is assigned. We can try to copy it from PlayerInventory.
            SerializedObject invSO = new SerializedObject(inv);
            SerializedProperty itemRegProp = invSO.FindProperty("itemRegistry");

            SerializedObject containerSO = new SerializedObject(container);
            containerSO.FindProperty("itemRegistry").objectReferenceValue = itemRegProp.objectReferenceValue;
            containerSO.ApplyModifiedProperties();

            Debug.Log("Successfully added LootTrigger and Enemy_LootContainer to Player prefab!");
        }
    }
}
