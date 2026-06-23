// Assets/Scripts/Equipment/IEquippable.cs

/// <summary>
/// 可装备物品的公共接口 —— 武器和护甲都实现它
/// </summary>
public interface IEquippable
{
    EquipSlotType EquipSlot { get; }
    EquipAttributes Attributes { get; }
    EffectData[] Effects { get; }
}