using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

public class HealthController : NetworkBehaviour, IDamagable
{
    public int maxHealth;
    
    public readonly SyncVar<int> currentHealth = new SyncVar<int>();

    public void TakeDamage(int damage)
    {
        ReduceHealth(damage);
    }

    protected virtual void Awake()
    {
        currentHealth.OnChange += OnHealthChanged;
    }


    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[HealthController] OnStartServer on {gameObject.name}. maxHealth={maxHealth}, currentHealth={currentHealth.Value}");
        if (currentHealth.Value == 0)
        {
            currentHealth.Value = maxHealth;
            Debug.Log($"[HealthController] Set currentHealth to {maxHealth}");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        Debug.Log($"[HealthController] OnStartClient on {gameObject.name}. currentHealth: {currentHealth.Value}, maxHealth: {maxHealth}");

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
        currentHealth.Value = Mathf.Clamp(currentHealth.Value - damage, 0, maxHealth);

        ServerLogger.LogCombat($"{gameObject.name} took {damage} damage. Health: {currentHealth.Value}/{maxHealth}");

        if (currentHealth.Value <= 0)
        {
            ServerLogger.LogCombat($"{gameObject.name} has died.");
        }
    }

    public bool IsAtMaxHealth()
    {
        return currentHealth.Value >= maxHealth;
    }

    public void Heal(int amount)
    {
        if (!IsServer) return;
        currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0, maxHealth);
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
