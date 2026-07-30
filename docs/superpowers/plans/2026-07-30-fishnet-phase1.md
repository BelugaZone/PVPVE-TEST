# FishNet Phase 1: Multiplayer Roaming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert core Player scripts to FishNet's `NetworkBehaviour`, restrict inputs to local owners, and create a networked test scene.

**Architecture:** We will convert `Player`, `Player_Movement`, `Player_AimController`, and `Player_WeaponController` to `NetworkBehaviour`. We will restrict Unity's new Input System to only enable when `base.IsOwner == true`. We will provide a Unity Editor script to automatically duplicate the current scene, strip out the single-player elements, and inject FishNet's `NetworkManager`.

**Tech Stack:** Unity 2023, FishNet, C#

## Global Constraints

- Must not break the existing single-player scene. Create a new scene for multiplayer testing.
- Only modify scripts to use `NetworkBehaviour` in a way that respects `IsOwner`.

---

### Task 1: Convert Core Player Scripts to NetworkBehaviour

**Files:**
- Modify: `Assets/Scripts/Player/Player.cs`
- Modify: `Assets/Scripts/Player/Player_Movement.cs`
- Modify: `Assets/Scripts/Player/Player_AimController.cs`
- Modify: `Assets/Scripts/Player/Player_WeaponController.cs`

**Interfaces:**
- Consumes: FishNet API (`FishNet.Object.NetworkBehaviour`)
- Produces: Owner-restricted player inputs.

- [ ] **Step 1: Update Player.cs Base Class and Lifecycle**

Modify `Assets/Scripts/Player/Player.cs`:
1. Add `using FishNet.Object;` and `using Cinemachine;`
2. Change inheritance from `MonoBehaviour` to `NetworkBehaviour`.
3. Remove the existing `OnEnable()` method.
4. Add `OnStartClient()` to only enable controls and set the camera target if local player:
```csharp
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (base.IsOwner)
        {
            controls.Enable();
            var cam = FindObjectOfType<CinemachineVirtualCamera>();
            if (cam != null)
            {
                cam.Follow = transform;
            }
        }
    }
```
5. Modify `OnDisable()`:
```csharp
    private void OnDisable()
    {
        if (base.IsOwner && controls != null)
            controls.Disable();
    }
```

- [ ] **Step 2: Update Player_Movement.cs**
1. Add `using FishNet.Object;`
2. Change inheritance to `NetworkBehaviour`.
3. At the beginning of `Update()`, add:
```csharp
        if (!base.IsOwner) return;
```

- [ ] **Step 3: Update Player_AimController.cs**
1. Add `using FishNet.Object;`
2. Change inheritance to `NetworkBehaviour`.
3. At the beginning of `Update()`, add:
```csharp
        if (!base.IsOwner) return;
```

- [ ] **Step 4: Update Player_WeaponController.cs**
1. Add `using FishNet.Object;`
2. Change inheritance to `NetworkBehaviour`.
3. At the beginning of `Update()`, add:
```csharp
        if (!base.IsOwner) return;
```

- [ ] **Step 5: Commit Script Conversions**
```bash
git add Assets/Scripts/Player/Player*.cs
git commit -m "refactor: convert Player scripts to NetworkBehaviour and restrict input to owner"
```

---

### Task 2: Multiplayer Scene & Prefab Setup Automation

**Files:**
- Create: `Assets/Editor/FishNetMigrationSetup.cs`

**Interfaces:**
- Produces: An editor menu item `FishNet > Setup Phase 1 Scene` that creates the multiplayer scene and configures the Player prefab.

- [ ] **Step 1: Write the Editor Script**
Create `Assets/Editor/FishNetMigrationSetup.cs` with the following content to automate Unity operations:
```csharp
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Component.Transforming;
using FishNet.Component.Animating;

public class FishNetMigrationSetup : EditorWindow
{
    [MenuItem("FishNet/Setup Phase 1")]
    public static void SetupPhase1()
    {
        // 1. Add FishNet components to Player Prefab
        string playerPrefabPath = "Assets/Prefabs/Player.prefab"; // Adjust if path is different
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab != null)
        {
            if (playerPrefab.GetComponent<NetworkObject>() == null)
                playerPrefab.AddComponent<NetworkObject>();
                
            if (playerPrefab.GetComponent<NetworkTransform>() == null)
            {
                var nt = playerPrefab.AddComponent<NetworkTransform>();
                // In FishNet V4+, NetworkTransform defaults to Server-Authoritative.
                // We need to set it to Client-Authoritative for Phase 1.
                // (Note: manual tweak might be needed depending on FishNet version, but adding it is step 1)
            }
            
            if (playerPrefab.GetComponent<NetworkAnimator>() == null)
                playerPrefab.AddComponent<NetworkAnimator>();
                
            EditorUtility.SetDirty(playerPrefab);
            PrefabUtility.SavePrefabAsset(playerPrefab);
            Debug.Log("Added FishNet components to Player Prefab.");
        }
        else
        {
            Debug.LogError($"Could not find player prefab at {playerPrefabPath}. Please attach NetworkObject, NetworkTransform, and NetworkAnimator manually.");
        }

        Debug.Log("Phase 1 Script Setup Complete. Please duplicate your Main Scene, remove the local Player, and drag the FishNet NetworkManager prefab into the new scene to test.");
    }
}
```
*(Note: Because manipulating scenes directly in editor scripts without knowing the exact scene names is error-prone, this script handles the Prefab, and outputs clear instructions for the manual scene copy).*

- [ ] **Step 2: Commit Editor Script**
```bash
git add Assets/Editor/FishNetMigrationSetup.cs
git commit -m "tool: add FishNet migration setup editor script"
```
