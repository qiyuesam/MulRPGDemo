using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("面板引用")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventoryUI inventoryUI;

    [Header("状态")]
    [SerializeField] private bool isInventoryOpen;

    // ★ 新增：缓存的 Inventory 引用
    private Inventory playerInventory;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // ★ Start 时找一次（GameManager 通常在 Start 生成玩家）
        CachePlayerInventory();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.B))
            ToggleInventory();
    }

    // ★ 新增：缓存方法，外部（GameManager）也可以调用
    public void OnPlayerSpawned(Inventory inv)
    {
        playerInventory = inv;
    }

    public void OnPlayerDespawned()
    {
        playerInventory = null;
        if (isInventoryOpen)
        {
            // 玩家消失时自动关闭背包
            isInventoryOpen = false;
            inventoryPanel.SetActive(false);
            inventoryUI.Close();
            UpdateCursor();
        }
    }

    private void CachePlayerInventory()
    {
        // 只调用一次 FindObjectsOfType（或改成 FindObjectOfType）
        var invs = FindObjectsOfType<Inventory>();
        foreach (var inv in invs)
        {
            if (!inv.IsSpawned || inv.IsOwner)
            {
                playerInventory = inv;
                break;
            }
        }
    }

    void ToggleInventory()
    {
        // ★ 改：直接用缓存引用判断，O(1) 代替 O(n)
        if (playerInventory == null)
            return;

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
        bool anyPanelOpen = isInventoryOpen;
        Cursor.visible = anyPanelOpen;
        Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }
}
