using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;

public enum BossWeaponType {  Flamethrower, Hummer}

public class Enemy_Boss : Enemy
{
    [Header("Boss details")]
    public BossWeaponType bossWeaponType;
    public float actionCooldown = 10;
    public float attackRange;

    [Header("Ability")]
    public float minAbilityDistance;
    public float abilityCooldown;
    private float lastTimeUsedAbility;

    [Header("Flamethrower")]
    public int flameDamage;
    public float flameDamageCooldown;
    public ParticleSystem flamethrower;
    public float flamethrowDuration;
    public bool flamethrowActive { get; private set; }

    [Header("Hummer")]
    public int hummerActiveDamage;
    public GameObject activationPrefab;
    [SerializeField] private float hummerCheckRadius;


    [Header("Jump attack")]
    public int jumpAttackDamage;
    public float jumpAttackCooldown = 10;
    private float lastTimeJumped;
    public float travelTimeToTarget = 1;
    public float minJumpDistanceRequired;
    [Space]
    public float impactRadius = 2.5f;
    public float impactPower = 5;
    public Transform impactPoint;
    [SerializeField] private float upforceMultiplier = 10;
    [Space]
    [SerializeField] private LayerMask whatToIngore;

    [Header("Attack")]
    [SerializeField] private int meleeAttackDamage;
    [SerializeField] private Transform[] damagePoints;
    [SerializeField] private float attackCheckRadius;
    [SerializeField] private GameObject meleeAttackFx;


    public IdleState_Boss idleState { get; private set; }
    public MoveState_Boss moveState { get; private set; }
    public AttackState_Boss attackState { get; private set; }
    public JumpAttackState_Boss jumpAttackState { get; private set; }
    public AbilityState_Boss abilityState { get; private set; }
    public DeadState_Boss deadState { get; private set; }

    public Enemy_BossVisuals bossVisuals { get; private set; }
    protected override void Awake()
    {
        base.Awake();

        // Default configurations if not set in Inspector
        if (fovAngle <= 0f) fovAngle = 90f;
        if (chaseRange <= 0f) chaseRange = 25f;
        if (aggresionRange <= 0f) aggresionRange = 10f;
        
        // Halve the movement speed as requested
        walkSpeed /= 2f;
        runSpeed /= 2f;

        bossVisuals = GetComponent<Enemy_BossVisuals>();

        idleState = new IdleState_Boss(this, stateMachine, "Idle");
        moveState = new MoveState_Boss(this, stateMachine, "Move");
        attackState = new AttackState_Boss(this, stateMachine, "Attack");
        jumpAttackState = new JumpAttackState_Boss(this, stateMachine, "JumpAttack");
        abilityState = new AbilityState_Boss(this, stateMachine, "Ability");
        deadState = new DeadState_Boss(this, stateMachine, "Idle"); // Idle is just a placeholder we use ragdoll
    }

    protected override void Start()
    {
        base.Start();

        if (!isDead)
            stateMachine.Initialize(idleState);
    }

    protected override void Update()
    {
        base.Update();
        
        if (!base.IsServer) return;

        stateMachine.currentState.Update();

        MeleeAttackCheck(damagePoints, attackCheckRadius, meleeAttackFx,meleeAttackDamage);
    }


    public override void Die()
    {
        base.Die();


        if (stateMachine.currentState != deadState)
            stateMachine.ChangeState(deadState);
    }

    public override void EnterBattleMode()
    {
        if (inBattleMode)
            return;

        base.EnterBattleMode();
        stateMachine.ChangeState(moveState);
    }


    public void ActivateFlamethrower(bool activate)
    {
        if (base.IsServer)
            RpcActivateFlamethrower(activate);
    }

    [ObserversRpc(ExcludeServer = false)]
    private void RpcActivateFlamethrower(bool activate)
    {
        flamethrowActive = activate;

        if (!activate)
        {
            flamethrower.Stop();
            SetAnimTrigger("StopFlamethrower");
            Debug.Log("flame stopped");
            return;
        }

        var mainModule = flamethrower.main;
        var extraModule = flamethrower.transform.GetChild(0).GetComponent<ParticleSystem>().main;

        mainModule.duration = flamethrowDuration;
        extraModule.duration = flamethrowDuration;

        flamethrower.Clear();
        flamethrower.Play();
    }

    public void ActivateHummer()
    {
        if (base.IsServer)
        {
            MassDamage(damagePoints[0].position, hummerCheckRadius, hummerActiveDamage);
            RpcActivateHummer();
        }
    }

    [ObserversRpc(ExcludeServer = false)]
    private void RpcActivateHummer()
    {
        GameObject newActivation = ObjectPool.instance.GetObject(activationPrefab, impactPoint);
        ObjectPool.instance.ReturnObject(newActivation, 1);
    }

    public bool CanDoAbility()
    {
        if (player == null) return false;
        bool playerWithinDistance = Vector3.Distance(transform.position, player.position) < minAbilityDistance;

        if (playerWithinDistance == false)
            return false;

        if (Time.time > lastTimeUsedAbility + abilityCooldown)
        {
            return true;
        }

        return false;
    }

    public void SetAbilityOnCooldown() => lastTimeUsedAbility = Time.time;

    public void JumpImpact()
    {
        Transform impactPt = this.impactPoint;

        if (impactPt == null)
            impactPt = transform;

        // Called by animation event on all clients. Route damage through server only, handle physics locally.
        if (base.IsServer)
        {
            MassDamage(impactPt.position, impactRadius, jumpAttackDamage);
            RpcJumpImpact(impactPt.position);
        }
    }

    [ObserversRpc(ExcludeServer = false)]
    private void RpcJumpImpact(Vector3 impactPos)
    {
        // Visuals or physics that need to happen exactly on impact can go here
        // Currently handled partially by MassDamage physics
    }

    private void MassDamage(Vector3 impactPoint, float impactRadius,int damage)
    {
        HashSet<GameObject> uniqueEntities = new HashSet<GameObject>();
        Collider[] colliders = Physics.OverlapSphere(impactPoint, impactRadius, ~whatIsAlly);

        foreach (Collider hit in colliders)
        {
            IDamagable damagable = hit.GetComponent<IDamagable>();

            if (damagable != null)
            {
                GameObject rootEntity = hit.transform.root.gameObject;

                if (uniqueEntities.Add(rootEntity) == false)
                    continue;

                // Only apply damage on Server!
                if (base.IsServer)
                {
                    Debug.Log(hit.transform.root.name + " Was damaged!!!");
                    damagable.TakeDamage(damage);
                }
            }

            // Apply forces on all clients/server for physics sync
            ApplyPhysicalForceTo(impactPoint, impactRadius, hit);
        }
    }

    private void ApplyPhysicalForceTo(Vector3 impactPoint, float impactRadius, Collider hit)
    {
        Rigidbody rb = hit.GetComponent<Rigidbody>();

        if (rb != null)
            rb.AddExplosionForce(impactPower, impactPoint, impactRadius, upforceMultiplier, ForceMode.Impulse);
    }

    public bool CanDoJumpAttack()
    {
        if (player == null) return false;
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer < minJumpDistanceRequired)
            return false;

        if (Time.time > lastTimeJumped + jumpAttackCooldown && IsPlayerInClearSight())
        {
            return true;
        }

        return false;
    }

    public void SetJumpAttackOnCooldown() => lastTimeJumped = Time.time;

    public bool IsPlayerInClearSight()
    {
        if (player == null) return false;
        Vector3 myPos = transform.position + new Vector3(0, 1.5f, 0);
        Vector3 playerPos = player.position + Vector3.up;
        Vector3 directionToPlayer = (playerPos - myPos).normalized;

        if (Physics.Raycast(myPos, directionToPlayer, out RaycastHit hit, 100, ~whatToIngore))
        {
            if (hit.transform.root == player.root)
                return true;
        }

        return false;
    }
    public bool PlayerInAttackRange() => player != null && Vector3.Distance(transform.position, player.position) < attackRange;

    protected override void SpawnMeleeHitFx(int damagePointIndex)
    {
        if (damagePoints != null && damagePoints.Length > damagePointIndex)
        {
            Transform attackPoint = damagePoints[damagePointIndex];
            if (meleeAttackFx != null)
            {
                GameObject newAttackFx = ObjectPool.instance.GetObject(meleeAttackFx, attackPoint);
                ObjectPool.instance.ReturnObject(newAttackFx, 1);
            }
        }
    }

    protected override void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (player != null)
        {
            Vector3 myPos = transform.position + new Vector3(0, 1.5f, 0);
            Vector3 playerPos = player.position + Vector3.up;

            Gizmos.color = Color.yellow;

            Gizmos.DrawLine(myPos, playerPos);
        }

        

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, minAbilityDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, impactRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minJumpDistanceRequired);

        if (damagePoints.Length > 0)
        {
            foreach(var damagePoint in damagePoints)
            {
                Gizmos.DrawWireSphere(damagePoint.position, attackCheckRadius);
            }

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(damagePoints[0].position, hummerCheckRadius);
        }


        
    }

}
