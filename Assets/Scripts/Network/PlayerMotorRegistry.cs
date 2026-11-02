using System.Collections.Generic;

/// <summary>
/// Main-thread registry of spawned player motors. The state channel uses it to
/// map a client to its owning motor on the server and to route snapshots by
/// NetworkObjectId on clients.
/// </summary>
public static class PlayerMotorRegistry
{
    private static readonly List<PlayerMotor> motors = new List<PlayerMotor>();
    private static readonly Dictionary<ulong, PlayerMotor> byObjectId = new Dictionary<ulong, PlayerMotor>();

    public static IReadOnlyList<PlayerMotor> All => motors;

    public static void Register(PlayerMotor motor)
    {
        if (motor == null)
            return;

        ulong objectId = motor.PredictionObjectId;
        if (objectId == 0UL)
            return;

        if (!motors.Contains(motor))
            motors.Add(motor);
        byObjectId[objectId] = motor;
    }

    public static void Unregister(PlayerMotor motor)
    {
        if (motor == null)
            return;

        motors.Remove(motor);
        ulong objectId = motor.PredictionObjectId;
        if (objectId != 0UL && byObjectId.TryGetValue(objectId, out PlayerMotor registered) && registered == motor)
            byObjectId.Remove(objectId);
    }

    public static PlayerMotor GetByObjectId(ulong objectId)
    {
        return byObjectId.TryGetValue(objectId, out PlayerMotor motor) ? motor : null;
    }

    public static PlayerMotor GetByOwnerClientId(ulong ownerClientId)
    {
        for (int i = 0; i < motors.Count; i++)
        {
            PlayerMotor motor = motors[i];
            if (motor != null && motor.OwnerClientId == ownerClientId)
                return motor;
        }

        return null;
    }

    /// <summary>
    /// Picks the player object used to carry the per-client state RPC. Prefer
    /// the host player (it exists on every client from the start), then fall
    /// back to any spawned motor for a dedicated server.
    /// </summary>
    public static PlayerMotor GetStateSender()
    {
        PlayerMotor fallback = null;
        for (int i = 0; i < motors.Count; i++)
        {
            PlayerMotor motor = motors[i];
            if (motor == null || !motor.IsSpawned)
                continue;

            if (motor.IsServer && motor.IsOwner)
                return motor;
            if (fallback == null)
                fallback = motor;
        }

        return fallback;
    }
}
