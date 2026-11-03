using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single global fixed tick. Server and client use the same tick domain:
/// commands, simulation ticks and state snapshots are all stamped with this
/// clock. The clock follows NGO's synchronized ServerTime on both sides, so
/// client and server cannot drift apart. A client adds a small prediction lead
/// so its commands arrive before the server reaches that tick.
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class NetworkTickClock : MonoBehaviour
{
    public const int TickRate = 50;

    public static NetworkTickClock Instance { get; private set; }

    [SerializeField, Range(1, 10)] private int predictionLeadTicks = 6;

    private NetworkManager networkManager;
    private uint currentTick;
    private bool advancedThisFixedUpdate;

    public uint CurrentTick => currentTick;
    public int PredictionLeadTicks => predictionLeadTicks;
    public float TickInterval => 1f / TickRate;

    /// <summary>
    /// True when CurrentTick advanced this FixedUpdate. PlayerMotor uses this
    /// to avoid simulating twice when Unity runs several FixedUpdates for one
    /// network tick.
    /// </summary>
    public bool AdvancedThisFixedUpdate => advancedThisFixedUpdate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateEarlyInstance()
    {
        EnsureInstance();
    }

    public static NetworkTickClock EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        GameObject host = new GameObject("NetworkTickClock");
        DontDestroyOnLoad(host);
        return host.AddComponent<NetworkTickClock>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        predictionLeadTicks = Mathf.Clamp(predictionLeadTicks, 1, 10);
    }

    private void Start()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return;

        networkManager.OnServerStarted += OnNetworkStarted;
        networkManager.OnClientStarted += OnNetworkStarted;
        networkManager.OnServerStopped += OnNetworkStopped;
        networkManager.OnClientStopped += OnNetworkStopped;
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= OnNetworkStarted;
            networkManager.OnClientStarted -= OnNetworkStarted;
            networkManager.OnServerStopped -= OnNetworkStopped;
            networkManager.OnClientStopped -= OnNetworkStopped;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnNetworkStarted()
    {
        currentTick = 0;
        advancedThisFixedUpdate = false;
    }

    private void OnNetworkStopped(bool wasHost)
    {
        currentTick = 0;
        advancedThisFixedUpdate = false;
    }

    private void FixedUpdate()
    {
        advancedThisFixedUpdate = false;

        NetworkManager manager = networkManager;
        if (manager == null)
        {
            manager = NetworkManager.Singleton;
            networkManager = manager;
        }

        if (manager == null || !manager.IsListening)
        {
            // Local single-player mode: run one tick per FixedUpdate.
            currentTick++;
            advancedThisFixedUpdate = true;
            return;
        }

        int serverTick = manager.ServerTime.Tick;
        uint targetTick = (uint)Mathf.Max(0, serverTick);
        if (!manager.IsServer)
            targetTick += (uint)predictionLeadTicks;

        // Follow the synchronized network tick. If Unity runs more than one
        // FixedUpdate for the same network tick, do not simulate again.
        if (targetTick <= currentTick)
            return;

        currentTick = targetTick;
        advancedThisFixedUpdate = true;
    }
}
