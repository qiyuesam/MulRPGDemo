// Assets/Scripts/Inventory/Inventory.cs
using UnityEngine;
using Unity.Netcode;

public class Inventory : NetworkBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    public InventoryData data = new InventoryData();
    public event System.Action OnInventoryChanged;

    private const string SAVE_FILE = "player_inventory.sav";
    private const float MIN_REQUEST_INTERVAL = 0.05f;
    private double nextRequestTime;
    private PlayerStats playerStats;
    private bool questEventsBound;

    public ItemDatabase Database => itemDatabase;

    // ================================================================
    //  生命周期
    // ================================================================

    void Awake()
    {
        data.Initialize();
        playerStats = GetComponent<PlayerStats>();
    }

void Start()
    {
        // 若 NetworkManager 已启动（Host/Server/Client），交给 OnNetworkSpawn 处理
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return;

        // 纯本地模式
        if (!IsSpawned)
        {
            LoadFromDisk();
            data.OnChanged += AutoSave;
            BindQuestEvents();
        }
    }

public override void OnNetworkSpawn()
    {
        // 移除 Start 中可能注册的本地 AutoSave，避免与网络回调双重触发
        data.OnChanged -= AutoSave;
        data.OnChanged -= OnServerDataChanged;
        data.OnChanged -= OnClientDataChanged;

        if (IsServer)
            data.OnChanged += OnServerDataChanged;
        else
            data.OnChanged += OnClientDataChanged;

        BindQuestEvents();

        if (IsOwner)
            LoadAndApplySave();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            SaveToDisk();
        data.OnChanged -= OnServerDataChanged;
        data.OnChanged -= OnClientDataChanged;
        UnbindQuestEvents();
        base.OnNetworkDespawn();
    }

    void OnDestroy()
    {
        UnbindQuestEvents();
    }

    // ================================================================
    //  数据变更回调
    // ================================================================

    private void OnServerDataChanged()
    {
        OnInventoryChanged?.Invoke();
        SyncToOwnerClientRpc(Serialize(), CreateOwnerRpcParams());

        if (IsOwner)
            AutoSave();
    }

    private ClientRpcParams CreateOwnerRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId },
            },
        };
    }

    private void OnClientDataChanged()
    {
        OnInventoryChanged?.Invoke();
    }

    private void BindQuestEvents()
    {
        if (questEventsBound) return;
        data.OnItemAdded += OnItemAdded;
        data.OnItemRemoved += OnItemRemoved;
        data.OnItemUsed += OnItemUsed;
        questEventsBound = true;
    }

    private void UnbindQuestEvents()
    {
        if (!questEventsBound) return;
        data.OnItemAdded -= OnItemAdded;
        data.OnItemRemoved -= OnItemRemoved;
        data.OnItemUsed -= OnItemUsed;
        questEventsBound = false;
    }

    private void OnItemAdded(int itemId, int count)
    {
        if (!IsSpawned || IsServer)
            QuestManager.Publish(QuestEvent.ItemAdded(gameObject, itemId, count));
    }

    private void OnItemUsed(int itemId)
    {
        if (!IsSpawned || IsServer)
            QuestManager.Publish(QuestEvent.ItemUsed(gameObject, itemId));
    }

    private void OnItemRemoved(int itemId, int count)
    {
        if (!IsSpawned || IsServer)
            QuestManager.Publish(new QuestEvent
            {
                type = QuestEventType.ItemRemoved,
                actor = gameObject,
                itemId = itemId,
                amount = count
            });
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
            if (data.TryFromJson(json, itemDatabase, out string error))
                Debug.Log("[Inventory] 本地背包已从存档恢复");
            else
                Debug.LogWarning($"[Inventory] 忽略无效本地存档: {error}");
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
            if (data.TryFromJson(json, itemDatabase, out string error))
                Debug.Log($"[Inventory] Host 背包已从存档恢复 (Owner={OwnerClientId})");
            else
                Debug.LogWarning($"[Inventory] Host 存档无效，已忽略: {error}");
        }
        else
        {
            // 多人模式下客户端存档不是权威来源，等待服务器快照。
            Debug.Log("[Inventory] 客户端忽略本地存档，等待服务器权威背包");
        }

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
            Debug.LogWarning("[Inventory] 客户端不能直接授予物品，必须由服务器玩法逻辑发放");
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
            data.UseItem(itemDatabase, slotIndex, playerStats);
            return;
        }

        if (IsClient && !IsServer)
            UseItemServerRpc(slotIndex);
        else
            data.UseItem(itemDatabase, slotIndex, playerStats);
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

    [ServerRpc(RequireOwnership = true)]
    private void SwapSlotsServerRpc(int from, int to)
    {
        if (!CanProcessRequest()) return;
        data.SwapSlots(from, to);
    }

    [ServerRpc(RequireOwnership = true)]
    private void UseItemServerRpc(int slotIndex)
    {
        if (!CanProcessRequest()) return;
        data.UseItem(itemDatabase, slotIndex, playerStats);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RemoveItemServerRpc(int slotIndex, int count)
    {
        if (!CanProcessRequest() || count <= 0) return;
        InventorySlot slot = data.GetSlot(slotIndex);
        if (slot.IsEmpty || count > slot.count) return;
        data.RemoveItem(slotIndex, count);
    }
    [ServerRpc(RequireOwnership = true)]
    private void ClearAllItemsServerRpc()
    {
        if (!CanProcessRequest()) return;
        data.Clear();
    }

    // ================================================================
    //  网络同步
    // ================================================================

    [ClientRpc]
    private void SyncToOwnerClientRpc(string json, ClientRpcParams clientRpcParams = default)
    {
        if (data.TryFromJson(json, itemDatabase, out string error,
                             requireExactSlotCount: true, notify: false))
            OnInventoryChanged?.Invoke();
        else
            Debug.LogError($"[Inventory] 收到无效服务器快照: {error}");
    }

    private string Serialize() => data.ToJson();

    private bool CanProcessRequest()
    {
        double now = Time.unscaledTimeAsDouble;
        if (now < nextRequestTime) return false;
        nextRequestTime = now + MIN_REQUEST_INTERVAL;
        return true;
    }
}
