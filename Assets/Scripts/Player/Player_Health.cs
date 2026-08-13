using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishNet;
using FishNet.Connection;
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
            savedHealth = maxHealth;
        currentHealth.Value = savedHealth;
    }

    public override void ReduceHealth(int damage)
    {
        if (_isDying) return; // Already dying — ignore further damage
        base.ReduceHealth(damage);
        if (ShouldDie() && !isDead.Value)
            Die();
    }

    // ── Death ──────────────────────────────────────────────────

    private FishNet.Object.NetworkObject _corpseLootObj;
    private const float RespawnDelay = 10f;

    private bool _isDying = false; // Local guard against double-Die (SyncVar sync has a frame delay)

    private void Die()
    {
        if (_isDying) return; // Local guard — SyncVar isDead may not have synced yet
        _isDying = true;
        isDead.Value = true;

        if (IsServer)
        {
            // Drop all in-raid items into a standalone CorpseLoot prefab at the death position.
            if (player.inventory != null)
            {
                var droppedItems = player.inventory.DropAllItems();
                SpawnCorpseLoot(droppedItems);
            }

            // Clear in-raid save state (backpack/equipment) but preserve stash.
            if (ServerDataManager.Instance != null && !string.IsNullOrEmpty(player.playerID))
                ServerDataManager.Instance.ClearPlayerState(player.playerID);

            // Start timed respawn (10 seconds).
            StartCoroutine(TimedRespawn());
        }
    }

    private IEnumerator TimedRespawn()
    {
        // Wait for the respawn delay. The corpse stays searchable during this time.
        yield return new WaitForSeconds(RespawnDelay);

        if (!isDead.Value) yield break; // already revived (edge case)

        // Collect remaining (un-looted) items from the CorpseLoot container.
        List<Enemy_LootContainer.LootSlot> remainingItems = null;
        Debug.Log($"[Player_Health] TimedRespawn: _corpseLootObj={(_corpseLootObj!=null?_corpseLootObj.name:"NULL")}");
        if (_corpseLootObj != null)
        {
            var loot = _corpseLootObj.GetComponent<Enemy_LootContainer>();
            if (loot != null)
            {
                Debug.Log($"[Player_Health] TimedRespawn: lootSlots.Count={loot.lootSlots.Count}");
                remainingItems = loot.GetRemainingLoot();
                Debug.Log($"[Player_Health] TimedRespawn: GetRemainingLoot returned {remainingItems.Count} items");
            }
            else
                Debug.LogWarning("[Player_Health] TimedRespawn: CorpseLoot has no Enemy_LootContainer!");
            // Despawn the corpse.
            FishNet.InstanceFinder.ServerManager.Despawn(_corpseLootObj);
            _corpseLootObj = null;
        }

        // Pick spawn point.
        Transform spawn = ServerDataManager.Instance != null
            ? ServerDataManager.Instance.ResolveSpawnPoint()
            : null;
        Vector3 pos = spawn != null ? spawn.position : Vector3.zero;
        Quaternion rot = spawn != null ? spawn.rotation : Quaternion.identity;

        // Revive.
        currentHealth.Value = maxHealth;
        isDead.Value = false;
        _isDying = false; // Reset death guard for next death
        if (player.ragdoll != null) player.ragdoll.RagdollActive(false);
        if (player.anim != null) player.anim.enabled = true;
        
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = pos;
        transform.rotation = rot;
        if (cc != null) cc.enabled = true;

        RpcTeleport(pos, rot);

        // Restore remaining items to the player's inventory.
        // Always call RestoreLootItems — even when there are no remaining items. DropAllItems()
        // Clear()s netEquipment/netBackpack down to 0 length; RestoreLootItems is what re-pads
        // them back to capacity (BackpackCapacity / EquipSlotCount) via empty-Add ops. Skip it
        // and the SyncLists stay length 0: weaponSlots keeps a ghost gun (no ClearWeaponSlot Add
        // event fires), and CmdPickupWeapon later index-out-of-ranges on the empty list.
        if (player.inventory != null)
        {
            Debug.Log($"[Player_Health] Restoring {remainingItems?.Count ?? 0} items to {player.playerID}. netBackpack.Count={player.inventory.netBackpack.Count}");
            player.inventory.RestoreLootItems(remainingItems ?? new List<Enemy_LootContainer.LootSlot>());
        }
        else
        {
            Debug.Log($"[Player_Health] No inventory to restore items for {player.playerID}.");
        }

        Debug.Log($"[Player_Health] Player {player.playerID} auto-respawned at {pos}.");
    }

    [ObserversRpc]
    private void RpcTeleport(Vector3 pos, Quaternion rot)
    {
        if (IsServer) return; // Server/Host already teleported locally
        
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = pos;
        transform.rotation = rot;
        if (cc != null) cc.enabled = true;
    }

    [SerializeField] private GameObject corpseLootPrefab;

    private void SpawnCorpseLoot(List<Enemy_LootContainer.LootSlot> droppedItems)
    {
        if (droppedItems == null || droppedItems.Count == 0) return;
        if (corpseLootPrefab == null)
        {
            corpseLootPrefab = Resources.Load<GameObject>("CorpseLoot");
            if (corpseLootPrefab == null) return;
        }

        Vector3 deathPos = transform.position;
        var corpseGO = Instantiate(corpseLootPrefab, deathPos, Quaternion.identity);
        // Spawn FIRST — FishNet SyncLists are cleared during Spawn if modified before spawning.
        FishNet.InstanceFinder.ServerManager.Spawn(corpseGO, null);
        _corpseLootObj = corpseGO.GetComponent<FishNet.Object.NetworkObject>();
        var loot = corpseGO.GetComponent<Enemy_LootContainer>();
        if (loot != null)
        {
            loot.ServerPopulateLoot(droppedItems);
            // Bind the loot trigger to the dead player's body. The CorpseLoot prefab has no
            // renderer/animator of its own, so without this each client can neither glow the
            // corpse mesh nor track the ragdoll for the search prompt. Must precede EnableLootTrigger().
            loot.LinkToCorpseBody(player.NetworkObject);
            loot.EnableLootTrigger();
        }
        Debug.Log($"[Player_Health] Spawned CorpseLoot at {deathPos} with {droppedItems.Count} items.");
    }

    private void OnDeathChanged(bool oldVal, bool newVal, bool asServer)
    {
        if (newVal)
        {
            Debug.Log("Player was killed at " + Time.time);
            player.anim.enabled = false;
            player.ragdoll.RagdollActive(true);

            if (IsOwner)
            {
                if (_damageVignette != null)
                    _damageVignette.color = new Color(1, 0, 0, 0);
                ShowDeathScreen();
            }
        }
        else
        {
            // Revived
            player.anim.enabled = true;
            player.ragdoll.RagdollActive(false);
            if (IsOwner)
            {
                HideDeathScreen();
                // Re-create HUD/Inventory UI — OnStartClient doesn't re-fire on same-body respawn,
                // so the UI that was created at initial spawn may be stale or missing.
                if (player.inventory != null)
                    player.inventory.RebuildUI();
            }
        }
    }

    // ── Death Screen UI (10s countdown) ────────────────────────

    private GameObject _deathScreen;
    private Text _countdownText;

    private IEnumerator UpdateCountdown()
    {
        float remaining = RespawnDelay;
        while (remaining > 0 && isDead.Value && _countdownText != null)
        {
            _countdownText.text = $"{remaining:F0} 秒后复活...";
            yield return new WaitForSeconds(0.1f);
            remaining -= 0.1f;
        }
    }

    private void ShowDeathScreen()
    {
        if (_deathScreen == null)
        {
            _deathScreen = new GameObject("DeathScreen");
            var canvas = _deathScreen.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = _deathScreen.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _deathScreen.AddComponent<GraphicRaycaster>();

            // Dark overlay
            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(_deathScreen.transform, false);
            var overlayImg = overlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.7f);
            var ort = overlay.GetComponent<RectTransform>();
            ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero; ort.offsetMax = Vector2.zero;

            // "You Died" text
            var textObj = new GameObject("DeathText");
            textObj.transform.SetParent(_deathScreen.transform, false);
            var deathText = textObj.AddComponent<Text>();
            deathText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            deathText.text = "你已死亡";
            deathText.fontSize = 64;
            deathText.color = Color.red;
            deathText.alignment = TextAnchor.MiddleCenter;
            deathText.raycastTarget = false;
            var trt = deathText.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.7f); trt.anchorMax = new Vector2(0.5f, 0.7f);
            trt.sizeDelta = new Vector2(600, 100);

            // Countdown text (updated by coroutine)
            var countdownObj = new GameObject("CountdownText");
            countdownObj.transform.SetParent(_deathScreen.transform, false);
            _countdownText = countdownObj.AddComponent<Text>();
            _countdownText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _countdownText.text = $"{RespawnDelay:F0} 秒后复活...";
            _countdownText.fontSize = 36;
            _countdownText.color = Color.white;
            _countdownText.alignment = TextAnchor.MiddleCenter;
            _countdownText.raycastTarget = false;
            var crt = _countdownText.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(600, 60);
        }

        _deathScreen.SetActive(true);
        
        StartCoroutine(UpdateCountdown());

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HideDeathScreen()
    {
        if (_deathScreen != null)
            _deathScreen.SetActive(false);
    }

    // ── Damage Vignette (unchanged) ───────────────────────────

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
