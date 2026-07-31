// Assets/Scripts/Combat/CombatSystem.cs
using UnityEngine;
using Unity.Netcode;

public class CombatSystem : NetworkBehaviour
{
    // ===== 依赖 =====
    private PlayerStats stats;
    private EffectRunner effectRunner;
    private Equipment equipment;
    private Animator animator;
    private CharacterController cc;

    // ===== 黑板：攻击状态 =====
    public float AttackCooldown { get; private set; }
    public bool IsOnCooldown => AttackCooldown > 0;
    public bool AttackRequest { get; set; }

    [Header("攻击设置")]
    [SerializeField] private float attackCooldownTime = 0.5f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("攻击位移")]
    [SerializeField] private float attackLungeDistance = 1.2f;  // 每段攻击的前冲距离
    [SerializeField] private float attackLungeDuration = 0.15f; // 位移持续时长（秒），0=瞬发

    // ★ 连段状态（用属性暴露给 PlayerAnimation 读）
    public int ComboIndex { get; private set; }       // 0=空闲, 1~N=连段中
    public int ComboMax   { get; private set; } = 5;  // 当前武器段数
    private bool comboWindowOpen;
    private float comboWindowStart = 0.7f;

    // ===== 事件 =====
    public System.Action<CombatResult> OnCombatResolved;

    void Awake()
    {
        stats        = GetComponent<PlayerStats>();
        effectRunner = GetComponent<EffectRunner>();
        equipment    = GetComponent<Equipment>();
        animator     = GetComponentInChildren<Animator>();
        cc           = GetComponent<CharacterController>();
    }

    void Update()
    {
        // CD 计时
        if (AttackCooldown > 0)
            AttackCooldown -= Time.deltaTime;

        // ── 连段窗口检测 ──
        if (ComboIndex > 0 && animator != null)
        {
            if (animator.IsInTransition(0))   // ★ 过渡期间不检测，等动画稳定
                goto SkipComboCheck;

            var state = animator.GetCurrentAnimatorStateInfo(0);
            float t = state.normalizedTime % 1f;
            comboWindowOpen = (t >= comboWindowStart && t < 0.95f);

            if (t >= 0.9f || !state.IsTag("Attack"))
                ResetCombo();
        }
        SkipComboCheck:

        // ── 攻击请求 ──
        if (AttackRequest && !IsOnCooldown)
        {
            AttackRequest = false;
            TryComboAttack();
        }
    }

    // ────────── 连段攻击入口 ──────────
    void TryComboAttack()
    {
        // 从武器读配置（每次攻击都读，换武器即时生效）
        RefreshWeaponConfig();

        // 非空闲时，必须处于连段窗口内
        if (ComboIndex > 0 && !comboWindowOpen)
            return;

        // 推进连段
        ComboIndex = (ComboIndex % ComboMax) + 1;

        // ★ 攻击朝向锁定为摄像机前方
        FaceCameraDirection();

        // 驱驱动画
        if (animator != null)
            animator.SetInteger("ComboIndex", ComboIndex);

        // ★ 攻击前冲位移
        ApplyAttackLunge();

        // 伤害
        PerformAttackDamage();

        // CD
        AttackCooldown = attackCooldownTime;
    }

    void RefreshWeaponConfig()
    {
        ComboMax = 5;
        comboWindowStart = 0.7f;

        if (equipment != null)
        {
            var weapon = equipment.GetWeapon();
            if (weapon is WeaponData wd && wd.maxCombo > 0)
            {
                ComboMax = wd.maxCombo;
                comboWindowStart = wd.comboWindowStart;
            }
        }
    }

    void ResetCombo()
    {
        ComboIndex = 0;
        comboWindowOpen = false;
        if (animator != null)
            animator.SetInteger("ComboIndex", 0);
    }

    // ────────── 攻击位移 ──────────
    private Coroutine activeLunge;

    /// <summary>获取摄像机面朝方向（水平面投影）</summary>
    Vector3 GetCameraForward()
    {
        if (Camera.main == null) return transform.forward;
        Vector3 f = Camera.main.transform.forward;
        f.y = 0;
        return f.sqrMagnitude > 0.001f ? f.normalized : transform.forward;
    }

    /// <summary>瞬间将角色转向摄像机前方</summary>
    void FaceCameraDirection()
    {
        Vector3 dir = GetCameraForward();
        transform.rotation = Quaternion.LookRotation(dir);
    }

    void ApplyAttackLunge()
    {
        if (cc == null || attackLungeDistance <= 0) return;

        if (activeLunge != null)
            StopCoroutine(activeLunge);

        Vector3 dir = GetCameraForward();

        if (attackLungeDuration > 0)
            activeLunge = StartCoroutine(LungeRoutine(dir));
        else
            cc.Move(dir * attackLungeDistance);
    }

    System.Collections.IEnumerator LungeRoutine(Vector3 direction)
    {
        float elapsed = 0f;
        float totalDist = attackLungeDistance;
        float duration = attackLungeDuration;

        while (elapsed < duration)
        {
            float step = totalDist * (Time.deltaTime / duration);
            cc.Move(direction * step);
            elapsed += Time.deltaTime;
            yield return null;
        }
        activeLunge = null;
    }

    // ────────── 伤害管道（原 TryAttack 逻辑）──────────
    void PerformAttackDamage()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, enemyLayer);
        if (hits.Length == 0) return;

        Transform target = GetClosestTarget(hits);
        if (target == null) return;

        PlayerStats targetStats = target.GetComponent<PlayerStats>();
        EffectRunner targetEffects = target.GetComponent<EffectRunner>();
        if (targetStats == null) return;

        CombatResult result = ExecutePipeline(targetStats, targetEffects);
        AttackCooldown = attackCooldownTime;

        if (IsSpawned && IsServer)
            SyncDamageClientRpc(target.gameObject, result.finalDamage);

        OnCombatResolved?.Invoke(result);
    }

    CombatResult ExecutePipeline(PlayerStats defenderStats, EffectRunner defenderEffects)
    {
        float rawDamage = stats.Attack;
        AttackResult atkResult = effectRunner.ProcessOnAttack(rawDamage);
        DefendResult defResult = defenderEffects.ProcessOnHit(rawDamage);

        if (defResult.dodged)
            return new CombatResult { dodged = true };

        float finalDamage = CalculateDamage(rawDamage, defenderStats.Defense,
                                            stats.CritRate, stats.CritDamage);
        ApplyDamage(finalDamage, atkResult, defResult, defenderStats);

        return new CombatResult
        {
            finalDamage  = finalDamage,
            selfHeal     = atkResult.selfHeal,
            reflectDamage = defResult.reflectDamage,
            targetDead   = defenderStats.CurrentHp <= 0
        };
    }

    float CalculateDamage(float raw, int defense, float critRate, float critDamage)
    {
        float reduction = defense / (defense + 100f);
        float damage = raw * (1f - reduction);
        if (Random.value < critRate) damage *= critDamage;
        return Mathf.Max(1, damage);
    }

    void ApplyDamage(float damage, AttackResult atkResult,
                     DefendResult defResult, PlayerStats defenderStats)
    {
        defenderStats.CurrentHp -= (int)damage;
        if (atkResult.selfHeal > 0)
            stats.CurrentHp += (int)atkResult.selfHeal;
        if (defResult.reflectDamage > 0)
            stats.CurrentHp -= (int)defResult.reflectDamage;
        if (defenderStats.CurrentHp <= 0)
            stats.CurrentHp += (int)effectRunner.ProcessOnKill();
    }

    Transform GetClosestTarget(Collider[] hits)
    {
        Transform best = null;
        float minDist = float.MaxValue;
        foreach (var hit in hits)
        {
            float d = Vector3.Distance(transform.position, hit.transform.position);
            if (d < minDist) { minDist = d; best = hit.transform; }
        }
        return best;
    }

    [ClientRpc]
    void SyncDamageClientRpc(NetworkObjectReference targetRef, float damage) { }
}

public struct CombatResult
{
    public bool dodged;
    public float finalDamage;
    public float selfHeal;
    public float reflectDamage;
    public bool targetDead;
}
