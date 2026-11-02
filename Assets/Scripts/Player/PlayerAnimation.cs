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

    private int lastAttackSequence = -1;

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

    public override void OnNetworkSpawn()
    {
        enabled = true;
        lastAttackSequence = -1;
        base.OnNetworkSpawn();
        if (IsServer)
            networkSpeed.Value = 0f;
    }

    public override void OnNetworkDespawn()
    {
        lastAttackSequence = -1;
        if (animator != null)
            animator.speed = 1f;
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (motor == null || animator == null) return;

        // 攻击段号和序列都由服务器维护。序列号确保同一段号再次
        // 出现时仍会重播，而不是依赖一个可能没有变化的 int 参数。
        if (combat != null)
        {
            int comboIndex = combat.ComboIndex;
            int sequence = combat.AttackSequence;
            animator.speed = comboIndex > 0 ? combat.AttackPlaybackSpeed : 1f;
            animator.SetInteger("ComboIndex", comboIndex);
            if (comboIndex > 0 && sequence != lastAttackSequence)
            {
                lastAttackSequence = sequence;
                int stateHash = Animator.StringToHash($"Attack0{comboIndex}");
                // Weapon controllers are optional.  If the active controller
                // does not contain the conventional Attack01..05 states, keep
                // locomotion running instead of issuing a noisy invalid-state
                // transition; an animation event can still drive hit timing
                // when a weapon override supplies those states.
                if (animator.HasState(0, stateHash))
                    animator.CrossFade(stateHash, 0.08f, 0, 0f);
            }
        }

        // ═══ 移动动画 ═══
        if (!IsSpawned)
        {
            animator.SetFloat("Vertical Speed", motor.currentSpeed);
            return;
        }

        if (IsOwner)
            networkSpeed.Value = motor.currentSpeed;

        animator.SetFloat("Vertical Speed", networkSpeed.Value);
    }

}
