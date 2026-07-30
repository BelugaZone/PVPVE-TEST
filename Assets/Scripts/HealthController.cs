using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

public class HealthController : NetworkBehaviour
{
    public int maxHealth;
    
    public readonly SyncVar<int> currentHealth = new SyncVar<int>();

    private void Awake()
    {
        currentHealth.OnChange += OnHealthChanged;
    }


    public override void OnStartServer()
    {
        base.OnStartServer();
        currentHealth.Value = maxHealth;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        if (currentHealth.Value <= 0) return;

        if (GetComponent<UI_HealthBar>() == null)
        {
            UI_HealthBar ui = gameObject.AddComponent<UI_HealthBar>();
            bool isPlayer = GetComponent<Player>() != null;
            ui.Initialize(this, IsOwner, isPlayer);
        }
    }

    public virtual void ReduceHealth(int damage)
    {
        if (!base.IsServer) return;
        currentHealth.Value -= damage;

        ServerLogger.LogCombat($"{gameObject.name} took {damage} damage. Health: {currentHealth.Value}/{maxHealth}");

        if (currentHealth.Value <= 0)
        {
            ServerLogger.LogCombat($"{gameObject.name} has died.");
        }
    }

    public virtual void IncreaseHealth()
    {
        if (!base.IsServer) return;
        currentHealth.Value++;

        if(currentHealth.Value > maxHealth)
            currentHealth.Value = maxHealth;
    }

    public bool ShouldDie() => currentHealth.Value <= 0;

    protected virtual void OnHealthChanged(int oldHealth, int newHealth, bool asServer)
    {
        // Override in children for UI/effects
    }
}
