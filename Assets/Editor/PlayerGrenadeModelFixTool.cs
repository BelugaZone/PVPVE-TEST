using UnityEngine;
using UnityEditor;

// Rebuilds the player grenade models from Grenade_LOW.fbx as a WHOLE instance
// (preserving the FBX's internal hierarchy so the multi-part mesh keeps its
// correct relative transforms), computes a correct uniform scale from real mesh
// bounds (replacing the guessed 8/50), recreates the GunPoint, and adds a
// TrailRenderer to the projectile (mirroring Enemy's_Grenade).
//
// Run once via menu: Tools > Fix Player Grenade Models
public class PlayerGrenadeModelFixTool : EditorWindow
{
    private const string FBX_PATH = "Assets/Models/grenade/grenade/Grenade_LOW.fbx";
    private const string PLAYER_PREFAB_PATH = "Assets/Prefab/Player.prefab";
    private const string PROJECTILE_PREFAB_PATH = "Assets/Prefab/Player_Grenade.prefab";

    // Target world-space size (max dimension) of the grenade. ~0.2m. Tweak if the
    // grenade looks too small/large after running.
    private const float TARGET_GRENADE_SIZE = 0.2f;

    // Trail material reused from the enemy grenade (generic trail look).
    private const string TRAIL_MATERIAL_GUID = "001ea9fd989d94448be0f48ef3dac888";

    [MenuItem("Tools/Fix Player Grenade Models")]
    public static void FixModels()
    {
        GameObject fbxRoot = AssetDatabase.LoadMainAssetAtPath(FBX_PATH) as GameObject;
        if (fbxRoot == null)
        {
            Debug.LogError($"Could not load grenade FBX at {FBX_PATH}");
            return;
        }

        Material trailMat = AssetDatabase.LoadAssetAtPath<Material>(
            AssetDatabase.GUIDToAssetPath(TRAIL_MATERIAL_GUID));
        if (trailMat == null)
            Debug.LogWarning("Trail material not found; TrailRenderer will have no material.");

        FixEquippedModel(fbxRoot);
        FixProjectilePrefab(fbxRoot, trailMat);

        AssetDatabase.SaveAssets();
        Debug.Log("Player grenade model rebuild complete.");
    }

    // --- Equipped Grenade_Model on Player.prefab ---
    private static void FixEquippedModel(GameObject fbxRoot)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PLAYER_PREFAB_PATH);
        try
        {
            Transform grenadeModel = FindDeep(root.transform, "Grenade_Model");
            if (grenadeModel == null)
            {
                Debug.LogError("Grenade_Model not found on Player.prefab");
                return;
            }

            WeaponModel wm = grenadeModel.GetComponent<WeaponModel>();

            // Remove the broken hand-placed mesh children (all collapsed at origin).
            // Keep none — we rebuild GunPoint fresh below.
            for (int i = grenadeModel.childCount - 1; i >= 0; i--)
                DestroyImmediate(grenadeModel.GetChild(i).gameObject);

            // Add the FBX as a whole instance → internal hierarchy/transforms preserved.
            // Measure bounds BEFORE parenting so the parent's old scale doesn't
            // contaminate the measurement.
            GameObject fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot);
            Bounds bounds = ComputeRendererBounds(fbxInstance);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0.0001f) maxDim = 1f;
            float scale = TARGET_GRENADE_SIZE / maxDim;

            fbxInstance.transform.SetParent(grenadeModel, false);
            fbxInstance.transform.localPosition = Vector3.zero;
            grenadeModel.localScale = Vector3.one * scale;

            // Recreate GunPoint and reassign on the WeaponModel.
            GameObject gunPoint = new GameObject("GunPoint");
            gunPoint.transform.SetParent(grenadeModel, false);
            gunPoint.transform.localPosition = Vector3.zero;
            if (wm != null)
            {
                wm.gunPoint = gunPoint.transform;
                wm.holdPoint = gunPoint.transform;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PLAYER_PREFAB_PATH);
            Debug.Log($"[GrenadeFix] Rebuilt equipped Grenade_Model: nativeSize={bounds.size} scale={scale}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // --- Projectile Player_Grenade.prefab ---
    private static void FixProjectilePrefab(GameObject fbxRoot, Material trailMat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PROJECTILE_PREFAB_PATH);
        try
        {
            // Replace the single-part mesh child with the full FBX instance.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.transform.GetChild(i).gameObject;
                // Preserve any non-mesh child if added later (none expected here).
                if (child.GetComponent<Enemy_Grenade>() != null) continue;
                DestroyImmediate(child);
            }

            GameObject fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot);
            // Measure bounds BEFORE parenting so the root's old scale (50) doesn't
            // contaminate the measurement.
            Bounds bounds = ComputeRendererBounds(fbxInstance);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0.0001f) maxDim = 1f;
            float scale = TARGET_GRENADE_SIZE / maxDim;

            fbxInstance.transform.SetParent(root.transform, false);
            fbxInstance.transform.localPosition = Vector3.zero;
            root.transform.localScale = Vector3.one * scale;

            // Resize the collider to match the grenade.
            SphereCollider sc = root.GetComponent<SphereCollider>();
            if (sc != null)
            {
                sc.center = bounds.center;
                sc.radius = maxDim * 0.5f;
            }

            EnsureTrailRenderer(root, trailMat);

            PrefabUtility.SaveAsPrefabAsset(root, PROJECTILE_PREFAB_PATH);
            Debug.Log($"[GrenadeFix] Rebuilt Player_Grenade projectile: nativeSize={bounds.size} scale={scale} (trail added)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureTrailRenderer(GameObject root, Material trailMat)
    {
        TrailRenderer tr = root.GetComponent<TrailRenderer>();
        if (tr == null) tr = root.AddComponent<TrailRenderer>();

        tr.enabled = true;
        tr.time = 0.25f;
        // Keep the grenade casting shadows as before; trail motion vectors off.
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        tr.widthMultiplier = 0.1f;
        // Taper from ~0.21 down to 0 over the trail length.
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.21091686f),
            new Keyframe(1f, 0f));
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        if (trailMat != null)
            tr.sharedMaterial = trailMat;
        // Smooth corners for a nicer trail.
        tr.numCornerVertices = 0;
        tr.numCapVertices = 0;
        tr.minVertexDistance = 0.1f;
        tr.autodestruct = false;
        tr.emitting = true;
    }

    private static Bounds ComputeRendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (var r in renderers)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
