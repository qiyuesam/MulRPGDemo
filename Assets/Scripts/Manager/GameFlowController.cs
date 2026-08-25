using System;
using UnityEngine;

public enum GameMode
{
    None,
    Local,
    Host,
    Client
}

public sealed class GameFlowController : IDisposable
{
    private readonly NetworkSessionController networkSession;
    private readonly PlayerSpawner playerSpawner;
    private bool suppressDisconnectFallback;

    public GameMode CurrentMode { get; private set; } = GameMode.None;

    public GameFlowController(
        NetworkSessionController networkSession,
        PlayerSpawner playerSpawner)
    {
        this.networkSession = networkSession;
        this.playerSpawner = playerSpawner;
        networkSession.ClientDisconnected += OnClientDisconnected;
    }

    public void StartSinglePlayer()
    {
        if (CurrentMode == GameMode.Local)
            return;

        if (IsNetworkMode(CurrentMode))
            StopNetworkSession();

        EnterLocalMode(false);
    }

    public void StartHost()
    {
        if (CurrentMode == GameMode.Host)
        {
            Debug.Log("已经是 Host 模式");
            return;
        }

        PrepareNetworkMode(GameMode.Host);
        if (networkSession.StartHost())
        {
            Debug.Log("房间已创建（Host），等待其他玩家加入...");
            return;
        }

        Debug.LogError("启动 Host 失败！回退到本地模式");
        EnterLocalMode(true);
    }

    public void StartClient(string address)
    {
        if (CurrentMode == GameMode.Client)
        {
            Debug.Log("已经是 Client 模式");
            return;
        }

        PrepareNetworkMode(GameMode.Client);
        if (networkSession.StartClient(address))
        {
            Debug.Log("正在加入房间...");
            return;
        }

        Debug.LogError("加入房间失败！回退到本地模式");
        EnterLocalMode(true);
    }

    public void ShutdownNetwork()
    {
        if (!IsNetworkMode(CurrentMode))
            return;

        StopNetworkSession();
        EnterLocalMode(false);
    }

    public void Dispose()
    {
        networkSession.ClientDisconnected -= OnClientDisconnected;
    }

    private void PrepareNetworkMode(GameMode targetMode)
    {
        if (CurrentMode == GameMode.Local)
            playerSpawner.SaveAndDestroyLocalPlayer();
        else if (IsNetworkMode(CurrentMode))
            StopNetworkSession();

        CurrentMode = targetMode;
    }

    private void StopNetworkSession()
    {
        suppressDisconnectFallback = true;
        CurrentMode = GameMode.None;

        try
        {
            networkSession.Shutdown();
        }
        finally
        {
            suppressDisconnectFallback = false;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (suppressDisconnectFallback || !IsNetworkMode(CurrentMode))
            return;

        if (clientId == networkSession.LocalClientId)
            EnterLocalMode(true);
    }

    private void EnterLocalMode(bool stopNetworkFirst)
    {
        if (stopNetworkFirst && networkSession.IsListening)
            StopNetworkSession();

        playerSpawner.ClearCurrentPlayerReference();
        playerSpawner.SpawnLocalPlayer();
        CurrentMode = GameMode.Local;
        Debug.Log("[GameFlow] 已进入本地模式");
    }

    private static bool IsNetworkMode(GameMode mode)
    {
        return mode == GameMode.Host || mode == GameMode.Client;
    }
}
