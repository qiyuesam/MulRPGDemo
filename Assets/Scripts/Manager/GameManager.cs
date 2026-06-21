using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField]
    private GameObject playerPrefab;
    // Start is called before the first frame update
    void Start()
    {
        
        
        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Debug.Log("A client connected to the server,id=" + id);
            if(NetworkManager.Singleton.IsServer)CreatePlayer(id);
        };
        NetworkManager.Singleton.OnClientDisconnectCallback += (id) =>
        {
            Debug.Log("A client disconnected from the server,id=" + id);
            
        };
        NetworkManager.Singleton.OnServerStarted += () =>
        {
            Debug.Log("Server has started");
        };
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void CreatePlayer(ulong clientId)
    {
        // 给每个客户端一个不同的出生位置，避免重叠
        Vector3 spawnPos = new Vector3(
            Random.Range(-3f, 3f),
            2f,           // ← Y 轴抬高，确保 CC 底部在地面以上
            Random.Range(-3f, 3f)
        );
    
        GameObject playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(clientId);
        Debug.Log($"Player spawned for client {clientId} at {spawnPos}");
    }

    
    public void OnStartServerButton()
    {
        if (NetworkManager.Singleton.StartServer())
        {
            Debug.Log("Server started");
        }
        else
        {
            Debug.Log("Failed to start server");
        }
    }

    public void OnStartClientButton()
    {
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Start as client suc");
        }
        else
        {
            Debug.Log("Failed to start client");
        }
    }

    public void OnStartHostButton()
    {
        if(NetworkManager.Singleton.StartHost())
        {
            Debug.Log("Start as Host suc");
        }
        else
        {
            Debug.Log("Failed to start Host");
        }
    }

    public void OnShutDownButton()
    {
        NetworkManager.Singleton.Shutdown();
        Debug.Log("Shut down");
    }
}
