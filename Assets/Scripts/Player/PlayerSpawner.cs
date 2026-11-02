using System;
using Unity.Netcode;
using UnityEngine;

public sealed class PlayerSpawner : IDisposable
{
    private readonly GameObject playerPrefab;
    private readonly NetworkSessionController networkSession;
    private GameObject scenePlayer;

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

        // Game.unity already contains a Player prefab instance so that the
        // scene can be previewed directly. Reuse that instance for local and
        // host play; instantiating a second copy would make the scene object
        // auto-spawn as an extra network player when Host starts.
        GameObject existingScenePlayer = FindScenePlayer();
        if (existingScenePlayer != null)
        {
            if (CurrentPlayer != null && CurrentPlayer != existingScenePlayer)
                UnityEngine.Object.Destroy(CurrentPlayer);

            existingScenePlayer.SetActive(true);
            existingScenePlayer.transform.SetPositionAndRotation(
                new Vector3(0f, 2f, 0f),
                Quaternion.identity);
            existingScenePlayer.GetComponent<PlayerMotor>()?.ResetForLocalMode();
            existingScenePlayer.GetComponent<CombatSystem>()?.ResetForLocalMode();
            existingScenePlayer.GetComponent<PlayerController>()?.PrepareForLocalMode();
            CurrentPlayer = existingScenePlayer;
            Debug.Log("鏈湴鐜╁宸茬敤鐢ㄥ満鏅柟 Player");
            return;
        }

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

        if (IsScenePlayer(CurrentPlayer))
        {
            // Keep the scene NetworkObject alive so NetworkManager can
            // auto-spawn it for the Host. It will be reused when returning to
            // local mode after shutdown.
            CurrentPlayer = null;
            return;
        }

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
        // Scene Player objects are intentionally reused when a network
        // session shuts down and local mode resumes. Their NetworkObject
        // despawn callback can arrive after EnterLocalMode has already
        // assigned CurrentPlayer, so do not erase that reusable reference.
        if (CurrentPlayer == player && !IsScenePlayer(player))
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

        // The scene Player is the Host's player object. NetworkManager may
        // have spawned it automatically before OnClientConnected fires. Do
        // not create a duplicate; simply make sure ownership is assigned to
        // the local server client and register it.
        if (clientId == networkSession.LocalClientId)
        {
            GameObject existingScenePlayer = FindScenePlayer();
            NetworkObject existingNetworkObject = existingScenePlayer != null
                ? existingScenePlayer.GetComponent<NetworkObject>()
                : null;
            if (existingNetworkObject != null && existingNetworkObject.IsSpawned)
            {
                if (existingNetworkObject.OwnerClientId != clientId)
                    existingNetworkObject.ChangeOwnership(clientId);
                CurrentPlayer = existingScenePlayer;
                Debug.Log($"鍏煎澶勭悊 Host Player: clientId={clientId}");
                return;
            }
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

    private GameObject FindScenePlayer()
    {
        if (scenePlayer != null)
            return scenePlayer;

        string expectedName = playerPrefab != null ? playerPrefab.name : "Player";
        GameObject candidate = GameObject.Find(expectedName);
        if (candidate == null || candidate.transform.parent != null)
            return null;

        NetworkObject networkObject = candidate.GetComponent<NetworkObject>();
        if (networkObject == null)
            return null;

        scenePlayer = candidate;
        return scenePlayer;
    }

    private bool IsScenePlayer(GameObject candidate)
    {
        return candidate != null && candidate == scenePlayer;
    }
}
