using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 层唯一权威：玩家上下文分发、面板栈、cursor、输入路由。
/// 面板与 HUD 通过它访问本地玩家，不再各自 FindObjectsOfType。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    public static event Action<PlayerContext> LocalPlayerChanged;

    [Header("面板引用")]
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private QuestUI questUI;
    [SerializeField] private NetworkPanel networkPanel;

    private readonly List<UIPanel> openPanels = new List<UIPanel>();

    public PlayerContext Player { get; private set; }
    public InventoryUI InventoryPanel => inventoryUI;
    public QuestUI QuestPanel => questUI;
    public NetworkPanel NetworkPanel => networkPanel;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // QuestUI 在独立根节点「QuestSystem」上，启动时找一次（仅一次，非每帧/每次打开）。
        if (questUI == null)
            questUI = FindObjectOfType<QuestUI>();
        if (networkPanel == null)
            networkPanel = FindObjectOfType<NetworkPanel>();
    }

    void Start()
    {
        // 场景里可能已经摆好了本地玩家，启动时先绑定一次。
        CachePlayerInventory();
    }

    // ================================================================
    //  玩家上下文
    // ================================================================

    public void OnPlayerSpawned(Inventory inv)
    {
        if (inv == null)
            return;
        Player = new PlayerContext(inv);
        LocalPlayerChanged?.Invoke(Player);
    }

    public void OnPlayerDespawned(Inventory inv = null)
    {
        if (inv != null && Player.Valid && Player.Inventory != inv)
            return;

        Player = default;
        CloseAllPanels();
        LocalPlayerChanged?.Invoke(default);
    }

    // ================================================================
    //  面板栈 + cursor
    // ================================================================

    public void NotifyPanelOpened(UIPanel panel)
    {
        if (panel == null || openPanels.Contains(panel))
            return;
        openPanels.Add(panel);
        UpdateCursor();
    }

    public void NotifyPanelClosed(UIPanel panel)
    {
        if (panel == null)
            return;
        openPanels.Remove(panel);
        UpdateCursor();
    }

    private void CloseAllPanels()
    {
        for (int i = openPanels.Count - 1; i >= 0; i--)
            openPanels[i].Close();
    }

    // cursor 唯一权威处
    private void UpdateCursor()
    {
        bool anyOpen = openPanels.Count > 0;
        Cursor.visible = anyOpen;
        Cursor.lockState = anyOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }

    public void RefreshCursor() => UpdateCursor();

    // ================================================================
    //  输入路由
    // ================================================================

    public void HandleEscape()
    {
        if (openPanels.Count > 0 && openPanels[openPanels.Count - 1].ClosableWithEscape)
        {
            openPanels[openPanels.Count - 1].Close();
            return;
        }
        ToggleInventory();
    }

    public void ToggleInventory()
    {
        if (!Player.Valid || inventoryUI == null)
            return;
        if (inventoryUI.IsOpen)
            inventoryUI.Close();
        else
            inventoryUI.Open();
    }

    public void ToggleQuest()
    {
        if (!Player.Valid || questUI == null)
            return;
        if (questUI.IsOpen)
            questUI.Close();
        else
            questUI.Open();
    }

    /// <summary>打开网络模式选择菜单（主菜单）。</summary>
    public void OpenNetworkPanel() => networkPanel?.Open();

    /// <summary>关闭网络模式选择菜单（进入游戏）。</summary>
    public void CloseNetworkPanel() => networkPanel?.Close();

    /// <summary>切换网络模式选择菜单（快捷键 I）。</summary>
    public void ToggleNetworkPanel()
    {
        if (networkPanel == null)
            return;
        if (networkPanel.IsOpen)
            networkPanel.Close();
        else
            networkPanel.Open();
    }

    void Update()
    {
        // 任务日志快捷键 J（暂用旧 Input，后续可迁入 Input System 的 Journal action）
        if (Input.GetKeyDown(KeyCode.J))
            ToggleQuest();

        // 网络模式选择菜单快捷键 I
        if (Input.GetKeyDown(KeyCode.I))
            ToggleNetworkPanel();
    }

    // ================================================================
    //  跨系统访问（QuestGiver / QuestLog 经此访问面板，不再依赖单例）
    // ================================================================

    public void ShowGiver(QuestGiver giver, QuestLog log) => questUI?.ShowGiver(giver, log);
    public void BindQuestLog(QuestLog log) => questUI?.Bind(log);
    public void UnbindQuestLog(QuestLog log) => questUI?.Unbind(log);

    // ================================================================
    //  内部
    // ================================================================

    private void CachePlayerInventory()
    {
        var invs = FindObjectsOfType<Inventory>();
        foreach (var inv in invs)
        {
            if (!inv.IsSpawned || inv.IsOwner)
            {
                OnPlayerSpawned(inv);
                break;
            }
        }
    }

    void OnDestroy()
    {
        if (Instance != this)
            return;
        LocalPlayerChanged?.Invoke(default);
        LocalPlayerChanged = null;
        Instance = null;
    }
}
