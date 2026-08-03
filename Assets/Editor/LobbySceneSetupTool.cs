#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class LobbySceneSetupTool
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string GameScenePath = "Assets/Scenes/Scene_multi.unity";
    private const string LobbySceneName = "Lobby";

    [MenuItem("Tools/Lobby Setup/Build Lobby Scene")]
    public static void BuildLobbyScene()
    {
        // --- 1. Open Scene_multi (the current Game scene) to harvest the NetworkManager ---
        if (!System.IO.File.Exists(GameScenePath))
        {
            Debug.LogError($"[LobbySetup] Game scene not found: {GameScenePath}");
            return;
        }
        var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        // --- 2. Strip demo HUD (NetworkHudCanvas) and old LoginCanvas from Scene_multi ---
        StripByName(gameScene, "NetworkHudCanvas");
        StripByName(gameScene, "LoginCanvas");

        // --- 3. Create the Lobby scene additively ---
        Scene lobbyScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.MarkSceneDirty(lobbyScene);

        // --- 4. Move NetworkManager (+ children) into Lobby ---
        GameObject nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null)
        {
            Debug.LogError("[LobbySetup] No NetworkManager in Scene_multi. Aborting.");
            // Close the empty lobby scene we just created to leave things clean.
            EditorSceneManager.CloseScene(lobbyScene, true);
            return;
        }
        SceneManager.MoveGameObjectToScene(nmGo, lobbyScene);

        // --- 5. Move ServerDataManager (its own GameObject + NetworkObject) into Lobby ---
        // ServerDataManager is a NetworkBehaviour with its own NetworkObject; it must NOT be
        // added to the NetworkManager GameObject (FishNet forbids a NetworkObject on the NM GO).
        // Moving it intact into the Lobby scene preserves its original working setup. Cross-scene
        // persistence is a Phase 3 concern (scene transition), not Phase 0 (single Lobby scene).
        GameObject sdmGo = GameObject.Find("ServerDataManager");
        ServerDataManager sdm = null;
        if (sdmGo != null)
        {
            sdm = sdmGo.GetComponent<ServerDataManager>();
            SceneManager.MoveGameObjectToScene(sdmGo, lobbyScene);
        }
        else
        {
            Debug.LogWarning("[LobbySetup] No GameObject named 'ServerDataManager' in Scene_multi. playerPrefab/lobbySpawnPoint bindings skipped.");
        }

        // --- 6. Add a LobbySpawnPoint marker ---
        var spawnGo = new GameObject("LobbySpawnPoint");
        spawnGo.transform.position = new Vector3(0, 0, 0);
        SceneManager.MoveGameObjectToScene(spawnGo, lobbyScene);

        // Bind lobbySpawnPoint + lobbySceneName on ServerDataManager.
        if (sdm != null)
        {
            sdm.lobbySpawnPoint = spawnGo.transform;
            sdm.lobbySceneName = LobbySceneName;
            EditorUtility.SetDirty(sdm);
        }

        // --- 7. Add UI_StartScreen canvas + EventSystem to Lobby ---
        var uiGo = new GameObject("UI_StartScreen");
        SceneManager.MoveGameObjectToScene(uiGo, lobbyScene);
        uiGo.AddComponent<UI_StartScreen>();

        var esGo = new GameObject("EventSystem");
        SceneManager.MoveGameObjectToScene(esGo, lobbyScene);
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();

        // --- 8. Save the Lobby scene ---
        EditorSceneManager.SaveScene(lobbyScene, LobbyScenePath);

        // --- 9. Close Lobby, save the stripped Game scene ---
        EditorSceneManager.CloseScene(lobbyScene, false);
        EditorSceneManager.SaveScene(gameScene);

        // --- 10. Build Settings: Lobby at index 0, Game at index 1 ---
        EnsureInBuildSettings(LobbyScenePath, true);
        EnsureInBuildSettings(GameScenePath, true);
        ReorderBuildScenes();

        Debug.Log("[LobbySetup] Done. Lobby scene created at index 0, Game (Scene_multi) at index 1.");
    }

    private static void StripByName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                Object.DestroyImmediate(root);
                return;
            }
            var t = root.transform.Find(name);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
                return;
            }
        }
    }

    private static void EnsureInBuildSettings(string path, bool enabled)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int idx = scenes.FindIndex(s => s.path == path);
        if (idx < 0)
        {
            scenes.Add(new EditorBuildSettingsScene(path, enabled));
        }
        else
        {
            scenes[idx].enabled = enabled;
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ReorderBuildScenes()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int lobbyIdx = scenes.FindIndex(s => s.path == LobbyScenePath);
        if (lobbyIdx <= 0) return; // already first or absent
        var lobby = scenes[lobbyIdx];
        scenes.RemoveAt(lobbyIdx);
        scenes.Insert(0, lobby);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
