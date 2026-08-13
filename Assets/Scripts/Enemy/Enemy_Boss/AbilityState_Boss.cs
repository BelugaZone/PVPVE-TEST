using UnityEngine;

public class AbilityState_Boss : EnemyState
{
    private Enemy_Boss enemy;
    private bool isStopping;

    public AbilityState_Boss(Enemy enemyBase, EnemyStateMachine stateMachine, string animBoolName) : base(enemyBase, stateMachine, animBoolName)
    {
        enemy = enemyBase as Enemy_Boss;
    }

    public override void Enter()
    {
        base.Enter();

        stateTimer = enemy.flamethrowDuration;
        isStopping = false;

        enemy.agent.isStopped = true;
        enemy.agent.velocity = Vector3.zero;
        enemy.RpcEnableWeaponTrail(true);
    }

    public override void Update()
    {
        base.Update();

        if (enemy.player != null)
            enemy.FaceTarget(enemy.player.position);

        if (!isStopping && ShouldDisableFlamethrower())
        {
            DisableFlamethrower();
            if (enemy.bossWeaponType == BossWeaponType.Flamethrower)
            {
                isStopping = true;
                stateTimer = 1.5f; // Fallback timer in case animation event is missing
            }
        }

        if (triggerCalled || (isStopping && stateTimer < 0))
            stateMachine.ChangeState(enemy.moveState);
    }

    private bool ShouldDisableFlamethrower() => stateTimer < 0;

    public void DisableFlamethrower()
    {
        if (enemy.bossWeaponType != BossWeaponType.Flamethrower)
            return;

        if (enemy.flamethrowActive == false)
            return;

        enemy.ActivateFlamethrower(false);
    }

    public override void AbilityTrigger()
    {
        base.AbilityTrigger();

        if (enemy.bossWeaponType == BossWeaponType.Flamethrower)
        {
            enemy.ActivateFlamethrower(true);
            enemy.RpcDischargeBatteries();
            enemy.RpcEnableWeaponTrail(false);
        }

        if (enemy.bossWeaponType == BossWeaponType.Hummer)
        {
            enemy.ActivateHummer();
        }
    }

    public override void Exit()
    {
        base.Exit();
        enemy.SetAbilityOnCooldown();
        enemy.RpcResetBatteries();
    }
}
