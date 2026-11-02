using Unity.Netcode;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("预制体")]
    [SerializeField] private GameObject playerPrefab;

    [Header("网络连接")]
    [SerializeField] private string clientAddress = "127.0.0.1";
    [SerializeField] private string serverListenAddress = "0.0.0.0";
    [SerializeField, Range(1, 65535)] private int networkPort = 7777;
    [SerializeField, Min(1)] private int maxPlayers = 8;
    [SerializeField] private string connectionKey = "MulRPGDemo-v1";

    private NetworkSessionController networkSession;
    private PlayerSpawner playerSpawner;
    private GameFlowController gameFlow;

    public GameMode CurrentMode => gameFlow != null ? gameFlow.CurrentMode : GameMode.None;
    public GameObject CurrentPlayer => playerSpawner?.CurrentPlayer;

    private void Start()
    {
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        gameFlow?.Dispose();
        playerSpawner?.Dispose();
        networkSession?.Dispose();
    }

    public void OnStartSinglePlayerButton()
    {
        if (EnsureInitialized())
        {
            gameFlow.StartSinglePlayer();
            if (gameFlow.CurrentMode != GameMode.None)
                UIManager.Instance?.CloseNetworkPanel();
        }
    }

    public void OnStartHostButton()
    {
        if (EnsureInitialized())
        {
            gameFlow.StartHost();
            if (gameFlow.CurrentMode != GameMode.None)
                UIManager.Instance?.CloseNetworkPanel();
        }
    }

    public void OnStartClientButton()
    {
        if (EnsureInitialized())
        {
            gameFlow.StartClient(clientAddress);
            if (gameFlow.CurrentMode != GameMode.None)
                UIManager.Instance?.CloseNetworkPanel();
        }
    }

    public void OnShutDownButton()
    {
        if (EnsureInitialized())
        {
            gameFlow.ShutdownNetwork();
            UIManager.Instance?.OpenNetworkPanel();
        }
    }

    public void SetClientAddress(string address)
    {
        if (!string.IsNullOrWhiteSpace(address))
            clientAddress = address.Trim();
    }

    public void RegisterOwnedNetworkPlayer(GameObject player)
    {
        if (EnsureInitialized())
            playerSpawner.RegisterOwnedNetworkPlayer(player);
    }

    public void UnregisterOwnedNetworkPlayer(GameObject player)
    {
        playerSpawner?.UnregisterOwnedNetworkPlayer(player);
    }

    private bool EnsureInitialized()
    {
        if (gameFlow != null)
            return true;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[GameManager] 场景缺少 NetworkManager");
            return false;
        }

        networkSession = new NetworkSessionController(
            manager,
            serverListenAddress,
            networkPort,
            maxPlayers,
            connectionKey);

        NetworkPredictionChannel.EnsureInstance();
        playerSpawner = new PlayerSpawner(playerPrefab, networkSession);
        gameFlow = new GameFlowController(networkSession, playerSpawner);
        return true;
    }

    private void OnValidate()
    {
        networkPort = Mathf.Clamp(networkPort, 1, 65535);
        maxPlayers = Mathf.Max(1, maxPlayers);
    }
}
