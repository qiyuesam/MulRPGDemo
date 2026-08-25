using System;
using UnityEngine;

[Serializable]
public class EquipmentData
{
    public const int EQUIP_SLOT_COUNT = 4;
    public const int PRESET_LENGTH = 5;
    public const int MAX_JSON_LENGTH = 2048;

    public int weaponItemId;
    public int[] equipItemIds = new int[EQUIP_SLOT_COUNT];
    public event Action OnChanged;

    [Serializable]
    private class SaveData
    {
        public int weapon;
        public int[] equips;
    }

    public void Initialize()
    {
        if (equipItemIds == null || equipItemIds.Length != EQUIP_SLOT_COUNT)
            equipItemIds = new int[EQUIP_SLOT_COUNT];
    }

    public int WeaponItemId => weaponItemId;

    public void SetWeapon(int itemId)
    {
        weaponItemId = itemId;
        OnChanged?.Invoke();
    }

    public int GetEquipItemId(int slot)
    {
        Initialize();
        return slot >= 0 && slot < EQUIP_SLOT_COUNT ? equipItemIds[slot] : 0;
    }

    public void SetEquip(int slot, int itemId)
    {
        Initialize();
        if (slot < 0 || slot >= EQUIP_SLOT_COUNT) return;
        equipItemIds[slot] = itemId;
        OnChanged?.Invoke();
    }

    public bool ApplyPreset(ItemDatabase db, int[] preset)
    {
        if (!TryValidatePreset(db, preset, out string error))
        {
            Debug.LogWarning($"[EquipmentData] 套装无效: {error}");
            return false;
        }

        weaponItemId = preset[0];
        for (int i = 0; i < EQUIP_SLOT_COUNT; i++)
            equipItemIds[i] = preset[i + 1];
        OnChanged?.Invoke();
        return true;
    }

    public void ClearAll()
    {
        weaponItemId = 0;
        equipItemIds = new int[EQUIP_SLOT_COUNT];
        OnChanged?.Invoke();
    }

    public bool IsEquipped(int itemId)
    {
        Initialize();
        if (itemId <= 0) return false;
        if (weaponItemId == itemId) return true;
        for (int i = 0; i < EQUIP_SLOT_COUNT; i++)
            if (equipItemIds[i] == itemId) return true;
        return false;
    }

    public string ToJson()
    {
        Initialize();
        return JsonUtility.ToJson(new SaveData { weapon = weaponItemId, equips = equipItemIds });
    }

    public bool TryFromJson(string json, ItemDatabase db, out string error, bool notify = true)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MAX_JSON_LENGTH)
        {
            error = "存档为空或超过长度限制";
            return false;
        }

        SaveData save;
        try
        {
            save = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e)
        {
            error = $"JSON 解析失败: {e.Message}";
            return false;
        }

        if (save?.equips == null || save.equips.Length != EQUIP_SLOT_COUNT)
        {
            error = "装备槽数量无效";
            return false;
        }

        int[] preset = { save.weapon, save.equips[0], save.equips[1], save.equips[2], save.equips[3] };
        if (!TryValidatePreset(db, preset, out error))
            return false;

        weaponItemId = preset[0];
        equipItemIds = new[] { preset[1], preset[2], preset[3], preset[4] };
        if (notify)
            OnChanged?.Invoke();
        return true;
    }

    public static bool TryValidatePreset(ItemDatabase db, int[] preset, out string error)
    {
        error = null;
        if (db == null || preset == null || preset.Length != PRESET_LENGTH)
        {
            error = "数据库或预设长度无效";
            return false;
        }

        if (!MatchesSlot(db, preset[0], EquipSlotType.Weapon))
        {
            error = "武器槽物品类型不匹配";
            return false;
        }

        EquipSlotType[] expected =
        {
            EquipSlotType.Head, EquipSlotType.Chest, EquipSlotType.Legs, EquipSlotType.Feet
        };
        for (int i = 0; i < EQUIP_SLOT_COUNT; i++)
        {
            if (!MatchesSlot(db, preset[i + 1], expected[i]))
            {
                error = $"护甲槽 {i} 物品类型不匹配";
                return false;
            }
        }
        return true;
    }

    public static bool MatchesSlot(ItemDatabase db, int itemId, EquipSlotType expected)
    {
        if (itemId == 0) return true;
        ItemData item = db != null ? db.GetItem(itemId) : null;
        return item is IEquippable equippable && equippable.EquipSlot == expected;
    }
}
