// Assets/Scripts/Inventory/UI/InventoryUI.cs
using UnityEngine;

public class InventoryUI : UIPanel
{
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
    private IInventoryProvider provider;
    private InventorySlotUI[] slotUIs;
    private int selectedIndex = -1;

    void Awake()
    {
        slotUIs = new InventorySlotUI[InventoryData.SLOT_COUNT];
        for (int i = 0; i < slotUIs.Length; i++)
        {
            var go = Instantiate(slotPrefab, slotContainer);
            slotUIs[i] = go.GetComponent<InventorySlotUI>();
        }
    }

    protected override void OnOpen()
    {
        // 先激活面板根（面板根就是自身时，激活会触发 Awake 初始化 slotUIs）
        if (panel != null)
            panel.SetActive(true);

        // 每次打开都重新解析（可能已切换模式/重连）
        if (provider == null)
            provider = ResolveProvider();
        if (provider == null)
            return;

        provider.UnbindChanged(RefreshAll);
        provider.BindChanged(RefreshAll);

        for (int i = 0; i < slotUIs.Length; i++)
            slotUIs[i].Setup(this, provider, i);

        if (equipment == null)
            equipment = ResolveEquipment();
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

    protected override void OnClose()
    {
        panel.SetActive(false);
        selectedIndex = -1;
        if (equipment != null)
        {
            equipment.OnEquipmentChanged -= RefreshEquipmentSlots;
            equipment = null;
        }
        provider?.UnbindChanged(RefreshAll);
        provider = null;
    }

    public void OnSlotClicked(int index)
    {
        if (selectedIndex == -1)
        {
            selectedIndex = index;
            slotUIs[index].SetSelected(true);
        }
        else if (selectedIndex == index)
        {
            slotUIs[index].SetSelected(false);
            selectedIndex = -1;
        }
        else
        {
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

    private IInventoryProvider ResolveProvider()
    {
        var inv = UIManager.Instance != null ? UIManager.Instance.Player.Inventory : null;
        return inv != null ? new NetworkInventoryProvider(inv) : null;
    }

    void RefreshEquipmentSlots()
    {
        weaponSlot?.Refresh();
        headSlot?.Refresh();
        chestSlot?.Refresh();
        legsSlot?.Refresh();
        feetSlot?.Refresh();
    }

    private Equipment ResolveEquipment()
    {
        return UIManager.Instance != null ? UIManager.Instance.Player.Equipment : null;
    }

    void OnDestroy()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged -= RefreshEquipmentSlots;
        provider?.UnbindChanged(RefreshAll);
    }
}
