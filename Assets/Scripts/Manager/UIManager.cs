using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("面板引用")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventoryUI inventoryUI;

    [Header("状态")]
    [SerializeField] private bool isInventoryOpen;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        // ====== 背包 ======
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.B))
            ToggleInventory();
    }

    void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;

        if (isInventoryOpen)
        {
            inventoryPanel.SetActive(true);
            inventoryUI.Open();
        }
        else
        {
            inventoryPanel.SetActive(false);
            inventoryUI.Close();
        }

        UpdateCursor();
    }

    void UpdateCursor()
    {
        bool anyPanelOpen = isInventoryOpen; // 将来: || isMapOpen || isShopOpen
        Cursor.visible = anyPanelOpen;
        Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }
}