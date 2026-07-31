using Unity.Netcode;
using UnityEngine;

public class PlayerAnimation : NetworkBehaviour
{
    private PlayerMotor motor;
    private Animator animator;
    private CombatSystem combat;

    private NetworkVariable<float> networkSpeed = new NetworkVariable<float>(
        writePerm: NetworkVariableWritePermission.Owner
    );

    // ★ 新增：ComboIndex 网络同步（与 Speed 同模式：Owner 写入，其他人读取）
    private NetworkVariable<int> networkComboIndex = new NetworkVariable<int>(
        writePerm: NetworkVariableWritePermission.Owner
    );

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        animator = GetComponentInChildren<Animator>();
        if (motor == null)
            Debug.LogError("PlayerMotor is null");
        if (animator == null)
            Debug.LogError("Animator is null");
        combat = GetComponent<CombatSystem>();
    }

    void Update()
    {
        if (motor == null || animator == null) return;

        // ═══ 攻击动画同步 ═══
        if (!IsSpawned)
        {
            // 本地模式：直接读 CombatSystem 的值
            if (combat != null)
                animator.SetInteger("ComboIndex", combat.ComboIndex);
        }
        else
        {
            // 联机模式：Owner 写 → NetworkVariable 同步 → 所有人读
            if (IsOwner && combat != null)
                networkComboIndex.Value = combat.ComboIndex;

            animator.SetInteger("ComboIndex", networkComboIndex.Value);
        }

        // ═══ 移动动画 ═══
        if (!IsSpawned)
        {
            animator.SetFloat("Speed", motor.currentSpeed);
            return;
        }

        if (IsOwner)
            networkSpeed.Value = motor.currentSpeed;

        animator.SetFloat("Speed", networkSpeed.Value);
    }
}
