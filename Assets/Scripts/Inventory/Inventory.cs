using UnityEngine;
using Unity.Netcode;
using System;

public class Inventory : NetworkBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    public const int SLOT_COUNT = 50;//固定50格
    
    // NetworkList 自动网络同步。任何服务端的修改，自动推到所有客户端
    private NetworkList<InventorySlot> slots;//NetworkList<T> 要求 T 必须同时实现 INetworkSerializable 和 IEquatable<T>。
    
    public event Action OnInventoryChanged;

    private void Awake()
    {
        slots = new NetworkList<InventorySlot>(new InventorySlot[SLOT_COUNT],NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
    }
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if(slots.Count<=i)
                    slots.Add(new InventorySlot());
                else if(slots[i].itemId!=0)
                    slots[i]= new InventorySlot();
            }
        }
        slots.OnListChanged+=(eventArgs)=>OnInventoryChanged?.Invoke();
    }
    
    //======查询接口（本地调用）====
    public InventorySlot GetSlot(int index)=>slots[index];
    public int SlotCount=>SLOT_COUNT;

    public bool IsFull
    {
        get
        {
            for(int i=0;i<SlotCount;i++)
                if(slots[i].IsEmpty) return false;
            return true;
        }
    }
    public ItemData GetItemData(int id) => itemDatabase.GetItem(id);

    
    //=====添加物品====
    [ServerRpc(RequireOwnership = false)]
    public void AddItemServerRpc(int itemId, int count)
    {
        //先尝试堆叠
        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i].itemId == itemId)
            {
                var item=itemDatabase.GetItem(itemId);
                int maxStack = item != null ? item.maxStack : 99;
                int space=maxStack-slots[i].count;
                int toAdd=Mathf.Min(count,space);
                var updated = slots[i];
                updated.count+=toAdd;
                slots[i] = updated;
                count-=toAdd;
                if (count <= 0) return;
            }
        }
        //放到空格子
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (slots[i].IsEmpty)
            {
                var item=itemDatabase.GetItem(itemId);
                int maxStack = item != null ? item.maxStack : 99;
                int toAdd=Mathf.Min(count,maxStack);
                slots[i]=new InventorySlot{itemId=itemId,count=toAdd};
                count -= toAdd;
                if (count <= 0) return;
            }
        }
        Debug.LogWarning($"背包已满，无法装入 {itemId} x{count}");
    }
    // ====== 移除物品 ======
    [ServerRpc(RequireOwnership = false)]
    public void RemoveItemServerRpc(int slotIndex, int count)
    {
        if (slotIndex < 0 || slotIndex >= SLOT_COUNT) return;
        var slot = slots[slotIndex];
        if (slot.IsEmpty) return;

        slot.count -= count;
        if (slot.count <= 0)
            slot = new InventorySlot();  // 清零变空格
        slots[slotIndex] = slot;
    }

    // ====== 交换两个格子 ======
    [ServerRpc(RequireOwnership = false)]
    public void SwapSlotsServerRpc(int from, int to)
    {
        if (from < 0 || from >= SLOT_COUNT) return;
        if (to < 0 || to >= SLOT_COUNT) return;
        var temp = slots[from];
        slots[from] = slots[to];
        slots[to] = temp;
    }

    // ====== 使用物品 ======
    [ServerRpc(RequireOwnership = false)]
    public void UseItemServerRpc(int slotIndex)
    {
        var slot = slots[slotIndex];
        if (slot.IsEmpty) return;

        var item = itemDatabase.GetItem(slot.itemId);
        if (item == null) return;

        if (item.type == ItemData.itemType.Consumable)
        {
            // 消耗品：减少 1 个
            RemoveItemServerRpc(slotIndex, 1);
            // TODO: 触发效果（加血、加蓝等）
        }
        // 装备类物品不能用，切到装备系统处理
    }
    void Update()
    {
        if (!IsOwner) return;  // 只控制自己的背包

        // 按 1：加一把铁剑
        if (Input.GetKeyDown(KeyCode.Alpha1))
            AddItemServerRpc(1, 1);

        // 按 2：加 5 瓶生命药水
        if (Input.GetKeyDown(KeyCode.Alpha2))
            AddItemServerRpc(2, 5);

        // 按 3：打印当前背包内容
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            Debug.Log("==== 背包内容 ====");
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (!slots[i].IsEmpty)
                    Debug.Log($"格子{i}: ID={slots[i].itemId} x{slots[i].count}");
            }
        }
        if (Input.GetKeyDown(KeyCode.Alpha0))
            for (int i = 0; i < SLOT_COUNT; i++)
                RemoveItemServerRpc(i, 99);
    }
}

