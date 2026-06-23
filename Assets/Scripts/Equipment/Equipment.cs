// Assets/Scripts/Equipment/Equipment.cs
using UnityEngine;
using Unity.Netcode;

public class Equipment : NetworkBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    public EquipmentData data = new EquipmentData();
    public event System.Action OnEquipmentChanged;

    private const string SAVE_FILE = "player_equipment.sav";

    // ================================================================
    //  生命周期
    // ================================================================

    void Awake()
    {
        data.Initialize();
    }

void Start()
    {
        // 若 NetworkManager 已启动，交给 OnNetworkSpawn 处理
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return;

        if (!IsSpawned)
        {
            LoadFromDisk();
            data.OnChanged += AutoSave;
        }
    }

public override void OnNetworkSpawn()
    {
        // 移除 Start 中可能注册的本地 AutoSave
        data.OnChanged -= AutoSave;

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
        OnEquipmentChanged?.Invoke();
        SyncToClientsClientRpc(Serialize());

        if (IsOwner)
            AutoSave();
    }

    private void OnClientDataChanged()
    {
        OnEquipmentChanged?.Invoke();
    }

    // ================================================================
    //  业务操作 — 箱子/套装调用这些
    // ================================================================

    /// 装备物品（从箱子选一件装备）
    public void EquipFromChest(int itemId)
    {
        var item = itemDatabase.GetItem(itemId);
        if (item == null) return;
        // 只接受武器或护甲
        if (item is not IEquippable) return;
        if (!IsSpawned || IsServer)
        {
            ApplyEquip(item, itemId);
            if (IsSpawned && IsServer)
                SyncToClientsClientRpc(Serialize());
        }
        else
        {
            EquipFromChestServerRpc(itemId);
        }
    }

    /// 卸下装备（放回箱子）
    public int UnequipSlot(EquipSlotType slotType)
    {
        int removedId = 0;

        if (!IsSpawned || IsServer)
        {
            removedId = ApplyUnequip(slotType);
            if (IsSpawned && IsServer)
                SyncToClientsClientRpc(Serialize());
        }
        else
        {
            UnequipSlotServerRpc((int)slotType);
            // 客户端无法同步获取返回值，这里返回 0
        }

        return removedId;
    }

    /// 应用预设套装
    public void ApplyPreset(int[] presetIds)
    {
        if (!IsSpawned || IsServer)
        {
            data.ApplyPreset(presetIds);
            if (IsSpawned && IsServer)
                SyncToClientsClientRpc(Serialize());
        }
        else
        {
            ApplyPresetServerRpc(presetIds);
        }
    }

    // ================================================================
    //  内部：实际执行装备/卸下（服务器和本地通用）
    // ================================================================

    private void ApplyEquip(ItemData item, int itemId)
    {
        if (item is not IEquippable equippable) return;  // 不是装备就忽略

        switch (equippable.EquipSlot)   // ✅ 从接口读
        {
            case EquipSlotType.Weapon: data.SetWeapon(itemId); break;
            case EquipSlotType.Head:   data.SetEquip(0, itemId); break;
            case EquipSlotType.Chest:  data.SetEquip(1, itemId); break;
            case EquipSlotType.Legs:   data.SetEquip(2, itemId); break;
            case EquipSlotType.Feet:   data.SetEquip(3, itemId); break;
        }
    }

    private int ApplyUnequip(EquipSlotType slotType)
    {
        int removedId = 0;
        switch (slotType)
        {
            case EquipSlotType.Weapon:
                removedId = data.WeaponItemId;
                data.SetWeapon(0);
                break;
            case EquipSlotType.Head:
                removedId = data.GetEquipItemId(0);
                data.SetEquip(0, 0);
                break;
            case EquipSlotType.Chest:
                removedId = data.GetEquipItemId(1);
                data.SetEquip(1, 0);
                break;
            case EquipSlotType.Legs:
                removedId = data.GetEquipItemId(2);
                data.SetEquip(2, 0);
                break;
            case EquipSlotType.Feet:
                removedId = data.GetEquipItemId(3);
                data.SetEquip(3, 0);
                break;
        }
        return removedId;
    }

    // ================================================================
    //  查询
    // ================================================================

    public ItemData GetWeapon()     => itemDatabase.GetItem(data.WeaponItemId);
    public ItemData GetEquip(int i) => itemDatabase.GetItem(data.GetEquipItemId(i));
    public bool IsEquipped(int itemId) => data.IsEquipped(itemId);

    /// 汇总所有装备的属性加成
    public EquipAttributes GetTotalAttributes()
    {
        EquipAttributes total = EquipAttributes.Zero;

        var w = GetWeapon();
        if (w is IEquippable wep)
            total += wep.Attributes;           // ✅ 从接口读

        for (int i = 0; i < 4; i++)
        {
            var a = GetEquip(i);
            if (a is IEquippable armor)
                total += armor.Attributes;
        }
        return total;
    }

    /// 汇总所有装备的效果
    public EffectData[] GetActiveEffects()
    {
        var list = new System.Collections.Generic.List<EffectData>();

        var w = GetWeapon();
        if (w is IEquippable wep && wep.Effects != null)
            list.AddRange(wep.Effects);

        for (int i = 0; i < 4; i++)
        {
            var a = GetEquip(i);
            if (a is IEquippable armor && armor.Effects != null)
                list.AddRange(armor.Effects);
        }
        return list.ToArray();
    }

    // ================================================================
    //  存档/读档
    // ================================================================

    private void LoadFromDisk()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (!string.IsNullOrEmpty(json))
        {
            data.FromJson(json);
            Debug.Log("[Equipment] 本地装备已从存档恢复");
        }
    }

    private void LoadAndApplySave()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (string.IsNullOrEmpty(json))
        {
            Debug.Log($"[Equipment] 无存档，空装备 (Owner={OwnerClientId})");
            return;
        }

        if (IsServer)
        {
            data.FromJson(json);
            Debug.Log($"[Equipment] Host 装备已从存档恢复 (Owner={OwnerClientId})");
        }
        else
        {
            data.FromJson(json);
            UploadSaveDataServerRpc(json);
        }

        OnEquipmentChanged?.Invoke();

        if (IsServer)
            SyncToClientsClientRpc(Serialize());
    }

    private void AutoSave()   => SaveManager.Save(SAVE_FILE, data.ToJson());
    private void SaveToDisk() => SaveManager.Save(SAVE_FILE, data.ToJson());

    public void ForceSave()   => SaveManager.Save(SAVE_FILE, data.ToJson());

    // ================================================================
    //  ServerRPC
    // ================================================================

    [ServerRpc(RequireOwnership = true)]
    private void EquipFromChestServerRpc(int itemId)
    {
        var item = itemDatabase.GetItem(itemId);
        if (item == null) return;
        ApplyEquip(item, itemId);
        SyncToClientsClientRpc(Serialize());
    }

    [ServerRpc(RequireOwnership = true)]
    private void UnequipSlotServerRpc(int slotTypeInt)
    {
        ApplyUnequip((EquipSlotType)slotTypeInt);
        SyncToClientsClientRpc(Serialize());
    }

    [ServerRpc(RequireOwnership = true)]
    private void ApplyPresetServerRpc(int[] presetIds)
    {
        data.ApplyPreset(presetIds);
        SyncToClientsClientRpc(Serialize());
    }

    [ServerRpc(RequireOwnership = true)]
    private void UploadSaveDataServerRpc(string json)
    {
        data.FromJson(json);
        SyncToClientsClientRpc(Serialize());
        OnEquipmentChanged?.Invoke();
    }

    // ================================================================
    //  网络同步
    // ================================================================

    [ClientRpc]
    private void SyncToClientsClientRpc(string json)
    {
        data.FromJson(json);
        OnEquipmentChanged?.Invoke();
    }

    private string Serialize() => data.ToJson();

    // ================================================================
    //  调试热键
    // ================================================================

void Update()
    {
        if (IsSpawned && !IsOwner) return;

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Alpha7)) EquipFromChest(5);
        if (Input.GetKeyDown(KeyCode.Alpha8)) EquipFromChest(0);
        if (Input.GetKeyDown(KeyCode.Alpha9)) data.ClearAll();
#endif
    }
}
