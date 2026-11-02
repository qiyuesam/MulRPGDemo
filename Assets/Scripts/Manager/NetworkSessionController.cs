using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public sealed class NetworkSessionController : IDisposable
{
    private readonly NetworkManager networkManager;
    private readonly string serverListenAddress;
    private readonly ushort port;
    private readonly int maxPlayers;
    private readonly byte[] connectionKey;

    public event Action<ulong> ClientConnected;
    public event Action<ulong> ClientDisconnected;

    public bool IsServer => networkManager != null && networkManager.IsServer;
    public bool IsListening => networkManager != null && networkManager.IsListening;
    public ulong LocalClientId => networkManager != null ? networkManager.LocalClientId : 0;

    public NetworkSessionController(
        NetworkManager networkManager,
        string serverListenAddress,
        int networkPort,
        int maxPlayers,
        string connectionKey)
    {
        this.networkManager = networkManager;
        this.serverListenAddress = string.IsNullOrWhiteSpace(serverListenAddress)
            ? "0.0.0.0"
            : serverListenAddress.Trim();
        port = (ushort)Mathf.Clamp(networkPort, 1, 65535);
        this.maxPlayers = Mathf.Max(1, maxPlayers);
        this.connectionKey = Encoding.UTF8.GetBytes(connectionKey ?? string.Empty);

        ConfigureNetworkManager();
        SubscribeCallbacks();
    }

    public bool StartHost()
    {
        if (!ConfigureTransport("127.0.0.1", true))
            return false;

        LogNetworkConfig("Host");
        return networkManager.StartHost();
    }

    public bool StartClient(string address)
    {
        string endpoint = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        if (!ConfigureTransport(endpoint, false))
            return false;

        LogNetworkConfig("Client");
        return networkManager.StartClient();
    }

    public void Shutdown()
    {
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();
    }

    public void DisconnectClient(ulong clientId)
    {
        if (networkManager != null && networkManager.IsServer)
            networkManager.DisconnectClient(clientId);
    }

    public void Dispose()
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        networkManager.ConnectionApprovalCallback -= ApprovalCheck;
    }

    private void ConfigureNetworkManager()
    {
        NetworkConfig config = networkManager.NetworkConfig;
        config.ProtocolVersion = 0;
        // Keep the network simulation cadence aligned with the 50 Hz fixed
        // movement step. Transport may still batch packets independently.
        config.TickRate = 50;
        config.ConnectionApproval = true;
        config.ForceSamePrefabs = true;
        config.EnableSceneManagement = true;
        config.EnsureNetworkVariableLengthSafety = true;
        config.RpcHashSize = HashSize.VarIntEightBytes;
        config.ConnectionData = connectionKey;
    }

    private void SubscribeCallbacks()
    {
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        networkManager.ConnectionApprovalCallback += ApprovalCheck;
    }

    private bool ConfigureTransport(string endpoint, bool hosting)
    {
        UnityTransport transport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            Debug.LogError("[NetworkSession] NetworkTransport 不是 UnityTransport");
            return false;
        }

        transport.SetConnectionData(endpoint, port, hosting ? serverListenAddress : null);
        return true;
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"客户端连接, id={clientId}");
        ClientConnected?.Invoke(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"客户端断开, id={clientId}");
        ClientDisconnected?.Invoke(clientId);
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        bool keyMatches = KeysMatch(request.Payload, connectionKey);
        bool hasCapacity = networkManager.ConnectedClientsIds.Count < maxPlayers;

        response.Approved = keyMatches && hasCapacity;
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Reason = !keyMatches
            ? "客户端版本或连接密钥不匹配"
            : !hasCapacity
                ? "房间人数已满"
                : string.Empty;
    }

    private void LogNetworkConfig(string role)
    {
        NetworkConfig config = networkManager.NetworkConfig;
        Debug.Log($"[{role}] ConfigHash = {config.GetConfig(false)}");
        Debug.Log(
            $"[{role}] ProtocolVersion={config.ProtocolVersion} " +
            $"TickRate={config.TickRate} ForceSamePrefabs={config.ForceSamePrefabs}");
    }

    private static bool KeysMatch(byte[] actual, byte[] expected)
    {
        if (actual == null || actual.Length != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
                return false;
        }

        return true;
    }
}
