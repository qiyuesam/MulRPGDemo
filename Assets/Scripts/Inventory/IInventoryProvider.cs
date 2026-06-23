// Assets/Scripts/Inventory/IInventoryProvider.cs
public interface IInventoryProvider
{
    InventorySlot GetSlot(int index);
    void SwapSlots(int from, int to);
    void AddItem(int itemId, int count);
    void RemoveItem(int slotIndex, int count);
    void UseItem(int slotIndex);
    ItemData GetItemData(int itemId);
    int SlotCount { get; }
    void BindChanged(System.Action callback);
    void UnbindChanged(System.Action callback);
}