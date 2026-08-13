using UnityEngine;

public class Bullet : MonoBehaviour
{
    private int bulletDamage;
    private float impactForce;

    private BoxCollider cd;
    private Rigidbody rb;
    private MeshRenderer meshRenderer;
    private TrailRenderer trailRenderer;


    [SerializeField] private GameObject bulletImpactFX;


    private Vector3 startPosition;
    private float flyDistance;
    private bool bulletDisabled;

    private LayerMask allyLayerMask;
    

    public Player ownerPlayer;

    protected virtual void Awake()
    {
        cd = GetComponent<BoxCollider>();
        rb = GetComponent<Rigidbody>();
        meshRenderer = GetComponent<MeshRenderer>();
        trailRenderer = GetComponent<TrailRenderer>();
    }

    public void BulletSetup(LayerMask allyLayerMask,int bulletDamage, float flyDistance = 100, float impactForce = 100, Player firingPlayer = null)
    {
        this.allyLayerMask = allyLayerMask;
        this.impactForce = impactForce;
        this.bulletDamage = bulletDamage;
        this.ownerPlayer = firingPlayer;

        bulletDisabled = false;
        cd.enabled = true;
        meshRenderer.enabled = true;

        trailRenderer.Clear();
        trailRenderer.time = .25f;
        startPosition = transform.position;
        this.flyDistance = flyDistance + .5f; // magic number .5f is a length of tip of the laser ( Check method UpdateAimVisuals() on PlayerAim script) ;
    }

    protected virtual void Update()
    {
        FadeTrailIfNeeded();
        DisableBulletIfNeeded();
        ReturnToPoolIfNeeded();
    }

    protected void ReturnToPoolIfNeeded()
    {
        if (trailRenderer.time < 0)
            ReturnBulletToPool();
    }
    protected void DisableBulletIfNeeded()
    {
        if (Vector3.Distance(startPosition, transform.position) > flyDistance && !bulletDisabled)
        {
            cd.enabled = false;
            meshRenderer.enabled = false;
            bulletDisabled = true;
        }
    }
    protected void FadeTrailIfNeeded()
    {
        if (Vector3.Distance(startPosition, transform.position) > flyDistance - 1.5f)
            trailRenderer.time -= 2 * Time.deltaTime; // magic number 2 is choosen trhou testing
    }

    protected virtual void OnCollisionEnter(Collision collision)
    {
        if (FriendlyFare() == false)
        {
            // Use a bitwise AND to check if the collsion layer is in the allyLayerMask
            if ((allyLayerMask.value & (1 << collision.gameObject.layer)) > 0)
            {
                CreateImpactFx();
                ReturnBulletToPool();
                return;
            }
        }

        CreateImpactFx();
        ReturnBulletToPool();

        if (ownerPlayer != null)
        {
            if (ownerPlayer.IsOwner)
            {
                FishNet.Object.NetworkObject netObj = collision.gameObject.GetComponentInParent<FishNet.Object.NetworkObject>();
                HitBox hitbox = collision.gameObject.GetComponent<HitBox>();
                
                if (netObj != null)
                {
                    UI_HealthBar uiHealth = netObj.GetComponent<UI_HealthBar>();
                    if (uiHealth != null)
                    {
                        Debug.Log("Bullet hit, calling ShowUI on " + netObj.name);
                        uiHealth.ShowUI();
                    }
                    else
                    {
                        Debug.Log("Bullet hit " + netObj.name + " but no UI_HealthBar found!");
                    }

                    int finalDamage = bulletDamage;
                    if (hitbox != null)
                    {
                        finalDamage = Mathf.RoundToInt(bulletDamage * hitbox.damageMultiplier);
                    }
                    ownerPlayer.weapon.CmdReportHit(netObj, finalDamage);
                }
            }
        }
        else
        {
            // Fired by AI/Enemy
            if (FishNet.InstanceFinder.IsServer)
            {
                HealthController health = collision.gameObject.GetComponentInParent<HealthController>();
                HitBox hitbox = collision.gameObject.GetComponent<HitBox>();
                
                if (health != null)
                {
                    int finalDamage = bulletDamage;
                    if (hitbox != null)
                    {
                        finalDamage = Mathf.RoundToInt(bulletDamage * hitbox.damageMultiplier);
                    }
                    health.ReduceHealth(finalDamage);
                }
            }
        }
        ApplyBulletImpactToEnemy(collision);
    }

    private void ApplyBulletImpactToEnemy(Collision collision)
    {
        Enemy enemy = collision.gameObject.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            Vector3 force = rb.velocity.normalized * impactForce;
            Rigidbody hitRigidbody = collision.collider.attachedRigidbody;
            enemy.BulletImpact(force, collision.contacts[0].point, hitRigidbody);
        }
    }

    protected void ReturnBulletToPool(float delay = 0) => ObjectPool.instance.ReturnObject(gameObject,delay);


    protected void CreateImpactFx()
    {
        GameObject newImpactFx = ObjectPool.instance.GetObject(bulletImpactFX, transform);
        ObjectPool.instance.ReturnObject(newImpactFx, 1);
    }

    private bool FriendlyFare() => GameManager.instance.friendlyFire;
}
