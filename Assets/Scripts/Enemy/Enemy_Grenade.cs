using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum DetonationMode { Timed, OnLand }

public class Enemy_Grenade : MonoBehaviour
{
    [SerializeField] private GameObject explosionFx;
    [SerializeField] private float impactRadius;
    [SerializeField] private float upwardsMultiplier = 1;
    private Rigidbody rb;
    private float timer;
    private float impactPower;

    private LayerMask allyLayerMask;
    private bool canExplode = true;

    private int grenadeDamage;
    private bool dealDamage = true;

    // Landing-based detonation (player grenades). Enemy grenades keep Timed mode
    // (unchanged behavior) via the default parameter in SetupGrenade.
    // The launch velocity is constructed (CalculateLaunchVelocity) so that the
    // grenade arrives at the aim point at exactly t = timeToTarget. So instead of
    // relying on physics collision (which tunnels through the thin ground
    // MeshCollider at speed / discrete detection), we land on a TIMER at
    // timeToTarget: stop the grenade there and start the fuse. Collision is
    // still honored for early hits (walls) via OnCollisionEnter.
    private DetonationMode detonationMode = DetonationMode.Timed;
    private float landedFuse = 1.5f;
    private bool hasLanded;
    private float safetyFuseTimer;
    private float landingPointY;
    private float spawnGraceTimer;
    private const float SPAWN_GRACE = 0.1f; // ignore collisions in the first moments after spawn

    private void Awake() => rb = GetComponent<Rigidbody>();

    private void Update()
    {
        spawnGraceTimer -= Time.deltaTime;

        if (!canExplode) return;

        if (detonationMode == DetonationMode.OnLand)
        {
            // Safety fuse: guarantees detonation even if the grenade never lands
            // (e.g. something disabled it). timeToTarget + fuse + margin.
            safetyFuseTimer -= Time.deltaTime;
            if (safetyFuseTimer <= 0f)
            {
                Explode();
                return;
            }

            if (!hasLanded)
            {
                // Count down to the constructed landing time. The launch velocity
                // is built so the grenade reaches the aim point (ground) at
                // timeToTarget, regardless of whether physics collision fires.
                timer -= Time.deltaTime;
                if (timer <= 0f)
                    Land(snapToGround: true);
            }
            else
            {
                landedFuse -= Time.deltaTime;
                if (landedFuse <= 0f)
                    Explode();
            }
            return;
        }

        // Timed mode (enemy grenades — original behavior)
        timer -= Time.deltaTime;
        if (timer < 0)
            Explode();
    }

    // Stop the grenade at the landing point so it sits still during the fuse.
    // snapToGround: true for the constructed ground landing (fix tunneling);
    // false for collision-based early hits (keep it where it hit, e.g. a wall).
    private void Land(bool snapToGround)
    {
        if (hasLanded) return;
        hasLanded = true;
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            // Make kinematic during the fuse so gravity does not pull the grenade
            // through the ground (the thin ground MeshCollider can't stop a fast,
            // discrete-collision body reliably). Re-enabled on Explode/return.
            rb.isKinematic = true;
        }
        if (snapToGround)
        {
            Vector3 p = transform.position;
            p.y = landingPointY;
            transform.position = p;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (detonationMode != DetonationMode.OnLand) return;
        if (hasLanded) return;
        // Ignore collisions in the first instant after spawn so the grenade does
        // not register the player's hand/body as "landed" and instantly stick.
        if (spawnGraceTimer > 0f) return;

        // Early landing (e.g. hit a wall mid-arc). For ground, the timer in
        // Update() is the guaranteed path; this handles obstacles the timer
        // doesn't know about. Keep the grenade at the contact point (no snap).
        Land(snapToGround: false);
    }

    private void Explode()
    {
        canExplode = false;
        PlayExplosionFx();

        if (!dealDamage)
            return;

        HashSet<GameObject> uniqueEntities = new HashSet<GameObject>();
        Collider[] colliders = Physics.OverlapSphere(transform.position, impactRadius);

        foreach (Collider hit in colliders)
        {
            // Search the collider itself first, then walk up the hierarchy.
            // This handles sub-bone colliders (e.g. enemy skeletons) that don't
            // have IDamagable on the exact GameObject but have it on a parent.
            IDamagable damagable = hit.GetComponent<IDamagable>();
            if (damagable == null)
                damagable = hit.GetComponentInParent<IDamagable>();

            if (damagable != null)
            {
                if (IsTargetValid(hit) == false)
                    continue;

                // Deduplicate by root so a multi-collider entity only takes damage once.
                GameObject rootEntity = hit.transform.root.gameObject;
                if (uniqueEntities.Add(rootEntity) == false)
                    continue;

                Enemy enemy = rootEntity.GetComponent<Enemy>();
                if (enemy != null)
                {
                    enemy.GetHit(grenadeDamage);
                }
                else
                {
                    damagable.TakeDamage(grenadeDamage);
                }
            }

            ApplyPhysicalForceTo(hit);
        }
    }

    private void ApplyPhysicalForceTo(Collider hit)
    {
        Rigidbody rb = hit.GetComponent<Rigidbody>();

        if (rb != null)
            rb.AddExplosionForce(impactPower, transform.position, impactRadius, upwardsMultiplier, ForceMode.Impulse);
    }

    private void PlayExplosionFx()
    {
        if (explosionFx == null) { Debug.LogWarning("[Grenade] explosionFx is NULL on " + gameObject.name); return; }
        GameObject newFx = ObjectPool.instance.GetObject(explosionFx, transform);

        // SineVFX (and most particle effects) do NOT re-emit when a pooled instance
        // is merely reactivated via SetActive(true) — playOnAwake only fires on
        // the very first instantiation. For pooled re-use we must explicitly
        // restart every particle system (including children).
        ParticleSystem[] systems = newFx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        // SineVFX particle systems live up to ~4s; return to pool only after they
        // finish, otherwise the effect is cut off mid-play.
        ObjectPool.instance.ReturnObject(newFx, 5);
        ObjectPool.instance.ReturnObject(gameObject);
    }

    public void SetupGrenade(LayerMask allyLayerMask, Vector3 target, float timeToTarget, float countdown, float impactPower, int grenadeDamage, bool dealDamage = true, DetonationMode mode = DetonationMode.Timed, float landedFuse = 1.5f)
    {
        canExplode = true;
        this.dealDamage = dealDamage;
        this.grenadeDamage = grenadeDamage;
        this.allyLayerMask = allyLayerMask;
        // Re-enable physics (Land() sets isKinematic=true for the fuse; pool reuse
        // must reset it so the grenade flies again).
        if (rb != null) rb.isKinematic = false;
        rb.velocity = CalculateLaunchVelocity(transform.position, target, timeToTarget);
        timer = countdown + timeToTarget;
        this.impactPower = impactPower;

        this.detonationMode = mode;
        this.landedFuse = landedFuse;
        this.hasLanded = false;
        this.spawnGraceTimer = SPAWN_GRACE;
        this.landingPointY = target.y; // aim point is on the ground (precise-aim hit)

        if (mode == DetonationMode.OnLand)
        {
            // Launch velocity reaches the aim point at exactly timeToTarget, so
            // land on a timer rather than relying on physics collision.
            timer = timeToTarget;
            safetyFuseTimer = timeToTarget + landedFuse + 1f;
        }
        else
        {
            timer = countdown + timeToTarget; // original timed behavior (enemy)
            safetyFuseTimer = 0f;
        }
    }

    private bool IsTargetValid(Collider collider)
    {
        //If friendly fire is enabled, all colliders are valid targets
        if (GameManager.instance.friendlyFire)
            return true;

        //If collider is on allyLayer, target is not valid
        if((allyLayerMask.value & (1 << collider.gameObject.layer)) > 0)
            return false;

        return true;
    }

    public static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 target, float timeToTarget)
    {
        Vector3 direction = target - start;
        Vector3 directionXZ = new Vector3(direction.x, 0, direction.z);

        Vector3 velocityXZ = directionXZ / timeToTarget;

        float velocityY =
            (direction.y - (Physics.gravity.y * Mathf.Pow(timeToTarget, 2)) / 2) / timeToTarget;

        Vector3 launchVelocity = velocityXZ + Vector3.up * velocityY;

        return launchVelocity;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, impactRadius);
    }
}
