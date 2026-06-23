// Assets/Scripts/Inventory/UI/InventoryUI.cs（修改后）

using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance;

    [SerializeField] private GameObject panel;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private GameObject slotPrefab;
    [Header("装备槽")]
    [SerializeField] private EquipmentSlotUI weaponSlot;
    [SerializeField] private EquipmentSlotUI headSlot;
    [SerializeField] private EquipmentSlotUI chestSlot;
    [SerializeField] private EquipmentSlotUI legsSlot;
    [SerializeField] private EquipmentSlotUI feetSlot;
    private Equipment equipment;
    
    // ★ 改：Inventory → IInventoryProvider
    private IInventoryProvider provider;
    private InventorySlotUI[] slotUIs;
    private int selectedIndex = -1;
    private bool isOpen;

    void Awake()
    {
        Instance = this;
        // ★ 最大槽位先用常量；provider 确定后再真正初始化
        slotUIs = new InventorySlotUI[InventoryData.SLOT_COUNT];

        for (int i = 0; i < slotUIs.Length; i++)
        {
            var go = Instantiate(slotPrefab, slotContainer);
            slotUIs[i] = go.GetComponent<InventorySlotUI>();
        }
    }

    public void Open()
    {
        // ★ 每次都重新查找（打开背包前可能已切换模式/重连）
        if (provider == null)
        {
            provider = ResolveProvider();
        }
        if (provider == null) return;

        // ★ 注册变更回调，保证只注册一次
        provider.UnbindChanged(RefreshAll);
        provider.BindChanged(RefreshAll);

        for (int i = 0; i < slotUIs.Length; i++)
            slotUIs[i].Setup(provider, i);

        panel.SetActive(true);
        isOpen = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (equipment == null)
            equipment = FindLocalEquipment();
        if (equipment != null)
        {
            equipment.OnEquipmentChanged -= RefreshEquipmentSlots;
            equipment.OnEquipmentChanged += RefreshEquipmentSlots;
            weaponSlot.Setup(equipment, EquipSlotType.Weapon);
            headSlot.Setup(equipment, EquipSlotType.Head);
            chestSlot.Setup(equipment, EquipSlotType.Chest);
            legsSlot.Setup(equipment, EquipSlotType.Legs);
            feetSlot.Setup(equipment, EquipSlotType.Feet);
            RefreshEquipmentSlots();
        }
    }

    public void Close()
    {
        panel.SetActive(false);
        isOpen = false;
        selectedIndex = -1;
        // ★ 关闭时解绑，避免空回调
        if (equipment != null)
        {
            equipment.OnEquipmentChanged -= RefreshEquipmentSlots;
            equipment = null;
        }
        provider?.UnbindChanged(RefreshAll);
        provider = null;  // 下次 Open 重新解析（适应模式切换）
    }

    public void OnSlotClicked(int index)
    {
        if (selectedIndex == -1)
        {
            // 选中
            selectedIndex = index;
            slotUIs[index].SetSelected(true);
        }
        else if (selectedIndex == index)
        {
            // 取消选中
            slotUIs[index].SetSelected(false);
            selectedIndex = -1;
        }
        else
        {
            // ★ 通过接口交换，不再直接调 ServerRpc
            provider.SwapSlots(selectedIndex, index);
            slotUIs[selectedIndex].SetSelected(false);
            selectedIndex = -1;
        }
    }

    void RefreshAll()
    {
        for (int i = 0; i < slotUIs.Length; i++)
            slotUIs[i]?.Refresh();
    }

    // ★★★ 核心新增方法：自动选择 Provider ★★★
    private IInventoryProvider ResolveProvider()
    {
        // ★ 遍历所有 Inventory，找到属于自己（Owner）的
        foreach (var inv in FindObjectsOfType<Inventory>())
        {
            // 本地模式 (IsSpawned=false) 或 联机自己是 Owner
            if (!inv.IsSpawned || inv.IsOwner)
                return new NetworkInventoryProvider(inv);
        }
        return null;
    }
    void RefreshEquipmentSlots()
    {
        weaponSlot?.Refresh();
        headSlot?.Refresh();
        chestSlot?.Refresh();
        legsSlot?.Refresh();
        feetSlot?.Refresh();
    }

    Equipment FindLocalEquipment()
    {
        foreach (var eq in FindObjectsOfType<Equipment>())
            if (!eq.IsSpawned || eq.IsOwner) return eq;
        return null;
    }
}
