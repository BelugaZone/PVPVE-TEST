using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using FishNet.Object;

[RequireComponent(typeof(NavMeshAgent))]
public class Enemy : NetworkBehaviour
{

    public LayerMask whatIsAlly;
    public LayerMask whatIsPlayer;
    [Space]
    public int healthPoints = 20;
    
    [Header("Idle data")]
    public float idleTime;
    public float aggresionRange;
    public float chaseRange = 15f;
    [Range(0, 360)] public float fovAngle = 90f;
    public LayerMask whatIsObstacle;

    [Header("Move data")]
    public float walkSpeed = 1.5f;
    public float runSpeed = 3;
    public float turnSpeed;
    private bool manualMovement;
    private bool manualRotation;

    [SerializeField] private Transform[] patrolPoints;
    private Vector3[] patrolPointsPosition;
    private int currentPatrolIndex;

    public bool inBattleMode { get; private set; }
    protected bool isMeleeAttackReady;

    public Transform player {  get; private set; }
    public Transform playerBody { get; private set; }
    public Animator anim { get; private set; }
    public NavMeshAgent agent { get; private set; }
    public EnemyStateMachine stateMachine { get; private set; }
    public Enemy_Visuals visuals { get; private set; }

    public Enemy_Health health { get; private set; }

    public Ragdoll ragdoll { get; private set; }

    public Enemy_LootContainer lootContainer { get; private set; }

    private float updatePlayerTimer;

    protected virtual void Awake()
    {
        stateMachine = new EnemyStateMachine();

        health = GetComponent<Enemy_Health>();
        ragdoll = GetComponent<Ragdoll>();
        visuals = GetComponent<Enemy_Visuals>();
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        lootContainer = GetComponentInChildren<Enemy_LootContainer>();
    }

    protected virtual void Start()
    {
        if (fovAngle <= 0f) fovAngle = 90f; // Fix Unity serialization setting new floats to 0
        
        InitializePatrolPoints();

        // Fallback: If no patrol points are assigned in the inspector, generate some random ones around spawn
        if (patrolPointsPosition == null || patrolPointsPosition.Length == 0)
        {
            patrolPointsPosition = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * 10f;
                Vector3 randomPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                if (UnityEngine.AI.NavMesh.SamplePosition(randomPos, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    patrolPointsPosition[i] = hit.position;
                }
                else
                {
                    patrolPointsPosition[i] = transform.position;
                }
            }
        }
    }

    protected virtual void Update()
    {
        if (!base.IsServer) return;
        if (isDead) return;

        updatePlayerTimer -= Time.deltaTime;
        if (updatePlayerTimer <= 0)
        {
            UpdateClosestPlayer();
            updatePlayerTimer = 1f;
        }

        if (ShouldEnterBattleMode())
            EnterBattleMode();
    }

    private void UpdateClosestPlayer()
    {
        Player[] allPlayers = FindObjectsOfType<Player>();
        float closestDistance = Mathf.Infinity;
        Player closest = null;

        foreach (Player p in allPlayers)
        {
            if (p.health != null && p.health.isDead.Value) continue;

            float dist = Vector3.Distance(transform.position, p.transform.position);
            bool canSee = false;

            if (inBattleMode)
            {
                if (dist <= chaseRange) canSee = true;
            }
            else
            {
                if (dist <= aggresionRange)
                {
                    Vector3 dirToPlayer = (p.transform.position - transform.position).normalized;
                    float angle = Vector3.Angle(transform.forward, dirToPlayer);
                    if (angle <= fovAngle / 2f)
                    {
                        Vector3 rayStart = transform.position + Vector3.up;
                        Vector3 directionToPlayer = (p.transform.position + Vector3.up) - rayStart;

                        if (Physics.Raycast(rayStart, directionToPlayer, out RaycastHit hit, Mathf.Infinity, ~whatIsAlly))
                        {
                            if (hit.transform.root == p.transform.root)
                            {
                                canSee = true;
                            }
                        }
                    }
                }
            }

            if (canSee && dist < closestDistance)
            {
                closestDistance = dist;
                closest = p;
            }
        }
        
        if (closest != null)
        {
            player = closest.transform;
            playerBody = closest.playerBody;
        }
        else
        {
            player = null;
            playerBody = null;
            inBattleMode = false;
        }
    }

    protected virtual void InitializePerk()
    {

    }

    protected bool ShouldEnterBattleMode()
    {
        if (IsPlayerInAgrresionRange() && !inBattleMode)
        {
            EnterBattleMode();
            return true;
        }

        return false;
    }

    public virtual void EnterBattleMode()
    {
        inBattleMode = true;
    }

    [HideInInspector] public bool isDead;

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[Enemy.OnStartClient] on {gameObject.name}");

        if (health != null && health.currentHealth.Value <= 0)
        {
            isDead = true;
            Die();
            DeathCleanup();
            // Late joiners: enable loot trigger so they can search the corpse
            lootContainer?.EnableLootTrigger();
            StartCoroutine(SettleRagdollRoutine());
        }
    }

    private IEnumerator SettleRagdollRoutine()
    {
        // For late joiners, hide the mesh so they don't see the ragdoll collapsing from a standing position.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach(var r in renderers)
        {
            r.enabled = false;
        }

        // Wait for physics to settle the ragdoll on the ground
        yield return new WaitForSeconds(1.5f);

        // Make the corpse visible again
        foreach(var r in renderers)
        {
            if (r != null)
                r.enabled = true;
        }
    }

    public virtual void GetHit(int damage)
    {
        if (isDead) return;

        health.ReduceHealth(damage);
        Debug.Log($"[Enemy.GetHit] on {gameObject.name}. Damage: {damage}. New Health: {health.currentHealth.Value}");

        if (health.ShouldDie())
        {
            isDead = true;
            Die();
            DeathCleanup();
            // Activate loot trigger on server side before broadcasting death
            lootContainer?.EnableLootTrigger();
            RpcDie();
            return;
        }

        EnterBattleMode();
    }

    [ObserversRpc(ExcludeServer = true)]
    public void RpcDie()
    {
        isDead = true;
        Die();
        DeathCleanup();
        // Activate loot trigger on clients so players can walk up and search
        lootContainer?.EnableLootTrigger();
    }

    private void DeathCleanup()
    {
        // Disable all root colliders so they don't push the ragdoll
        Collider[] colliders = GetComponents<Collider>();
        foreach(Collider c in colliders)
        {
            c.enabled = false;
        }

        // Disable any child colliders that don't have a Rigidbody (e.g. weapons, shields, triggers)
        // These can cause violent self-collisions with the ragdoll bones if left active.
        Collider[] childColliders = GetComponentsInChildren<Collider>();
        foreach(Collider c in childColliders)
        {
            if (c.GetComponent<Rigidbody>() == null)
            {
                c.enabled = false;
            }
        }

        // Clean up UI HealthBar if it exists
        UI_HealthBar healthBar = GetComponent<UI_HealthBar>();
        if (healthBar != null)
        {
            Destroy(healthBar);
        }

        // Disable Network components so they stop fighting local physics
        if (TryGetComponent(out FishNet.Component.Transforming.NetworkTransform nt))
            nt.enabled = false;
        if (TryGetComponent(out FishNet.Component.Animating.NetworkAnimator na))
            na.enabled = false;
            
        // Stop agent
        if (agent != null)
        {
            if (agent.isOnNavMesh)
                agent.isStopped = true;
            agent.enabled = false;
        }
    }

    public virtual void Die()
    {

    }

    public virtual void MeleeAttackCheck(Transform[] damagePoints, float attackCheckRadius,GameObject fx,int damage)
    {
        if (isMeleeAttackReady == false)
            return;

        for (int p = 0; p < damagePoints.Length; p++)
        {
            Transform attackPoint = damagePoints[p];
            Collider[] detectedHits =
                Physics.OverlapSphere(attackPoint.position, attackCheckRadius, whatIsPlayer);


            for (int i = 0; i < detectedHits.Length; i++)
            {
                IDamagable damagable = detectedHits[i].GetComponent<IDamagable>();

                if (damagable != null)
                {
                    damagable.TakeDamage(damage);
                    isMeleeAttackReady = false;
                    SpawnLocalMeleeHitFx(fx, attackPoint);
                    if (base.IsServer)
                        RpcSpawnMeleeHitFx(p);
                    return;
                }
                else
                {
                    HealthController hc = detectedHits[i].GetComponentInParent<HealthController>();
                    if (hc != null)
                    {
                        hc.ReduceHealth(damage);
                        isMeleeAttackReady = false;
                        SpawnLocalMeleeHitFx(fx, attackPoint);
                        if (base.IsServer)
                            RpcSpawnMeleeHitFx(p);
                        return;
                    }
                }
            }

        }

    }

    private void SpawnLocalMeleeHitFx(GameObject fx, Transform attackPoint)
    {
        if (fx != null)
        {
            GameObject newAttackFx = ObjectPool.instance.GetObject(fx, attackPoint);
            ObjectPool.instance.ReturnObject(newAttackFx, 1);
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    protected void RpcSpawnMeleeHitFx(int damagePointIndex)
    {
        SpawnMeleeHitFx(damagePointIndex);
    }

    protected virtual void SpawnMeleeHitFx(int damagePointIndex)
    {
        // Override in children to call local spawn with their specific fx and damagePoints array
    }

    public void EnableMeleeAttackCheck(bool enable) => isMeleeAttackReady = enable;


    public virtual void BulletImpact( Vector3 force,Vector3 hitPoint,Rigidbody rb)
    {
        if(health.ShouldDie())
            StartCoroutine(DeathImpactCourutine(force,hitPoint,rb));
    }
    private IEnumerator DeathImpactCourutine(Vector3 force, Vector3 hitPoint, Rigidbody rb)
    {
        yield return new WaitForSeconds(.1f);

        rb.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);
    }

    public void FaceTarget(Vector3 target,float turnSpeed = 0)
    {
        Vector3 direction = target - transform.position;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        Vector3 currentEulerAngels = transform.rotation.eulerAngles;

        if (turnSpeed == 0)
            turnSpeed = this.turnSpeed;

        float yRotation = 
            Mathf.LerpAngle(currentEulerAngels.y, targetRotation.eulerAngles.y, turnSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Euler(currentEulerAngels.x, yRotation, currentEulerAngels.z);
    }

    #region Network Animation Sync Wrappers

    public void SetAnimBool(string name, bool value)
    {
        anim.SetBool(name, value);
        if (base.IsServer)
            RpcSetAnimBool(name, value);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSetAnimBool(string name, bool value)
    {
        anim.SetBool(name, value);
    }

    public void SetAnimFloat(string name, float value)
    {
        anim.SetFloat(name, value);
        if (base.IsServer)
            RpcSetAnimFloat(name, value);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSetAnimFloat(string name, float value)
    {
        anim.SetFloat(name, value);
    }

    public void SetAnimTrigger(string name)
    {
        anim.SetTrigger(name);
        if (base.IsServer)
            RpcSetAnimTrigger(name);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void RpcSetAnimTrigger(string name)
    {
        anim.SetTrigger(name);
    }

    #endregion

    #region Animation events
    public void ActivateManualMovement(bool manualMovement) => this.manualMovement = manualMovement;
    public bool ManualMovementActive() => manualMovement;

    public void ActivateManualRotation(bool manualRotation) => this.manualRotation = manualRotation;
    public bool ManualRotationActive() => manualRotation;
    public void AnimationTrigger() => stateMachine.currentState.AnimationTrigger();



    public virtual void AbilityTrigger()
    {
        stateMachine.currentState.AbilityTrigger();
    }

    #endregion

    #region Patrol logic
    public Vector3 GetPatrolDestination()
    {
        if (patrolPointsPosition == null || patrolPointsPosition.Length == 0)
            return transform.position;

        Vector3 destination = patrolPointsPosition[currentPatrolIndex];

        currentPatrolIndex++;

        if (currentPatrolIndex >= patrolPointsPosition.Length)
            currentPatrolIndex = 0;

        return destination;
    }
    private void InitializePatrolPoints()
    {
        if (patrolPoints == null)
            return;

        patrolPointsPosition = new Vector3[patrolPoints.Length];

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] != null)
            {
                patrolPointsPosition[i] = patrolPoints[i].position;
                patrolPoints[i].gameObject.SetActive(false);
            }
        }
    }

    #endregion

    public bool IsPlayerInAgrresionRange()
    {
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) < aggresionRange;
    }
    
    protected virtual void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, aggresionRange);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.cyan;
        Vector3 leftBoundary = Quaternion.Euler(0, -fovAngle / 2f, 0) * transform.forward * aggresionRange;
        Vector3 rightBoundary = Quaternion.Euler(0, fovAngle / 2f, 0) * transform.forward * aggresionRange;
        Gizmos.DrawLine(transform.position, transform.position + leftBoundary);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary);
    }
}
