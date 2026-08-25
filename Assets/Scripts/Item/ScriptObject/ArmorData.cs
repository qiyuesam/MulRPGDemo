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
        maxStack = 1;
        if (equipSlot == EquipSlotType.Weapon)
            equipSlot = EquipSlotType.Head;
    }
}
