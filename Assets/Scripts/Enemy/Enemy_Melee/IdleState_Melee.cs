using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IdleState_Melee : EnemyState
{
    private Enemy_Melee enemy;

    public IdleState_Melee(Enemy enemyBase, EnemyStateMachine stateMachine, string animBoolName) : base(enemyBase, stateMachine, animBoolName)
    {
        enemy = enemyBase as Enemy_Melee;
    }

    public override void Enter()
    {
        base.Enter();

        enemy.SetAnimFloat("IdleAnimIndex", Random.Range(0, 3)); // we have 3 animtions with index 0 to 2

        enemy.visuals.EnableIK(true, false);

        stateTimer = enemy.idleTime;
    }

    public override void Update()
    {
        base.Update();


        if (stateTimer < 0)
            stateMachine.ChangeState(enemy.moveState);
    }
}
