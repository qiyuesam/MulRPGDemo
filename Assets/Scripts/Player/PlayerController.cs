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
        if (!IsOwner)
        {
            enabled = false;
            Debug.Log("不是控制者");
            return;
        }

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
        if (InputControls == null) return;

        Vector2 move = InputControls.Gameplay.Move.ReadValue<Vector2>();
        motor.InputDirection = new Vector3(move.x, 0f, move.y);
        if (InputControls.Gameplay.Jump.triggered)
        {
            motor.JumpRequest = true;
        }
        if (InputControls.Gameplay.Fire.triggered)
            combat?.RequestAttack();
        if (InputControls.Gameplay.ESC.triggered)
            UIManager.Instance?.HandleEscape();
        if (InputControls.Gameplay.Interact.triggered)
            InteractionSystem.TryInteract(gameObject);
    }

    void OnDestroy()
    {
        if (inputInitialized)
            UIManager.Instance?.OnPlayerDespawned(GetComponent<Inventory>());
        InputControls?.Dispose();
    }
}


    
