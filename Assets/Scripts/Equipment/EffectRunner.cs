using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectRunner : MonoBehaviour
{
    // ===== 效果缓存（按触发时机分组）=====
    private EffectData[] onAttackEffects;    // FireDamage + Lifesteal
    private EffectData[] onHitEffects;       // ThornArmor + DodgeChance
    private EffectData[] onKillEffects;      // OnKillHeal
    private EffectData[] passiveEffects;     // SpeedBoost

    private Equipment equipment;
    private PlayerStats playerStats;

    // ===== 公开属性（被动效果对外暴露）=====
    public float SpeedMultiplier { get; private set; } = 1f;

    // ===== 初始化 =====
    void Awake()
    {
        equipment = GetComponent<Equipment>();
        playerStats = GetComponent<PlayerStats>();

        if (equipment == null)
            Debug.LogError("[EffectRunner] 未找到 Equipment 组件！");
        if (playerStats == null)
            Debug.LogError("[EffectRunner] 未找到 PlayerStats 组件！");
    }

    void Start()
    {
        RebuildCache();

        if (equipment != null)
            equipment.OnEquipmentChanged += OnEquipmentChanged;
    }

    void OnDestroy()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged -= OnEquipmentChanged;
    }

    // ===== 装备变更时重建缓存 =====
    private void OnEquipmentChanged()
    {
        RebuildCache();
    }

    /// <summary>
    /// 从 Equipment 读取当前所有效果，按触发时机分组
    /// </summary>
private void RebuildCache()
    {
        if (equipment == null)
        {
            onAttackEffects  = new EffectData[0];
            onHitEffects     = new EffectData[0];
            onKillEffects    = new EffectData[0];
            passiveEffects   = new EffectData[0];
            SpeedMultiplier  = 1f;
            return;
        }

        EffectData[] all = equipment.GetActiveEffects();

        var attackList  = new System.Collections.Generic.List<EffectData>();
        var hitList     = new System.Collections.Generic.List<EffectData>();
        var killList    = new System.Collections.Generic.List<EffectData>();
        var passiveList = new System.Collections.Generic.List<EffectData>();

        float speedMult = 1f;

        foreach (var effect in all)
        {
            if (effect == null) continue;

            switch (effect.type)
            {
                case EffectData.EffectType.FireDamage:
                case EffectData.EffectType.Lifesteal:
                    attackList.Add(effect);
                    break;

                case EffectData.EffectType.ThornArmor:
                case EffectData.EffectType.DodgeChance:
                    hitList.Add(effect);
                    break;

                case EffectData.EffectType.OnKillHeal:
                    killList.Add(effect);
                    break;

                case EffectData.EffectType.SpeedBoost:
                    passiveList.Add(effect);
                    speedMult += effect.value1;
                    break;
            }
        }

        onAttackEffects  = attackList.ToArray();
        onHitEffects     = hitList.ToArray();
        onKillEffects    = killList.ToArray();
        passiveEffects   = passiveList.ToArray();
        SpeedMultiplier  = speedMult;

        Debug.Log($"[EffectRunner] 缓存重建: 攻击效果{onAttackEffects.Length} 受击效果{onHitEffects.Length} 击杀效果{onKillEffects.Length} 被动{passiveEffects.Length} 速度倍率={SpeedMultiplier}");
    }

    // ================================================================
    //  攻击命中时调用
    // ================================================================

    /// <summary>
    /// 当持有者攻击命中目标时调用
    /// </summary>
    /// <param name="damageDealt">实际造成的伤害值（减伤后）</param>
    /// <returns>攻击效果计算结果</returns>
    public AttackResult ProcessOnAttack(float damageDealt)
    {
        AttackResult result = new AttackResult();

        foreach (var effect in onAttackEffects)
        {
            switch (effect.type)
            {
                case EffectData.EffectType.FireDamage:
                    // value1 = 触发概率, value2 = 每跳伤害, value3 = 持续时间
                    if (Random.value < effect.value1)
                    {
                        result.hasDot      = true;
                        result.dotDamage   = effect.value2;
                        result.dotDuration = effect.value3;
                    }
                    break;

                case EffectData.EffectType.Lifesteal:
                    // value1 = 吸血比例, 如 0.15 = 15%
                    result.selfHeal += damageDealt * effect.value1;
                    break;
            }
        }

        return result;
    }

    // ================================================================
    //  受到攻击时调用
    // ================================================================

    /// <summary>
    /// 当持有者受到攻击时调用
    /// </summary>
    /// <param name="incomingDamage">即将受到的伤害（减伤前）</param>
    /// <returns>防御效果计算结果</returns>
    public DefendResult ProcessOnHit(float incomingDamage)
    {
        DefendResult result = new DefendResult();

        foreach (var effect in onHitEffects)
        {
            switch (effect.type)
            {
                case EffectData.EffectType.DodgeChance:
                    // value1 = 闪避概率
                    if (!result.dodged && Random.value < effect.value1)
                    {
                        result.dodged = true;
                    }
                    break;

                case EffectData.EffectType.ThornArmor:
                    // value1 = 反弹比例, value2 = 固定反弹伤害
                    result.reflectDamage += incomingDamage * effect.value1 + effect.value2;
                    break;
            }
        }

        // 闪避后反伤不生效
        if (result.dodged)
            result.reflectDamage = 0;

        return result;
    }

    // ================================================================
    //  击杀敌人时调用
    // ================================================================

    /// <summary>
    /// 当持有者击杀敌人时调用
    /// </summary>
    /// <returns>应治疗的生命值</returns>
    public float ProcessOnKill()
    {
        float healAmount = 0;

        foreach (var effect in onKillEffects)
        {
            switch (effect.type)
            {
                case EffectData.EffectType.OnKillHeal:
                    // value1 = 回复比例（基于最大生命值）
                    if (playerStats != null)
                        healAmount += playerStats.MaxHp * effect.value1;
                    break;
            }
        }

        return healAmount;
    }
}

// ================================================================
//  结果 Struct（放在同一文件底部，或单独文件）
// ================================================================

/// <summary>
/// 攻击效果计算结果
/// </summary>
public struct AttackResult
{
    public float extraDamage;      // 额外即时伤害
    public float selfHeal;         // 攻击者自身回复量
    public float dotDamage;        // DoT 每跳伤害
    public float dotDuration;      // DoT 持续时间（秒）
    public bool hasDot;            // 是否触发了 DoT
}

/// <summary>
/// 防御效果计算结果
/// </summary>
public struct DefendResult
{
    public bool dodged;            // 是否闪避本次攻击
    public float reflectDamage;    // 反弹给攻击者的伤害
}