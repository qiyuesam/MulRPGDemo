// Assets/Scripts/Item/ScriptObject/ArmorData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "New Armor", menuName = "Item/Armor")]
public class ArmorData : ItemData, IEquippable
{
    [Header("护甲属性")]
    public EquipSlotType equipSlot;  // Head/Chest/Legs/Feet 由策划选
    public EquipAttributes attributes;
    public EffectData[] effects;

    // IEquippable 实现
    public EquipSlotType EquipSlot => equipSlot;
    public EquipAttributes Attributes => attributes;
    public EffectData[] Effects => effects;

    void OnValidate()
    {
        type = itemType.Equipment;
    }
}
[CreateAssetMenu(fileName = "New Weapon", menuName = "Item/Weapon")]
public class WeaponData : ItemData, IEquippable
{
    [Header("武器属性")]
    public EquipAttributes attributes;
    public EffectData[] effects;

    // IEquippable 实现
    public EquipSlotType EquipSlot => EquipSlotType.Weapon;  // 武器锁死 Weapon 槽
    public EquipAttributes Attributes => attributes;
    public EffectData[] Effects => effects;

    // Awake 时自动修正 type
    void OnValidate()
    {
        type = itemType.Weapon;
    }
}
[CreateAssetMenu(fileName = "New Consumable", menuName = "Item/Consumable")]
public class ConsumableData : ItemData
{
    void OnValidate() { type = itemType.Consumable; }
}
[CreateAssetMenu(fileName = "New Material", menuName = "Item/Material")]
public class MaterialData : ItemData
{
    void OnValidate() { type = itemType.Material; }
}
[CreateAssetMenu(fileName = "New Quest Item", menuName = "Item/Quest")]
public class QuestData : ItemData
{
    void OnValidate() { type = itemType.Quest; }
}