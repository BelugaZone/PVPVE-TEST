using UnityEngine;
using UnityEditor;

public class FixEnemyMeleeIdleTime : EditorWindow
{
    [MenuItem("Tools/Fix Enemy Melee Idle Time")]
    public static void FixIdleTime()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab != null)
            {
                Enemy_Melee meleeEnemy = prefab.GetComponent<Enemy_Melee>();
                if (meleeEnemy != null)
                {
                    if (meleeEnemy.idleTime > 10f) // Threshold to catch unreasonable times like 60
                    {
                        float oldTime = meleeEnemy.idleTime;
                        meleeEnemy.idleTime = 3f;
                        EditorUtility.SetDirty(prefab);
                        count++;
                        Debug.Log($"Fixed idleTime for {prefab.name} at {path} from {oldTime} to {meleeEnemy.idleTime}");
                    }
                }
            }
        }

        if (count > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"Successfully fixed {count} Enemy_Melee prefab(s).");
        }
        else
        {
            Debug.Log("No Enemy_Melee prefabs needed fixing.");
        }
    }
}
