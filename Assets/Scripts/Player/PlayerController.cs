// Assets/Scripts/Player/PlayerController.cs
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    public PlayerInputControls InputControls;
    private PlayerMotor motor;
    private CombatSystem combat;
    private bool inputInitialized;

    // ================================================================
    //  初始化
    // ================================================================

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        combat = GetComponent<CombatSystem>();   // 新增
    }

    void Start()
    {
        // ★ 本地模式：NetworkObject 未被 Spawn，OnNetworkSpawn 不会触发
        if (!IsSpawned)
        {
            InitPlayer();
        }
    }

    public override void OnNetworkSpawn()
    {
        // A scene Player can be reused after a network shutdown. Always reset
        // the component's enabled state before evaluating ownership; a prior
        // remote spawn may have disabled this behaviour permanently.
        enabled = true;
        base.OnNetworkSpawn();
        if (!IsOwner)
        {
            enabled = false;
            Debug.Log("不是控制者");
            return;
        }

        InitPlayer();
    }

    public override void OnNetworkDespawn()
    {
        // Keep the reusable scene Player ready for the local-mode fallback.
        // NetworkObject is already despawned when this callback runs, so the
        // normal local Start() path will initialize input on the next mode
        // transition.
        enabled = true;
        if (inputInitialized)
            UIManager.Instance?.OnPlayerDespawned(GetComponent<Inventory>());
        InputControls?.Dispose();
        InputControls = null;
        inputInitialized = false;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Re-arms input when the reusable scene Player leaves a network session
    /// and is returned to local mode. Unity does not invoke Start() again for
    /// the same scene object.
    /// </summary>
    public void PrepareForLocalMode()
    {
        enabled = true;
        InitPlayer();
    }

    // ★ 本地和联机共用的初始化逻辑
    private void InitPlayer()
    {
        if (InputControls != null)
            InputControls.Dispose();
        InputControls = new PlayerInputControls();
        InputControls.Gameplay.Enable();
        inputInitialized = true;

        Transform camTarget = transform.Find("CameraTarget");
        var freeLook = FindObjectOfType<Cinemachine.CinemachineFreeLook>();
        if (freeLook != null && camTarget != null)
        {
            freeLook.Follow = camTarget;
            freeLook.LookAt = camTarget;
        }

        UIManager.Instance?.OnPlayerSpawned(GetComponent<Inventory>());
    }
    void Update()
    {
        // ★ 联机非 Owner 不处理；本地模式 (IsSpawned=false) 放行
        if (IsSpawned && !IsOwner) return;
        // A reusable scene Player may be returned to local mode after
        // NetworkManager.Shutdown without another Start/OnEnable callback.
        // Lazily re-arm the input asset so callback ordering cannot leave the
        // local player permanently unresponsive.
        if (InputControls == null)
        {
            if (!IsSpawned || IsOwner)
                InitPlayer();
            else
                return;
        }

        Vector2 move = InputControls.Gameplay.Move.ReadValue<Vector2>();
        if (motor != null)
            motor.SetInput(new Vector3(move.x, 0f, move.y), false);
        if (InputControls.Gameplay.Jump.triggered)
        {
            motor?.SetInput(motor.InputDirection, true);
        }
        if (InputControls.Gameplay.Fire.triggered)
        {
            motor?.QueueAction(HybridInputAction.Attack);
            combat?.RequestAttack();
        }
        if (InputControls.Gameplay.ESC.triggered)
            UIManager.Instance?.HandleEscape();
        if (InputControls.Gameplay.Interact.triggered)
            InteractionSystem.TryInteract(gameObject);
    }

    public override void OnDestroy()
    {
        if (inputInitialized)
            UIManager.Instance?.OnPlayerDespawned(GetComponent<Inventory>());
        InputControls?.Dispose();
        base.OnDestroy();
    }

    private void OnDisable()
    {
        InputControls?.Dispose();
        InputControls = null;
        inputInitialized = false;
    }
}


    
