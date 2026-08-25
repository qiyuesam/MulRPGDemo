using System;
using Unity.Netcode;
using UnityEngine;

public sealed class PlayerSpawner : IDisposable
{
    private readonly GameObject playerPrefab;
    private readonly NetworkSessionController networkSession;

    public GameObject CurrentPlayer { get; private set; }

    public PlayerSpawner(GameObject playerPrefab, NetworkSessionController networkSession)
    {
        this.playerPrefab = playerPrefab;
        this.networkSession = networkSession;
        networkSession.ClientConnected += OnClientConnected;
    }

    public void SpawnLocalPlayer()
    {
        if (!ValidatePlayerPrefab())
            return;

        if (CurrentPlayer != null)
            UnityEngine.Object.Destroy(CurrentPlayer);

        CurrentPlayer = UnityEngine.Object.Instantiate(
            playerPrefab,
            new Vector3(0f, 2f, 0f),
            Quaternion.identity);

        Debug.Log("本地玩家已生成");
    }

    public void SaveAndDestroyLocalPlayer()
    {
        if (CurrentPlayer == null)
            return;

        Inventory inventory = CurrentPlayer.GetComponent<Inventory>();
        inventory?.ForceSave();

        QuestLog questLog = CurrentPlayer.GetComponent<QuestLog>();
        questLog?.ForceSave();

        UnityEngine.Object.Destroy(CurrentPlayer);
        CurrentPlayer = null;
    }

    public void RegisterOwnedNetworkPlayer(GameObject player)
    {
        if (player == null)
            return;

        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject == null || !networkObject.IsOwner)
            return;

        CurrentPlayer = player;
        Debug.Log($"已登记本地拥有的联网玩家: clientId={networkObject.OwnerClientId}");
    }

    public void UnregisterOwnedNetworkPlayer(GameObject player)
    {
        if (CurrentPlayer == player)
            CurrentPlayer = null;
    }

    public void ClearCurrentPlayerReference()
    {
        CurrentPlayer = null;
    }

    public void Dispose()
    {
        networkSession.ClientConnected -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (networkSession.IsServer)
            SpawnNetworkPlayer(clientId);
    }

    private void SpawnNetworkPlayer(ulong clientId)
    {
        if (!ValidatePlayerPrefab())
        {
            networkSession.DisconnectClient(clientId);
            return;
        }

        Vector3 spawnPosition = new Vector3(
            UnityEngine.Random.Range(-3f, 3f),
            2f,
            UnityEngine.Random.Range(-3f, 3f));

        GameObject player = UnityEngine.Object.Instantiate(
            playerPrefab,
            spawnPosition,
            Quaternion.identity);

        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        networkObject.SpawnWithOwnership(clientId);

        if (clientId == networkSession.LocalClientId)
            CurrentPlayer = player;

        Debug.Log($"联机玩家已生成: clientId={clientId}, pos={spawnPosition}");
    }

    private bool ValidatePlayerPrefab()
    {
        if (playerPrefab != null && playerPrefab.GetComponent<NetworkObject>() != null)
            return true;

        Debug.LogError("[PlayerSpawner] Player Prefab 为空或缺少 NetworkObject");
        return false;
    }
}
