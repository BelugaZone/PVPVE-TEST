using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class PlayerRollSetupTool : EditorWindow
{
    [MenuItem("Tools/Setup Player Roll Animation")]
    public static void SetupRoll()
    {
        string fbxPath = "Assets/Animations/Player/Falling To Roll.fbx";
        string controllerPath = "Assets/Animations/Animator Controllers/Player.controller";
        string newClipName = "Roll";

        // 1. Rename the Animation Clip in FBX
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                if (clips[0].name != newClipName)
                {
                    clips[0].name = newClipName;
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                    Debug.Log("Renamed animation clip to 'Roll'.");
                }
            }
            else
            {
                Debug.LogWarning("No default clip animations found in FBX. You might need to manually extract or assign it.");
            }
        }
        else
        {
            Debug.LogError($"Could not find FBX at {fbxPath}");
            return;
        }

        // 2. Load the Animation Clip
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        AnimationClip rollClip = null;
        foreach (var asset in allAssets)
        {
            if (asset is AnimationClip && !asset.name.StartsWith("__preview__"))
            {
                // Wait for the reimport to take effect, or just match by type
                // It should be named "Roll" now, or Mixamo.com if reimport failed, just take the first valid clip
                if (rollClip == null || asset.name == newClipName)
                {
                    rollClip = asset as AnimationClip;
                }
            }
        }

        if (rollClip == null)
        {
            Debug.LogError("Could not load AnimationClip from FBX.");
            return;
        }

        // 3. Load the Animator Controller
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError($"Could not find AnimatorController at {controllerPath}");
            return;
        }

        // 4. Add 'Roll' Trigger parameter
        bool hasParam = false;
        foreach (var param in controller.parameters)
        {
            if (param.name == "Roll")
            {
                hasParam = true;
                break;
            }
        }
        if (!hasParam)
        {
            controller.AddParameter("Roll", AnimatorControllerParameterType.Trigger);
            Debug.Log("Added 'Roll' parameter to Animator Controller.");
        }

        // 5. Add State and Transitions in Base Layer
        AnimatorStateMachine baseStateMachine = controller.layers[0].stateMachine;

        // Find or create 'Roll' state
        AnimatorState rollState = null;
        foreach (var childState in baseStateMachine.states)
        {
            if (childState.state.name == "Roll")
            {
                rollState = childState.state;
                break;
            }
        }

        if (rollState == null)
        {
            rollState = baseStateMachine.AddState("Roll");
            Debug.Log("Added 'Roll' state.");
        }
        
        rollState.motion = rollClip;

        // Find AnyState transition to Roll
        AnimatorStateTransition anyToRoll = null;
        foreach (var t in baseStateMachine.anyStateTransitions)
        {
            if (t.destinationState == rollState)
            {
                anyToRoll = t;
                break;
            }
        }

        if (anyToRoll == null)
        {
            anyToRoll = baseStateMachine.AddAnyStateTransition(rollState);
            anyToRoll.hasExitTime = false;
            anyToRoll.duration = 0.1f;
            anyToRoll.AddCondition(AnimatorConditionMode.If, 0, "Roll");
            Debug.Log("Added AnyState -> Roll transition.");
        }

        // Find Idle/Walk state to transition back
        AnimatorState idleState = null;
        foreach (var childState in baseStateMachine.states)
        {
            if (childState.state.name == "Idle/Walk")
            {
                idleState = childState.state;
                break;
            }
        }

        if (idleState != null)
        {
            AnimatorStateTransition rollToIdle = null;
            foreach (var t in rollState.transitions)
            {
                if (t.destinationState == idleState)
                {
                    rollToIdle = t;
                    break;
                }
            }

            if (rollToIdle == null)
            {
                rollToIdle = rollState.AddTransition(idleState);
                rollToIdle.hasExitTime = true;
                rollToIdle.exitTime = 0.85f; // Most of the roll animation finishes
                rollToIdle.duration = 0.15f;
                Debug.Log("Added Roll -> Idle/Walk transition.");
            }
        }
        else
        {
            Debug.LogWarning("Could not find 'Idle/Walk' state to transition back to.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Player Roll Setup Completed Successfully!");
    }
}
