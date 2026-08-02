using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

public class Player_Health : HealthController
{
    private Player player;

    public readonly SyncVar<bool> isDead = new SyncVar<bool>();

    protected override void Awake()
    {
        base.Awake();
        player = GetComponent<Player>();
        isDead.OnChange += OnDeathChanged;
    }

    public void ServerInitialize(int savedHealth)
    {
        if (savedHealth <= 0)
        {
            savedHealth = maxHealth;
        }
        currentHealth.Value = savedHealth;
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

        if (IsServer)
        {
            if (player.lootContainer != null)
            {
                var droppedItems = player.inventory.DropAllItems();
                player.lootContainer.ServerPopulateLoot(droppedItems);
                player.lootContainer.EnableLootTrigger();
            }

            // Clear their save data so they start fresh when reconnecting
            if (ServerDataManager.Instance != null && !string.IsNullOrEmpty(player.playerID))
            {
                ServerDataManager.Instance.ClearPlayerState(player.playerID);
            }

            // Remove ownership from the client so that when they disconnect,
            // FishNet does not destroy this GameObject. It remains as a permanent corpse.
            base.NetworkObject.RemoveOwnership();
        }
    }

    private void OnDeathChanged(bool oldVal, bool newVal, bool asServer)
    {
        if (newVal)
        {
            Debug.Log("Player was killed at " + Time.time);
            player.anim.enabled = false;
            player.ragdoll.RagdollActive(true);
            
            if (player.lootContainer != null)
            {
                player.lootContainer.EnableLootTrigger();
            }

            if (IsOwner && _damageVignette != null)
            {
                _damageVignette.color = new Color(1, 0, 0, 0); // Hide flash on death
            }
        }
    }

    private UnityEngine.UI.Image _damageVignette;
    private Coroutine _flashRoutine;

    protected override void OnHealthChanged(int oldHealth, int newHealth, bool asServer)
    {
        base.OnHealthChanged(oldHealth, newHealth, asServer);
        
        if (!IsOwner) return;

        if (newHealth < oldHealth) // Took damage
        {
            if (player.aim != null)
            {
                player.aim.ShakeCamera(0.5f, 0.2f);
            }

            ShowDamageFlash(newHealth);
        }
        else if (newHealth > oldHealth && newHealth > maxHealth * 0.3f && _damageVignette != null)
        {
            // If healed above low health threshold, stop pulsing
            _damageVignette.color = new Color(1, 0, 0, 0);
        }
    }

    private void ShowDamageFlash(int currentHealth)
    {
        if (_damageVignette == null)
        {
            GameObject canvasObj = new GameObject("DamageCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            
            GameObject imgObj = new GameObject("DamageVignette");
            imgObj.transform.SetParent(canvas.transform, false);
            _damageVignette = imgObj.AddComponent<UnityEngine.UI.Image>();
            
            _damageVignette.sprite = CreateVignetteSprite();
            _damageVignette.color = new Color(1, 0, 0, 0);
            
            RectTransform rt = _damageVignette.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            
            _damageVignette.raycastTarget = false;
        }

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);
        
        _flashRoutine = StartCoroutine(DamageFlashRoutine(currentHealth));
    }

    private Sprite CreateVignetteSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size/2f, size/2f);
        float radius = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((dist - (radius * 0.4f)) / (radius * 0.6f)); // Soft edge
                tex.SetPixel(x, y, new Color(1, 0, 0, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private IEnumerator DamageFlashRoutine(int currentHealthVal)
    {
        Color c = _damageVignette.color;
        
        if (currentHealthVal <= (maxHealth * 0.3f))
        {
            // Low health pulse
            while (this.currentHealth.Value <= maxHealth * 0.3f && !isDead.Value)
            {
                c.a = Mathf.Lerp(0.1f, 0.5f, Mathf.PingPong(Time.time * 2f, 1f));
                _damageVignette.color = c;
                yield return null;
            }
        }
        else
        {
            // Normal hit flash
            c.a = 0.4f;
            _damageVignette.color = c;
            yield return new WaitForSeconds(0.05f);
        }

        // Fade out
        float elapsed = 0;
        float startA = _damageVignette.color.a;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(startA, 0, elapsed / 0.3f);
            _damageVignette.color = c;
            yield return null;
        }
        
        c.a = 0;
        _damageVignette.color = c;
    }
}
