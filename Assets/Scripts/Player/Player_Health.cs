using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

public class Player_Health : HealthController
{
    private Player player;

    public readonly SyncVar<bool> isDead = new SyncVar<bool>();

    private void Awake()
    {
        player = GetComponent<Player>();
        isDead.OnChange += OnDeathChanged;
    }

    public override void ReduceHealth(int damage)
    {
        base.ReduceHealth(damage);

        if (ShouldDie() && !isDead.Value)
            Die();
    }

    private void Die()
    {
        isDead.Value = true;
    }

    private void OnDeathChanged(bool oldVal, bool newVal, bool asServer)
    {
        if (newVal)
        {
            Debug.Log("Player was killed at " + Time.time);
            player.anim.enabled = false;
            player.ragdoll.RagdollActive(true);
        }
    }
}
