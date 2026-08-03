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

        // Phase 0: the full Player prefab is spawned in the Lobby scene, but its gameplay
        // scripts (Aim/Movement/WeaponVisuals/FOV/Interaction) depend on Game-scene context
        // (camera, weapon models, etc.) that does not exist in the Lobby. Disable those
        // components so they don't run per-frame Update/LateUpdate and NRE. Inventory and
        // Health are kept (Phase 1 will replace this with a dedicated stripped lobby body).
        bool inLobby = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";
        if (inLobby)
        {
            DisableBehaviour(aim);
            DisableBehaviour(movement);
            DisableBehaviour(weaponVisuals);
            DisableBehaviour(fov);
            DisableBehaviour(interaction);
            // weapon (Player_WeaponController) is needed by inventory init, but its Shoot/
            // camera calls are gated; leave it enabled, its per-frame work only runs when
            // isShooting/isMeleeAttackReady are set, which won't happen in the lobby.
            if (anim != null) anim.enabled = false;
        }

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
