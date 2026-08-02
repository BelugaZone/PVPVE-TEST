using UnityEngine;

public class MoveState_Boss : EnemyState
{
    private Enemy_Boss enemy;
    private Vector3 destination;

    private float actionTimer;
    private float timeBeforeSpeedUp = 5;

    private bool speedUpActivated;
    public MoveState_Boss(Enemy enemyBase, EnemyStateMachine stateMachine, string animBoolName) : base(enemyBase, stateMachine, animBoolName)
    {
        enemy = enemyBase as Enemy_Boss;
    }

    public override void Enter()
    {
        base.Enter();

        SpeedReset();
        enemy.agent.isStopped = false;

        destination = enemy.GetPatrolDestination();
        enemy.agent.SetDestination(destination);

        actionTimer = enemy.actionCooldown;
    }

    private void SpeedReset()
    {
        speedUpActivated = false;
        enemy.SetAnimFloat("MoveAnimSpeedMultiplier", 1);
        enemy.SetAnimFloat("MoveAnimIndex", 0);
        enemy.agent.speed = enemy.walkSpeed;
    }

    public override void Update()
    {
        base.Update();

        actionTimer -= Time.deltaTime;
        enemy.FaceTarget(GetNextPathPoint());

        if (enemy.inBattleMode)
        {
            if (enemy.player == null)
            {
                enemy.EnterBattleMode(); // Actually we shouldn't be in battle mode if player is null, but let base update handle it, just fallback here.
                return;
            }

            if (ShouldSpeedUp())
                SpeedUp();

            Vector3 playerPos = enemy.player.position;
            enemy.agent.SetDestination(playerPos);

            if (actionTimer < 0)
            {
                PerformRandomAction();
            }
            else if (enemy.PlayerInAttackRange())
                stateMachine.ChangeState(enemy.attackState);
        }
        else
        {
            if (Vector3.Distance(enemy.transform.position, destination) < .25f)
                stateMachine.ChangeState(enemy.idleState);
        }
    }

    private void SpeedUp()
    {
        enemy.agent.speed = enemy.runSpeed;
        enemy.SetAnimFloat("MoveAnimIndex", 1);
        enemy.SetAnimFloat("MoveAnimSpeedMultiplier", 1.5f);
        speedUpActivated = true;
    }

    private void PerformRandomAction()
    {
        actionTimer = enemy.actionCooldown;

        if (Random.Range(0, 2) == 0) // rolls number from 0 to 1
        {
            TryAbility();
        }
        else
        {
            if (enemy.CanDoJumpAttack())
                stateMachine.ChangeState(enemy.jumpAttackState);
            else if (enemy.bossWeaponType == BossWeaponType.Hummer)
                TryAbility();
        }
    }

    private void TryAbility()
    {
        if (enemy.CanDoAbility())
            stateMachine.ChangeState(enemy.abilityState);
    }

    private bool ShouldSpeedUp()
    {
        if (speedUpActivated)
            return false;

        if (Time.time > enemy.attackState.lastTimeAttacked + timeBeforeSpeedUp)
        {
            return true;
        }

        return false;
    }

}
