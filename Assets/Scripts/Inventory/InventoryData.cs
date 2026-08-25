using System;
using UnityEngine;

[Serializable]
public class InventoryData
{
    public const int SLOT_COUNT = 50;
    public const int MAX_JSON_LENGTH = 32768;

    public InventorySlot[] slots;
    public event Action OnChanged;
    public event Action<int, int> OnItemAdded;
    public event Action<int, int> OnItemRemoved;
    public event Action<int> OnItemUsed;
    private int notificationBatchDepth;
    private bool batchedChanged;

    [Serializable]
    private class SaveData
    {
        public InventorySlot[] slots;
    }

    public void Initialize()
    {
        slots = new InventorySlot[SLOT_COUNT];
    }

    public InventorySlot GetSlot(int index)
    {
        EnsureInitialized();
        return IsValidSlot(index) ? slots[index] : default;
    }

    public bool IsFull
    {
        get
        {
            EnsureInitialized();
            for (int i = 0; i < SLOT_COUNT; i++)
                if (slots[i].IsEmpty) return false;
            return true;
        }
    }

    public bool AddItem(ItemDatabase db, int itemId, int count)
    {
        EnsureInitialized();
        ItemData item = db != null ? db.GetItem(itemId) : null;
        if (item == null || itemId <= 0 || count <= 0 || item.maxStack <= 0 || item is IEquippable)
            return false;

        bool changed = false;
        int remaining = count;
        int added = 0;

        for (int i = 0; i < SLOT_COUNT && remaining > 0; i++)
        {
            if (slots[i].itemId != itemId || slots[i].count >= item.maxStack)
                continue;

            int toAdd = Mathf.Min(remaining, item.maxStack - slots[i].count);
            slots[i].count += toAdd;
            remaining -= toAdd;
            added += toAdd;
            changed |= toAdd > 0;
        }

        for (int i = 0; i < SLOT_COUNT && remaining > 0; i++)
        {
            if (!slots[i].IsEmpty)
                continue;

            int toAdd = Mathf.Min(remaining, item.maxStack);
            slots[i] = new InventorySlot { itemId = itemId, count = toAdd };
            remaining -= toAdd;
            added += toAdd;
            changed |= toAdd > 0;
        }

        if (changed)
            NotifyChanged();
        if (changed && notificationBatchDepth == 0)
            OnItemAdded?.Invoke(itemId, added);
        if (remaining > 0)
            Debug.LogWarning($"背包空间不足，物品 {itemId} 尚有 {remaining} 个未加入");
        return changed;
    }

    public bool RemoveItem(int slotIndex, int count)
    {
        EnsureInitialized();
        if (!IsValidSlot(slotIndex) || count <= 0 || slots[slotIndex].IsEmpty)
            return false;

        int itemId = slots[slotIndex].itemId;
        int removed = Mathf.Min(count, slots[slotIndex].count);
        slots[slotIndex].count -= removed;
        if (slots[slotIndex].count <= 0)
            slots[slotIndex] = default;
        NotifyChanged();
        if (notificationBatchDepth == 0)
            OnItemRemoved?.Invoke(itemId, removed);
        return removed > 0;
    }

    public bool SwapSlots(int from, int to)
    {
        EnsureInitialized();
        if (!IsValidSlot(from) || !IsValidSlot(to) || from == to)
            return false;
        (slots[from], slots[to]) = (slots[to], slots[from]);
        NotifyChanged();
        return true;
    }

    public bool UseItem(ItemDatabase db, int slotIndex, PlayerStats playerStats)
    {
        EnsureInitialized();
        if (!IsValidSlot(slotIndex) || slots[slotIndex].IsEmpty || playerStats == null)
            return false;

        int itemId = slots[slotIndex].itemId;
        if (db == null || db.GetItem(itemId) is not ConsumableData consumable)
            return false;
        if (!consumable.TryApply(playerStats))
            return false;
        bool removed = RemoveItem(slotIndex, 1);
        if (removed)
            OnItemUsed?.Invoke(itemId);
        return removed;
    }

    public int CountItem(int itemId)
    {
        EnsureInitialized();
        if (itemId <= 0) return 0;
        int total = 0;
        foreach (InventorySlot slot in slots)
            if (slot.itemId == itemId && slot.count > 0)
                total += slot.count;
        return total;
    }

    public bool CanAddItem(ItemDatabase db, int itemId, int count)
    {
        EnsureInitialized();
        if (db == null || count <= 0) return false;
        ItemData item = db.GetItem(itemId);
        if (item == null || item is IEquippable || item.maxStack <= 0)
            return false;

        int remaining = count;
        foreach (InventorySlot slot in slots)
        {
            if (slot.itemId == itemId && slot.count < item.maxStack)
                remaining -= Mathf.Min(remaining, item.maxStack - slot.count);
            if (remaining <= 0) return true;
        }

        foreach (InventorySlot slot in slots)
        {
            if (!slot.IsEmpty)
                continue;
            remaining -= Mathf.Min(remaining, item.maxStack);
            if (remaining <= 0) return true;
        }

        return false;
    }

    public bool RemoveItemById(int itemId, int count)
    {
        EnsureInitialized();
        if (itemId <= 0 || count <= 0 || CountItem(itemId) < count)
            return false;

        int remaining = count;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].itemId != itemId || slots[i].IsEmpty)
                continue;
            int removed = Mathf.Min(remaining, slots[i].count);
            RemoveItem(i, removed);
            remaining -= removed;
        }
        return remaining == 0;
    }

    public void BeginBatch()
    {
        notificationBatchDepth++;
    }

    public void EndBatch()
    {
        if (notificationBatchDepth <= 0) return;
        notificationBatchDepth--;
        if (notificationBatchDepth == 0 && batchedChanged)
        {
            batchedChanged = false;
            OnChanged?.Invoke();
        }
    }

    public void Clear()
    {
        Initialize();
        NotifyChanged();
    }

    public string ToJson()
    {
        EnsureInitialized();
        return JsonUtility.ToJson(new SaveData { slots = slots });
    }

    public bool TryFromJson(string json, ItemDatabase db, out string error,
                            bool requireExactSlotCount = false, bool notify = true)
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

        if (save?.slots == null || save.slots.Length > SLOT_COUNT ||
            (requireExactSlotCount && save.slots.Length != SLOT_COUNT))
        {
            error = "槽位数量无效";
            return false;
        }

        var normalized = new InventorySlot[SLOT_COUNT];
        for (int i = 0; i < save.slots.Length; i++)
        {
            InventorySlot slot = save.slots[i];
            if (slot.IsEmpty)
                continue;

            ItemData item = db != null ? db.GetItem(slot.itemId) : null;
            if (item == null || item is IEquippable || slot.count <= 0 ||
                item.maxStack <= 0 || slot.count > item.maxStack)
            {
                error = $"槽位 {i} 包含非法物品或数量";
                return false;
            }
            normalized[i] = slot;
        }

        slots = normalized;
        if (notify)
            NotifyChanged();
        return true;
    }

    private void NotifyChanged()
    {
        if (notificationBatchDepth > 0)
        {
            batchedChanged = true;
            return;
        }
        OnChanged?.Invoke();
    }

    private void EnsureInitialized()
    {
        if (slots == null || slots.Length != SLOT_COUNT)
            Initialize();
    }

    private static bool IsValidSlot(int index) => index >= 0 && index < SLOT_COUNT;
}
