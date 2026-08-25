using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;
    public static event System.Action<PlayerStats> LocalPlayerStatsChanged;

    [Header("面板引用")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private InventoryUI inventoryUI;

    [Header("状态")]
    [SerializeField] private bool isInventoryOpen;

    // ★ 新增：缓存的 Inventory 引用
    private Inventory playerInventory;
    public PlayerStats LocalPlayerStats { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // ★ Start 时找一次（GameManager 通常在 Start 生成玩家）
        CachePlayerInventory();
    }

    // ★ 新增：缓存方法，外部（GameManager）也可以调用
    public void OnPlayerSpawned(Inventory inv)
    {
        if (inv == null) return;
        playerInventory = inv;
        LocalPlayerStats = inv.GetComponent<PlayerStats>();
        QuestUI.Instance?.Bind(inv.GetComponent<QuestLog>());
        LocalPlayerStatsChanged?.Invoke(LocalPlayerStats);
    }

    public void OnPlayerDespawned(Inventory inv = null)
    {
        if (inv != null && playerInventory != inv)
            return;
        playerInventory = null;
        LocalPlayerStats = null;
        QuestUI.Instance?.Bind(null);
        LocalPlayerStatsChanged?.Invoke(null);
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
                LocalPlayerStats = inv.GetComponent<PlayerStats>();
                LocalPlayerStatsChanged?.Invoke(LocalPlayerStats);
                break;
            }
        }
    }

    public void HandleEscape()
    {
        if (QuestUI.Instance != null && QuestUI.Instance.IsVisible)
        {
            QuestUI.Instance.Hide();
            return;
        }
        ToggleInventory();
    }

    public void ToggleInventory()
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
        bool anyPanelOpen = isInventoryOpen ||
                            (QuestUI.Instance != null && QuestUI.Instance.IsVisible);
        Cursor.visible = anyPanelOpen;
        Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }

    public void RefreshCursor() => UpdateCursor();

    void OnDestroy()
    {
        if (Instance != this) return;
        LocalPlayerStatsChanged?.Invoke(null);
        LocalPlayerStatsChanged = null;
        Instance = null;
    }
}
