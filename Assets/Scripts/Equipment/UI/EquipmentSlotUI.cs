// Assets/Scripts/Equipment/UI/EquipmentSlotUI.cs
using UnityEngine;
using UnityEngine.UI;

public class EquipmentSlotUI : MonoBehaviour
{
    [SerializeField] private Image icon;

    private EquipSlotType slotType;
    private Equipment equipment;

        public void Setup(Equipment eq, EquipSlotType type)
    {
        // 先解绑旧对象，防止 Open/Close 反复调用时重复订阅
        if (equipment != null)
            equipment.OnEquipmentChanged -= Refresh;
        equipment = eq;
        slotType = type;
        if (equipment != null)
            equipment.OnEquipmentChanged += Refresh;}

    void OnDestroy()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged -= Refresh;
    }

    public void Refresh()
    {
        if (equipment == null || icon == null) return;

        ItemData item = GetItem();
        if (item != null && item.itemIcon != null)
        {
            icon.sprite = item.itemIcon;
            icon.color = Color.white;
        }
        else
        {
            icon.sprite = null;
            icon.color = new Color(1, 1, 1, 0);   // 透明
        }
    }

    ItemData GetItem() => slotType switch
    {
        EquipSlotType.Weapon => equipment.GetWeapon(),
        EquipSlotType.Head   => equipment.GetEquip(0),
        EquipSlotType.Chest  => equipment.GetEquip(1),
        EquipSlotType.Legs   => equipment.GetEquip(2),
        EquipSlotType.Feet   => equipment.GetEquip(3),
        _ => null
    };
}