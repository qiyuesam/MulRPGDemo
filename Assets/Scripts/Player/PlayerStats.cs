// Assets/Scripts/Player/PlayerStats.cs
using UnityEngine;

/// <summary>
/// 玩家属性汇总器 —— 基础属性 + 装备加成 = 最终属性
/// 挂在 Player 预制体上，与 Equipment 同级
/// </summary>
public class PlayerStats : MonoBehaviour
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
    [SerializeField] private int currentHp;
    [SerializeField] private int currentMp;

    // ===== 缓存 =====
    private Equipment equipment;
    private EquipAttributes finalAttributes;

    // ===== 事件 =====
    public System.Action OnStatsChanged;

    // ===== 最终属性（只读）=====
    public int Attack         => finalAttributes.attack;
    public int Defense        => finalAttributes.defense;
    public int MaxHp          => finalAttributes.maxHp;
    public int MaxMp          => finalAttributes.maxMp;
    public float MoveSpeed    => baseMoveSpeed + finalAttributes.moveSpeedBonus;
    public float AttackSpeed  => baseAttackSpeed + finalAttributes.attackSpeedBonus;
    public float CritRate     => finalAttributes.critRate;
    public float CritDamage   => finalAttributes.critDamage;

    public int CurrentHp
    {
        get => currentHp;
        set
        {
            int clamped = Mathf.Clamp(value, 0, MaxHp);
            if (currentHp != clamped)
            {
                currentHp = clamped;
                OnStatsChanged?.Invoke();
            }
        }
    }

    public int CurrentMp
    {
        get => currentMp;
        set
        {
            int clamped = Mathf.Clamp(value, 0, MaxMp);
            if (currentMp != clamped)
            {
                currentMp = clamped;
                OnStatsChanged?.Invoke();
            }
        }
    }

    // ===== 初始化 =====
    void Awake()
    {
        equipment = GetComponent<Equipment>();
        if (equipment == null)
        {
            Debug.LogError("[PlayerStats] 未找到 Equipment 组件！请确保挂在同一 GameObject 上");
        }
    }

    void Start()
    {
        Recalculate();
        currentHp = MaxHp;
        currentMp = MaxMp;

        if (equipment != null)
            equipment.OnEquipmentChanged += OnEquipmentChanged;
        Debug.Log($"[PlayerStats] 初始化完成: ATK={Attack} DEF={Defense} HP={MaxHp} MP={MaxMp} SPD={MoveSpeed}");
    }

    void OnDestroy()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged -= OnEquipmentChanged;
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
        Debug.Log($"[PlayerStats] 重算完成: ATK={Attack} DEF={Defense} HP={MaxHp} SPD={MoveSpeed}");
        OnStatsChanged?.Invoke();
    }

    // ===== 装备变更回调 =====
    private void OnEquipmentChanged()
    {
        Recalculate();
    }

    // ===== 便捷方法 =====
    public EquipAttributes GetFinalAttributes() => finalAttributes;
}
