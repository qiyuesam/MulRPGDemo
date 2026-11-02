using UnityEngine;

/// <summary>
/// Captures Animator root motion on the visual/model child and hands it to
/// PlayerMotor.  The child never becomes the collision authority: its local
/// pose is restored after animation evaluation and the logical root consumes
/// the captured delta through CharacterController.Move.
/// </summary>
[DisallowMultipleComponent]
public sealed class RootMotionRelay : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float correctionSmoothTime = 0.12f;
    [SerializeField, Min(0.01f)] private float maxVisualCorrection = 2f;

    private Animator animator;
    private PlayerMotor motor;
    private Transform logicalRoot;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 visualOffset;
    private Vector3 visualOffsetVelocity;
    private Quaternion visualRotationOffset = Quaternion.identity;
    private Vector3 accumulatedWorldDelta;
    private float accumulatedYaw;
    private bool captureEnabled;
    private bool initialized;

    public bool CaptureEnabled => captureEnabled;
    public bool HasRootMotion => animator != null && animator.hasRootMotion;

    private void Awake()
    {
        Initialize();
    }

    public void Configure(PlayerMotor owner, bool shouldCapture)
    {
        Initialize();
        motor = owner != null ? owner : GetComponentInParent<PlayerMotor>();
        SetCaptureEnabled(shouldCapture);
    }

    public void SetCaptureEnabled(bool shouldCapture)
    {
        Initialize();
        captureEnabled = shouldCapture && animator != null && motor != null;
        if (animator != null)
        {
            animator.applyRootMotion = captureEnabled;
            if (captureEnabled)
                animator.updateMode = AnimatorUpdateMode.AnimatePhysics;
            else
                animator.updateMode = AnimatorUpdateMode.Normal;
        }

        if (!captureEnabled)
        {
            accumulatedWorldDelta = Vector3.zero;
            accumulatedYaw = 0f;
            RestoreVisualPose();
        }
    }

    public RootMotionSample Consume()
    {
        RootMotionSample sample = new RootMotionSample
        {
            WorldDelta = accumulatedWorldDelta,
            YawDelta = accumulatedYaw,
        };
        accumulatedWorldDelta = Vector3.zero;
        accumulatedYaw = 0f;
        return sample;
    }

    public void ClearAccumulatedMotion()
    {
        accumulatedWorldDelta = Vector3.zero;
        accumulatedYaw = 0f;
    }

    /// <summary>
    /// Keeps the model visually where it was while the logical root is
    /// corrected, then springs the model back to its authored local pose.
    /// </summary>
    public void PreserveWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        Initialize();
        if (logicalRoot == null)
            return;

        Transform visualParent = transform.parent != null ? transform.parent : logicalRoot;
        Vector3 targetLocalPosition = visualParent.InverseTransformPoint(worldPosition);
        Vector3 desiredOffset = targetLocalPosition - baseLocalPosition;
        if (desiredOffset.magnitude > maxVisualCorrection)
        {
            visualOffset = Vector3.zero;
            visualOffsetVelocity = Vector3.zero;
            visualRotationOffset = Quaternion.identity;
            RestoreVisualPose();
            return;
        }

        Quaternion targetLocalRotation = Quaternion.Inverse(visualParent.rotation) * worldRotation;
        Quaternion desiredRotationOffset = Quaternion.Inverse(baseLocalRotation) * targetLocalRotation;
        visualOffset = desiredOffset;
        visualOffsetVelocity = Vector3.zero;
        visualRotationOffset = desiredRotationOffset;
        RestoreVisualPose();
    }

    private void OnAnimatorMove()
    {
        Initialize();

        if (captureEnabled && animator != null)
        {
            Vector3 localDelta = animator.deltaPosition;
            Quaternion deltaRotation = animator.deltaRotation;
            if (HybridMotionSimulator.IsFinite(localDelta) && logicalRoot != null)
            {
                // Animator.deltaPosition is expressed in world space for the
                // evaluated avatar. Apply it directly to the logical root;
                // transforming it again would rotate the delta twice.
                accumulatedWorldDelta += localDelta;
                if (IsFinite(deltaRotation))
                    accumulatedYaw += Mathf.DeltaAngle(0f, deltaRotation.eulerAngles.y);
            }
        }

        // Animator root motion must not move the visual child independently of
        // the logical CharacterController root.
        RestoreVisualPose();
    }

    private void LateUpdate()
    {
        if (!initialized)
            return;

        if (visualOffset.sqrMagnitude > 0.000001f)
        {
            visualOffset = Vector3.SmoothDamp(
                visualOffset,
                Vector3.zero,
                ref visualOffsetVelocity,
                correctionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }
        else
        {
            visualOffset = Vector3.zero;
            visualOffsetVelocity = Vector3.zero;
        }

        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, correctionSmoothTime));
        visualRotationOffset = Quaternion.Slerp(visualRotationOffset, Quaternion.identity, blend);
        RestoreVisualPose();
    }

    private void OnValidate()
    {
        correctionSmoothTime = Mathf.Max(0.01f, correctionSmoothTime);
        maxVisualCorrection = Mathf.Max(0.01f, maxVisualCorrection);
    }

    private void Initialize()
    {
        if (initialized)
            return;

        animator = GetComponent<Animator>();
        motor = GetComponentInParent<PlayerMotor>();
        logicalRoot = motor != null ? motor.transform : transform.parent;
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        initialized = true;
    }

    private void RestoreVisualPose()
    {
        if (!initialized)
            return;

        transform.localPosition = baseLocalPosition + visualOffset;
        transform.localRotation = baseLocalRotation * visualRotationOffset;
    }

    private static bool IsFinite(Quaternion value)
    {
        return HybridMotionSimulator.IsFinite(value.x)
            && HybridMotionSimulator.IsFinite(value.y)
            && HybridMotionSimulator.IsFinite(value.z)
            && HybridMotionSimulator.IsFinite(value.w);
    }
}

public struct RootMotionSample
{
    public Vector3 WorldDelta;
    public float YawDelta;
}
