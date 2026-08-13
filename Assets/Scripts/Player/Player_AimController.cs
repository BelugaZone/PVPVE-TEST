using System;
using UnityEngine;
using FishNet.Object;

public class Player_AimController : NetworkBehaviour
{
    private Player player;
    private PlayerControls controls;

    [Header("Aim Viusal - Laser")]
    [SerializeField] private LineRenderer aimLaser; // this component is on the waepon holder(child of a player)

    [Header("Aim control")]
    [SerializeField] private Transform aim;

    public bool isAimingPrecisly;
    [SerializeField] private bool isLockingToTarget;

    [Header("Camera control")]
    [SerializeField] private Transform cameraTarget;
    [Range(.5f, 1)]
    [SerializeField] private float minCameraDistance = 1.5f;
    [Range(1, 3f)]
    [SerializeField] private float maxCameraDistance = 4;
    [Range(3f, 5f)]
    [SerializeField] private float cameraSensetivity = 5f;

    [Space]

    [SerializeField] private LayerMask aimLayerMask;

    private Vector2 mouseInput;
    private RaycastHit lastKnownMouseHit;
    
    private Texture2D crosshairTexture;

    private void Awake()
    {
        if (aim == null)
        {
            aim = new GameObject("Aim_" + gameObject.name).transform;
        }

        if (cameraTarget == null)
        {
            cameraTarget = new GameObject("CameraTarget_" + gameObject.name).transform;
        }

        // Disable the aim laser by default — only the local owner enables it in UpdateAimVisuals.
        // This prevents non-owner players (and pre-ownership-init frames) from showing a static laser.
        if (aimLaser != null)
            aimLaser.enabled = false;
        
        // Ensure player and enemy layers are included in aim raycasts
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer != -1) aimLayerMask |= (1 << playerLayer);
        
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer != -1) aimLayerMask |= (1 << enemyLayer);

        CreateCrosshairTexture();
    }
    
    private void CreateCrosshairTexture()
    {
        int size = 32;
        crosshairTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color transparent = new Color(0, 0, 0, 0);
        Color white = Color.white;
        
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
                crosshairTexture.SetPixel(i, j, transparent);

        int center = size / 2;
        int length = 8;
        int thickness = 2;
        
        for (int i = center - length; i <= center + length; i++)
        {
            for (int j = center - thickness / 2; j <= center + thickness / 2; j++)
            {
                if (i >= 0 && i < size && j >= 0 && j < size)
                {
                    crosshairTexture.SetPixel(i, j, white);
                    crosshairTexture.SetPixel(j, i, white);
                }
            }
        }
        crosshairTexture.Apply();
    }

    private void Start()
    {
        player = GetComponent<Player>();
        AssignInputEvents();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (base.IsOwner)
        {
            // Delay camera setup — during scene transitions the Virtual Camera may not be
            // loaded yet when OnStartClient fires. Wait a frame for the scene to settle.
            StartCoroutine(SetupCameraDelayed());
        }
        else
        {
            if (aimLaser != null)
                aimLaser.enabled = false;
        }
    }

    private System.Collections.IEnumerator SetupCameraDelayed()
    {
        // Wait until the scene has a CinemachineVirtualCamera.
        Cinemachine.CinemachineVirtualCamera vcam = null;
        int attempts = 0;
        while (vcam == null && attempts < 60) // ~1s max
        {
            vcam = FindObjectOfType<Cinemachine.CinemachineVirtualCamera>();
            if (vcam == null) yield return null;
            attempts++;
        }
        if (vcam != null && cameraTarget != null)
        {
            cameraTarget.parent = null; // Decouple from player to avoid jitter
            vcam.Follow = cameraTarget;
            vcam.LookAt = null;
            Debug.Log($"[Player_AimController] Camera follow set to {cameraTarget.name} (vcam={vcam.gameObject.name})");
        }
        else
        {
            Debug.LogWarning($"[Player_AimController] Could not find VirtualCamera after {attempts} attempts. cameraTarget={(cameraTarget!=null?cameraTarget.name:"NULL")}");
        }
    }

    private void Update()
    {
        // Non-owner players: keep aim laser off (it's a local-owner-only visual).
        if (!base.IsOwner)
        {
            if (aimLaser != null && aimLaser.enabled)
                aimLaser.enabled = false;
            return;
        }

        if (player.health.isDead.Value)
            return;

        bool wasAiming = isAimingPrecisly;
        isAimingPrecisly = Input.GetMouseButton(1);
        
        if (isAimingPrecisly != wasAiming)
        {
            if (isAimingPrecisly)
                Cursor.SetCursor(crosshairTexture, new Vector2(16, 16), CursorMode.Auto);
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        if(Input.GetKeyDown(KeyCode.L))
            isLockingToTarget = !isLockingToTarget;

        UpdateAimVisuals();
        UpdateAimPosition();
    }

    private void LateUpdate()
    {
        if (!base.IsOwner) return;
        if (player.health.isDead.Value) return;

        UpdateCameraPosition();
    }

    private void UpdateAimVisuals()
    {
        bool isMelee = false;
        Weapon currentWeapon = player.weapon.CurrentWeapon();
        if (currentWeapon != null && currentWeapon.weaponType == WeaponType.Melee)
        {
            isMelee = true;
        }

        // Only enable laser if we actually have a weapon equipped
        aimLaser.enabled = (currentWeapon != null) && player.weapon.WeaponReady() && !isMelee;

        if (aimLaser.enabled == false)
            return;

        WeaponModel weaponModel = player.weaponVisuals.CurrentWeaponModel();
        if (weaponModel == null)
        {
            aimLaser.enabled = false;
            return;
        }

        weaponModel.transform.LookAt(aim);
        weaponModel.gunPoint.LookAt(aim);


        Transform gunPoint = player.weapon.GunPoint();
        Vector3 laserDirection = player.weapon.BulletDirection();

        float laserTipLenght = .5f;
        float gunDistance = player.weapon.CurrentWeapon().gunDistance;

        Vector3 endPoint = gunPoint.position + laserDirection * gunDistance;

        if (Physics.Raycast(gunPoint.position, laserDirection, out RaycastHit hit, gunDistance))
        {
            endPoint = hit.point;
            laserTipLenght = 0;
        }

        aimLaser.SetPosition(0, gunPoint.position);
        aimLaser.SetPosition(1, endPoint);
        aimLaser.SetPosition(2, endPoint + laserDirection * laserTipLenght);
    }
    private void UpdateAimPosition()
    {
        Transform target = Target();

        if (target != null && isLockingToTarget)
        {
            if(target.GetComponent<Renderer>() != null)
                aim.position = target.GetComponent<Renderer>().bounds.center;
            else
                aim.position = target.position;


            return;
        }   

        aim.position = GetMouseHitInfo().point;

        if (!isAimingPrecisly)
            aim.position = new Vector3(aim.position.x, transform.position.y + 1, aim.position.z);
    }




    public Transform Target()
    {
        Transform target = null;

        if (GetMouseHitInfo().transform.GetComponent<Target>() != null)
        {
            target = GetMouseHitInfo().transform;
        }

        return target;
    }
    public Transform Aim() => aim;
    public bool CanAimPrecisly() => isAimingPrecisly;
    public RaycastHit GetMouseHitInfo()
    {
        Ray ray = Camera.main.ScreenPointToRay(mouseInput);

        if (Physics.Raycast(ray, out var hitInfo, Mathf.Infinity, aimLayerMask))
        {
            lastKnownMouseHit = hitInfo;
            return hitInfo;
        }

        return lastKnownMouseHit;
    }

    #region Camera Region

    private float shakeTimer;
    private float shakeIntensity;
    private Vector3 shakeOffset;

    public void ShakeCamera(float intensity, float duration)
    {
        shakeIntensity = intensity;
        shakeTimer = duration;
    }

    private void UpdateCameraPosition()
    {
        Vector3 basePos = DesieredCameraPosition();
        Vector3 targetLerped = Vector3.Lerp(cameraTarget.position - shakeOffset, basePos, cameraSensetivity * Time.deltaTime);

        shakeOffset = Vector3.zero;
        if (shakeTimer > 0)
        {
            shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
            // keep it strictly 2D/3D depending on needs, but insideUnitSphere is fine
            shakeOffset.y = 0; // usually don't shake height too much in top-down
            shakeTimer -= Time.deltaTime;
        }

        cameraTarget.position = targetLerped + shakeOffset;
    }

    private Vector3 DesieredCameraPosition()
    {
        float actualMaxCameraDistance = player.movement.moveInput.y < -.5f ? minCameraDistance : maxCameraDistance;

        if (isAimingPrecisly && player.weapon != null)
        {
            Weapon currentWeapon = player.weapon.CurrentWeapon();
            if (currentWeapon != null && currentWeapon.weaponData != null)
            {
                if (currentWeapon.weaponData.weaponType == WeaponType.Rifle || currentWeapon.weaponData.weaponName == "weap_rifle")
                {
                    actualMaxCameraDistance = 20f; // Massively increase FOV drag range for sniper
                }
            }
        }

        Vector3 desiredCameraPosition = GetMouseHitInfo().point;
        Vector3 aimDirection = (desiredCameraPosition - transform.position).normalized;

        float distanceToDesierdPosition = Vector3.Distance(transform.position, desiredCameraPosition);
        float clampedDistance = Mathf.Clamp(distanceToDesierdPosition, minCameraDistance, actualMaxCameraDistance);

        desiredCameraPosition = transform.position + aimDirection * clampedDistance;
        desiredCameraPosition.y = transform.position.y + 1;

        return desiredCameraPosition;
    }

    #endregion

    private void AssignInputEvents()
    {
        controls = player.controls;

        controls.Character.Aim.performed += context => mouseInput = context.ReadValue<Vector2>();
        controls.Character.Aim.canceled += context => mouseInput = Vector2.zero;
    }

}
