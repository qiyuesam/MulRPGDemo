// Assets/Scripts/Player/PlayerController.cs
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    private CharacterController characterController;
    public PlayerInputControls InputControls;
    private PlayerStats playerStats;       // ★ 新增

    [Header("Movement")]
    [SerializeField] private float defaultMoveSpeed = 6f;   // ★ 改名：fallback 值
    [SerializeField] private float rotationSmoothTime = 0.1f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedGravity = -2f;
    [SerializeField] private float jumpHeight = 1.5f;

    // 输入
    [SerializeField] private Vector3 inputDirection;

    // 旋转平滑
    private float rotationVelocity;
    private float currentVelocityY;

    // 地面检测
    private bool isGrounded;
    [SerializeField] private LayerMask groundMask = ~0;

    // ================================================================
    //  初始化
    // ================================================================

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerStats = GetComponent<PlayerStats>();   // ★ 新增
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
        InputControls = new PlayerInputControls();
        InputControls.Gameplay.Enable();

        Transform camTarget = transform.Find("CameraTarget");
        var freeLook = FindObjectOfType<Cinemachine.CinemachineFreeLook>();
        if (freeLook != null && camTarget != null)
        {
            freeLook.Follow = camTarget;
            freeLook.LookAt = transform;
        }
    }

    // ================================================================
    //  每帧输入
    // ================================================================

    void Update()
    {
        // ★ 联机非 Owner 不处理；本地模式 (IsSpawned=false) 放行
        if (IsSpawned && !IsOwner) return;

        inputDirection = InputControls.Gameplay.Move.ReadValue<Vector3>();
        if (InputControls.Gameplay.Jump.triggered)
        {
            Jump();
        }
    }

    // ================================================================
    //  物理更新
    // ================================================================

    void FixedUpdate()
    {
        if (IsSpawned && !IsOwner) return;

        ApplyGravity();
        Move();
    }

    void ApplyGravity()
    {
        isGrounded = characterController.isGrounded;

        if (isGrounded && currentVelocityY < 0)
        {
            currentVelocityY = groundedGravity;
        }
        else
        {
            currentVelocityY += gravity * Time.fixedDeltaTime;
        }
    }

    void Move()
    {
                if (characterController == null) return;

        // 获取相机方向（投影到地面 XZ 平面）
        if (Camera.main == null) return;
        Transform camTransform = Camera.main.transform;
Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        // 相机相对移动方向
        Vector3 targetDirection = (camForward * inputDirection.z + camRight * inputDirection.x).normalized;

        Vector3 moveDelta;

        if (targetDirection != Vector3.zero)
        {
            // ★ 改动：从 PlayerStats 动态读取移速，失效时 fallback 到 Inspector 值
            float currentSpeed = playerStats != null ? playerStats.MoveSpeed : defaultMoveSpeed;
            moveDelta = targetDirection * currentSpeed;

            float targetAngle = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
            float smoothedAngle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                targetAngle,
                ref rotationVelocity,
                rotationSmoothTime
            );
            transform.rotation = Quaternion.Euler(0, smoothedAngle, 0);
        }
        else
        {
            moveDelta = Vector3.zero;
        }

        moveDelta.y = currentVelocityY;
        characterController.Move(moveDelta * Time.fixedDeltaTime);
    }

public void Jump()
    {
        // 直接读取 characterController.isGrounded，避免 Update/FixedUpdate 时序不一致
        if (characterController.isGrounded)
        {
            currentVelocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }
}
