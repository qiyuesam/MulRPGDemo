using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance;  // 简单单例

    [SerializeField] private GameObject panel;
    [SerializeField] private Transform slotContainer;
    [SerializeField] private GameObject slotPrefab;

    private Inventory inventory;
    private InventorySlotUI[] slotUIs;
    private int selectedIndex = -1;   // -1 = 没选中
    private bool isOpen;

    void Awake()
    {
        Instance = this;
        slotUIs = new InventorySlotUI[20];

        // 生成 20 个格子
        for (int i = 0; i < 20; i++)
        {
            var go = Instantiate(slotPrefab, slotContainer);
            slotUIs[i] = go.GetComponent<InventorySlotUI>();
        }
    }
    
    public void Open()
    {
        // 先初始化格子（刚生成玩家时 Inventory 可能还没 Ready）
        if (inventory == null)
        {
            var found = FindObjectsOfType<Inventory>();
            if(found==null)
                Debug.LogError("未找到");
            foreach (var inv in found)
            {
                if (inv.IsOwner)
                {
                    inventory = inv;
                    inventory.OnInventoryChanged += RefreshAll;
                    break;
                }
            }
        }
        if (inventory == null) return;
        for (int i = 0; i < 20; i++)
            slotUIs[i].Setup(inventory, i);

        panel.SetActive(true);
        isOpen = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // 停掉摄像机旋转（可选）
        // 停掉角色移动输入
    }

    public void Close()
    {
        panel.SetActive(false);
        isOpen = false;
        selectedIndex = -1;
    }

    public void OnSlotClicked(int index)
    {
        if (selectedIndex == -1)
        {
            // 还没选中 → 选中这个
            selectedIndex = index;
            slotUIs[index].SetSelected(true);
        }
        else if (selectedIndex == index)
        {
            // 点同一个 → 取消选中
            slotUIs[index].SetSelected(false);
            selectedIndex = -1;
        }
        else
        {
            // 已经选中别的 → 交换
            inventory.SwapSlotsServerRpc(selectedIndex, index);
            slotUIs[selectedIndex].SetSelected(false);
            selectedIndex = -1;
        }
    }

    void RefreshAll()
    {
        for (int i = 0; i < 20; i++)
            slotUIs[i]?.Refresh();
    }
}
