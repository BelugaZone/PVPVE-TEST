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

    [SerializeField] private int maxSlots = 3;
    [SerializeField] private List<Weapon> weaponSlots;

    [SerializeField] private GameObject weaponPickupPrefab;
    [SerializeField] private Weapon_Data defaultMeleeWeaponData;

    [Header("Melee Hit Detection")]
    public GameObject meleeAttackFx;
    public LayerMask whatIsEnemy; // Need to hit enemies
    private bool isMeleeAttackReady;
    private HashSet<NetworkObject> alreadyHitEnemies = new HashSet<NetworkObject>();

    private void Start()
    {
        player = GetComponent<Player>();
        AssignInputEvents();

        // Ensure both Enemy and Player layers are included for PvP melee
        if (whatIsEnemy.value == 0 || whatIsEnemy.value == LayerMask.GetMask("Enemy"))
        {
            whatIsEnemy = LayerMask.GetMask("Enemy", "Player");
        }

        Invoke(nameof(EquipStartingWeapon), .1f);
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

        weaponSlots[0] = new Weapon(defaultWeaponData);
        
        if (defaultMeleeWeaponData != null)
        {
            weaponSlots[2] = new Weapon(defaultMeleeWeaponData);
        }

        EquipWeapon(0);
    }
    private void EquipWeapon(int i)
    {
        if (i >= weaponSlots.Count || weaponSlots[i] == null)
            return;

        SetWeaponReady(false);

        currentWeapon = weaponSlots[i];
        player.weaponVisuals.PlayWeaponEquipAnimation();

        CameraManager.instance.ChangeCameraDistance(currentWeapon.cameraDistance);
    }
    public void PickupWeapon(Weapon newWeapon)
    {
        if (WeaponInSlots(newWeapon.weaponType) != null)
        {
            WeaponInSlots(newWeapon.weaponType).totalReserveAmmo += newWeapon.bulletsInMagazine;
            return;
        }

        // Only allow picking up non-melee weapons in slot 0 and 1
        if (weaponSlots.Count >= maxSlots - 1 && currentWeapon.weaponType != WeaponType.Melee)
        {
            int currentWeaponIndex = weaponSlots.IndexOf(currentWeapon);
            player.weaponVisuals.SwitchOffWeaponModels();
            weaponSlots[currentWeaponIndex] = newWeapon;
            CreateWeaponOnTheGround();
            EquipWeapon(currentWeaponIndex);
            return;
        }

        // Add to first available empty slot (usually 1)
        for (int i = 0; i < maxSlots; i++)
        {
            if (weaponSlots[i] == null)
            {
                weaponSlots[i] = newWeapon;
                EquipWeapon(i);
                return;
            }
        }
    }
    private void DropWeapon()
    {
        if (HasOnlyOneWeapon())
            return;

        // Don't drop melee weapon
        if (currentWeapon.weaponType == WeaponType.Melee)
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

        controls.Character.Fire.performed += context => isShooting = true;
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
