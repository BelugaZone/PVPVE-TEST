#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishNet.Object;

public static class RoomSetupTool
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string LobbyPlayerPrefabPath = "Assets/Prefab/LobbyPlayer.prefab";

    [MenuItem("Tools/Lobby Setup/Build Room Assets")]
    public static void BuildRoomAssets()
    {
        // 1. Add RoomManager to the Lobby scene (if absent).
        var lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
        if (Object.FindObjectOfType<RoomManager>(true) == null)
        {
            var go = new GameObject("RoomManager");
            go.AddComponent<NetworkObject>();
            go.AddComponent<RoomManager>();
            EditorSceneManager.MarkSceneDirty(lobbyScene);
            Debug.Log("[RoomSetup] Added RoomManager to Lobby scene.");
        }
        else
        {
            Debug.Log("[RoomSetup] RoomManager already present in Lobby scene.");
        }
        EditorSceneManager.SaveScene(lobbyScene);

        // 2. Add a RoomPanel child (UI_RoomPanel) to the LobbyPlayer prefab (if absent).
        var root = PrefabUtility.LoadPrefabContents(LobbyPlayerPrefabPath);
        try
        {
            bool found = false;
            foreach (Transform child in root.transform)
                if (child.name == "RoomPanel") { found = true; break; }
            if (!found)
            {
                var panelGo = new GameObject("RoomPanel");
                panelGo.transform.SetParent(root.transform, false);
                panelGo.AddComponent<UI_RoomPanel>();
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPlayerPrefabPath);
                Debug.Log("[RoomSetup] Added RoomPanel child to LobbyPlayer prefab.");
            }
            else
            {
                Debug.Log("[RoomSetup] RoomPanel already present on LobbyPlayer prefab.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[RoomSetup] Done.");
    }
}
#endif
