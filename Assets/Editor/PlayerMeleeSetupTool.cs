using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class PlayerMeleeSetupTool : EditorWindow
{
    [MenuItem("Tools/Setup Player Melee")]
    public static void SetupPlayerMelee()
    {
        // 1. Create Weapon_Data
        Weapon_Data meleeData = AssetDatabase.LoadAssetAtPath<Weapon_Data>("Assets/Melee_Data.asset");
        if (meleeData == null)
        {
            meleeData = ScriptableObject.CreateInstance<Weapon_Data>();
            meleeData.weaponType = WeaponType.Melee;
            meleeData.bulletDamage = 50;
            meleeData.fireRate = 1.5f; // swing speed
            AssetDatabase.CreateAsset(meleeData, "Assets/Melee_Data.asset");
            AssetDatabase.SaveAssets();
        }

        // 2. Setup Animator Controller
        AnimatorController playerController = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Animator Controllers/Player.controller");
        AnimatorController enemyController = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Animator Controllers/Enemy_Melee.controller");

        if (playerController != null && enemyController != null)
        {
            // Add parameter Melee if it doesn't exist
            if (!playerController.parameters.Any(p => p.name == "Melee"))
            {
                playerController.AddParameter("Melee", AnimatorControllerParameterType.Trigger);
            }

            // Find enemy attack clip
            AnimationClip attackClip = null;
            var enemyBaseLayer = enemyController.layers[0];
            foreach (var state in enemyBaseLayer.stateMachine.states)
            {
                if (state.state.name == "Attack")
                {
                    if (state.state.motion is AnimationClip clip)
                    {
                        attackClip = clip;
                    }
                    else if (state.state.motion is BlendTree tree && tree.children.Length > 0)
                    {
                        attackClip = tree.children[0].motion as AnimationClip;
                    }
                    break;
                }
            }

            if (attackClip != null)
            {
                var playerBaseLayer = playerController.layers[0];
                
                // Find and delete existing Melee state to fix any corruption
                var states = playerBaseLayer.stateMachine.states;
                for (int i = 0; i < states.Length; i++)
                {
                    if (states[i].state.name == "Melee")
                    {
                        playerBaseLayer.stateMachine.RemoveState(states[i].state);
                        break;
                    }
                }

                // Create fresh Melee state
                AnimatorState meleeState = playerBaseLayer.stateMachine.AddState("Melee");
                meleeState.motion = attackClip;
                
                var transition = playerBaseLayer.stateMachine.AddAnyStateTransition(meleeState);
                transition.AddCondition(AnimatorConditionMode.If, 0, "Melee");
                transition.duration = 0.1f;
                
                // Add exit transition back to locomotion/idle
                var exitTransition = meleeState.AddExitTransition();
                exitTransition.hasExitTime = true;
                exitTransition.exitTime = 0.9f;
                exitTransition.duration = 0.1f;
            }

            // Extract Idle for Melee Hold Pose
            AnimationClip idleClip = null;
            foreach (var state in enemyBaseLayer.stateMachine.states)
            {
                if (state.state.name == "Idle")
                {
                    idleClip = state.state.motion as AnimationClip;
                    break;
                }
            }

            if (idleClip != null)
            {
                // Setup Melee Weapon Layer
                int meleeLayerIndex = -1;
                for (int i = 0; i < playerController.layers.Length; i++)
                {
                    if (playerController.layers[i].name == "Melee Weapon Layer")
                    {
                        meleeLayerIndex = i;
                        break;
                    }
                }

                if (meleeLayerIndex == -1)
                {
                    AnimatorControllerLayer meleeLayer = new AnimatorControllerLayer();
                    meleeLayer.name = "Melee Weapon Layer";
                    meleeLayer.defaultWeight = 1f;
                    
                    // Copy mask from Common Weapon Layer (index 1) if it exists
                    if (playerController.layers.Length > 1)
                    {
                        meleeLayer.avatarMask = playerController.layers[1].avatarMask;
                    }
                    
                    AnimatorStateMachine meleeStateMachine = new AnimatorStateMachine();
                    meleeStateMachine.name = meleeLayer.name;
                    meleeStateMachine.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(meleeStateMachine, playerController);
                    
                    meleeLayer.stateMachine = meleeStateMachine;
                    
                    playerController.AddLayer(meleeLayer);
                    meleeLayerIndex = playerController.layers.Length - 1;
                }

                var meleeLayerDef = playerController.layers[meleeLayerIndex];
                AnimatorState meleeIdleState = meleeLayerDef.stateMachine.states.FirstOrDefault(s => s.state.name == "Melee_Idle").state;
                if (meleeIdleState == null)
                {
                    meleeIdleState = meleeLayerDef.stateMachine.AddState("Melee_Idle");
                    meleeLayerDef.stateMachine.defaultState = meleeIdleState;
                }
                meleeIdleState.motion = idleClip;
            }

            // IMPORTANT: Save the changes to the Animator Controller!
            EditorUtility.SetDirty(playerController);
            AssetDatabase.SaveAssets();
        }

        // 3. Setup Player Prefab
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Enemy_Melee.prefab");

        if (playerPrefab != null && enemyPrefab != null)
        {
        GameObject instPlayer = null;
        GameObject instEnemy = null;
        try
        {
            instPlayer = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            instEnemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);

            WeaponModel[] allModels = instPlayer.GetComponentsInChildren<WeaponModel>(true);
            Transform playerWeaponModelsContainer = null;
            if (allModels.Length > 0)
            {
                playerWeaponModelsContainer = allModels[0].transform.parent;
            }

            if (playerWeaponModelsContainer != null)
            {
                // Find axe in enemy by name
                Transform axe = null;
                Transform[] allChildren = instEnemy.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child.name == "weap_enemy_axe")
                    {
                        axe = child;
                        break;
                    }
                }

                if (axe != null)
                {
                    // Check if player already has an axe
                    bool hasAxe = false;
                    foreach (Transform child in playerWeaponModelsContainer)
                    {
                        if (child.name == "Melee_Axe")
                            hasAxe = true;
                    }

                    if (!hasAxe)
                    {
                        GameObject cloneAxe = Instantiate(axe.gameObject, playerWeaponModelsContainer);
                        cloneAxe.name = "Melee_Axe";
                        
                        // Clean up enemy specific scripts if they accidentally came along
                        var oldScripts = cloneAxe.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach(var s in oldScripts) DestroyImmediate(s);
                        
                        // Add Player Weapon Model
                        WeaponModel wm = cloneAxe.AddComponent<WeaponModel>();
                        wm.weaponType = WeaponType.Melee;
                        wm.holdType = HoldType.MeleeHold;
                        wm.equipAnimationType = EquipType.SideEquipAnimation;
                        
                        GameObject holdPoint = new GameObject("HoldPoint");
                        holdPoint.transform.SetParent(cloneAxe.transform);
                        holdPoint.transform.localPosition = Vector3.zero;
                        wm.holdType = HoldType.CommonHold;
                        
                        wm.gunPoint = cloneAxe.transform;
                        wm.holdPoint = cloneAxe.transform;

                        // Add damage points
                        wm.attackRadius = 0.4f;
                        Transform p1 = new GameObject("DamagePoint_1").transform;
                        Transform p2 = new GameObject("DamagePoint_2").transform;
                        Transform p3 = new GameObject("DamagePoint_3").transform;
                        
                        p1.SetParent(cloneAxe.transform, false);
                        p2.SetParent(cloneAxe.transform, false);
                        p3.SetParent(cloneAxe.transform, false);
                        
                        p1.localPosition = new Vector3(0, 0.5f, 0);
                        p2.localPosition = new Vector3(0, 0.8f, 0);
                        p3.localPosition = new Vector3(0, 1.1f, 0);

                        wm.damagePoints = new Transform[] { p1, p2, p3 };
                        
                        cloneAxe.SetActive(false);
                        
                        // Assign defaultMeleeWeaponData in Player_WeaponController
                        Player_WeaponController pwc = instPlayer.GetComponent<Player_WeaponController>();
                        if (pwc != null)
                        {
                            SerializedObject so = new SerializedObject(pwc);
                            so.FindProperty("defaultMeleeWeaponData").objectReferenceValue = meleeData;
                            
                            // Try to find the generic hit fx from enemy to use for player
                            if (instEnemy != null)
                            {
                                Enemy_Melee enemyMelee = instEnemy.GetComponent<Enemy_Melee>();
                                if (enemyMelee != null)
                                {
                                    SerializedObject enemySo = new SerializedObject(enemyMelee);
                                    SerializedProperty fxProp = enemySo.FindProperty("meleeAttackFx");
                                    if (fxProp != null && fxProp.objectReferenceValue != null)
                                    {
                                        so.FindProperty("meleeAttackFx").objectReferenceValue = fxProp.objectReferenceValue;
                                    }
                                }
                            }
                            so.ApplyModifiedProperties();
                        }

                        PrefabUtility.SaveAsPrefabAsset(instPlayer, "Assets/Prefab/Player.prefab");
                        Debug.Log("Successfully added Melee_Axe with advanced Hit Detection to Player.prefab!");
                    }
                    else
                    {
                        Transform existingAxe = playerWeaponModelsContainer.Find("Melee_Axe");
                        if (existingAxe != null)
                        {
                            WeaponModel wm = existingAxe.GetComponent<WeaponModel>();
                            if (wm != null)
                            {
                                wm.holdType = HoldType.CommonHold; 
                                
                                // Re-setup damage points if they don't exist
                                if (wm.damagePoints == null || wm.damagePoints.Length == 0)
                                {
                                    wm.attackRadius = 0.4f;
                                    Transform p1 = new GameObject("DamagePoint_1").transform;
                                    Transform p2 = new GameObject("DamagePoint_2").transform;
                                    Transform p3 = new GameObject("DamagePoint_3").transform;
                                    
                                    p1.SetParent(existingAxe, false);
                                    p2.SetParent(existingAxe, false);
                                    p3.SetParent(existingAxe, false);
                                    
                                    p1.localPosition = new Vector3(0, 0.5f, 0);
                                    p2.localPosition = new Vector3(0, 0.8f, 0);
                                    p3.localPosition = new Vector3(0, 1.1f, 0);

                                    wm.damagePoints = new Transform[] { p1, p2, p3 };
                                }

                                Player_WeaponController pwc = instPlayer.GetComponent<Player_WeaponController>();
                                if (pwc != null)
                                {
                                    SerializedObject so = new SerializedObject(pwc);
                                    if (instEnemy != null)
                                    {
                                        Enemy_Melee enemyMelee = instEnemy.GetComponent<Enemy_Melee>();
                                        if (enemyMelee != null)
                                        {
                                            SerializedObject enemySo = new SerializedObject(enemyMelee);
                                            SerializedProperty fxProp = enemySo.FindProperty("meleeAttackFx");
                                            if (fxProp != null && fxProp.objectReferenceValue != null)
                                            {
                                                so.FindProperty("meleeAttackFx").objectReferenceValue = fxProp.objectReferenceValue;
                                            }
                                        }
                                    }
                                    so.ApplyModifiedProperties();
                                }

                                PrefabUtility.SaveAsPrefabAsset(instPlayer, "Assets/Prefab/Player.prefab");
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            if (instPlayer != null) DestroyImmediate(instPlayer);
            if (instEnemy != null) DestroyImmediate(instEnemy);
        }
        }
        
        AssetDatabase.Refresh();
        Debug.Log("Player Melee Setup Complete!");
    }
}
