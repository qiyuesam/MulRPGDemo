// Assets/Scripts/Inventory/Inventory.cs
using UnityEngine;
using Unity.Netcode;

public class Inventory : NetworkBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    public InventoryData data = new InventoryData();
    public event System.Action OnInventoryChanged;

    private const string SAVE_FILE = "player_inventory.sav";

    // ================================================================
    //  生命周期
    // ================================================================

    void Awake()
    {
        data.Initialize();
    }

    void Start()
    {
        // ★ 本地模式：NetworkObject 未被 Spawn，OnNetworkSpawn 不会触发
        if (!IsSpawned)
        {
            LoadFromDisk();
            data.OnChanged += AutoSave;
        }
    }

    public override void OnNetworkSpawn()
    {
        // data 已在 Awake 中 Initialize，这里只注册网络回调
        if (IsServer)
            data.OnChanged += OnServerDataChanged;
        else
            data.OnChanged += OnClientDataChanged;

        if (IsOwner)
            LoadAndApplySave();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            SaveToDisk();
        base.OnNetworkDespawn();
    }

    // ================================================================
    //  数据变更回调
    // ================================================================

    private void OnServerDataChanged()
    {
        OnInventoryChanged?.Invoke();
        SyncToClientsClientRpc(Serialize());

        if (IsOwner)
            AutoSave();
    }

    private void OnClientDataChanged()
    {
        OnInventoryChanged?.Invoke();
    }

    // ================================================================
    //  存档/读档
    // ================================================================

    /// 纯本地读档（Start 里调用，简单直接）
    private void LoadFromDisk()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (!string.IsNullOrEmpty(json))
        {
            data.FromJson(json);
            Debug.Log($"[Inventory] 本地背包已从存档恢复");
        }
        else
        {
            Debug.Log("[Inventory] 无存档，使用空背包");
        }
    }

    public void ForceSave()
    {
        SaveManager.Save(SAVE_FILE, data.ToJson());
    }
    /// 联机读档（OnNetworkSpawn 里调用，分 Host/Client 两条路径）
    private void LoadAndApplySave()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (string.IsNullOrEmpty(json))
        {
            Debug.Log($"[Inventory] 无存档，使用空背包 (Owner={OwnerClientId})");
            return;
        }

        if (IsServer)
        {
            data.FromJson(json);
            Debug.Log($"[Inventory] Host 背包已从存档恢复 (Owner={OwnerClientId})");
        }
        else
        {
            data.FromJson(json);
            Debug.Log($"[Inventory] 客户端本地加载存档，同时上传服务器");
            UploadSaveDataServerRpc(json);
        }

        OnInventoryChanged?.Invoke();

        if (IsServer)
            SyncToClientsClientRpc(Serialize());
    }

    [ServerRpc(RequireOwnership = false)]
    private void UploadSaveDataServerRpc(string json)
    {
        Debug.Log($"[Inventory] 服务器收到客户端 {OwnerClientId} 的存档上传");
        data.FromJson(json);
        SyncToClientsClientRpc(Serialize());
        OnInventoryChanged?.Invoke();
    }

    private void AutoSave()
    {
        SaveManager.Save(SAVE_FILE, data.ToJson());
    }

    private void SaveToDisk()
    {
        SaveManager.Save(SAVE_FILE, data.ToJson());
        Debug.Log($"[Inventory] 背包已存档 (Owner={OwnerClientId})");
    }

    // ================================================================
    //  操作入口：自动判断本地/联机
    // ================================================================

    public void TryAddItem(int itemId, int count)
    {
        // ★ 本地模式：直接操作，不走网络
        if (!IsSpawned)
        {
            data.AddItem(itemDatabase, itemId, count);
            return;
        }

        if (IsClient && !IsServer)
            AddItemServerRpc(itemId, count);
        else
            data.AddItem(itemDatabase, itemId, count);
    }

    public void TrySwapSlots(int from, int to)
    {
        if (!IsSpawned)
        {
            data.SwapSlots(from, to);
            return;
        }

        if (IsClient && !IsServer)
            SwapSlotsServerRpc(from, to);
        else
            data.SwapSlots(from, to);
    }

    public void TryUseItem(int slotIndex)
    {
        if (!IsSpawned)
        {
            data.UseItem(itemDatabase, slotIndex);
            return;
        }

        if (IsClient && !IsServer)
            UseItemServerRpc(slotIndex);
        else
            data.UseItem(itemDatabase, slotIndex);
    }

    public void TryRemoveItem(int slotIndex, int count)
    {
        if (!IsSpawned)
        {
            data.RemoveItem(slotIndex, count);
            return;
        }

        if (IsClient && !IsServer)
            RemoveItemServerRpc(slotIndex, count);
        else
            data.RemoveItem(slotIndex, count);
    }
    // 本地/联机通用的清空入口
    public void ClearAllItems()
    {
        if (!IsSpawned || IsServer)
        {
            data.Clear();
            // 如果是联机模式，同步给所有客户端
            if (IsSpawned && IsServer)
                SyncToClientsClientRpc(Serialize());
        }
        else
        {
            // 纯客户端：请求服务器清空
            ClearAllItemsServerRpc();
        }
    }

    // ================================================================
    //  查询接口
    // ================================================================

    public InventorySlot GetSlot(int index) => data.GetSlot(index);
    public ItemData GetItemData(int id) => itemDatabase.GetItem(id);
    public int SlotCount => InventoryData.SLOT_COUNT;

    // ================================================================
    //  ServerRPC
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    private void AddItemServerRpc(int itemId, int count)
        => data.AddItem(itemDatabase, itemId, count);

    [ServerRpc(RequireOwnership = false)]
    private void SwapSlotsServerRpc(int from, int to)
        => data.SwapSlots(from, to);

    [ServerRpc(RequireOwnership = false)]
    private void UseItemServerRpc(int slotIndex)
        => data.UseItem(itemDatabase, slotIndex);

    [ServerRpc(RequireOwnership = false)]
    private void RemoveItemServerRpc(int slotIndex, int count)
        => data.RemoveItem(slotIndex, count);
    [ServerRpc(RequireOwnership = false)]
    private void ClearAllItemsServerRpc()
    {
        data.Clear();
        SyncToClientsClientRpc(Serialize());
    }

    // ================================================================
    //  网络同步
    // ================================================================

    [ClientRpc]
    private void SyncToClientsClientRpc(string json)
    {
        data.FromJson(json);
        OnInventoryChanged?.Invoke();
    }

    private string Serialize() => data.ToJson();

    // ================================================================
    //  调试热键
    // ================================================================

    void Update()
    {
        // ★ 本地模式放行；联机非 Owner 不放行
        if (IsSpawned && !IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) TryAddItem(1, 1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) TryAddItem(8, 8);
        if (Input.GetKeyDown(KeyCode.Alpha3)) { /*...*/ }
        if (Input.GetKeyDown(KeyCode.Alpha0)) ClearAllItems();  // 按 0 清空

    }
}
