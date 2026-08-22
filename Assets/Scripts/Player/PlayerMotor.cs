using Unity.Netcode;
using UnityEngine;

//黑板模式
public class PlayerMotor : NetworkBehaviour
{
    [Header("移动")]
    [SerializeField]private float defaultMoveSpeed = 6f;
    [SerializeField]private float rotationSmoothTime = 0.1f;
    
    [Header("重力")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedGravity = -2f;
    [SerializeField] private float jumpHeight = 1.5f;
    
    
    public Vector3 InputDirection{ get; set; }
    public bool JumpRequest { get;set; }
    public bool IsGrounded { get;set; }
    public bool IsMoving { get;set; }
    public float currentSpeed { get; set; }
    public Vector3 Velocity { get;set; }

    private CharacterController cc;
    private PlayerStats playerStats;
    private CombatSystem combat;
    private float currentVelocityY;
    private float rotationVelocity;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerStats = GetComponent<PlayerStats>();
        combat = GetComponent<CombatSystem>();
    }

    void FixedUpdate()
    {
        if(IsSpawned&&!IsOwner) return;

        // ★ 死亡后停止移动和跳跃（重力照常，让尸体落地）
        if (playerStats != null && playerStats.IsDead)
        {
            InputDirection = Vector3.zero;
            JumpRequest = false;
        }

        ApplyGravity();
        ApplyJump();
        PerformMove();
    }
    //重力
    void ApplyGravity()
    {
        IsGrounded = cc.isGrounded;

        if (IsGrounded && currentVelocityY < 0)
        {
            currentVelocityY = groundedGravity;
        }
        else
        {
            currentVelocityY += gravity * Time.fixedDeltaTime;
        }
    }
    //跳跃
    void ApplyJump()
    {
        if (JumpRequest && IsGrounded)
        {
            currentVelocityY = Mathf.Sqrt(jumpHeight * -2f * gravity);//v=-2gh
        }
        JumpRequest = false;
    }

    void PerformMove()
    {
        Vector3 dir = InputDirection;

        // ★ 攻击时禁止水平移动（重力照常），动画播完自动解除
        if (combat != null && combat.ComboIndex > 0)
            dir = Vector3.zero;

        if (dir != Vector3.zero)
        {
            // 相机相对方向
            if (Camera.main != null)
            {
                Transform camera = Camera.main.transform;
                Vector3 forward = camera.forward;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = camera.right;
                right.y = 0;
                right.Normalize();
                dir = (forward * dir.z + right * dir.x).normalized;
            }

            float speed = playerStats != null ? playerStats.MoveSpeed : defaultMoveSpeed;
            currentSpeed = speed;
            Vector3 moveDelta = dir * speed;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float smoothed = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                targetAngle,
                ref rotationVelocity,
                rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0, smoothed, 0);

            moveDelta.y = currentVelocityY;
            cc.Move(moveDelta * Time.fixedDeltaTime);
            Velocity = moveDelta;
            IsMoving = true;
        }
        else
        {
            Vector3 verticalDelta = new Vector3(0, currentVelocityY, 0);
            cc.Move(verticalDelta * Time.fixedDeltaTime);

            Velocity = Vector3.zero;
            IsMoving = false;
			currentSpeed=0;
        }
    }
}
