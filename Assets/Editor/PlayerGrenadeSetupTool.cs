using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class PlayerGrenadeSetupTool : EditorWindow
{
    private const string FBX_PATH = "Assets/Animations/Player/Grenade Throw.fbx";
    private const string CONTROLLER_PATH = "Assets/Animations/Animator Controllers/Player.controller";
    private const string UPPERBODY_MASK_GUID = "8e55b3dbd2cfff24d8411cf9932517dd";
    private const string CLIP_NAME = "GrenadeThrow";
    private const string IDLE_STATE = "Grenade_Idle";
    private const string THROW_STATE = "Grenade_Throw";
    private const string LAYER_NAME = "Grenade Weapon Layer";
    private const string TRIGGER_PARAM = "ThrowGrenade";

    [MenuItem("Tools/Setup Player Grenade Animation")]
    public static void SetupGrenade()
    {
        // 1. Define the clip on the FBX (rename default take to GrenadeThrow)
        ModelImporter importer = AssetImporter.GetAtPath(FBX_PATH) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Could not find FBX at {FBX_PATH}");
            return;
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            // Create a clip definition from the default take
            clips = new ModelImporterClipAnimation[]
            {
                new ModelImporterClipAnimation
                {
                    name = CLIP_NAME,
                    takeName = importer.clipAnimations.Length > 0 ? importer.clipAnimations[0].takeName : (importer.defaultClipAnimations.Length > 0 ? importer.defaultClipAnimations[0].takeName : "")
                }
            };
        }
        else if (clips[0].name != CLIP_NAME)
        {
            clips[0].name = CLIP_NAME;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        // 2. Load the AnimationClip from the FBX
        AnimationClip throwClip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(FBX_PATH))
        {
            if (asset is AnimationClip c && !asset.name.StartsWith("__preview__"))
            {
                if (throwClip == null || asset.name == CLIP_NAME)
                    throwClip = c;
            }
        }
        if (throwClip == null)
        {
            Debug.LogError("Could not load GrenadeThrow AnimationClip from FBX.");
            return;
        }

        // 3. Add the ThrowGrenade trigger parameter
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
        if (controller == null)
        {
            Debug.LogError($"Could not find AnimatorController at {CONTROLLER_PATH}");
            return;
        }
        if (!controller.parameters.Any(p => p.name == TRIGGER_PARAM))
            controller.AddParameter(TRIGGER_PARAM, AnimatorControllerParameterType.Trigger);

        // 4. Add layer 5 "Grenade Weapon Layer" with UpperBody mask
        AvatarMask upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            AssetDatabase.GUIDToAssetPath(UPPERBODY_MASK_GUID));
        if (upperBodyMask == null)
            Debug.LogWarning("UpperBody mask not found; layer will have no mask.");

        AnimatorControllerLayer grenadeLayer = controller.layers.FirstOrDefault(l => l.name == LAYER_NAME);
        if (grenadeLayer == null)
        {
            grenadeLayer = new AnimatorControllerLayer
            {
                name = LAYER_NAME,
                defaultWeight = 0f,
                syncedLayerIndex = -1,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = upperBodyMask
            };

            var layerList = controller.layers.ToList();
            layerList.Add(grenadeLayer);
            controller.layers = layerList.ToArray();
            Debug.Log("Added 'Grenade Weapon Layer' (index 5).");
        }

        // A layer's stateMachine can be null both for newly created layers and for
        // layers left over from a previous failed run. Create + embed one if missing.
        AnimatorStateMachine sm = grenadeLayer.stateMachine;
        if (sm == null)
        {
            sm = new AnimatorStateMachine();
            sm.name = grenadeLayer.name;
            sm.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(sm, controller);
            grenadeLayer.stateMachine = sm;
            EditorUtility.SetDirty(controller);
        }

        // 5. Find the melee idle clip to reuse for Grenade_Idle.
        // Load it from the Base Layer's Melee_Idle state motion (that state already
        // references the melee hold clip the player uses when holding a melee weapon).
        AnimatorStateMachine baseSm = controller.layers[0].stateMachine;
        AnimatorState meleeIdleState = null;
        foreach (var cs in baseSm.states)
        {
            if (cs.state.name == "Melee_Idle") { meleeIdleState = cs.state; break; }
        }
        AnimationClip meleeIdleClip = null;
        if (meleeIdleState != null && meleeIdleState.motion is AnimationClip mc)
            meleeIdleClip = mc;

        // 6. Create / find states
        AnimatorState idleState = null, throwState = null;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == IDLE_STATE) idleState = cs.state;
            if (cs.state.name == THROW_STATE) throwState = cs.state;
        }
        if (idleState == null) idleState = sm.AddState(IDLE_STATE);
        if (throwState == null) throwState = sm.AddState(THROW_STATE);
        idleState.motion = meleeIdleClip != null ? meleeIdleClip : throwClip; // reuse melee hold; fallback to throw clip
        throwState.motion = throwClip;

        // 7. Transitions: Idle --ThrowGrenade--> Throw --exitTime 0.9--> Idle
        AnimatorStateTransition idleToThrow = idleState.transitions.FirstOrDefault(t => t.destinationState == throwState);
        if (idleToThrow == null)
        {
            idleToThrow = idleState.AddTransition(throwState);
            idleToThrow.hasExitTime = false;
            idleToThrow.duration = 0.1f;
            idleToThrow.AddCondition(AnimatorConditionMode.If, 0, TRIGGER_PARAM);
        }
        AnimatorStateTransition throwToIdle = throwState.transitions.FirstOrDefault(t => t.destinationState == idleState);
        if (throwToIdle == null)
        {
            throwToIdle = throwState.AddTransition(idleState);
            throwToIdle.hasExitTime = true;
            throwToIdle.exitTime = 0.9f;
            throwToIdle.duration = 0.15f;
        }

        // 8. Animation event on the throw clip at ~55% length -> ThrowGrenadeTrigger
        var events = AnimationUtility.GetAnimationEvents(throwClip);
        if (events == null || events.Length == 0 || System.Array.Find(events, e => e.functionName == "ThrowGrenadeTrigger") == null)
        {
            var evt = new AnimationEvent
            {
                functionName = "ThrowGrenadeTrigger",
                time = throwClip.length * 0.55f,
                floatParameter = 0f,
                intParameter = 0,
                stringParameter = "",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.DontRequireReceiver
            };
            var newEvents = new AnimationEvent[events == null ? 1 : events.Length + 1];
            if (events != null) System.Array.Copy(events, newEvents, events.Length);
            newEvents[newEvents.Length - 1] = evt;
            AnimationUtility.SetAnimationEvents(throwClip, newEvents);
            // Mark clip dirty so the event persists on the imported sub-asset
            EditorUtility.SetDirty(throwClip);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Player Grenade Animation Setup Completed. Run this once. Then verify layer index == 5 in the Animator window.");
    }
}
