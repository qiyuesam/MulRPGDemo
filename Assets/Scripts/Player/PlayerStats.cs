// Assets/Scripts/Player/PlayerStats.cs

using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 玩家属性汇总器 —— 基础属性 + 装备加成 = 最终属性
/// 挂在 Player 预制体上，与 Equipment 同级
/// </summary>
public class PlayerStats : NetworkBehaviour
{
    // ===== 基础属性 =====
    [Header("基础属性")]
    [SerializeField] private int baseAttack = 10;
    [SerializeField] private int baseDefense = 5;
    [SerializeField] private int baseMaxHp = 100;
    [SerializeField] private int baseMaxMp = 50;
    [SerializeField] private float baseMoveSpeed = 6f;
    [SerializeField] private float baseAttackSpeed = 1f;
    [SerializeField] private float baseCritRate = 0.05f;
    [SerializeField] private float baseCritDamage = 1.5f;

    // ===== 运行时状态 =====
    [Header("当前状态")] 
    private NetworkVariable<int> currentHp = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private NetworkVariable<int> currentMp=new NetworkVariable<int>(
        50,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ===== 缓存 =====
    private Equipment equipment;
    private EquipAttributes finalAttributes;

    // ===== 事件 =====
    public System.Action OnStatsChanged;
    public System.Action OnDeath;   // 血量首次归零时触发一次

    // ===== 死亡状态 =====
    public int CurrentHp=>currentHp.Value;
    public int CurrentMp=>currentMp.Value;
    public bool IsDead => currentHp.Value <= 0;

    // ===== 最终属性（只读）=====
    public int Attack         => finalAttributes.attack;
    public int Defense        => finalAttributes.defense;
    public int MaxHp          => finalAttributes.maxHp;
    public int MaxMp          => finalAttributes.maxMp;
    public float MoveSpeed    => baseMoveSpeed + finalAttributes.moveSpeedBonus;
    public float AttackSpeed  => baseAttackSpeed + finalAttributes.attackSpeedBonus;
    public float CritRate     => finalAttributes.critRate;
    public float CritDamage   => finalAttributes.critDamage;

    // ===== 初始化 =====
    void Awake()
    {
        equipment = GetComponent<Equipment>();
        if (equipment == null)
        {
            Debug.LogError("[PlayerStats] 未找到 Equipment 组件！请确保挂在同一 GameObject 上");
        }
        currentHp.OnValueChanged += OnHpChanged;   // 本地+联机都订阅
        currentMp.OnValueChanged += OnMPChanged;
    }

    void Start()
    {
        Recalculate();
        if (!IsSpawned)
        {
            currentHp.Value = MaxHp;
            currentMp.Value = MaxMp;
        }

        if (equipment != null)
            equipment.OnEquipmentChanged += OnEquipmentChanged;
        Debug.Log($"[PlayerStats] 初始化完成: ATK={Attack} DEF={Defense} HP={MaxHp} MP={MaxMp} SPD={MoveSpeed}");
    }

    void OnDestroy()
    {
        currentHp.OnValueChanged -= OnHpChanged;   // 从 OnNetworkDespawn 挪到这里
        currentMp.OnValueChanged -= OnMPChanged;
        if (equipment != null)
            equipment.OnEquipmentChanged -= OnEquipmentChanged;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Recalculate();
            currentHp.Value = MaxHp;
            currentMp.Value = MaxMp;
        }
    }

    private void OnHpChanged(int prev, int next)
    {
        OnStatsChanged?.Invoke();
        if (prev > 0 && next <= 0)
        {
            OnDeath?.Invoke();
        }
    }

    void OnMPChanged(int prev, int next)
    {
        OnStatsChanged?.Invoke();
    }
    // ===== 属性重算 =====
    public void Recalculate()
    {
        EquipAttributes baseAttr = new EquipAttributes
        {
            attack           = baseAttack,
            defense          = baseDefense,
            maxHp            = baseMaxHp,
            maxMp            = baseMaxMp,
            moveSpeedBonus   = 0,
            attackSpeedBonus = 0,
            critRate         = baseCritRate,
            critDamage       = baseCritDamage,
        };

        EquipAttributes equipAttr = EquipAttributes.Zero;
        if (equipment != null)
            equipAttr = equipment.GetTotalAttributes();

        finalAttributes = baseAttr + equipAttr;
        if (!IsSpawned || IsServer)
        {
            currentHp.Value = Mathf.Clamp(currentHp.Value, 0, MaxHp);
            currentMp.Value = Mathf.Clamp(currentMp.Value, 0, MaxMp);
        }

        // ★ 修复：MaxHp/MaxMp 变化后重新钳制当前值，防止脱装备后超上限

        Debug.Log($"[PlayerStats] 重算完成: ATK={Attack} DEF={Defense} HP={MaxHp} SPD={MoveSpeed}");
        OnStatsChanged?.Invoke();
    }

    // ===== 装备变更回调 =====
    private void OnEquipmentChanged()
    {
        Recalculate();
    }

    public void ChangeHp(int delta)
    {
        if (IsSpawned&&!IsServer) return;
        currentHp.Value=Mathf.Clamp(currentHp.Value+delta,0,MaxHp);
    }

    public void ChangeMp(int delta)
    {
        if (IsSpawned&&!IsServer) return;
        currentMp.Value=Mathf.Clamp(currentMp.Value+delta,0,MaxMp);
    }
    // ===== 便捷方法 =====
    public EquipAttributes GetFinalAttributes() => finalAttributes;
}
