using Unity.Netcode;
using UnityEngine;

public sealed class OwnedPlayerRegistration : NetworkBehaviour
{
    private GameManager gameManager;
    private bool registered;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        gameManager = FindObjectOfType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[OwnedPlayerRegistration] 场景中找不到 GameManager");
            return;
        }

        gameManager.RegisterOwnedNetworkPlayer(gameObject);
        registered = true;
    }

    public override void OnNetworkDespawn()
    {
        if (registered && gameManager != null)
            gameManager.UnregisterOwnedNetworkPlayer(gameObject);

        registered = false;
        base.OnNetworkDespawn();
    }
}
