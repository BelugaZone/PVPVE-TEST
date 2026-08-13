using UnityEngine;
using FishNet.Object;
using Cinemachine;

public class Player : NetworkBehaviour
{
    public Transform playerBody;
    
    [HideInInspector]
    public string playerID;

    public PlayerControls controls { get; private set; }
    public Player_AimController aim { get; private set; }
    public Player_Movement movement { get; private set; }
    public Player_WeaponController weapon { get; private set; }
    public Player_WeaponVisuals weaponVisuals { get; private set; }
    public Player_Interaction interaction { get; private set; }
    public Player_Health health { get; private set; }
    public Ragdoll ragdoll { get; private set; }
    public Player_FOV fov { get; private set; }
    public PlayerInventory inventory { get; private set; }
    public Enemy_LootContainer lootContainer { get; private set; }

    public Animator anim { get; private set; }

    private void Awake()
    {
        controls = new PlayerControls();

        anim = GetComponentInChildren<Animator>();
        ragdoll = GetComponent<Ragdoll>();
        health = GetComponent<Player_Health>();
        aim = GetComponent<Player_AimController>();
        movement = GetComponent<Player_Movement>();
        weapon = GetComponent<Player_WeaponController>();
        weaponVisuals = GetComponent<Player_WeaponVisuals>();
        interaction = GetComponent<Player_Interaction>();
        fov = GetComponent<Player_FOV>();
        inventory = GetComponent<PlayerInventory>();
        lootContainer = GetComponentInChildren<Enemy_LootContainer>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Phase 0's lobby-scene gameplay-disable guard has been removed. Since Phase 1,
        // the full Player prefab is only spawned in the Game scene (ServerDataManager spawns
        // LobbyPlayer in the Lobby). The old guard checked GetActiveScene().name on the CLIENT,
        // but the client's active scene may still be "Lobby" during the scene-transition window
        // (server loads Game first, client follows), which falsely disabled gameplay components.

        if (base.IsOwner)
        {
            controls.Enable();
        }
    }

    private void DisableBehaviour(MonoBehaviour nb)
    {
        if (nb != null) nb.enabled = false;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (ServerDataManager.Instance != null && !string.IsNullOrEmpty(playerID))
        {
            ServerDataManager.Instance.SavePlayerState(playerID, this);
            ServerDataManager.Instance.SaveAllData();
        }
    }

    private void OnDisable()
    {
        if (base.IsOwner && controls != null)
            controls.Disable();
    }
}
