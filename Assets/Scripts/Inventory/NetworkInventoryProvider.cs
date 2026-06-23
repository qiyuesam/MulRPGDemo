// Assets/Scripts/Inventory/NetworkInventoryProvider.cs
using UnityEngine;

public class NetworkInventoryProvider : IInventoryProvider
{
    private Inventory inventory;

    public NetworkInventoryProvider(Inventory inv)
    {
        inventory = inv;
    }

    public InventorySlot GetSlot(int index) => inventory.GetSlot(index);
    public int SlotCount => inventory.SlotCount;
    public ItemData GetItemData(int itemId) => inventory.GetItemData(itemId);

    // 写操作 → 走 Try（自动判断本地/网络）
    public void AddItem(int itemId, int count) => inventory.TryAddItem(itemId, count);
    public void RemoveItem(int slotIndex, int count) => inventory.TryRemoveItem(slotIndex, count);
    public void SwapSlots(int from, int to) => inventory.TrySwapSlots(from, to);
    public void UseItem(int slotIndex) => inventory.TryUseItem(slotIndex);

    // 事件转发
    public void BindChanged(System.Action callback)
    {
        inventory.OnInventoryChanged += callback;
    }
    public void UnbindChanged(System.Action callback)
    {
        inventory.OnInventoryChanged -= callback;
    }
}