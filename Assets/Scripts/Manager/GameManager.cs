using UnityEngine;
using Unity.Netcode;

public class GameManager : MonoBehaviour
{
    [Header("预制体")]
    [SerializeField] private GameObject playerPrefab;       // 联机玩家（挂 NetworkObject）
   
    // ================================================================
    //  ★ 新增：状态机
    // ================================================================

    private enum GameMode { None, Local, Host, Client }
    private GameMode currentMode = GameMode.None;
    private GameObject currentPlayer;   // 跟踪当前玩家实例（无论本地还是联机）

    // ================================================================
    //  生命周期
    // ================================================================

    void Start()
    {
        // ────── 客户端连接 ──────
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void OnDestroy()
    {
        // 安全解绑（如果 NetworkManager 还存在的话）
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ================================================================
    //  网络回调（改成了具名方法，不再是 lambda）
    // ================================================================

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"客户端连接, id={clientId}");
        if (NetworkManager.Singleton.IsServer)
            CreateNetworkPlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"客户端断开, id={clientId}");

        // ★ 如果是我们自己断开了（被踢/房主关房）
        if (clientId == NetworkManager.Singleton.LocalClientId 
            && currentMode != GameMode.Local)  // 如果已经是 Local 说明已处理过
        {
            ReturnToLocalMode();
        }
    }

    // ================================================================
    //  ★★★ 核心：三个入口 + 一个出口 ★★★
    // ================================================================

    /// 本地模式（单机）
    public void OnStartSinglePlayerButton()
    {
        if (currentMode == GameMode.Local)
            return;

        if (currentMode == GameMode.Host || currentMode == GameMode.Client)
        {
            NetworkManager.Singleton.Shutdown();
            // ReturnToLocalMode 已经帮你 SpawnLocalPlayer 了
            // 不需要再 Spawn
            return;   // ← 加这一行
        }

        SpawnLocalPlayer();
        currentMode = GameMode.Local;
    }


    /// 创建房间（Host）
    public void OnStartHostButton()
    {
        if (currentMode == GameMode.Host)
        {
            Debug.Log("已经是 Host 模式");
            return;
        }

        // 切到 Host 前：保存并清理当前状态
        SwitchToNetworkMode(GameMode.Host);

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("房间已创建（Host），等待其他玩家加入...");
        }
        else
        {
            Debug.LogError("启动 Host 失败！回退到本地模式");
            SpawnLocalPlayer();
            currentMode = GameMode.Local;
        }
    }

    /// 加入房间（Client）
    public void OnStartClientButton()
    {
        if (currentMode == GameMode.Client)
        {
            Debug.Log("已经是 Client 模式");
            return;
        }

        SwitchToNetworkMode(GameMode.Client);

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("正在加入房间...");
            // 注意：客户端连接成功后，服务器会调用 CreateNetworkPlayer
            // 客户端的 currentPlayer 在那时才赋值
        }
        else
        {
            Debug.LogError("加入房间失败！回退到本地模式");
            SpawnLocalPlayer();
            currentMode = GameMode.Local;
        }
    }

    /// 关闭网络
    public void OnShutDownButton()
    {
        if (currentMode != GameMode.Host && currentMode != GameMode.Client)
            return;

        NetworkManager.Singleton.Shutdown();
        // ★ 不等回调，Shutdown 是同步的，直接在这里切回本地
        ReturnToLocalMode();
    }


    // ================================================================
    //  ★★★ 模式切换核心方法 ★★★
    // ================================================================

    /// 从任意模式切到联机模式的前置处理
    private void SwitchToNetworkMode(GameMode targetMode)
    {
        if (currentMode == GameMode.Local && currentPlayer != null)
        {
            // ★ 在 Destroy 之前强制存盘（Destroy 是延迟的！）
            var inv = currentPlayer.GetComponent<Inventory>();
            if (inv != null)
            {
                inv.ForceSave();   // 立刻写入磁盘
            }

            Destroy(currentPlayer);
            currentPlayer = null;
        }

        if (currentMode == GameMode.Host || currentMode == GameMode.Client)
        {
            NetworkManager.Singleton.Shutdown();
        }

        currentMode = targetMode;
    }

    /// 从联机模式回到本地模式
    private void ReturnToLocalMode()
    {
        // 联机玩家的 Inventory.OnNetworkDespawn 已经被调用，存档已写入
        // currentPlayer 已经被 Netcode 销毁
        currentPlayer = null;

        Debug.Log("[GameManager] 已断开连接，回到本地模式");

        // 生成本地玩家（LocalInventory.Awake 会加载刚存的档）
        SpawnLocalPlayer();
        currentMode = GameMode.Local;
    }

    // ================================================================
    //  玩家生成
    // ================================================================

    /// 生成联机玩家（由服务器在客户端连接时调用）
    private void CreateNetworkPlayer(ulong clientId)
    {
        Vector3 spawnPos = new Vector3(
            Random.Range(-3f, 3f),
            2f,
            Random.Range(-3f, 3f)
        );

        GameObject playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(clientId);

        Debug.Log($"联机玩家已生成: clientId={clientId}, pos={spawnPos}");

        // ★ 记录：如果这是本地客户端，这就是 currentPlayer
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            currentPlayer = playerObj;
        }
    }

    /// 生成本地玩家
    private void SpawnLocalPlayer()
    {
        if (currentPlayer != null)
            Destroy(currentPlayer);

        Vector3 spawnPos = new Vector3(0, 2, 0);
        // ★ 只 Instantiate，不 Spawn → IsSpawned = false → 本地模式
        currentPlayer = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

        Debug.Log("本地玩家已生成");
    }
}
