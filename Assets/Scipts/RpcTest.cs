using Unity.Netcode;
using UnityEngine;

public class RpcTest : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer && IsOwner) // 只有拥有该对象的客户端发送第一个 RPC
        {
            ServerOnlyRpcServerRpc(0, NetworkObjectId);
        }
    }

    [ClientRpc]
    private void ClientAndHostRpcClientRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"Client Received the RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        if (IsOwner) // 只有拥有者发送回服务器
        {
            ServerOnlyRpcServerRpc(value + 1, sourceNetworkObjectId);
        }
    }

    [ServerRpc]
    private void ServerOnlyRpcServerRpc(int value, ulong sourceNetworkObjectId)
    {
        Debug.Log($"Server Received the RPC #{value} on NetworkObject #{sourceNetworkObjectId}");
        ClientAndHostRpcClientRpc(value, sourceNetworkObjectId);
    }
}