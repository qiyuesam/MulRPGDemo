using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    private CharacterController characterController;
    public PlayerInputControls InputControls;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSmoothTime = 0.1f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedGravity = -2f; // 贴地小重力，防止悬空
    [SerializeField] private float jumpHeight = 1.5f;

    // 输入
    [SerializeField] private Vector3 inputDirection;

    // 旋转平滑
    private float rotationVelocity;
    private float currentVelocityY;

    // 地面检测
    private bool isGrounded;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundMask = ~0; // 默认检测所有层

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[OnNetworkSpawn] LocalClientId={NetworkManager.LocalClientId}, OwnerClientId={OwnerClientId}, IsServer={IsServer}, IsHost={IsHost}");
        characterController = GetComponent<CharacterController>();

        if (!IsOwner)
        {
            enabled = false;
            Debug.Log("不是控制者");
            return;
        }


        // 只有 Owner 端执行以下逻辑
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

    void Update()
    {
        if (!IsOwner) return;
        inputDirection = InputControls.Gameplay.Move.ReadValue<Vector3>();
        if (InputControls.Gameplay.Jump.triggered)  // .triggered = 按下那一帧为 true
        {
            Jump();
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        ApplyGravity();
        Move();
    }

    void ApplyGravity()
    {
        // 地面检测
        isGrounded = characterController.isGrounded;

        if (isGrounded && currentVelocityY < 0)
        {
            currentVelocityY = groundedGravity; // 保持贴地
        }
        else
        {
            currentVelocityY += gravity * Time.fixedDeltaTime;
        }
    }

    void Move()
    {
        if (characterController == null) return;

        // ====== 获取相机方向（投影到地面 XZ 平面）======
        Transform camTransform = Camera.main.transform;
        Vector3 camForward = camTransform.forward;
        Vector3 camRight = camTransform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        // ====== 相机相对移动方向 ======
        Vector3 targetDirection = (camForward * inputDirection.z + camRight * inputDirection.x).normalized;

        // ====== 构建移动向量 ======
        Vector3 moveDelta;

        if (targetDirection != Vector3.zero)
        {
            // 水平移动
            moveDelta = targetDirection * moveSpeed;

            // 旋转朝向移动方向（SmoothDampAngle 做平滑旋转）
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

        // ====== 叠加垂直速度（重力/跳跃） ======
        moveDelta.y = currentVelocityY;

        // ====== CharacterController 驱动移动 ======
        Vector3 posBefore = transform.position;
        Vector3 displacement = moveDelta * Time.fixedDeltaTime;
        characterController.Move(displacement);
        Vector3 posAfterCC = transform.position;
        Vector3 actualDelta = posAfterCC - posBefore;
        
    }

    // 可选：跳跃
    public void Jump()
    {
        if (isGrounded)
        {
            currentVelocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }
}
