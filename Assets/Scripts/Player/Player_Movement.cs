using UnityEngine;
using FishNet.Object;

public class Player_Movement : NetworkBehaviour
{
    private Player player;

    private CharacterController characterController;
    private PlayerControls controls;
    private Animator animator;

    [Header("Movement info")]
    [SerializeField] private float walkSpeed;
    [SerializeField] private float runSpeed;
    [SerializeField] private float turnSpeed;
    private float speed;
    private float verticalVelocity;

    [Header("Roll info")]
    [SerializeField] private float rollSpeed = 3.5f; 
    [SerializeField] private float rollDuration = 1.2f;
    private float rollTimer;
    public bool isRolling { get; private set; }
    private Vector3 rollDirection;

    public Vector2 moveInput { get; private set; }
    private Vector3 movementDirection;

    private bool isRunning;

    private void Awake()
    {
        // Force these values so that any old Prefab/Scene overrides don't break the movement!
        rollSpeed = 3.5f;
        rollDuration = 1.2f;
    }

    private void Start()
    {
        player = GetComponent<Player>();

        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        
        // Force root motion off to prevent Mixamo 100x scale catapult bugs
        animator.applyRootMotion = false;

        speed = walkSpeed;


        AssignInputEvents();
    }


    private float proxyXVelocity;
    private float proxyZVelocity;
    private bool proxyIsRunning;
    private float lastAnimSyncTime;

    private void Update()
    {
        if (player.health.isDead.Value)
            return;

        if (!base.IsOwner)
        {
            if (animator != null)
            {
                animator.SetFloat("xVelocity", proxyXVelocity, .1f, Time.deltaTime);
                animator.SetFloat("zVelocity", proxyZVelocity, .1f, Time.deltaTime);
                animator.SetBool("isRunning", proxyIsRunning);
            }
            return;
        }

        // Temporary input check for testing. 
        // We recommend adding a "Roll" action in PlayerControls.inputactions later.
        if (UnityEngine.Input.GetKeyDown(KeyCode.Space) && !isRolling)
        {
            Player_WeaponController weaponController = GetComponent<Player_WeaponController>();
            if (weaponController != null && weaponController.WeaponReady())
            {
                LocalStartRoll();
            }
        }

        if (isRolling)
        {
            ApplyRollMovement();
            return;
        }

        ApplyMovement();
        ApplyRotation();
        AnimatorControllers();
    }

    private void LocalStartRoll()
    {
        StartRollLogic();
        if (base.IsServer)
            RpcStartRoll();
        else
            CmdStartRoll();
    }

    [ServerRpc]
    private void CmdStartRoll()
    {
        RpcStartRoll();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcStartRoll()
    {
        StartRollLogic();
    }

    private void StartRollLogic()
    {
        isRolling = true;
        rollTimer = rollDuration;

        if (movementDirection.magnitude > 0)
            rollDirection = movementDirection.normalized;
        else
            rollDirection = transform.forward;
        
        // Force character to face the roll direction immediately
        transform.rotation = Quaternion.LookRotation(rollDirection);

        // Disable upper body weapon layers to fix animation blending issues
        for (int i = 1; i < animator.layerCount; i++)
        {
            animator.SetLayerWeight(i, 0f);
        }
        
        // Disable IK rig so arms don't lock to the gun during roll
        player.weaponVisuals.EnableRigAndIK(false);

        // Speed up the animation to match the gameplay duration
        // The original Mixamo Roll is 1.783 seconds long. 
        animator.speed = 1.783f / rollDuration;
        animator.SetTrigger("Roll");
    }

    private void ApplyRollMovement()
    {
        rollTimer -= Time.deltaTime;
        
        // The Mixamo 'Falling To Roll' animation actually plants its feet around 70%.
        // We must completely stop movement during the final 30% to prevent any "ice skating" or sliding.
        float normalizedTime = 1f - Mathf.Clamp01(rollTimer / rollDuration); 
        float speedMultiplier = 1f;

        if (normalizedTime > 0.55f && normalizedTime <= 0.9f)
        {
            // Smoothly decelerate from 1 to 0 between 45% and 70% of the roll
            speedMultiplier = Mathf.Lerp(1f, 0f, (normalizedTime - 0.55f) / 0.25f);
        }
        else if (normalizedTime > 0.9f)
        {
            // Fully stopped while standing up
            speedMultiplier = 0f;
        }

        Vector3 moveDir = rollDirection * (rollSpeed * speedMultiplier); 
        
        // Ignore gravity during roll to prevent slope-sliding catapult bugs
        moveDir.y = 0f; 

        characterController.Move(moveDir * Time.deltaTime);

        if (rollTimer <= 0)
        {
            isRolling = false;
            
            // Restore weapon layers specifically for current weapon
            WeaponModel currentModel = player.weaponVisuals.CurrentWeaponModel();
            int currentLayerIndex = currentModel != null ? (int)currentModel.holdType : -1;
            
            for (int i = 1; i < animator.layerCount; i++)
            {
                animator.SetLayerWeight(i, i == currentLayerIndex ? 1f : 0f);
            }
            
            // Re-enable IK rig
            player.weaponVisuals.EnableRigAndIK(true);
            
            // Restore animator speed and force it back to Locomotion to prevent getting stuck
            animator.speed = 1f;
            animator.CrossFade("Idle/Walk", 0.1f, 0);
        }
    }

    private void AnimatorControllers()
    {
        float xVelocity = Vector3.Dot(movementDirection.normalized, transform.right);
        float zVelocity = Vector3.Dot(movementDirection.normalized, transform.forward);

        animator.SetFloat("xVelocity", xVelocity, .1f, Time.deltaTime);
        animator.SetFloat("zVelocity", zVelocity, .1f, Time.deltaTime);

        bool playRunAnimation = isRunning & movementDirection.magnitude > 0;
        animator.SetBool("isRunning", playRunAnimation);

        if (Time.time - lastAnimSyncTime > 0.1f)
        {
            lastAnimSyncTime = Time.time;
            CmdSyncAnim(xVelocity, zVelocity, playRunAnimation);
        }
    }

    [ServerRpc]
    private void CmdSyncAnim(float x, float z, bool run)
    {
        RpcSyncAnim(x, z, run);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void RpcSyncAnim(float x, float z, bool run)
    {
        proxyXVelocity = x;
        proxyZVelocity = z;
        proxyIsRunning = run;
    }
    private void ApplyRotation()
    {
        Vector3 lookingDirection = player.aim.GetMouseHitInfo().point - transform.position;
        lookingDirection.y = 0f;
        lookingDirection.Normalize();

        Quaternion desiredRotation = Quaternion.LookRotation(lookingDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, turnSpeed * Time.deltaTime);

    }
    private void ApplyMovement()
    {
        movementDirection = new Vector3(moveInput.x, 0, moveInput.y);
        ApplyGravity();

        if (movementDirection.magnitude > 0)
        {
            float currentSpeed = speed;
            if (player.aim != null && player.aim.isAimingPrecisly)
            {
                currentSpeed *= 0.7f; // Reduce speed by 30% when aiming
            }

            characterController.Move(movementDirection * Time.deltaTime * currentSpeed);
        }
    }
    private void ApplyGravity()
    {
        if (characterController.isGrounded == false)
        {
            verticalVelocity -= 9.81f * Time.deltaTime;
            movementDirection.y = verticalVelocity;
        }
        else
            verticalVelocity = -.5f;
    }
    private void AssignInputEvents()
    {
        controls = player.controls;

        controls.Character.Movement.performed += context => moveInput = context.ReadValue<Vector2>();
        controls.Character.Movement.canceled += context => moveInput = Vector2.zero;

        controls.Character.Run.performed += context =>
        {
            speed = runSpeed;
            isRunning = true;
        };


        controls.Character.Run.canceled += context =>
        {
            speed = walkSpeed;
            isRunning = false;
        };
    }
}