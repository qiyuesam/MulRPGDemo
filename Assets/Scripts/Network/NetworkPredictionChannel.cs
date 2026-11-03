using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central state channel. The server compiles one quantized state packet per
/// client (owner ack + nearby remote snapshots) and sends it through a pooled
/// ClientRpc on a spawned player object. Clients route the snapshots back to
/// their PlayerMotor instances by NetworkObjectId.
///
/// Input stays on the per-player batched ServerRpc; only the state path is
/// centralized here so it can be quantized and interest-culled.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class NetworkPredictionChannel : MonoBehaviour
{
    public static NetworkPredictionChannel Instance { get; private set; }

    [Header("State cadence")]
    [SerializeField, Range(1, 4)] private int stateSendIntervalTicks = 2;
    [SerializeField, Min(1f)] private float stateCullRadius = 60f;
    [SerializeField, Range(1, 32)] private int maxRemoteSnapshots = 16;

    private struct RemoteCandidate
    {
        public float SqrDistance;
        public CompactPlayerSnapshot Snapshot;
    }

    private NetworkManager networkManager;
    private uint nextStateSendTick;
    private readonly List<RemoteCandidate> remoteCandidates = new List<RemoteCandidate>();

    public static NetworkPredictionChannel EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        GameObject host = new GameObject("NetworkPredictionChannel");
        DontDestroyOnLoad(host);
        return host.AddComponent<NetworkPredictionChannel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        stateSendIntervalTicks = Mathf.Clamp(stateSendIntervalTicks, 1, 4);
        stateCullRadius = Mathf.Max(1f, stateCullRadius);
        maxRemoteSnapshots = Mathf.Clamp(maxRemoteSnapshots, 1, 32);
    }

    private void Start()
    {
        networkManager = NetworkManager.Singleton;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void FixedUpdate()
    {
        if (networkManager == null || !networkManager.IsServer || !networkManager.IsListening)
            return;

        NetworkTickClock clock = NetworkTickClock.Instance;
        if (clock == null || !clock.AdvancedThisFixedUpdate)
            return;

        // A tick accumulator avoids skipping every send if the network tick
        // jumps by an even number and the modulo phase is unlucky. It also
        // resets automatically when the global clock restarts at 0.
        if (clock.CurrentTick < nextStateSendTick)
            nextStateSendTick = clock.CurrentTick;
        if (clock.CurrentTick < nextStateSendTick)
            return;

        nextStateSendTick = clock.CurrentTick + (uint)Mathf.Max(1, stateSendIntervalTicks);
        SendStatePackets();
    }

    private void SendStatePackets()
    {
        PlayerMotor sender = PlayerMotorRegistry.GetStateSender();
        if (sender == null)
            return;

        IReadOnlyList<ulong> clientIds = networkManager.ConnectedClientsIds;
        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            PlayerMotor owner = PlayerMotorRegistry.GetByOwnerClientId(clientId);
            if (owner == null || !owner.TryCaptureAuthoritativeSnapshot(out HybridMotionState ownerState))
                continue;

            CompactPlayerSnapshot[] remotes = BuildRemoteSnapshots(owner, ownerState.Position);
            PredictionStatePacket packet = new PredictionStatePacket
            {
                ServerTick = NetworkTickClock.Instance != null
                    ? NetworkTickClock.Instance.CurrentTick
                    : 0u,
                Owner = CompactPlayerSnapshot.FromState(ownerState, owner.PredictionObjectId),
                Remote = remotes,
            };

            sender.SendPredictionStatePacket(packet, clientId);
        }
    }

    private CompactPlayerSnapshot[] BuildRemoteSnapshots(PlayerMotor owner, Vector3 ownerPosition)
    {
        remoteCandidates.Clear();
        float maxSqrDistance = stateCullRadius * stateCullRadius;

        IReadOnlyList<PlayerMotor> motors = PlayerMotorRegistry.All;
        for (int i = 0; i < motors.Count; i++)
        {
            PlayerMotor motor = motors[i];
            if (motor == owner || motor == null)
                continue;
            if (!motor.TryCaptureAuthoritativeSnapshot(out HybridMotionState state))
                continue;

            float sqrDistance = (state.Position - ownerPosition).sqrMagnitude;
            if (sqrDistance > maxSqrDistance)
                continue;

            remoteCandidates.Add(new RemoteCandidate
            {
                SqrDistance = sqrDistance,
                Snapshot = CompactPlayerSnapshot.FromState(state, motor.PredictionObjectId),
            });
        }

        remoteCandidates.Sort((left, right) => left.SqrDistance.CompareTo(right.SqrDistance));

        int count = Mathf.Min(remoteCandidates.Count, maxRemoteSnapshots);
        if (count == 0)
            return Array.Empty<CompactPlayerSnapshot>();

        CompactPlayerSnapshot[] snapshots = new CompactPlayerSnapshot[count];
        for (int i = 0; i < count; i++)
            snapshots[i] = remoteCandidates[i].Snapshot;

        return snapshots;
    }

    /// <summary>
    /// Called by the receiving client-side PlayerMotor RPC handler. Static so
    /// routing still works if the channel component was not created yet.
    /// </summary>
    public static void ApplyStatePacket(PredictionStatePacket packet)
    {
        if (packet.Owner.ObjectId == 0UL)
            return;

        PlayerMotor owner = PlayerMotorRegistry.GetByObjectId(packet.Owner.ObjectId);
        if (owner != null)
            owner.ApplyOwnerSnapshot(packet.Owner.ToState());

        if (packet.Remote == null)
            return;

        for (int i = 0; i < packet.Remote.Length; i++)
        {
            CompactPlayerSnapshot snapshot = packet.Remote[i];
            PlayerMotor motor = PlayerMotorRegistry.GetByObjectId(snapshot.ObjectId);
            if (motor != null && motor != owner)
                motor.ApplyRemoteSnapshot(snapshot.ToState());
        }
    }
}
