// Assets/Scripts/Combat/CombatSystem.cs
using UnityEngine;
using Unity.Netcode;

public class CombatSystem : NetworkBehaviour
{
    // ===== 依赖（Awake 时 GetComponent）=====
    private PlayerStats stats;
    private EffectRunner effectRunner;

    // ===== 黑板：攻击状态 =====
    public float AttackCooldown { get; private set; }  // 当前 CD 剩余
    public bool IsOnCooldown => AttackCooldown > 0;
    public bool AttackRequest { get; set; }             // Controller 写入

    [Header("攻击设置")]
    [SerializeField] private float attackCooldownTime = 0.5f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private LayerMask enemyLayer;

    // ===== 事件 =====
    public System.Action<CombatResult> OnCombatResolved; // UI 层订阅→显示飘字

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        effectRunner = GetComponent<EffectRunner>();
    }

    void Update()
    {
        // CD 计时
        if (AttackCooldown > 0)
            AttackCooldown -= Time.deltaTime;

        // 处理攻击请求
        if (AttackRequest && !IsOnCooldown)
        {
            AttackRequest = false;
            TryAttack();
        }
    }

    // ---------- 攻击入口（Step 0：目标检测）----------
    void TryAttack()
    {
        // 范围检测
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, enemyLayer);
        if (hits.Length == 0) return;

        // 取最近的敌人
        Transform target = GetClosestTarget(hits);
        if (target == null) return;

        // 获取目标组件
        PlayerStats targetStats = target.GetComponent<PlayerStats>();
        EffectRunner targetEffects = target.GetComponent<EffectRunner>();
        if (targetStats == null) return;

        // 执行管道
        CombatResult result = ExecutePipeline(targetStats, targetEffects);

        // CD
        AttackCooldown = attackCooldownTime;

        // 网络同步
        if (IsSpawned && IsServer)
            SyncDamageClientRpc(target.gameObject, result.finalDamage);

        // 通知 UI
        OnCombatResolved?.Invoke(result);
    }

    // ---------- 6步管道 ----------
    CombatResult ExecutePipeline(PlayerStats defenderStats, EffectRunner defenderEffects)
    {
        // Step 1: 原始伤害 = 攻击力
        float rawDamage = stats.Attack;

        // Step 2: 攻击方效果（吸血、DoT）
        AttackResult atkResult = effectRunner.ProcessOnAttack(rawDamage);

        // Step 3: 防御方效果（闪避、反伤）
        DefendResult defResult = defenderEffects.ProcessOnHit(rawDamage);

        // Step 4: 闪避检查 —— 管道终止
        if (defResult.dodged)
        {
            return new CombatResult { dodged = true };
        }

        // Step 5: 伤害公式
        float finalDamage = CalculateDamage(rawDamage, defenderStats.Defense,
                                            stats.CritRate, stats.CritDamage);

        // Step 6: 执行伤害
        ApplyDamage(finalDamage, atkResult, defResult, defenderStats);

        return new CombatResult
        {
            finalDamage = finalDamage,
            selfHeal = atkResult.selfHeal,
            reflectDamage = defResult.reflectDamage,
            targetDead = defenderStats.CurrentHp <= 0
        };
    }

    // ---------- 伤害公式 ----------
    float CalculateDamage(float raw, int defense, float critRate, float critDamage)
    {
        // 减伤
        float reduction = defense / (defense + 100f);
        float damage = raw * (1f - reduction);

        // 暴击
        bool crit = Random.value < critRate;
        if (crit) damage *= critDamage;

        return Mathf.Max(1, damage); // 最低1点伤害
    }

    // ---------- 伤害执行 ----------
    void ApplyDamage(float damage, AttackResult atkResult,
                     DefendResult defResult, PlayerStats defenderStats)
    {
        // 扣血
        defenderStats.CurrentHp -= (int)damage;

        // 吸血
        if (atkResult.selfHeal > 0)
            stats.CurrentHp += (int)atkResult.selfHeal;

        // 反伤
        if (defResult.reflectDamage > 0)
            stats.CurrentHp -= (int)defResult.reflectDamage;

        // 击杀回复
        if (defenderStats.CurrentHp <= 0)
            stats.CurrentHp += (int)effectRunner.ProcessOnKill();
    }

    // ---------- 辅助 ----------
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
    void SyncDamageClientRpc(NetworkObjectReference targetRef, float damage) {  }
}

// 战斗结果数据（用于 UI 飘字）
public struct CombatResult
{
    public bool dodged;
    public float finalDamage;
    public float selfHeal;
    public float reflectDamage;
    public bool targetDead;
}
