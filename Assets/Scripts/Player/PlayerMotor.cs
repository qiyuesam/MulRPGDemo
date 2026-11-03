using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hybrid root-motion motor.
///
/// The owner evaluates the same fixed-tick movement model as the server and
/// keeps a short command/snapshot history. The server owns the logical
/// CharacterController position, validates the client's reported position and
/// sends an authoritative state back for reconciliation. Animator root motion
/// is captured by RootMotionRelay and treated as a bounded movement hint,
/// never as an untrusted teleport.
/// </summary>
// Render interpolation writes the transform in LateUpdate; keeping this
// behaviour ahead of the camera brain guarantees the view samples the
// interpolated pose instead of last frame's position.
[DefaultExecutionOrder(-100)]
public class PlayerMotor : NetworkBehaviour
{
    [Header("Hybrid motion")]
    [SerializeField] private bool enableHybridMotion = true;
    [SerializeField, Range(8, 512)] private int predictionBufferSize = 128;
    [SerializeField, Min(0.001f)] private float positionErrorThreshold = 0.05f;
    [SerializeField, Min(0.1f)] private float hardCorrectionThreshold = 1.5f;
    [SerializeField, Min(0.01f)] private float remoteSmoothing = 18f;    // 已由 Render interpolation 取代，保留仅为 prefab 序列化兼容。
    [SerializeField, Min(1)] private int maxCommandLead = 120;
    [SerializeField, Min(0.05f)] private float maxScriptedMotionPerTick = 0.25f;

    [Header("Network channels")]
    [Tooltip("How many fixed ticks between batched input sends. 2 = one packet every 40ms at 50Hz.")]
    [SerializeField, Range(1, 4)] private int inputSendIntervalTicks = 2;
    [Tooltip("How many recent commands are repeated in each input packet.")]
    [SerializeField, Range(1, 8)] private int redundantCommandCount = 4;
    [Tooltip("Absolute cap of the server-side input queue. Older commands are dropped first.")]
    [SerializeField, Range(4, 32)] private int serverQueueMaxDepth = 16;
    [Tooltip("How many authoritative states the server keeps for replay/lag compensation.")]
    [SerializeField, Range(16, 512)] private int serverStateHistorySize = 128;

    [Header("Movement")]
    [SerializeField] private float defaultMoveSpeed = 6f;
    // Kept for prefab compatibility with the previous motor inspector.
    [SerializeField] private float rotationSmoothTime = 0.1f;
    [SerializeField] private float turnSpeed = 720f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedGravity = -2f;
    [SerializeField] private float jumpHeight = 1.5f;

    [Header("Render interpolation")]
    [SerializeField] private bool enableRenderInterpolation = true;
    // 远端快照需要额外缓冲来吸收到达抖动；2 tick = 40ms。
    [SerializeField, Range(0f, 6f)] private float remoteInterpolationTicks = 2f;
    // 本地模拟的 tick 序列是连续的，只需 1 tick 就能消除固定步长阶梯。
    [SerializeField, Range(0f, 2f)] private float localInterpolationTicks = 1f;
    // 根级位置插值接管视觉后，对账不再对模型子物体做二次位姿补偿。
    [SerializeField] private bool skipVisualPosePreservation = true;

    [Header("Root motion validation")]
    [SerializeField, Range(0f, 1f)] private float rootMotionWeight = 1f;
    [SerializeField, Min(1f)] private float rootMotionMaxMultiplier = 1.5f;
    [SerializeField] private bool acceptValidatedRootMotion = true;

    // Retained for the existing animation/UI scripts.
    public Vector3 InputDirection { get; set; }
    public bool JumpRequest { get; set; }
    // Keep the original public setters for compatibility with existing HUD,
    // animation and gameplay scripts.  The motor itself remains the only
    // writer during normal simulation.
    public bool IsGrounded { get; set; }
    public bool IsMoving { get; set; }
    public float currentSpeed { get; set; }
    public Vector3 Velocity { get; set; }

    public bool UsesHybridMotion => enableHybridMotion;
    public bool HasUsableRootMotion => rootMotionRelay != null && rootMotionRelay.HasRootMotion && rootMotionRelay.CaptureEnabled;
    public uint LocalSimulationTick => localTick;
    public uint LastAcknowledgedTick => lastAckTick;
    public Vector3 LogicalPosition => localState.Position;
    public Vector3 RenderPosition => transform.position;
    public int PendingServerCommandCount => serverInputQueue != null ? serverInputQueue.Count : 0;
    public float PendingScriptedMotionDistance => scriptedMotionDistanceRemaining;
    public float LastServerPositionError { get; private set; }

    private CharacterController characterController;
    private PlayerStats playerStats;
    private CombatSystem combat;
    private RootMotionRelay rootMotionRelay;

    // Fixed-tick gameplay motion profile.  Combat (and future dodge/
    // knockback systems) enqueue a profile instead of moving the
    // CharacterController from an Update coroutine.  The owner records each
    // consumed slice in its prediction command; the server consumes its own
    // authoritative profile and ignores the client-provided slice.
    private bool scriptedMotionActive;
    private Vector3 scriptedMotionDirection;
    private float scriptedMotionDistanceRemaining;
    private float scriptedMotionTimeRemaining;
    private float scriptedMotionSpeed;

    // Authoritative state now travels through the batched StateChannel RPC
    // instead of an every-tick NetworkVariable. The server keeps the latest
    // state locally and clients receive it on a fixed cadence.
    public HybridMotionState AuthoritativeState => serverState;

    private PlayerCommandBatch outgoingBatch;
    private HybridMotionState[] serverStateHistory;
    private uint lastAckSimulationTick;
    private int reconcileCount;

    private HybridMotionState localState;
    private HybridMotionState serverState;
    private HybridMotionState remoteTargetState;
    private bool serverStateInitialized;
    private bool remoteTargetInitialized;

    private uint localTick;
    private uint nextInputSendTick;
    private uint lastAckTick;
    private uint lastReceivedInputTick;
    private uint lastProcessedInputTick;
    private HybridInputCommand lastServerCommand;
    private bool hasServerInput;
    private byte pendingActionFlags;
    private int predictedMovementLockTicks;
    private float nextValidationLogTime;

    private SortedDictionary<uint, HybridInputCommand> serverInputQueue;
    private PredictionFrame[] predictionBuffer;

    // ── Render interpolation ──────────────────────────────────────────
    // The logical capsule is advanced in fixed ticks (50 Hz). Rendering at a
    // different frame rate would otherwise show that step directly, so the
    // authoritative position stream is stored as tick-stamped snapshots and
    // the visible transform is sampled between them every frame.
    private const int RenderSnapshotCapacity = 24;
    private RenderSnapshot[] renderSnapshots;
    private int renderSnapshotHead;
    private int renderSnapshotCount;
    private uint renderClockTick;
    private float renderClockTime;
    private bool renderClockInitialized;
    private uint lastRenderSnapshotTick;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerStats = GetComponent<PlayerStats>();
        combat = GetComponent<CombatSystem>();

        predictionBufferSize = Mathf.Clamp(predictionBufferSize, 8, 512);
        predictionBuffer = new PredictionFrame[predictionBufferSize];
        renderSnapshots = new RenderSnapshot[RenderSnapshotCapacity];
        serverInputQueue = new SortedDictionary<uint, HybridInputCommand>();

        redundantCommandCount = Mathf.Clamp(redundantCommandCount, 1, 8);
        serverStateHistorySize = Mathf.Clamp(serverStateHistorySize, 16, 512);
        serverStateHistory = new HybridMotionState[serverStateHistorySize];
        outgoingBatch = new PlayerCommandBatch
        {
            Count = 0,
            Commands = Array.Empty<HybridInputCommand>(),
        };

        localState = CreateStateFromTransform();
        serverState = localState;
        EnsureRootMotionRelay();
    }

    private void Start()
    {
        // Local single-player objects never receive OnNetworkSpawn.
        if (!IsSpawned)
        {
            localState = CreateStateFromTransform();
            ConfigureRootMotion(true);
        }
    }

    private void OnValidate()
    {
        predictionBufferSize = Mathf.Clamp(predictionBufferSize, 8, 512);
        positionErrorThreshold = Mathf.Max(0.001f, positionErrorThreshold);
        hardCorrectionThreshold = Mathf.Max(positionErrorThreshold, hardCorrectionThreshold);
        remoteSmoothing = Mathf.Max(0.01f, remoteSmoothing);
        maxCommandLead = Mathf.Max(1, maxCommandLead);
        maxScriptedMotionPerTick = Mathf.Max(0.05f, maxScriptedMotionPerTick);
        defaultMoveSpeed = Mathf.Max(0f, defaultMoveSpeed);
        rotationSmoothTime = Mathf.Max(0.01f, rotationSmoothTime);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        rootMotionMaxMultiplier = Mathf.Max(1f, rootMotionMaxMultiplier);
        remoteInterpolationTicks = Mathf.Clamp(remoteInterpolationTicks, 0f, 6f);
        localInterpolationTicks = Mathf.Clamp(localInterpolationTicks, 0f, 2f);
        inputSendIntervalTicks = Mathf.Clamp(inputSendIntervalTicks, 1, 4);
        redundantCommandCount = Mathf.Clamp(redundantCommandCount, 1, 8);
        serverQueueMaxDepth = Mathf.Clamp(serverQueueMaxDepth, 4, 32);
        serverStateHistorySize = Mathf.Clamp(serverStateHistorySize, 16, 512);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        localState = CreateStateFromTransform();
        localTick = NetworkTickClock.Instance != null ? NetworkTickClock.Instance.CurrentTick : 0u;
        nextInputSendTick = 0u;
        lastAckTick = 0;
        lastReceivedInputTick = 0;
        lastProcessedInputTick = 0;
        hasServerInput = false;
        lastServerCommand = default;
        serverStateInitialized = false;
        remoteTargetInitialized = false;
        remoteTargetState = default;
        LastServerPositionError = 0f;
        lastAckSimulationTick = 0u;
        reconcileCount = 0;
        if (predictionBuffer != null)
            Array.Clear(predictionBuffer, 0, predictionBuffer.Length);
        if (serverStateHistory != null)
            Array.Clear(serverStateHistory, 0, serverStateHistory.Length);
        serverInputQueue.Clear();
        ClearScriptedMotion();
        pendingActionFlags = HybridInputAction.None;
        predictedMovementLockTicks = 0;
        InputDirection = Vector3.zero;
        JumpRequest = false;

        if (IsServer)
        {
            serverState = CreateStateFromTransform();
            serverStateInitialized = true;
        }
        else
        {
            // State arrives through NetworkPredictionChannel. Until the first
            // packet lands, keep the prefab pose instead of trusting a stale
            // replicated value.
            remoteTargetState = default;
            remoteTargetInitialized = false;
        }

        // Only an owning client evaluates Animator root motion. A host is both
        // owner and server, so it still captures root motion for local feel.
        ConfigureRootMotion(IsOwner && enableHybridMotion);

        ResetRenderInterpolation();
        PlayerMotorRegistry.Register(this);
    }

    public override void OnNetworkDespawn()
    {
        bool wasSceneObject = NetworkObject != null && NetworkObject.IsSceneObject == true;
        PlayerMotorRegistry.Unregister(this);
        ConfigureRootMotion(false);
        serverStateInitialized = false;
        remoteTargetInitialized = false;
        serverInputQueue?.Clear();
        lastServerCommand = default;
        hasServerInput = false;
        lastReceivedInputTick = 0;
        lastProcessedInputTick = 0;
        lastAckTick = 0;
        lastAckSimulationTick = 0u;
        reconcileCount = 0;
        localTick = 0;
        nextInputSendTick = 0u;
        pendingActionFlags = HybridInputAction.None;
        predictedMovementLockTicks = 0;
        InputDirection = Vector3.zero;
        JumpRequest = false;
        ClearScriptedMotion();
        if (predictionBuffer != null)
            Array.Clear(predictionBuffer, 0, predictionBuffer.Length);
        localState = CreateStateFromTransform();
        serverState = localState;
        remoteTargetState = default;
        UpdatePublicState(localState.Velocity, localState.Grounded);
        ResetRenderInterpolation();
        base.OnNetworkDespawn();

        // Scene Player is reused when a network session returns to local mode.
        // Marking it clean here prevents the next local simulation from
        // replaying the previous session's tick/state history.
        if (wasSceneObject)
            ResetForLocalMode();
    }

    /// <summary>
    /// Resets prediction, command and combat-motion state after a network
    /// session ends and the reusable scene Player returns to local mode.
    /// Call this after the caller has placed the transform at its local spawn.
    /// </summary>
    public void ResetForLocalMode()
    {
        if (predictionBuffer != null)
            Array.Clear(predictionBuffer, 0, predictionBuffer.Length);
        if (serverStateHistory != null)
            Array.Clear(serverStateHistory, 0, serverStateHistory.Length);
        serverInputQueue?.Clear();
        localTick = 0;
        nextInputSendTick = 0u;
        lastAckTick = 0;
        lastAckSimulationTick = 0u;
        reconcileCount = 0;
        lastReceivedInputTick = 0;
        lastProcessedInputTick = 0;
        lastServerCommand = default;
        hasServerInput = false;
        serverStateInitialized = false;
        remoteTargetInitialized = false;
        remoteTargetState = default;
        pendingActionFlags = HybridInputAction.None;
        predictedMovementLockTicks = 0;
        InputDirection = Vector3.zero;
        JumpRequest = false;
        LastServerPositionError = 0f;
        ClearScriptedMotion();
        localState = CreateStateFromTransform();
        serverState = localState;
        UpdatePublicState(localState.Velocity, localState.Grounded);
        ResetRenderInterpolation();
        ConfigureRootMotion(enableHybridMotion);
    }

    private void FixedUpdate()
    {
        // Network simulation is driven by the synchronized ServerTime tick.
        // Skip extra FixedUpdates that do not advance the global tick.
        if (NetworkTickClock.Instance != null && !NetworkTickClock.Instance.AdvancedThisFixedUpdate)
            return;

        // LateUpdate renders an interpolated position. Every tick that actually
        // simulates must restart from the logical state, otherwise
        // CharacterController.Move would sweep from the visual pose and drift.
        if (!IsSpawned || IsServer || IsOwner)
            RestoreLogicalPositionForSimulation();

        if (!enableHybridMotion)
        {
            // Keep the same network command/state path when root motion is
            // disabled. This makes the inspector fallback useful for a
            // conventional scripted controller without re-enabling the old
            // NetworkTransform position authority.
            if (!IsSpawned)
                SimulateLocalTick();
            else if (IsServer)
            {
                if (IsOwner)
                    SimulateOwnerTick(true);
                else
                    SimulateServerTick();
            }
            else if (IsOwner)
                SimulateOwnerTick(false);
            return;
        }

        if (!IsSpawned)
        {
            // A scene Player can transition Host -> Local without being
            // disabled or recreated. Re-arm the owner relay after
            // OnNetworkDespawn so local mode retains root-motion capture.
            ConfigureRootMotion(enableHybridMotion);
            SimulateLocalTick();
            return;
        }

        if (IsServer)
        {
            if (IsOwner)
                SimulateOwnerTick(true);
            else
                SimulateServerTick();
            return;
        }

        if (IsOwner)
            SimulateOwnerTick(false);
    }

    private void LateUpdate()
    {
        // Remote clients do not simulate locally. Their public speed/grounded
        // view still follows the newest authoritative snapshot so animation and
        // UI keep reading a coherent value.
        if (IsSpawned && !IsOwner && !IsServer && remoteTargetInitialized)
        {
            localState = remoteTargetState;
            UpdatePublicState(remoteTargetState.Velocity, remoteTargetState.Grounded);
        }

        if (!enableRenderInterpolation)
        {
            // Legacy fallback. Kept so disabling the interpolator restores the
            // previous behaviour instead of freezing remote players.
            if (IsSpawned && !IsOwner && !IsServer && remoteTargetInitialized)
            {
                float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, remoteSmoothing) * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, remoteTargetState.Position, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, remoteTargetState.Rotation, blend);
            }
            return;
        }

        if (renderSnapshotCount == 0)
            return;

        // Rotation is intentionally not interpolated here. The fixed step seeds
        // its yaw from the logical transform and CombatSystem writes facing
        // changes there between ticks; smoothing rotation would feed a stale
        // yaw back into the next simulation step.
        float tickRate = 1f / Mathf.Max(0.0001f, Time.fixedDeltaTime);
        bool locallySimulated = !IsSpawned || IsOwner || IsServer;
        float delayTicks = locallySimulated
            ? localInterpolationTicks
            : remoteInterpolationTicks;

        // The clock is established with frame time, never with the fixed step
        // time. It then advances only by elapsed render time, so a hitch that
        // makes FixedUpdate run ahead can no longer push the view backwards.
        float now = Time.time;
        if (!renderClockInitialized)
        {
            renderClockTick = lastRenderSnapshotTick;
            renderClockTime = now;
            renderClockInitialized = true;
        }

        float elapsed = Mathf.Max(0f, now - renderClockTime);
        float clockTickNow = renderClockTick + elapsed * tickRate;
        float drift = clockTickNow - lastRenderSnapshotTick;
        if (drift < -4f)
        {
            // The view has fallen far enough behind the newest snapshot that it
            // would stay frozen on stale samples. Re-seat the clock forward.
            // Leading drift is intentionally not re-seated: sampling already
            // clamps to the newest snapshot, so staying ahead is harmless.
            renderClockTick = lastRenderSnapshotTick;
            renderClockTime = now;
            clockTickNow = lastRenderSnapshotTick;
        }

        float renderTick = clockTickNow - delayTicks;
        bool remoteVisual = IsSpawned && !IsOwner && !IsServer;

        if (TrySampleRenderState(renderTick, out Vector3 sampledPosition, out float sampledYaw))
        {
            transform.position = sampledPosition;
            if (remoteVisual)
                transform.rotation = Quaternion.Euler(0f, sampledYaw, 0f);
        }
    }

    /// <summary>Queues an action bit for the next fixed input command.</summary>
    public void QueueAction(byte actionFlags)
    {
        actionFlags &= HybridInputAction.KnownMask;
        if (actionFlags == HybridInputAction.None)
            return;
        pendingActionFlags |= actionFlags;
        if ((actionFlags & HybridInputAction.Attack) != 0)
            predictedMovementLockTicks = Mathf.Max(predictedMovementLockTicks, 1);
    }

    public void NotifyAttackStarted()
    {
        QueueAction(HybridInputAction.Attack);
    }

    /// <summary>
    /// Starts a deterministic fixed-tick motion profile.  This is intentionally
    /// independent from the input lock: an attack can suppress normal input
    /// while still allowing its authored lunge to move the capsule.
    /// </summary>
    public void QueueAttackLunge(Vector3 direction, float distance, float duration)
    {
        direction.y = 0f;
        if (!HybridMotionSimulator.IsFinite(direction)
            || direction.sqrMagnitude <= 0.000001f)
            return;

        distance = HybridMotionSimulator.IsFinite(distance)
            ? Mathf.Clamp(distance, 0f, 8f)
            : 0f;
        duration = HybridMotionSimulator.IsFinite(duration)
            ? Mathf.Clamp(duration, Mathf.Max(0.0001f, Time.fixedDeltaTime), 2f)
            : 0f;
        if (distance <= 0.0001f || duration <= 0.0001f)
            return;

        direction.Normalize();

        // Profiles should not stack without a bound (for example if a combo
        // callback is delivered twice).  Appending preserves authored motion
        // while keeping the total displacement finite.
        if (scriptedMotionActive && scriptedMotionDistanceRemaining > 0f)
        {
            float previousDistance = scriptedMotionDistanceRemaining;
            Vector3 combinedDirection = scriptedMotionDirection * previousDistance
                + direction * distance;
            scriptedMotionDistanceRemaining = Mathf.Min(
                8f,
                previousDistance + distance);
            scriptedMotionTimeRemaining = Mathf.Clamp(
                scriptedMotionTimeRemaining + duration,
                Mathf.Max(0.0001f, Time.fixedDeltaTime),
                2f);
            scriptedMotionDirection = combinedDirection.sqrMagnitude > 0.000001f
                ? combinedDirection.normalized
                : direction;
        }
        else
        {
            scriptedMotionActive = true;
            scriptedMotionDirection = direction;
            scriptedMotionDistanceRemaining = distance;
            scriptedMotionTimeRemaining = duration;
        }

        scriptedMotionSpeed = scriptedMotionDistanceRemaining
            / Mathf.Max(0.0001f, scriptedMotionTimeRemaining);
    }

    public void ClearScriptedMotion()
    {
        scriptedMotionActive = false;
        scriptedMotionDirection = Vector3.zero;
        scriptedMotionDistanceRemaining = 0f;
        scriptedMotionTimeRemaining = 0f;
        scriptedMotionSpeed = 0f;
    }

    public void SetInput(Vector3 worldDirection, bool jump)
    {
        InputDirection = HybridMotionSimulator.SanitizeMoveDirection(worldDirection);
        if (jump)
            JumpRequest = true;
    }

    private void SimulateLocalTick()
    {
        localTick = NetworkTickClock.Instance != null
            ? NetworkTickClock.Instance.CurrentTick
            : localTick + 1u;
        HybridInputCommand command = BuildInputCommand(localTick);
        localState = SimulateState(localState, command, localTick);
        command.PredictedPosition = localState.Position;
        command.PredictedYaw = localState.Yaw;
        StorePrediction(command, localState);
        UpdatePublicState(localState.Velocity, localState.Grounded);
        PushRenderSnapshot(localState.SimulationTick, localState.Position, localState.Yaw);
    }

    private void SimulateOwnerTick(bool hostAuthoritative)
    {
        localTick = NetworkTickClock.Instance != null
            ? NetworkTickClock.Instance.CurrentTick
            : localTick + 1u;
        HybridInputCommand command = BuildInputCommand(localTick);
        localState = SimulateState(localState, command, localTick);
        command.PredictedPosition = localState.Position;
        command.PredictedYaw = localState.Yaw;
        StorePrediction(command, localState);
        UpdatePublicState(localState.Velocity, localState.Grounded);
        PushRenderSnapshot(localState.SimulationTick, localState.Position, localState.Yaw);

        if (hostAuthoritative)
        {
            // Host owns the same object that it simulates. Do not run the
            // server path a second time and double-apply Move.
            serverState = localState;
            serverState.InputTick = command.Tick;
            serverStateInitialized = true;
            StoreServerState(serverState);
        }
        else if (IsSpawned && IsClient && NetworkManager != null && NetworkManager.IsListening)
        {
            QueueCommandForSend();
        }
    }

    private void SimulateServerTick()
    {
        if (!serverStateInitialized)
        {
            serverState = CreateStateFromTransform();
            serverStateInitialized = true;
        }

        HybridInputCommand command = lastServerCommand;
        bool isNewInput = TryDequeueServerCommand(out HybridInputCommand dequeued);
        if (isNewInput)
        {
            command = SanitizeServerCommand(dequeued);
            lastProcessedInputTick = command.Tick;
            lastServerCommand = command;
            hasServerInput = true;
        }

        if (!hasServerInput)
        {
            command = new HybridInputCommand
            {
                Tick = 0,
                MoveDirection = Vector3.zero,
                RootMotionDelta = Vector3.zero,
                ScriptedMotionDelta = Vector3.zero,
                PredictedPosition = transform.position,
                PredictedYaw = transform.eulerAngles.y,
                MovementLocked = true,
            };
        }
        else
        {
            // Hold the last accepted input so gravity and movement remain
            // continuous during packet delay. It is not acknowledged as a
            // fresh client tick until a real command arrives.
            command = SanitizeServerCommand(command);
            if (!isNewInput)
            {
                // Root-motion deltas and one-shot actions belong to the input
                // sample that produced them. Replaying them while a packet is
                // missing would turn one animation frame into a teleport.
                command.RootMotionDelta = Vector3.zero;
                command.RootMotionYaw = 0f;
                command.ScriptedMotionDelta = Vector3.zero;
                command.Jump = false;
                command.ActionFlags = HybridInputAction.None;
                command.MovementLocked = combat != null && combat.IsAttackMovementLocked;
            }
        }

        bool clientReportedFinite = HybridMotionSimulator.IsFinite(command.PredictedPosition)
            && HybridMotionSimulator.IsFinite(command.PredictedYaw);

        // The server derives lock state from authoritative combat state. The
        // action bit is only a compact request marker for the attack tick;
        // arbitrary client MovementLocked values are ignored.
        command.MovementLocked = (combat != null && combat.IsAttackMovementLocked)
            || (command.ActionFlags & HybridInputAction.Attack) != 0;
        if (playerStats != null && playerStats.IsDead)
        {
            command.MoveDirection = Vector3.zero;
            command.RootMotionDelta = Vector3.zero;
            command.RootMotionYaw = 0f;
            command.Jump = false;
            command.MovementLocked = true;
        }

        // Root motion is accepted only after the bounded/directional validation
        // in SanitizeServerCommand. This keeps the server lightweight (it does
        // not evaluate the full Animator) while still allowing authored
        // locomotion clips to contribute to the logical capsule. Scripted
        // combat motion remains server-authored and is never taken from the
        // wire.
        HybridInputCommand serverSimulationCommand = command;
        if (!acceptValidatedRootMotion || command.MovementLocked)
        {
            serverSimulationCommand.RootMotionDelta = Vector3.zero;
            serverSimulationCommand.RootMotionYaw = 0f;
        }
        else if (command.MoveDirection.sqrMagnitude <= 0.0001f
            && command.ActionFlags == HybridInputAction.None)
        {
            // An idle client must not manufacture locomotion by sending a
            // bounded root-motion delta. Start/stop clips can still contribute
            // while an input sample is active; idle displacement is reserved
            // for the authoritative combat/profile path below.
            serverSimulationCommand.RootMotionDelta = Vector3.zero;
            serverSimulationCommand.RootMotionYaw = 0f;
        }
        // Never apply a client-authored scripted displacement.  The server
        // profile is enqueued by the authoritative CombatSystem callback.
        serverSimulationCommand.ScriptedMotionDelta = ConsumeScriptedMotionDelta();
        uint serverTick = NetworkTickClock.Instance != null
            ? NetworkTickClock.Instance.CurrentTick
            : serverState.SimulationTick + 1u;
        serverState = SimulateState(serverState, serverSimulationCommand, serverTick);
        if (isNewInput)
        {
            serverState.InputTick = command.Tick;
            if (clientReportedFinite)
            {
                LastServerPositionError = Vector3.Distance(command.PredictedPosition, serverState.Position);
                if (LastServerPositionError > positionErrorThreshold && Time.time >= nextValidationLogTime)
                {
                    Debug.LogWarning($"[HybridMotion] Position validation error on {name}: {LastServerPositionError:F3}m at input tick {command.Tick}");
                    nextValidationLogTime = Time.time + 1f;
                }
            }
        }

        // Keep the public motor view useful to server-side AI, hit queries and
        // diagnostics as well as to the network serializer. Previously only
        // the transform/network variable advanced for non-owner server
        // objects, leaving LogicalPosition/Velocity one spawn behind.
        localState = serverState;
        UpdatePublicState(serverState.Velocity, serverState.Grounded);
        PushRenderSnapshot(serverState.SimulationTick, serverState.Position, serverState.Yaw);

        StoreServerState(serverState);
    }

    /// <summary>
    /// Receives a redundant window of commands from one owning client. The
    /// newest command plus a few older ones cover packet loss without relying
    /// on a single per-tick RPC.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void SubmitInputBatchServerRpc(PlayerCommandBatch batch)
    {
        if (!IsServer || batch.Commands == null)
            return;

        int count = Mathf.Clamp(batch.Count, 0, batch.Commands.Length);
        for (int i = 0; i < count; i++)
        {
            HybridInputCommand command = batch.Commands[i];
            if (!IsValidCommand(command))
                continue;
            if (command.Tick == 0u
                || command.Tick <= lastProcessedInputTick
                || serverInputQueue.ContainsKey(command.Tick))
                continue;

            // Commands now live in the same global tick domain as the server
            // simulation. Commands for past ticks are stale; commands too far
            // in the future are malformed or a broken clock.
            uint serverTick = NetworkTickClock.Instance != null
                ? NetworkTickClock.Instance.CurrentTick
                : 0u;
            if (command.Tick < serverTick)
                continue;
            if (command.Tick > serverTick + (uint)Mathf.Max(8, maxCommandLead))
            {
                Debug.LogWarning($"[HybridMotion] Dropping over-lead input tick {command.Tick} from {OwnerClientId}, server tick {serverTick}");
                continue;
            }

            // Track the highest tick seen for diagnostics only. Do not reject a
            // lower-but-still-pending tick: transport reordering must not create
            // a permanent gap in the server queue.
            if (command.Tick > lastReceivedInputTick)
                lastReceivedInputTick = command.Tick;

            while (serverInputQueue.Count >= serverQueueMaxDepth)
            {
                KeyValuePair<uint, HybridInputCommand> oldest = GetFirstQueuedCommand();
                serverInputQueue.Remove(oldest.Key);
            }

            serverInputQueue[command.Tick] = SanitizeServerCommand(command);
        }
    }

    /// <summary>
    /// Takes the command for the current global server tick. Future commands
    /// stay queued; past commands are stale. If no command exists for the
    /// current tick the motor holds the last accepted one.
    /// </summary>
    private bool TryDequeueServerCommand(out HybridInputCommand command)
    {
        command = default;
        uint serverTick = NetworkTickClock.Instance != null
            ? NetworkTickClock.Instance.CurrentTick
            : 0u;

        while (serverInputQueue.Count > 0)
        {
            KeyValuePair<uint, HybridInputCommand> oldest = GetFirstQueuedCommand();
            if (oldest.Key < serverTick)
            {
                serverInputQueue.Remove(oldest.Key);
                continue;
            }

            if (oldest.Key > serverTick)
                return false;

            serverInputQueue.Remove(oldest.Key);
            command = oldest.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Batches the newest commands on a fixed cadence instead of sending one
    /// RPC every simulation tick.
    /// </summary>
    private void QueueCommandForSend()
    {
        int interval = Mathf.Max(1, inputSendIntervalTicks);
        if (localTick < nextInputSendTick)
            return;
        nextInputSendTick = localTick + (uint)interval;

        int count = 0;
        uint firstTick = localTick >= (uint)redundantCommandCount
            ? localTick - (uint)redundantCommandCount + 1u
            : 1u;

        HybridInputCommand[] commands = new HybridInputCommand[redundantCommandCount];
        for (uint tick = firstTick; tick <= localTick && count < commands.Length; tick++)
        {
            if (TryGetPrediction(tick, out PredictionFrame frame))
                commands[count++] = frame.Command;
        }

        if (count == 0)
            return;

        outgoingBatch.Count = count;
        outgoingBatch.Commands = commands;
        SubmitInputBatchServerRpc(outgoingBatch);
    }

    private void StoreServerState(HybridMotionState state)
    {
        if (serverStateHistory == null || serverStateHistory.Length == 0)
            return;
        if (state.SimulationTick == 0u)
            return;

        int index = (int)(state.SimulationTick % (uint)serverStateHistory.Length);
        serverStateHistory[index] = state;
    }

    /// <summary>
    /// Returns the authoritative snapshot recorded for a specific simulation
    /// tick. Currently used by diagnostics and reserved for server-side hit
    /// rewind (lag compensation).
    /// </summary>
    public bool TryGetServerStateAtTick(uint tick, out HybridMotionState state)
    {
        state = default;
        if (serverStateHistory == null || serverStateHistory.Length == 0)
            return false;

        int index = (int)(tick % (uint)serverStateHistory.Length);
        state = serverStateHistory[index];
        return state.SimulationTick == tick;
    }

    public ulong PredictionObjectId =>
        NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL;

    /// <summary>
    /// Server-side accessor used by the central state channel.
    /// </summary>
    public bool TryCaptureAuthoritativeSnapshot(out HybridMotionState state)
    {
        state = serverState;
        return IsServer
            && IsSpawned
            && serverStateInitialized
            && serverState.SimulationTick != 0u;
    }

    /// <summary>
    /// Client-side entry point for its own authoritative snapshot (ack).
    /// </summary>
    public void ApplyOwnerSnapshot(HybridMotionState state)
    {
        if (IsOwner && !IsServer)
            HandleAuthoritativeState(state);
    }

    /// <summary>
    /// Client-side entry point for another player's snapshot (interpolation).
    /// </summary>
    public void ApplyRemoteSnapshot(HybridMotionState state)
    {
        if (!IsOwner)
            ApplyRemoteAuthoritativeState(state);
    }

    /// <summary>
    /// Server-side sender for the central state channel. Any spawned player
    /// object can carry the ClientRpc; the receiving instance routes the packet
    /// to all local player motors.
    /// </summary>
    public void SendPredictionStatePacket(PredictionStatePacket packet, ulong clientId)
    {
        if (!IsServer || !IsSpawned)
            return;

        ReceivePredictionStateClientRpc(
            packet,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId },
                },
            });
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceivePredictionStateClientRpc(PredictionStatePacket packet, ClientRpcParams clientRpcParams = default)
    {
        if (!IsSpawned || IsServer)
            return;

        NetworkPredictionChannel.ApplyStatePacket(packet);
    }

    private void HandleAuthoritativeState(HybridMotionState authoritativeState)
    {
        if (!IsOwner || IsServer || authoritativeState.SimulationTick <= lastAckSimulationTick)
            return;

        lastAckSimulationTick = authoritativeState.SimulationTick;
        if (authoritativeState.InputTick > lastAckTick)
            lastAckTick = authoritativeState.InputTick;

        // Both sides now share the same global tick domain, so the server's
        // SimulationTick directly identifies the local prediction frame.
        PredictionFrame frame = default;
        bool hasAnchor = TryGetPredictionBySimulationTick(authoritativeState.SimulationTick, out frame);
        if (!hasAnchor && authoritativeState.InputTick != 0u)
            hasAnchor = TryGetPrediction(authoritativeState.InputTick, out frame);

        if (!hasAnchor)
        {
            // InputTick == 0 means the server has not applied any command yet.
            // Snapping to that state would teleport a freshly spawned owner
            // before the first input round-trip completes.
            if (authoritativeState.InputTick != 0u)
                ApplyAuthoritativeState(authoritativeState, true);
            return;
        }

        float error = Vector3.Distance(frame.State.Position, authoritativeState.Position);
        float yawError = Mathf.Abs(Mathf.DeltaAngle(frame.State.Yaw, authoritativeState.Yaw));
        LastServerPositionError = error;
        if (error <= positionErrorThreshold && yawError <= 2f)
            return;

        reconcileCount++;
        Reconcile(authoritativeState, frame.Command.Tick);
    }

    /// <summary>
    /// Feeds the newest authoritative pose into the remote render stream.
    /// Non-owners never simulate locally; they only interpolate this packet.
    /// </summary>
    private void ApplyRemoteAuthoritativeState(HybridMotionState authoritativeState)
    {
        if (remoteTargetInitialized
            && authoritativeState.SimulationTick <= remoteTargetState.SimulationTick)
            return;

        remoteTargetState = authoritativeState;
        if (!remoteTargetInitialized)
        {
            remoteTargetInitialized = true;
            transform.SetPositionAndRotation(authoritativeState.Position, authoritativeState.Rotation);
            localState = authoritativeState;
            UpdatePublicState(authoritativeState.Velocity, authoritativeState.Grounded);
        }

        PushRenderSnapshot(authoritativeState.SimulationTick, authoritativeState.Position, authoritativeState.Yaw);
    }

    private void Reconcile(HybridMotionState authoritativeState, uint anchorCommandTick)
    {
        Vector3 visualPosition = transform.position;
        Quaternion visualRotation = transform.rotation;
        bool hardSnap = Vector3.Distance(visualPosition, authoritativeState.Position) > hardCorrectionThreshold;

        HybridMotionState replayState = authoritativeState;
        SetLogicalTransform(authoritativeState.Position, authoritativeState.Rotation);

        // The anchor can be older than authoritativeState.InputTick when the
        // server simulated held input during packet delay.  Replaying from
        // InputTick would skip/duplicate local frames, so use the command
        // sequence represented by the selected prediction anchor.
        uint replayTick = anchorCommandTick + 1u;
        while (replayTick <= localTick)
        {
            if (!TryGetPrediction(replayTick, out PredictionFrame frame))
                break;

            replayState = SimulateState(replayState, frame.Command, replayTick);
            frame.Command.PredictedPosition = replayState.Position;
            frame.Command.PredictedYaw = replayState.Yaw;
            frame.State = replayState;
            StorePrediction(frame.Command, frame.State);
            replayTick++;
        }

        localState = replayState;
        UpdatePublicState(localState.Velocity, localState.Grounded);
        rootMotionRelay?.ClearAccumulatedMotion();

        // With root-level interpolation the visible transform is sampled from
        // the snapshot stream, so the renderer already hides the logical
        // correction. Compensating the model as well would double the offset.
        if (!hardSnap && !SkipsVisualPosePreservation)
            rootMotionRelay?.PreserveWorldPose(visualPosition, visualRotation);
    }

    private void ApplyAuthoritativeState(HybridMotionState authoritativeState, bool preserveVisualPose)
    {
        Vector3 visualPosition = transform.position;
        Quaternion visualRotation = transform.rotation;
        SetLogicalTransform(authoritativeState.Position, authoritativeState.Rotation);
        localState = authoritativeState;
        UpdatePublicState(localState.Velocity, localState.Grounded);
        rootMotionRelay?.ClearAccumulatedMotion();
        if (preserveVisualPose && !SkipsVisualPosePreservation)
            rootMotionRelay?.PreserveWorldPose(visualPosition, visualRotation);
    }

    private HybridInputCommand BuildInputCommand(uint tick)
    {
        bool movementLocked = (playerStats != null && playerStats.IsDead)
            || (combat != null && combat.IsAttackMovementLocked)
            || predictedMovementLockTicks > 0;
        if (predictedMovementLockTicks > 0)
            predictedMovementLockTicks--;

        bool jump = playerStats == null || !playerStats.IsDead ? JumpRequest : false;
        JumpRequest = false;
        byte actions = pendingActionFlags;
        pendingActionFlags = HybridInputAction.None;

        Vector3 moveDirection = ResolveWorldMoveDirection(InputDirection);
        RootMotionSample rootMotion = rootMotionRelay != null
            ? rootMotionRelay.Consume()
            : default;

        // Root motion is a locomotion hint, not an independent input source.
        // Only consume it while the client is actively moving and the current
        // action permits locomotion. This mirrors the server's idle/locked
        // validation and prevents an imported transition/idle clip (or a
        // malformed Animator delta) from slowly moving the capsule without
        // player input.
        bool allowRootMotion = !movementLocked
            && moveDirection.sqrMagnitude > 0.0001f
            && (rootMotionRelay == null || rootMotionRelay.HasRootMotion);
        if (!allowRootMotion)
        {
            rootMotion = default;
        }

        return new HybridInputCommand
        {
            Tick = tick,
            MoveDirection = moveDirection,
            Jump = jump,
            ActionFlags = actions,
            MovementLocked = movementLocked,
            RootMotionDelta = rootMotion.WorldDelta,
            RootMotionYaw = rootMotion.YawDelta,
            ScriptedMotionDelta = ConsumeScriptedMotionDelta(),
            PredictedPosition = transform.position,
            PredictedYaw = transform.eulerAngles.y,
        };
    }

    private Vector3 ResolveWorldMoveDirection(Vector3 input)
    {
        input = HybridMotionSimulator.SanitizeMoveDirection(input);
        float magnitude = input.magnitude;
        if (magnitude <= 0.0001f)
            return Vector3.zero;

        Vector3 local = input / magnitude;
        Transform camera = Camera.main != null ? Camera.main.transform : null;
        if (camera == null)
            return HybridMotionSimulator.SanitizeMoveDirection(transform.TransformDirection(local)) * magnitude;

        Vector3 forward = camera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;
        forward.Normalize();

        Vector3 right = camera.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;
        right.Normalize();

        return HybridMotionSimulator.SanitizeMoveDirection(
            (forward * local.z + right * local.x) * magnitude);
    }

    private HybridMotionState SimulateState(HybridMotionState state, HybridInputCommand command, uint stateTick)
    {
        float deltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        Vector3 currentPosition = transform.position;
        if (!HybridMotionSimulator.IsFinite(state.Position))
            state.Position = currentPosition;

        bool grounded = state.Grounded;
        if (characterController != null && state.SimulationTick == 0)
            grounded = characterController.isGrounded;

        float moveSpeed = playerStats != null ? playerStats.MoveSpeed : defaultMoveSpeed;
        float effectiveTurnSpeed = Mathf.Max(
            turnSpeed,
            90f / Mathf.Max(0.01f, rotationSmoothTime));
        HybridMotionStep step = HybridMotionSimulator.Evaluate(
            command.MoveDirection,
            command.Jump,
            command.MovementLocked,
            command.RootMotionDelta,
            command.RootMotionYaw,
            // CombatSystem and other authoritative actions may rotate the
            // logical root between fixed steps. Use that transform as the
            // starting yaw so the shared simulator does not undo an attack
            // facing change with a stale snapshot value.
            transform.eulerAngles.y,
            state.Velocity,
            grounded,
            moveSpeed,
            gravity,
            groundedGravity,
            jumpHeight,
            effectiveTurnSpeed,
            command.MovementLocked ? 0f : rootMotionWeight,
            rootMotionMaxMultiplier,
            enableHybridMotion,
            command.ScriptedMotionDelta,
            deltaTime);

        transform.rotation = Quaternion.Euler(0f, step.Yaw, 0f);
        CollisionFlags flags = CollisionFlags.None;
        if (characterController != null && characterController.enabled)
            flags = characterController.Move(step.Motion);
        else
            transform.position += step.Motion;

        bool groundedAfter = (flags & CollisionFlags.Below) != 0
            || (characterController != null && characterController.isGrounded);
        Vector3 velocity = step.Velocity;
        if (groundedAfter && velocity.y < 0f)
            velocity.y = groundedGravity;

        state.SimulationTick = stateTick;
        state.Position = transform.position;
        state.Velocity = velocity;
        state.Yaw = transform.eulerAngles.y;
        state.Grounded = groundedAfter;
        state.ActionFlags = command.ActionFlags;
        return state;
    }

    private HybridMotionState CreateStateFromTransform()
    {
        return new HybridMotionState
        {
            SimulationTick = NetworkTickClock.Instance != null
                ? NetworkTickClock.Instance.CurrentTick
                : 0u,
            InputTick = 0,
            Position = transform.position,
            Velocity = Vector3.zero,
            Yaw = transform.eulerAngles.y,
            Grounded = characterController != null && characterController.isGrounded,
            ActionFlags = HybridInputAction.None,
        };
    }

    private void UpdatePublicState(Vector3 velocity, bool grounded)
    {
        Velocity = velocity;
        IsGrounded = grounded;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        currentSpeed = horizontal.magnitude;
        IsMoving = currentSpeed > 0.01f;
    }

    private void StorePrediction(HybridInputCommand command, HybridMotionState state)
    {
        if (predictionBuffer == null || predictionBuffer.Length == 0)
            return;
        int index = (int)(command.Tick % (uint)predictionBuffer.Length);
        predictionBuffer[index] = new PredictionFrame
        {
            Valid = true,
            Command = command,
            State = state,
        };
    }

    private bool TryGetPrediction(uint tick, out PredictionFrame frame)
    {
        if (predictionBuffer != null && predictionBuffer.Length > 0)
        {
            frame = predictionBuffer[(int)(tick % (uint)predictionBuffer.Length)];
            if (frame.Valid && frame.Command.Tick == tick)
                return true;
        }

        frame = default;
        return false;
    }

    private bool TryGetPredictionBySimulationTick(uint simulationTick, out PredictionFrame frame)
    {
        if (predictionBuffer != null)
        {
            for (int i = 0; i < predictionBuffer.Length; i++)
            {
                PredictionFrame candidate = predictionBuffer[i];
                if (candidate.Valid && candidate.State.SimulationTick == simulationTick)
                {
                    frame = candidate;
                    return true;
                }
            }
        }

        frame = default;
        return false;
    }

    private void SetLogicalTransform(Vector3 position, Quaternion rotation)
    {
        if (characterController != null)
            characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (characterController != null)
            characterController.enabled = true;
    }

    // ── Render interpolation ──────────────────────────────────────────
    private bool SkipsVisualPosePreservation =>
        enableRenderInterpolation && skipVisualPosePreservation;

    /// <summary>
    /// Appends a tick-stamped logical pose to the render stream. Local
    /// simulation pushes one snapshot per fixed step, so the stream is
    /// contiguous; remote clients push whatever the authoritative state
    /// packet delivered.
    /// </summary>
    private void PushRenderSnapshot(uint tick, Vector3 position, float yaw)
    {
        if (renderSnapshots == null || renderSnapshots.Length == 0)
            return;
        if (!HybridMotionSimulator.IsFinite(position))
            return;
        if (!HybridMotionSimulator.IsFinite(yaw))
            yaw = 0f;

        // Snapshots must strictly increase. A delayed or duplicated remote
        // packet must never pull the visible pose backwards.
        if (renderSnapshotCount > 0 && tick <= lastRenderSnapshotTick)
            return;

        renderSnapshots[renderSnapshotHead] = new RenderSnapshot
        {
            Tick = tick,
            Position = position,
            Yaw = yaw,
        };
        renderSnapshotHead = (renderSnapshotHead + 1) % renderSnapshots.Length;
        if (renderSnapshotCount < renderSnapshots.Length)
            renderSnapshotCount++;
        lastRenderSnapshotTick = tick;

        // The render clock is deliberately not re-based here. This runs inside
        // FixedUpdate, and Time.time there reports the fixed step time, which
        // can sit ahead of the current frame's time when the game catches up
        // after a hitch. Using it as a start point produced a negative elapsed
        // span, which froze the visible pose until the next snapshot arrived.
    }

    /// <summary>Samples the visible pose at a fractional tick.</summary>
    private bool TrySampleRenderState(float renderTick, out Vector3 position, out float yaw)
    {
        position = transform.position;
        yaw = transform.eulerAngles.y;
        if (renderSnapshotCount == 0)
            return false;

        RenderSnapshot newest = GetRenderSnapshot(0);
        if (renderSnapshotCount == 1 || renderTick >= newest.Tick)
        {
            position = newest.Position;
            yaw = newest.Yaw;
            return true;
        }

        RenderSnapshot oldest = GetRenderSnapshot(renderSnapshotCount - 1);
        if (renderTick <= oldest.Tick)
        {
            position = oldest.Position;
            yaw = oldest.Yaw;
            return true;
        }

        for (int i = 0; i < renderSnapshotCount - 1; i++)
        {
            RenderSnapshot newer = GetRenderSnapshot(i);
            RenderSnapshot older = GetRenderSnapshot(i + 1);
            if (renderTick <= newer.Tick && renderTick >= older.Tick)
            {
                float span = newer.Tick - older.Tick;
                float t = span <= 0f ? 1f : (renderTick - older.Tick) / span;
                position = Vector3.Lerp(older.Position, newer.Position, t);
                yaw = Mathf.LerpAngle(older.Yaw, newer.Yaw, t);
                return true;
            }
        }

        return false;
    }

    private RenderSnapshot GetRenderSnapshot(int ageFromNewest)
    {
        int index = renderSnapshotHead - 1 - ageFromNewest;
        if (index < 0)
            index += renderSnapshots.Length;
        return renderSnapshots[index];
    }

    private void ResetRenderInterpolation()
    {
        renderSnapshotHead = 0;
        renderSnapshotCount = 0;
        renderClockTick = 0u;
        renderClockTime = 0f;
        renderClockInitialized = false;
        lastRenderSnapshotTick = 0u;
    }

    /// <summary>
    /// Puts the capsule back on the logical position before a fixed step runs.
    /// LateUpdate leaves the transform on the interpolated pose, which is
    /// correct for rendering but must never become the sweep origin.
    /// </summary>
    private void RestoreLogicalPositionForSimulation()
    {
        if (!enableRenderInterpolation || !renderClockInitialized)
            return;
        if (!HybridMotionSimulator.IsFinite(localState.Position))
            return;
        if ((transform.position - localState.Position).sqrMagnitude < 0.000001f)
            return;

        if (characterController != null)
            characterController.enabled = false;
        transform.position = localState.Position;
        if (characterController != null)
            characterController.enabled = true;
    }

    private void EnsureRootMotionRelay()
    {
        Animator animator = GetComponentInChildren<Animator>();
        if (animator == null)
            return;

        rootMotionRelay = animator.GetComponent<RootMotionRelay>();
        if (rootMotionRelay == null)
            rootMotionRelay = animator.gameObject.AddComponent<RootMotionRelay>();
        rootMotionRelay.Configure(this, false);
    }

    private void ConfigureRootMotion(bool capture)
    {
        if (rootMotionRelay == null)
            EnsureRootMotionRelay();
        if (rootMotionRelay == null)
            return;

        bool desiredCapture = capture && enableHybridMotion;
        if (rootMotionRelay.CaptureEnabled != desiredCapture)
            rootMotionRelay.Configure(this, desiredCapture);
    }

    private HybridInputCommand SanitizeServerCommand(HybridInputCommand command)
    {
        command.ActionFlags &= HybridInputAction.KnownMask;
        command.MoveDirection = HybridMotionSimulator.SanitizeMoveDirection(command.MoveDirection);
        command.RootMotionDelta = HybridMotionSimulator.SanitizeRootMotion(
            command.RootMotionDelta,
            command.MoveDirection,
            playerStats != null ? playerStats.MoveSpeed : defaultMoveSpeed,
            Mathf.Max(0.0001f, Time.fixedDeltaTime),
            rootMotionMaxMultiplier);
        command.RootMotionYaw = HybridMotionSimulator.IsFinite(command.RootMotionYaw)
            ? Mathf.Clamp(command.RootMotionYaw, -90f, 90f)
            : 0f;
        command.ScriptedMotionDelta = HybridMotionSimulator.SanitizeScriptedMotion(
            command.ScriptedMotionDelta,
            Mathf.Max(0.05f, 8f * Mathf.Max(0.0001f, Time.fixedDeltaTime)));
        if (!HybridMotionSimulator.IsFinite(command.PredictedPosition))
            command.PredictedPosition = transform.position;
        if (!HybridMotionSimulator.IsFinite(command.PredictedYaw))
            command.PredictedYaw = transform.eulerAngles.y;
        return command;
    }

    private static bool IsValidCommand(HybridInputCommand command)
    {
        return HybridMotionSimulator.IsFinite(command.MoveDirection)
            && HybridMotionSimulator.IsFinite(command.RootMotionDelta)
            && HybridMotionSimulator.IsFinite(command.RootMotionYaw)
            && HybridMotionSimulator.IsFinite(command.ScriptedMotionDelta)
            && HybridMotionSimulator.IsFinite(command.PredictedPosition)
            && HybridMotionSimulator.IsFinite(command.PredictedYaw);
    }

    private KeyValuePair<uint, HybridInputCommand> GetFirstQueuedCommand()
    {
        foreach (KeyValuePair<uint, HybridInputCommand> pair in serverInputQueue)
            return pair;
        return default;
    }

    private Vector3 ConsumeScriptedMotionDelta()
    {
        if (!scriptedMotionActive || scriptedMotionDistanceRemaining <= 0.0001f)
        {
            ClearScriptedMotion();
            return Vector3.zero;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        float distanceThisTick = Mathf.Min(
            scriptedMotionDistanceRemaining,
            scriptedMotionSpeed * deltaTime,
            maxScriptedMotionPerTick);
        if (distanceThisTick <= 0.000001f)
        {
            ClearScriptedMotion();
            return Vector3.zero;
        }

        Vector3 delta = scriptedMotionDirection * distanceThisTick;
        // Subtract the actually applied slice.  Previously a very short,
        // high-distance profile was sanitized after the remainder had
        // already been consumed, silently dropping part of the lunge.
        scriptedMotionDistanceRemaining -= distanceThisTick;
        scriptedMotionTimeRemaining = Mathf.Max(0f, scriptedMotionTimeRemaining - deltaTime);
        if (scriptedMotionDistanceRemaining <= 0.0001f || scriptedMotionTimeRemaining <= 0.0001f)
            ClearScriptedMotion();
        else
            scriptedMotionSpeed = scriptedMotionDistanceRemaining / scriptedMotionTimeRemaining;

        return HybridMotionSimulator.SanitizeScriptedMotion(delta);
    }

    private struct PredictionFrame
    {
        public bool Valid;
        public HybridInputCommand Command;
        public HybridMotionState State;
    }

    private struct RenderSnapshot
    {
        public uint Tick;
        public Vector3 Position;
        public float Yaw;
    }
}
