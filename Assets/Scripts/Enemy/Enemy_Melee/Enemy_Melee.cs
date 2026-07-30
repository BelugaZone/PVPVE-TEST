using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct AttackData_EnemyMelee
{
    public int attackDamage;
    public string attackName;
    public float attackRange;
    public float moveSpeed;
    public float attackIndex;
    [Range(1, 2)]
    public float animationSpeed;
    public AttackType_Melee attackType;
}
public enum AttackType_Melee { Close, Charge }
public enum EnemyMelee_Type { Regular, Shield, Dodge, AxeThrow }

public class Enemy_Melee : Enemy
{


    #region States
    public IdleState_Melee idleState { get; private set; }
    public MoveState_Melee moveState { get; private set; }
    public RecoveryState_Melee recoveryState { get; private set; }
    public ChaseState_Melee chaseState { get; private set; }
    public AttackState_Melee attackState { get; private set; }
    public DeadState_Melee deadState { get; private set; }
    public AbilityState_Melee abilityState { get; private set; }

    #endregion

    [Header("Enemy Settings")]
    public EnemyMelee_Type meleeType;
    public Enemy_MeleeWeaponType weaponType;

    [Header("Shield")]
    public int shieldDurability;
    public Transform shieldTransform;

    [Header("Dodge")]
    public float dodgeCooldown;
    private float lastTimeDodge = -10;

    [Header("Axe throw ability")]
    public int axeDamage;
    public GameObject axePrefab;
    public float axeFlySpeed;
    public float axeAimTimer;
    public float axeThrowCooldown;
    private float lastTimeAxeThrown;
    public Transform axeStartPoint;

    [Header("Attack Data")]
    public AttackData_EnemyMelee attackData;
    public List<AttackData_EnemyMelee> attackList;
    private Enemy_WeaponModel currentWeapon;
    private bool isAttackReady;
    [Space]
    [SerializeField] private GameObject meleeAttackFx;

    protected override void Awake()
    {
        base.Awake();

        idleState = new IdleState_Melee(this, stateMachine, "Idle");
        moveState = new MoveState_Melee(this, stateMachine, "Move");
        recoveryState = new RecoveryState_Melee(this, stateMachine, "Recovery");
        chaseState = new ChaseState_Melee(this, stateMachine, "Chase");
        attackState = new AttackState_Melee(this, stateMachine, "Attack");
        deadState = new DeadState_Melee(this, stateMachine, "Idle"); // Idle anim is just a place holder,we use ragdoll
        abilityState = new AbilityState_Melee(this, stateMachine, "AxeThrow");
    }

    protected override void Start()
    {
        base.Start();
        if (!isDead)
            stateMachine.Initialize(idleState);
        ResetCooldown();

        InitializePerk();
        visuals.SetupLook();
        UpdateAttackData();
    }


    protected override void Update()
    {
        base.Update();
        
        if (!base.IsServer) return;

        stateMachine.currentState.Update();


        MeleeAttackCheck(currentWeapon.damagePoints, currentWeapon.attackRadius, meleeAttackFx, attackData.attackDamage);
    }



    public override void EnterBattleMode()
    {
        if (inBattleMode)
            return;

        base.EnterBattleMode();
        stateMachine.ChangeState(recoveryState);
    }

    public override void AbilityTrigger()
    {
        base.AbilityTrigger();

        walkSpeed = walkSpeed * .6f;
        visuals.EnableWeaponModel(false);
    }


    public void UpdateAttackData()
    {
        currentWeapon = visuals.currentWeaponModel.GetComponent<Enemy_WeaponModel>();

        if (currentWeapon.weaponData != null)
        {
            attackList = new List<AttackData_EnemyMelee>(currentWeapon.weaponData.attackData);
            turnSpeed = currentWeapon.weaponData.turnSpeed;
        }
    }

    protected override void InitializePerk()
    {
        if (meleeType == EnemyMelee_Type.AxeThrow)
        {
            weaponType = Enemy_MeleeWeaponType.Throw;
        }


        if (meleeType == EnemyMelee_Type.Shield)
        {
            SetAnimFloat("ChaseIndex", 1);
            shieldTransform.gameObject.SetActive(true);
            weaponType = Enemy_MeleeWeaponType.OneHand;
        }

        if (meleeType == EnemyMelee_Type.Dodge)
        {
            weaponType = Enemy_MeleeWeaponType.Unarmed;
        }
    }

    public override void Die()
    {
        base.Die();

        // The shield and weapon have their own Rigidbodies and Colliders, 
        // which will violently push against the ragdoll bones when physics activate.
        // We must destroy them to stop the corpse from twitching/flying.
        if (shieldTransform != null)
        {
            if (shieldTransform.TryGetComponent(out Collider c)) c.enabled = false;
            if (shieldTransform.TryGetComponent(out Rigidbody rb)) Destroy(rb);
        }

        if (visuals.currentWeaponModel != null)
        {
            if (visuals.currentWeaponModel.TryGetComponent(out Collider c)) c.enabled = false;
            if (visuals.currentWeaponModel.TryGetComponent(out Rigidbody rb)) Destroy(rb);
        }

        if(stateMachine.currentState != deadState)
            stateMachine.ChangeState(deadState);
    }


    public void ActivateDodgeRoll()
    {
        if (meleeType != EnemyMelee_Type.Dodge)
            return;

        if (stateMachine.currentState != chaseState)
            return;

        if (Vector3.Distance(transform.position, player.position) < 2f)
            return;

        float dodgeAnimationDuration = GetAnimationClipDuration("Dodge roll");

        if (Time.time > dodgeCooldown + dodgeAnimationDuration + lastTimeDodge)
        {
            lastTimeDodge = Time.time;
            SetAnimTrigger("Dodge");
        }
    }

    public void ThrowAxe()
    {
        GameObject newAxe = ObjectPool.instance.GetObject(axePrefab, axeStartPoint);

        newAxe.GetComponent<Enemy_Axe>().AxeSetup(axeFlySpeed, player, axeAimTimer,axeDamage);
    }
    public bool CanThrowAxe()
    {
        if (meleeType != EnemyMelee_Type.AxeThrow)
            return false;

        if (Time.time > axeThrowCooldown + lastTimeAxeThrown)
        {
            lastTimeAxeThrown = Time.time;
            return true;
        }
        return false;
    }
    private void ResetCooldown()
    {
        lastTimeDodge -= dodgeCooldown;
        lastTimeAxeThrown -= axeThrowCooldown;
    }

    

    private float GetAnimationClipDuration(string clipName)
    {
        AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;

        foreach (AnimationClip clip in clips)
        {
            if (clip.name == clipName)
                return clip.length;
        }

        Debug.Log(clipName + "animation not found!");
        return 0;
    }

    public bool PlayerInAttackRange() => Vector3.Distance(transform.position, player.position) < attackData.attackRange;

    protected override void SpawnMeleeHitFx(int damagePointIndex)
    {
        if (currentWeapon != null && currentWeapon.damagePoints.Length > damagePointIndex)
        {
            Transform attackPoint = currentWeapon.damagePoints[damagePointIndex];
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

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackData.attackRange);

    }
}
