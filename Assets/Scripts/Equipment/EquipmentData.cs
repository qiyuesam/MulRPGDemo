using System;
using UnityEngine;

[Serializable]
public class EquipmentData
{
    // ===== 槽位数据 =====
    public int weaponItemId;              // 武器槽，0 = 空
    public int[] equipItemIds = new int[4]; // 护甲 4 槽，0 = 空

    public event Action OnChanged;

    // ===== 初始化 =====
    public void Initialize()
    {
        if (equipItemIds == null || equipItemIds.Length != 4)
            equipItemIds = new int[4];
    }

    // ===== 武器 =====
    public int WeaponItemId => weaponItemId;

    public void SetWeapon(int itemId)
    {
        weaponItemId = itemId;
        OnChanged?.Invoke();
    }

    // ===== 护甲 =====
    public int GetEquipItemId(int slot)   // slot: 0~3
        => (slot >= 0 && slot < 4) ? equipItemIds[slot] : 0;

    public void SetEquip(int slot, int itemId)
    {
        if (slot < 0 || slot >= 4) return;
        equipItemIds[slot] = itemId;
        OnChanged?.Invoke();
    }

    // ===== 套装 =====
    public void ApplyPreset(int[] presetWeaponAndEquips)
    {
        // presetWeaponAndEquips: [weaponId, equip0, equip1, equip2, equip3]
        if (presetWeaponAndEquips == null || presetWeaponAndEquips.Length < 5) return;
        weaponItemId = presetWeaponAndEquips[0];
        for (int i = 0; i < 4; i++)
            equipItemIds[i] = presetWeaponAndEquips[i + 1];
        OnChanged?.Invoke();
    }

    // ===== 清空 =====
    public void ClearAll()
    {
        weaponItemId = 0;
        for (int i = 0; i < 4; i++)
            equipItemIds[i] = 0;
        OnChanged?.Invoke();
    }

    // ===== 查询某物品是否已装备 =====
    public bool IsEquipped(int itemId)
    {
        if (weaponItemId == itemId) return true;
        for (int i = 0; i < 4; i++)
            if (equipItemIds[i] == itemId) return true;
        return false;
    }

    // ===== 存档 =====
    // ⚠️ JsonUtility 不能直接序列化顶层数组，
    //    用包装类包一层

    [Serializable]
    private class SaveData
    {
        public int weapon;
        public int[] equips;
    }

    public string ToJson()
    {
        var save = new SaveData
        {
            weapon = weaponItemId,
            equips = equipItemIds
        };
        return JsonUtility.ToJson(save);
    }

    public void FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var save = JsonUtility.FromJson<SaveData>(json);
            if (save != null)
            {
                weaponItemId = save.weapon;
                if (save.equips != null && save.equips.Length >= 4)
                {
                    for (int i = 0; i < 4; i++)
                        equipItemIds[i] = save.equips[i];
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[EquipmentData] 存档解析失败: {e.Message}");
        }
    }
}
