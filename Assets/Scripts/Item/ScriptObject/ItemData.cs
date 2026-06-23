// Assets/Scripts/Item/ScriptObject/ItemData.cs（改造后）
using UnityEngine;

/// <summary>
/// 物品基类（抽象）—— 所有物品类型的公共字段
/// 武器/护甲/消耗品/材料/任务 各自继承
/// </summary>
public abstract class ItemData : ScriptableObject
{
    // ── 类型枚举 ──
    public enum itemType
    {
        Consumable,
        Equipment,   // 护甲
        Weapon,      // 武器
        Material,
        Quest
    }

    // ── 公共字段（所有物品都有）──
    public int itemId;
    public string itemName;
    [TextArea]
    public string itemDescription;
    public Sprite itemIcon;
    public itemType type;
    public int maxStack = 99;
    public GameObject itemPrefab;
}