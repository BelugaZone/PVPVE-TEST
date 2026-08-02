using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;

public class Player_WeaponController : NetworkBehaviour
{
    [SerializeField] private LayerMask whatIsAlly;
    [Space]
    private Player player;
    private const float REFERENCE_BULLET_SPEED = 20;
    //This is the default speed from whcih our mass formula is derived.

    [SerializeField] private Weapon_Data defaultWeaponData;
    [SerializeField] private Weapon currentWeapon;
    private bool weaponReady;
    private bool isShooting;

    [Header("Bullet details")]
    [SerializeField] private float bulletImpactForce = 100;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed;


    [SerializeField] private Transform weaponHolder;

    [Header("Inventory")]

    [SerializeField] private int maxSlots = 4;
    [SerializeField] private List<Weapon> weaponSlots;

    [SerializeField] private GameObject weaponPickupPrefab;
    [SerializeField] private Weapon_Data defaultMeleeWeaponData;

    [Header("Grenade")]
    [SerializeField] private Weapon_Data defaultGrenadeWeaponData;
    [SerializeField] private GameObject grenadePrefab;          // Player_Grenade prefab (Rigidbody + Enemy_Grenade)
    [SerializeField] private float grenadeTimeToTarget = 1.2f;
    [SerializeField] private float grenadeCountdown = 0.2f;     // fuse after landing
    [SerializeField] private float grenadeImpactPower = 500f;

    [Header("Melee Hit Detection")]
    public GameObject meleeAttackFx;
    public LayerMask whatIsEnemy; // Need to hit enemies
    private bool isMeleeAttackReady;
    private HashSet<NetworkObject> alreadyHitEnemies = new HashSet<NetworkObject>();

    private void Awake()
    {
        player = GetComponent<Player>();
        
        if (weaponSlots == null) weaponSlots = new List<Weapon>();
        if (maxSlots < 4) maxSlots = 4;
        while (weaponSlots.Count < maxSlots) 
        {
            weaponSlots.Add(null);
        }
    }

    private void Start()
    {
        AssignInputEvents();

        // Ensure both Enemy and Player layers are included for PvP melee
        if (whatIsEnemy.value == 0 || whatIsEnemy.value == LayerMask.GetMask("Enemy"))
        {
            whatIsEnemy = LayerMask.GetMask("Enemy", "Player");
        }
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        if (isShooting)
            Shoot();

        if (isMeleeAttackReady)
        {
            MeleeAttackCheck();
        }
    }
    
    public void EnableMeleeAttackCheck(bool enable)
    {
        isMeleeAttackReady = enable;
        if (enable)
        {
            alreadyHitEnemies.Clear();
        }
    }

    // Owner-side grenade spawn, called from the ThrowGrenadeTrigger animation event.
public void SpawnGrenade()
    {
        Debug.Log($"[Grenade] SpawnGrenade called isOwner={base.IsOwner} currentType={(currentWeapon!=null?currentWeapon.weaponType.ToString():"NULL")}");
        if (!base.IsOwner) return; // only the owning client spawns from the anim event
        if (currentWeapon == null || currentWeapon.weaponType != WeaponType.Grenade) return; // safety: abort if switched away
        // Both the Invoke fallback and the ThrowGrenadeTrigger animation event
        // can fire around the release frame. Guard so only one grenade spawns.
        if (grenadeSpawnedThisThrow) return;
        grenadeSpawnedThisThrow = true;

        Transform start = GunPoint();
        Vector3 startPos = start.position;
        // Use the live mouse raycast hit (ground point) — same source as the aim
        // preview (Player_GrenadeAimVisual). Aim().position can be stale/chest-
        // height if the player released right-mouse on the same frame as the throw
        // (isAimingPrecisly flips in Player_AimController.Update, which may run
        // after Shoot). The live raycast always returns the ground hit.
        Vector3 aimPoint = player.aim.GetMouseHitInfo().point;
        Debug.Log($"[Grenade] SpawnGrenade start={startPos} aim={aimPoint} prefab={(grenadePrefab!=null?grenadePrefab.name:"NULL")}");

        // On a host (server + client in one process), the authoritative grenade
        // spawned by CmdThrowGrenade renders in the same scene. Spawning a local
        // predicted copy too would show two grenades — so only spawn the predicted
        // visual on a dedicated client (not server). The server's grenade handles
        // the host's view (and is the damaging one).
        if (!base.IsServer)
            SpawnGrenadeVisual(startPos, aimPoint, dealDamage: false);

        int damage = currentWeapon != null ? currentWeapon.bulletDamage : 0;
        CmdThrowGrenade(startPos, aimPoint, damage, whatIsAlly);
    }

    private void SpawnGrenadeVisual(Vector3 startPos, Vector3 aimPoint, bool dealDamage)
    {
        if (grenadePrefab == null) { Debug.LogWarning("grenadePrefab not assigned on Player_WeaponController"); return; }
        // GetObject positions the instance at target.position; we override position afterwards.
        GameObject grenadeObj = ObjectPool.instance.GetObject(grenadePrefab, transform);
        grenadeObj.transform.position = startPos;

        Enemy_Grenade g = grenadeObj.GetComponent<Enemy_Grenade>();
        if (g != null)
        {
            g.SetupGrenade(
                whatIsAlly,
                aimPoint,
                grenadeTimeToTarget,
                grenadeCountdown,
                grenadeImpactPower,
                currentWeapon.bulletDamage,
                dealDamage,
                DetonationMode.OnLand,
                1.5f);
        }
        else
        {
            Debug.LogWarning("grenadePrefab missing Enemy_Grenade component");
        }
    }

    [ServerRpc]
    private void CmdThrowGrenade(Vector3 spawnPos, Vector3 aimPoint, int damage, LayerMask allyMask)
    {
        // Server spawns the authoritative, damaging grenade using values from client
        SpawnGrenadeVisualWithParams(spawnPos, aimPoint, damage, allyMask, dealDamage: true);

        // Replicate a visual grenade to non-owner clients
        RpcThrowGrenade(spawnPos, aimPoint, grenadeTimeToTarget);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcThrowGrenade(Vector3 spawnPos, Vector3 aimPoint, float timeToTarget)
    {
        // Non-owner clients spawn a visual-only grenade following the same arc
        SpawnGrenadeVisual(spawnPos, aimPoint, dealDamage: false);
    }

    // Spawns grenade with explicit damage and ally mask (used by server via CmdThrowGrenade)
    private void SpawnGrenadeVisualWithParams(Vector3 startPos, Vector3 aimPoint, int damage, LayerMask allyMask, bool dealDamage)
    {
        if (grenadePrefab == null) { Debug.LogWarning("grenadePrefab not assigned on Player_WeaponController"); return; }
        GameObject grenadeObj = ObjectPool.instance.GetObject(grenadePrefab, transform);
        grenadeObj.transform.position = startPos;

        Enemy_Grenade g = grenadeObj.GetComponent<Enemy_Grenade>();
        if (g != null)
        {
            g.SetupGrenade(
                allyMask,
                aimPoint,
                grenadeTimeToTarget,
                grenadeCountdown,
                grenadeImpactPower,
                damage,
                dealDamage,
                DetonationMode.OnLand,
                1.5f);
        }
        else
        {
            Debug.LogWarning("grenadePrefab missing Enemy_Grenade component");
        }
    }


    private void MeleeAttackCheck()
    {
        WeaponModel model = player.weaponVisuals.CurrentWeaponModel();
        if (model == null) return;

        // Auto-generate damage points if the user forgot to run Setup Player Melee tool
        if (model.damagePoints == null || model.damagePoints.Length == 0)
        {
            model.attackRadius = 0.4f;
            model.damagePoints = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject dp = new GameObject($"TempDamagePoint_{i}");
                dp.transform.SetParent(model.transform, false);
                dp.transform.localPosition = new Vector3(0, 0.5f + (i * 0.3f), 0);
                model.damagePoints[i] = dp.transform;
            }
        }

        foreach (Transform attackPoint in model.damagePoints)
        {
            if (attackPoint == null) continue;

            Collider[] detectedHits = Physics.OverlapSphere(attackPoint.position, model.attackRadius, whatIsEnemy);
            for (int i = 0; i < detectedHits.Length; i++)
            {
                // Respect friendly fire setting
                if (!GameManager.instance.friendlyFire && detectedHits[i].gameObject.layer == LayerMask.NameToLayer("Player"))
                {
                    continue;
                }

                NetworkBehaviour netObj = detectedHits[i].GetComponentInParent<NetworkBehaviour>();
                if (netObj != null && !alreadyHitEnemies.Contains(netObj.NetworkObject))
                {
                    // Prevent hitting yourself
                    if (netObj.NetworkObject == this.NetworkObject) continue;

                    alreadyHitEnemies.Add(netObj.NetworkObject);
                    
                    UI_HealthBar uiHealth = netObj.GetComponent<UI_HealthBar>();
                    if (uiHealth != null)
                    {
                        uiHealth.ShowUI();
                    }

                    CmdReportMeleeHit(netObj.NetworkObject, currentWeapon.bulletDamage);
                    
                    if (meleeAttackFx != null)
                    {
                        GameObject newAttackFx = ObjectPool.instance.GetObject(meleeAttackFx, attackPoint);
                        ObjectPool.instance.ReturnObject(newAttackFx, 1);
                    }
                }
            }
        }
    }

    #region Slots managment - Pickup\Equip\Drop\Ready Weapon

    private void EquipStartingWeapon()
    {
        if (weaponSlots == null) weaponSlots = new List<Weapon>();

        if (maxSlots < 3) maxSlots = 3;

        while (weaponSlots.Count < maxSlots) 
        {
            weaponSlots.Add(null);
        }

        // Only equip defaults if the slots are completely empty (e.g. fresh spawn, not loaded from save)
        if (weaponSlots[0] == null)
            weaponSlots[0] = new Weapon(defaultWeaponData);
        
        if (weaponSlots[2] == null && defaultMeleeWeaponData != null)
        {
            weaponSlots[2] = new Weapon(defaultMeleeWeaponData);
        }

        if (weaponSlots[3] == null && defaultGrenadeWeaponData != null)
        {
            weaponSlots[3] = new Weapon(defaultGrenadeWeaponData);
        }

        if (currentWeapon == null || currentWeapon.weaponData == null || string.IsNullOrEmpty(currentWeapon.weaponData.weaponName))
        {
            EquipWeapon(0);
        }
    }
    public int CurrentWeaponIndex
    {
        get
        {
            if (currentWeapon == null || weaponSlots == null) return -1;
            return weaponSlots.IndexOf(currentWeapon);
        }
    }

    public void EquipWeapon(int i)
    {
        Debug.Log($"[Grenade] EquipWeapon({i}) slots.Count={weaponSlots.Count} slotNull={i<weaponSlots.Count && weaponSlots[i]==null}");
        if (i >= weaponSlots.Count || weaponSlots[i] == null)
            return;

        SetWeaponReady(false);

        currentWeapon = weaponSlots[i];
        Debug.Log($"[Grenade] EquipWeapon -> currentWeapon={currentWeapon.weaponType} ready set false");
        player.weaponVisuals.PlayWeaponEquipAnimation();

        CameraManager.instance.ChangeCameraDistance(currentWeapon.cameraDistance);

        if (base.IsOwner)
        {
            CmdEquipWeaponAnim(i);
        }
    }

    [ServerRpc]
    private void CmdEquipWeaponAnim(int index)
    {
        RpcEquipWeaponAnim(index);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcEquipWeaponAnim(int index)
    {
        if (index >= 0 && index < weaponSlots.Count && weaponSlots[index] != null)
        {
            currentWeapon = weaponSlots[index];
            player.weaponVisuals.PlayWeaponEquipAnimation();
        }
    }
    public void PickupWeapon(Weapon newWeapon)
    {
        if (newWeapon == null || newWeapon.weaponData == null) return;
        
        player.inventory.CmdPickupWeapon(newWeapon.weaponData.weaponName, newWeapon.bulletsInMagazine, newWeapon.totalReserveAmmo);
    }
    private void DropWeapon()
    {
        if (HasOnlyOneWeapon())
            return;

        // Don't drop melee weapon
        if (currentWeapon.weaponType == WeaponType.Melee)
            return;

        // Don't drop the grenade slot
        if (currentWeapon.weaponType == WeaponType.Grenade)
            return;

        CreateWeaponOnTheGround();

        weaponSlots.Remove(currentWeapon);
        EquipWeapon(0);
    }

    private void CreateWeaponOnTheGround()
    {
        GameObject droppedWeapon = ObjectPool.instance.GetObject(weaponPickupPrefab, transform);
        droppedWeapon.GetComponent<Pickup_Weapon>()?.SetupPickupWeapon(currentWeapon, transform);
    }

    public void SetWeaponReady(bool ready) => weaponReady = ready;
    public bool WeaponReady() => weaponReady;

    #region Inventory UI bridge (local-only; v1 does not sync to other clients)

    // Called by PlayerInventory when the player drags an item onto an equipment slot.
    // Local-only: the owner visually holds/fires the new gun; other clients won't see
    // the swap until a future ServerRpc pass.
    public void EquipFromInventory(int slot, Weapon_Data data, int ammoInMag = -1, int ammoReserve = -1, bool equip = true)
    {
        if (data == null) return;
        if (slot < 0 || slot >= weaponSlots.Count) return;

        weaponSlots[slot] = new Weapon(data);
        
        // Restore ammo if provided (from network sync / save data)
        if (ammoInMag >= 0) weaponSlots[slot].bulletsInMagazine = ammoInMag;
        if (ammoReserve >= 0) weaponSlots[slot].totalReserveAmmo = ammoReserve;
        
        if (equip)
            EquipWeapon(slot);
    }

    public Weapon GetWeaponAt(int slot)
    {
        if (slot < 0 || slot >= weaponSlots.Count) return null;
        return weaponSlots[slot];
    }

    // Clears a weapon slot (e.g. when its item is dragged to the backpack). If the
    // cleared weapon was the currently-held one, switches to another available weapon.
    // v1: if no other weapon remains, leaves currentWeapon as-is (rare edge case;
    // avoids null-deref in Shoot/HUD).
    public void ClearWeaponSlot(int slot)
    {
        if (slot < 0 || slot >= weaponSlots.Count) return;
        Weapon cleared = weaponSlots[slot];
        weaponSlots[slot] = null;

        if (cleared != null && currentWeapon == cleared)
        {
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                if (weaponSlots[i] != null)
                {
                    EquipWeapon(i);
                    return;
                }
            }
            // No other weapon available — leave currentWeapon holding the cleared
            // one for v1 (documented edge case).
        }
    }

    public System.Collections.Generic.IReadOnlyList<Weapon> GetWeaponSlots() => weaponSlots;

    // Mirrors EquipStartingWeapon's defaults so PlayerInventory can seed without racing
    // the 0.1s-delayed EquipStartingWeapon Invoke.
    public Weapon_Data GetDefaultWeaponData(int slot)
    {
        switch (slot)
        {
            case 0: return defaultWeaponData;
            case 2: return defaultMeleeWeaponData;
            case 3: return defaultGrenadeWeaponData;
            default: return null;
        }
    }

    #endregion

    #endregion


    private IEnumerator BurstFire()
    {
        SetWeaponReady(false);

        for (int i = 1; i <= currentWeapon.bulletsPerShot; i++)
        {
            FireSingleBullet();

            yield return new WaitForSeconds(currentWeapon.burstFireDelay);

            if (i >= currentWeapon.bulletsPerShot)
                SetWeaponReady(true);
        }
    }

    private float lastMeleeAttackTime;
    private bool grenadeSpawnedThisThrow;
    private const float GrenadeSpawnDelay = 2.0f; // seconds after throw start before the grenade leaves the hand

    private void Shoot()
    {
        if (WeaponReady() == false)
        {
            Debug.Log("Shoot blocked: WeaponReady is false");
            return;
        }

        if (currentWeapon.weaponType == WeaponType.Melee)
        {
            if (Time.time >= lastMeleeAttackTime + 1.0f)
            {
                Debug.Log("Performing Melee Attack!");
                lastMeleeAttackTime = Time.time;
                PerformMeleeAttack();
            }
            else
            {
                Debug.Log("Melee attack blocked by cooldown");
            }
            return;
        }

        if (currentWeapon.weaponType == WeaponType.Grenade)
        {
            PerformGrenadeThrow();
            return;
        }

        if (WeaponReady() == false)
            return;

        if (currentWeapon.CanShoot() == false)
            return;

        player.weaponVisuals.PlayFireAnimation();

        if (currentWeapon.shootType == ShootType.Single)
            isShooting = false;

        if (currentWeapon.BurstActivated() == true)
        {
            StartCoroutine(BurstFire());
            return;
        }


        FireSingleBullet();
        TriggerEnemyDodge();
    }

    private void PerformMeleeAttack()
    {
        isShooting = false;
        player.weaponVisuals.PlayMeleeAnimation();

        if (base.IsOwner)
        {
            CmdMeleeAttack();
        }
    }

private void PerformGrenadeThrow()
    {
        // Cache CanShoot() once — ReadyToFire() has a side effect (sets lastShootTime),
        // so calling it twice (e.g. in a debug log AND the guard) makes the second
        // call always false, silently blocking the throw.
        bool canAim = player.aim != null && player.aim.CanAimPrecisly();
        bool canShoot = currentWeapon.CanShoot();
        Debug.Log($"[Grenade] PerformGrenadeThrow canAim={canAim} canShoot={canShoot} remaining={currentWeapon.bulletsInMagazine}");
        if (!canAim) return;
        if (!canShoot) return;

        isShooting = false;
        currentWeapon.bulletsInMagazine--; // consume 1 grenade
        grenadeSpawnedThisThrow = false; // arm the dedupe guard for this throw
        Debug.Log($"[Grenade] PerformGrenadeThrow -> throwing, remaining={currentWeapon.bulletsInMagazine}");
        player.weaponVisuals.PlayGrenadeThrowAnimation();
        // Spawn the grenade at the release frame of the throw animation. Tuned
        // delay to sync with the arm's release point.
        Invoke(nameof(SpawnGrenade), GrenadeSpawnDelay);

        if (base.IsOwner)
        {
            CmdGrenadeThrowAnim();
        }
    }

    [ServerRpc]
    private void CmdGrenadeThrowAnim()
    {
        RpcGrenadeThrowAnim();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcGrenadeThrowAnim()
    {
        player.weaponVisuals.PlayGrenadeThrowAnimation();
    }


    [ServerRpc]
    private void CmdMeleeAttack()
    {
        RpcMeleeAttack();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcMeleeAttack()
    {
        player.weaponVisuals.PlayMeleeAnimation();
    }

    [ServerRpc]
    public void CmdReportMeleeHit(NetworkObject hitObject, int damage)
    {
        if (hitObject == null) return;
        
        ServerLogger.LogCombat($"Player {base.OwnerId} melee hit {hitObject.name} for {damage} damage.");

        Enemy enemy = hitObject.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.GetHit(damage);
        }
        else
        {
            HealthController health = hitObject.GetComponent<HealthController>();
            if (health != null)
            {
                health.ReduceHealth(damage);
            }
        }
    }

    private void FireSingleBullet()
    {
        currentWeapon.bulletsInMagazine--;
        CreateBullet(player);

        if (base.IsOwner) 
        {
            CmdShoot();
        }
    }

    private void CreateBullet(Player firingPlayer)
    {
        GameObject newBullet = ObjectPool.instance.GetObject(bulletPrefab,GunPoint());

        newBullet.transform.rotation = Quaternion.LookRotation(GunPoint().forward);

        Rigidbody rbNewBullet = newBullet.GetComponent<Rigidbody>();

        Bullet bulletScript = newBullet.GetComponent<Bullet>();
        bulletScript.BulletSetup(whatIsAlly,currentWeapon.bulletDamage, currentWeapon.gunDistance,bulletImpactForce, firingPlayer);


        Vector3 bulletsDirection = currentWeapon.ApplySpread(BulletDirection());

        rbNewBullet.mass = REFERENCE_BULLET_SPEED / bulletSpeed;
        rbNewBullet.velocity = bulletsDirection * bulletSpeed;
    }

    [ServerRpc]
    private void CmdShoot()
    {
        RpcShoot();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcShoot()
    {
        player.weaponVisuals.PlayFireAnimation();
        CreateBullet(null);
    }

    [ServerRpc]
    public void CmdReportHit(NetworkObject hitObject, int damage)
    {
        if (hitObject == null) return;
        
        ServerLogger.LogCombat($"Player {base.OwnerId} hit {hitObject.name} for {damage} damage.");

        Enemy enemy = hitObject.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.GetHit(damage);
        }
        else
        {
            HealthController health = hitObject.GetComponent<HealthController>();
            if (health != null)
            {
                health.ReduceHealth(damage);
            }
        }
    }

    private void Reload()
    {
        SetWeaponReady(false);
        player.weaponVisuals.PlayReloadAnimation();
        if (base.IsOwner) CmdReload();
    }

    [ServerRpc]
    private void CmdReload()
    {
        RpcReload();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcReload()
    {
        player.weaponVisuals.PlayReloadAnimation();
    }


    public Vector3 BulletDirection()
    {
        Transform aim = player.aim.Aim();

        Vector3 direction = (aim.position - GunPoint().position).normalized;

        if (player.aim.CanAimPrecisly() == false && player.aim.Target() == null)
            direction.y = 0;

        return direction;
    }

    public bool HasOnlyOneWeapon() => weaponSlots.Count <= 1;
    public Weapon WeaponInSlots(WeaponType weaponType)
    {
        foreach (Weapon weapon in weaponSlots)
        {
            if (weapon != null && weapon.weaponType == weaponType)
                return weapon;
        }

        return null;
    }
    public Weapon CurrentWeapon() => currentWeapon;
    public Transform GunPoint() => player.weaponVisuals.CurrentWeaponModel().gunPoint;

    private void TriggerEnemyDodge()
    {
        Vector3 rayOrigin = GunPoint().position;
        Vector3 rayDirection = BulletDirection();

        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, Mathf.Infinity))
        {
            Enemy_Melee enemy_Melee = hit.collider.gameObject.GetComponentInParent<Enemy_Melee>();

            if (enemy_Melee != null)
                enemy_Melee.ActivateDodgeRoll();
        }
    }

    #region Input Events

    private void AssignInputEvents()
    {
        PlayerControls controls = player.controls;

        controls.Character.Fire.performed += context => 
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            isShooting = true;
        };
        controls.Character.Fire.canceled += context => isShooting = false;

        controls.Character.EquipSlot1.performed += context => EquipWeapon(0);
        controls.Character.EquipSlot2.performed += context => EquipWeapon(1);
        controls.Character.EquipSlot3.performed += context => EquipWeapon(2);
        controls.Character.EquipSlot4.performed += context => EquipWeapon(3);
        controls.Character.EquipSlot5.performed += context => EquipWeapon(4);

        controls.Character.DropCurrentWeapon.performed += context => DropWeapon();

        controls.Character.Reload.performed += context =>
        {
            if (currentWeapon.CanReload() && WeaponReady())
            {
                Reload();
            }
        };

        controls.Character.ToogleWeaponMode.performed += context => currentWeapon.ToggleBurst();

    }



    #endregion
}
