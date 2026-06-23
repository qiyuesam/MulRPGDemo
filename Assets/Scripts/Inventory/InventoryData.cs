// Assets/Scripts/Inventory/InventoryData.cs
using System;
using UnityEngine;

[Serializable]
public class InventoryData
{
    public const int SLOT_COUNT = 50;
    public InventorySlot[] slots;
    public event Action OnChanged;

    // ===== 初始化 =====
    public void Initialize()
    {
        slots = new InventorySlot[SLOT_COUNT];
        for (int i = 0; i < SLOT_COUNT; i++)
            slots[i] = new InventorySlot();
    }

    // ===== 查询 =====
    public InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return default;
        return slots[index];
    }

    public bool IsFull
    {
        get
        {
            for (int i = 0; i < SLOT_COUNT; i++)
                if (slots[i].IsEmpty) return false;
            return true;
        }
    }

    // ===== 添加物品 =====
    public void AddItem(ItemDatabase db, int itemId, int count)
    {
        // ★ 装备不进背包
        var checkItem = db.GetItem(itemId);
        if (checkItem is IEquippable) return;
        // 1）先尝试堆叠到已有同种物品上
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (slots[i].itemId == itemId)
            {
                var item = db.GetItem(itemId);
                int maxStack = item != null ? item.maxStack : 99;
                int space = maxStack - slots[i].count;
                int toAdd = Mathf.Min(count, space);
                slots[i].count += toAdd;
                count -= toAdd;
                if (count <= 0) { OnChanged?.Invoke(); return; }
            }
        }

        // 2）剩余放空格
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (slots[i].IsEmpty)
            {
                var item = db.GetItem(itemId);
                int maxStack = item != null ? item.maxStack : 99;
                int toAdd = Mathf.Min(count, maxStack);
                slots[i] = new InventorySlot { itemId = itemId, count = toAdd };
                count -= toAdd;
                if (count <= 0) { OnChanged?.Invoke(); return; }
            }
        }

        if (count > 0) Debug.LogWarning("背包已满");
        OnChanged?.Invoke();
    }

    // ===== 移除物品 =====
    public void RemoveItem(int slotIndex, int count)
    {
        if (slotIndex < 0 || slotIndex >= SLOT_COUNT) return;
        if (slots[slotIndex].IsEmpty) return;

        slots[slotIndex].count -= count;
        if (slots[slotIndex].count <= 0)
            slots[slotIndex] = new InventorySlot();

        OnChanged?.Invoke();
    }

    // ===== 交换两个格子 =====
    public void SwapSlots(int from, int to)
    {
        if (from < 0 || from >= SLOT_COUNT) return;
        if (to < 0 || to >= SLOT_COUNT) return;
        (slots[from], slots[to]) = (slots[to], slots[from]);
        OnChanged?.Invoke();
    }

    // ===== 使用物品 =====
    public void UseItem(ItemDatabase db, int slotIndex)
    {
        if (slots[slotIndex].IsEmpty) return;
        var item = db.GetItem(slots[slotIndex].itemId);
        if (item == null) return;

        if (item.type == ItemData.itemType.Consumable)
        {
            RemoveItem(slotIndex, 1);
        }
    }
    public void Clear()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            slots[i] = new InventorySlot();
        OnChanged?.Invoke();    // 通知 UI 刷新 + 触发存档
    }


    // ===== 存档用 =====
    public string ToJson() => JsonUtility.ToJson(this);

    public void FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            JsonUtility.FromJsonOverwrite(json, this);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[InventoryData] 存档解析失败: {e.Message}");
        }
    }
}
