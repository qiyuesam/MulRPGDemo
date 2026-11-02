using System;
using Unity.Netcode;

/// <summary>
/// One batched input packet for a single owning client. The batch carries the
/// newest command plus a small redundant window of older commands so a lost or
/// delayed packet cannot stall the authoritative simulation.
/// </summary>
[Serializable]
public struct PlayerCommandBatch : INetworkSerializable
{
    public int Count;
    public HybridInputCommand[] Commands;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Commands);
    }
}
