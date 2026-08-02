using UnityEngine;

public class Player_GrenadeAimVisual : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Player player;
    [SerializeField] private Transform gunPoint; // fallback if null: player.weapon.GunPoint()

    [Header("Trajectory")]
    [SerializeField] private float timeToTarget = 1.2f;
    [SerializeField] private int sampleCount = 20;
    [SerializeField] private float maxThrowDistance = 25f;
    [SerializeField] private float dotScale = 0.08f;

    [Header("Visuals")]
    [SerializeField] private GameObject dotPrefab;       // small quad/sprite; auto-created if null
    [SerializeField] private GameObject landingRingPrefab; // flat ring; auto-created if null

    private bool visualsReady;
    private GameObject[] dots;
    private GameObject landingRing;
    private bool lastShouldShow;

    private void Awake()
    {
        if (player == null) player = GetComponent<Player>();
    }

    private void OnEnable()
    {
        // Note: IsOwner is not known yet at OnEnable time (FishNet sets it in
        // OnStartClient). Do NOT disable here — Update() already early-returns
        // for non-owners. Disabling here would permanently kill the component
        // before IsOwner is set.
    }

    private void EnsureVisuals()
    {
        if (visualsReady) return;
        // Lightweight dots: small unlit spheres, no collider, no shadow casting.
        // (Industry practice for trajectory previews: no physics, no lighting
        //  contribution — purely visual markers.)
        if (dotPrefab == null)
        {
            dotPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dotPrefab.GetComponent<Collider>());
            var dotMr = dotPrefab.GetComponent<UnityEngine.MeshRenderer>();
            var unlitShader = UnityEngine.Shader.Find("Unlit/Color");
            if (unlitShader != null)
            {
                var dotMat = new UnityEngine.Material(unlitShader);
                dotMat.color = UnityEngine.Color.yellow;
                dotMr.sharedMaterial = dotMat;
            }
            dotMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dotMr.receiveShadows = false;
            dotPrefab.transform.localScale = Vector3.one * dotScale;
            dotPrefab.SetActive(false);
        }
        if (landingRingPrefab == null)
        {
            landingRingPrefab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(landingRingPrefab.GetComponent<Collider>());
            var ringMr = landingRingPrefab.GetComponent<UnityEngine.MeshRenderer>();
            var unlitShader2 = UnityEngine.Shader.Find("Unlit/Color");
            if (unlitShader2 != null)
            {
                var ringMat = new UnityEngine.Material(unlitShader2);
                ringMat.color = new UnityEngine.Color(1f, 0.3f, 0f);
                ringMr.sharedMaterial = ringMat;
            }
            ringMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringMr.receiveShadows = false;
            landingRingPrefab.transform.localScale = new Vector3(2f, 0.02f, 2f);
            landingRingPrefab.SetActive(false);
        }

        dots = new GameObject[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            dots[i] = Instantiate(dotPrefab, transform);
            // Ensure no collider on the instance (Destroy on the prefab is deferred,
            // so the instance may have copied a not-yet-destroyed collider).
            var c = dots[i].GetComponent<Collider>();
            if (c != null) Destroy(c);
            dots[i].SetActive(false);
        }
        landingRing = Instantiate(landingRingPrefab, transform);
        var rc = landingRing.GetComponent<Collider>();
        if (rc != null) Destroy(rc);
        landingRing.SetActive(false);
        visualsReady = true;
    }

    private bool loggedConditions;

    private void Update()
    {
        if (player == null || !player.IsOwner) { HideAll(); return; }

        bool wReady = player.weapon != null && player.weapon.WeaponReady();
        bool isGrenade = player.weapon != null && player.weapon.CurrentWeapon() != null && player.weapon.CurrentWeapon().weaponType == WeaponType.Grenade;
        bool aiming = player.aim != null && player.aim.CanAimPrecisly();
        bool shouldShow = wReady && isGrenade && aiming;

        // Log condition state once per change to diagnose why parabola isn't showing
        if (!loggedConditions || shouldShow != lastShouldShow)
        {
            Debug.Log($"[Grenade] AimVisual shouldShow={shouldShow} (wReady={wReady} isGrenade={isGrenade} aiming={aiming})");
            loggedConditions = true;
            lastShouldShow = shouldShow;
        }

        if (!shouldShow) { HideAll(); return; }

        EnsureVisuals();

        Transform start = gunPoint != null ? gunPoint : player.weapon.GunPoint();
        Vector3 startPos = start.position;
        Vector3 aimPoint = player.aim.GetMouseHitInfo().point;

        // Clamp to max distance
        Vector3 toAim = aimPoint - startPos;
        if (toAim.magnitude > maxThrowDistance)
            aimPoint = startPos + toAim.normalized * maxThrowDistance;

        Vector3 v0 = Enemy_Grenade.CalculateLaunchVelocity(startPos, aimPoint, timeToTarget);
        DrawTrajectory(startPos, v0);
    }

    private void DrawTrajectory(Vector3 startPos, Vector3 v0)
    {
        float dt = timeToTarget / (sampleCount - 1);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i * dt;
            Vector3 p = startPos + v0 * t + 0.5f * Physics.gravity * t * t;
            if (dots[i] != null)
            {
                dots[i].SetActive(true);
                dots[i].transform.position = p;
            }
        }
        // Landing marker: step the projectile until it drops below start Y
        Vector3 landing = ComputeLanding(startPos, v0);
        if (landingRing != null)
        {
            landingRing.SetActive(true);
            landingRing.transform.position = new Vector3(landing.x, startPos.y, landing.z);
        }
    }

    private Vector3 ComputeLanding(Vector3 startPos, Vector3 v0)
    {
        Vector3 p = startPos;
        Vector3 v = v0;
        for (int i = 0; i < 300; i++)
        {
            p += v * 0.02f;
            v += Physics.gravity * 0.02f;
            if (p.y <= startPos.y && i > 0) break;
        }
        return p;
    }

    private void HideAll()
    {
        if (!visualsReady) return;
        foreach (var d in dots) if (d != null) d.SetActive(false);
        if (landingRing != null) landingRing.SetActive(false);
    }
}
