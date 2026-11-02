using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Quantized player state carried by the per-client state packet.
/// Position and velocity use centimetres so a two-byte value covers roughly
/// +/-327 metres (position) and +/-327 m/s (velocity). Yaw uses the full
/// ushort range for 360 degrees.
/// </summary>
[Serializable]
public struct CompactPlayerSnapshot : INetworkSerializable
{
    public const byte GroundedFlag = 1 << 0;

    public ulong ObjectId;
    public uint SimTick;
    public uint InputTick;
    public short PosX;
    public short PosY;
    public short PosZ;
    public ushort Yaw;
    public short VelX;
    public short VelY;
    public short VelZ;
    public byte Flags;
    public byte ActionFlags;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ObjectId);
        serializer.SerializeValue(ref SimTick);
        serializer.SerializeValue(ref InputTick);
        serializer.SerializeValue(ref PosX);
        serializer.SerializeValue(ref PosY);
        serializer.SerializeValue(ref PosZ);
        serializer.SerializeValue(ref Yaw);
        serializer.SerializeValue(ref VelX);
        serializer.SerializeValue(ref VelY);
        serializer.SerializeValue(ref VelZ);
        serializer.SerializeValue(ref Flags);
        serializer.SerializeValue(ref ActionFlags);
    }

    public static CompactPlayerSnapshot FromState(HybridMotionState state, ulong objectId)
    {
        return new CompactPlayerSnapshot
        {
            ObjectId = objectId,
            SimTick = state.SimulationTick,
            InputTick = state.InputTick,
            PosX = EncodeCentimetres(state.Position.x),
            PosY = EncodeCentimetres(state.Position.y),
            PosZ = EncodeCentimetres(state.Position.z),
            Yaw = EncodeYaw(state.Yaw),
            VelX = EncodeCentimetres(state.Velocity.x),
            VelY = EncodeCentimetres(state.Velocity.y),
            VelZ = EncodeCentimetres(state.Velocity.z),
            Flags = state.Grounded ? GroundedFlag : (byte)0,
            ActionFlags = state.ActionFlags,
        };
    }

    public HybridMotionState ToState()
    {
        return new HybridMotionState
        {
            SimulationTick = SimTick,
            InputTick = InputTick,
            Position = new Vector3(PosX * 0.01f, PosY * 0.01f, PosZ * 0.01f),
            Velocity = new Vector3(VelX * 0.01f, VelY * 0.01f, VelZ * 0.01f),
            Yaw = Yaw * (360f / 65535f),
            Grounded = (Flags & GroundedFlag) != 0,
            ActionFlags = ActionFlags,
        };
    }

    private static short EncodeCentimetres(float value)
    {
        if (!HybridMotionSimulator.IsFinite(value))
            return 0;

        int encoded = Mathf.RoundToInt(value * 100f);
        return (short)Mathf.Clamp(encoded, short.MinValue, short.MaxValue);
    }

    private static ushort EncodeYaw(float value)
    {
        if (!HybridMotionSimulator.IsFinite(value))
            return 0;

        float normalized = Mathf.Repeat(value, 360f) / 360f;
        int encoded = Mathf.RoundToInt(normalized * 65535f);
        return (ushort)Mathf.Clamp(encoded, 0, 65535);
    }
}

/// <summary>
/// One packet per receiving client: the owner's authoritative snapshot plus
/// the nearest remote snapshots selected by the server's interest culling.
/// </summary>
[Serializable]
public struct PredictionStatePacket : INetworkSerializable
{
    public uint ServerTick;
    public CompactPlayerSnapshot Owner;
    public CompactPlayerSnapshot[] Remote;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ServerTick);
        serializer.SerializeValue(ref Owner);
        serializer.SerializeValue(ref Remote);
    }
}
