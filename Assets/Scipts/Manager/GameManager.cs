using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Debug.Log("A client connected to the server,id=" + id);
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
        if(NetworkManager.Singleton.StartServer())
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
