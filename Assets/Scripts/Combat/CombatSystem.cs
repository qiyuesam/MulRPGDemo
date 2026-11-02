using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Authoritative attack timeline. Input starts an attack request; the
/// attack definition controls its hit point, combo window and movement lock.</summary>
public class CombatSystem : NetworkBehaviour
{
    private PlayerStats stats;
    private EffectRunner effectRunner;
    private Equipment equipment;
    private Animator animator;
    private PlayerMotor motor;

    public float AttackCooldown { get; private set; }
    public bool IsOnCooldown => AttackCooldown > 0f;
    public bool IsAttacking => ComboIndex > 0;
    public bool IsAttackMovementLocked => IsAttacking;

    [Header("攻击设置")]
    [SerializeField] private float attackCooldownTime = 0.5f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField, Range(-1f, 1f)] private float minimumFacingDot = 0.1f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("攻击位移")]
    [SerializeField] private float attackLungeDistance = 1.2f;
    [SerializeField] private float attackLungeDuration = 0.15f;

    [Header("输入缓存")]
    [SerializeField] private float inputBufferDuration = 0.25f;

    private readonly NetworkVariable<int> networkComboIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkAttackSequence = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkAttackPlaybackSpeed = new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private int localComboIndex;
    private float localAttackPlaybackSpeed = 1f;
    private int activeSequence;
    private int comboMax = 5;
    private float comboWindowStart = 0.7f;
    private float comboWindowEnd = 0.9f;
    private AttackStep[] attackSteps;
    private AttackStep activeStep;
    private float activeAttackDuration;
    private float activeAttackElapsed;
    private bool activeAttackHit;
    private Vector3 activeAttackDirection;
    private Vector3 bufferedDirection;
    private float bufferedRequestTime = -1f;
    private RuntimeAnimatorController baseAnimatorController;
    private AnimatorOverrideController appliedOverride;
    private WeaponData configuredWeapon;
    // ClientRpc delivery is reliable, but an explicit sequence guard keeps a
    // delayed/duplicated presentation message from applying an old lunge
    // after a newer combo step has already started.
    private int lastOwnerLungeSequence;

    public int ComboIndex => IsSpawned ? networkComboIndex.Value : localComboIndex;
    public int AttackSequence => IsSpawned ? networkAttackSequence.Value : activeSequence;
    public float AttackPlaybackSpeed => IsSpawned ? networkAttackPlaybackSpeed.Value : localAttackPlaybackSpeed;
    public int ComboMax => comboMax;
    public float ComboProgress => activeStep == null ? 0f : activeAttackElapsed / Mathf.Max(0.01f, activeAttackDuration);
    public Action<CombatResult> OnCombatResolved;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();
        effectRunner = GetComponent<EffectRunner>();
        equipment = GetComponent<Equipment>();
        motor = GetComponent<PlayerMotor>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            baseAnimatorController = animator.runtimeAnimatorController;
        RefreshWeaponConfig();
    }

    private void Start()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged += OnEquipmentChanged;
        if (stats != null)
            stats.OnDeath += OnOwnerDeath;
        RefreshWeaponConfig();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lastOwnerLungeSequence = 0;
        if (IsServer)
        {
            networkComboIndex.Value = 0;
            networkAttackSequence.Value = 0;
            networkAttackPlaybackSpeed.Value = 1f;
        }
    }

    public override void OnNetworkDespawn()
    {
        lastOwnerLungeSequence = 0;
        ResetForLocalMode();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Clears network-session combat state before the scene Player is reused
    /// by the local-mode fallback.
    /// </summary>
    public void ResetForLocalMode()
    {
        AttackCooldown = 0f;
        bufferedDirection = Vector3.zero;
        bufferedRequestTime = -1f;
        activeStep = null;
        activeAttackDuration = 0f;
        activeAttackElapsed = 0f;
        activeAttackHit = false;
        localComboIndex = 0;
        localAttackPlaybackSpeed = 1f;
        activeSequence = 0;
        lastOwnerLungeSequence = 0;
        motor?.ClearScriptedMotion();
        if (animator != null)
        {
            animator.speed = 1f;
            animator.SetInteger("ComboIndex", 0);
        }
    }

    public override void OnDestroy()
    {
        if (equipment != null)
            equipment.OnEquipmentChanged -= OnEquipmentChanged;
        if (stats != null)
            stats.OnDeath -= OnOwnerDeath;
        base.OnDestroy();
    }

    private void FixedUpdate()
    {
        if (AttackCooldown > 0f)
            AttackCooldown = Mathf.Max(0f, AttackCooldown - Time.fixedDeltaTime);
        if (!IsSpawned || IsServer)
            UpdateAttackTimeline(Time.fixedDeltaTime);
    }

    public void RequestAttack()
    {
        if (stats != null && stats.IsDead)
            return;
        Vector3 direction = GetCameraForward();
        if (!IsSpawned || IsServer)
            HandleAttackRequest(direction);
        else
            RequestAttackServerRpc(direction);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestAttackServerRpc(Vector3 direction)
    {
        if (!IsFinite(direction))
            return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            direction = transform.forward;
        HandleAttackRequest(direction.normalized);
    }

    private void HandleAttackRequest(Vector3 direction)
    {
        RefreshWeaponConfig();
        if (stats == null || stats.IsDead)
            return;
        if (TryStartAttack(direction))
            return;
        if (ComboIndex < comboMax || activeStep == null)
        {
            bufferedDirection = direction;
            bufferedRequestTime = Time.time;
        }
    }

    private bool TryStartAttack(Vector3 direction)
    {
        if (IsOnCooldown)
            return false;
        int nextIndex = ComboIndex == 0 ? 1 : ComboIndex + 1;
        if (nextIndex > comboMax || (ComboIndex > 0 && !IsComboWindowOpen()))
            return false;
        AttackStep step = GetStep(nextIndex);
        if (step == null)
            return false;
        StartAttack(nextIndex, step, direction);
        return true;
    }

    private void StartAttack(int index, AttackStep step, Vector3 direction)
    {
        motor?.NotifyAttackStarted();
        activeStep = step;
        float attackSpeed = stats != null ? Mathf.Max(0.1f, stats.AttackSpeed) : 1f;
        activeAttackDuration = Mathf.Max(0.05f, step.duration / attackSpeed);
        activeAttackElapsed = 0f;
        activeAttackHit = false;
        activeAttackDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
        activeSequence++;
        if (IsSpawned)
        {
            networkComboIndex.Value = index;
            networkAttackSequence.Value = activeSequence;
            networkAttackPlaybackSpeed.Value = attackSpeed;
        }
        else
        {
            localComboIndex = index;
            localAttackPlaybackSpeed = attackSpeed;
        }
        FaceDirection(activeAttackDirection);
        if (animator != null)
            animator.SetInteger("ComboIndex", index);
        float lungeDistance = step.lungeDistance > 0f ? step.lungeDistance : attackLungeDistance;
        float lungeDuration = (step.lungeDuration > 0f ? step.lungeDuration : attackLungeDuration) / attackSpeed;
        QueueAttackLunge(activeAttackDirection, lungeDistance, lungeDuration);
        if (IsSpawned)
            PlayAttackOwnerClientRpc(
                activeSequence,
                activeAttackDirection,
                lungeDistance,
                lungeDuration);
        float cooldown = step.cooldown > 0f ? step.cooldown : attackCooldownTime;
        AttackCooldown = Mathf.Max(0f, cooldown / attackSpeed);
    }

    [ClientRpc]
    private void PlayAttackOwnerClientRpc(
        int sequence,
        Vector3 direction,
        float lungeDistance,
        float lungeDuration)
    {
        // The dedicated server already applied the authoritative lunge. Only
        // the owning non-host client mirrors it for responsive presentation.
        if (!IsOwner || IsServer)
            return;
        if (sequence <= lastOwnerLungeSequence)
            return;
        lastOwnerLungeSequence = sequence;
        motor?.NotifyAttackStarted();
        FaceDirection(direction);
        QueueAttackLunge(direction, lungeDistance, lungeDuration);
    }

    private void UpdateAttackTimeline(float deltaTime)
    {
        if (stats != null && stats.IsDead)
        {
            ResetCombo();
            bufferedRequestTime = -1f;
            return;
        }
        if (activeStep != null)
        {
            activeAttackElapsed += deltaTime;
            if (!activeAttackHit && activeAttackElapsed >= activeStep.hitTime * activeAttackDuration)
                ResolveActiveAttackHit();
        }
        if (bufferedRequestTime >= 0f)
        {
            if (Time.time - bufferedRequestTime > inputBufferDuration)
                bufferedRequestTime = -1f;
            else if (TryStartAttack(bufferedDirection))
                bufferedRequestTime = -1f;
        }
        if (activeStep != null && activeAttackElapsed >= activeAttackDuration * activeStep.attackEndTime)
            ResetCombo();
    }

    private bool IsComboWindowOpen()
    {
        if (activeStep == null)
            return false;
        float normalized = activeAttackElapsed / Mathf.Max(0.01f, activeAttackDuration);
        float start = activeStep.useCustomComboWindow ? activeStep.comboWindowStart : comboWindowStart;
        float end = activeStep.useCustomComboWindow ? activeStep.comboWindowEnd : comboWindowEnd;
        return normalized >= start && normalized < end;
    }

    private void ResolveActiveAttackHit()
    {
        if (activeStep == null || activeAttackHit)
            return;
        activeAttackHit = true;
        if (!IsSpawned || IsServer)
            PerformAttackDamage(activeAttackDirection, activeStep);
    }

    public void OnAttackAnimationHit()
    {
        if (IsSpawned && !IsServer)
        {
            if (IsOwner && ComboIndex > 0)
                AttackAnimationHitServerRpc(networkAttackSequence.Value);
            return;
        }
        if (activeStep == null || activeAttackHit)
            return;
        ResolveActiveAttackHit();
    }

    [ServerRpc(RequireOwnership = true)]
    private void AttackAnimationHitServerRpc(int sequence)
    {
        float earliestHit = activeStep == null ? float.MaxValue : activeStep.hitTime * activeAttackDuration - 0.05f;
        if (activeStep != null && !activeAttackHit && sequence == networkAttackSequence.Value && activeAttackElapsed >= earliestHit)
            ResolveActiveAttackHit();
    }

    private void ResetCombo()
    {
        motor?.ClearScriptedMotion();
        activeStep = null;
        activeAttackDuration = 0f;
        activeAttackElapsed = 0f;
        activeAttackHit = false;
        if (IsSpawned && IsServer)
        {
            networkComboIndex.Value = 0;
            networkAttackPlaybackSpeed.Value = 1f;
        }
        else if (!IsSpawned)
        {
            localComboIndex = 0;
            localAttackPlaybackSpeed = 1f;
        }
        if (animator != null)
            animator.SetInteger("ComboIndex", 0);
    }

    public void InterruptAttack()
    {
        if (IsSpawned && !IsServer)
        {
            // The client cannot cancel the authoritative combo timeline, but
            // it must stop any locally predicted lunge immediately (for
            // example when death arrives while an attack is winding up).
            motor?.ClearScriptedMotion();
            return;
        }
        bufferedRequestTime = -1f;
        ResetCombo();
    }

    private void RefreshWeaponConfig()
    {
        comboMax = 5;
        comboWindowStart = 0.7f;
        comboWindowEnd = 0.9f;
        attackSteps = null;
        AnimatorOverrideController desiredOverride = null;
        WeaponData weapon = equipment != null ? equipment.GetWeapon() as WeaponData : null;
        if (weapon != configuredWeapon && activeStep != null && (!IsSpawned || IsServer))
            InterruptAttack();
        configuredWeapon = weapon;
        if (weapon != null)
        {
            comboMax = Mathf.Clamp(weapon.maxCombo, 1, 5);
            comboWindowStart = weapon.comboWindowStart;
            comboWindowEnd = weapon.comboWindowEnd;
            attackSteps = weapon.steps;
            desiredOverride = weapon.animatorOverride;
        }
        if (animator != null && desiredOverride != appliedOverride)
        {
            animator.runtimeAnimatorController = desiredOverride != null ? desiredOverride : baseAnimatorController;
            appliedOverride = desiredOverride;
        }
    }

    private void OnEquipmentChanged()
    {
        RefreshWeaponConfig();
    }

    private void OnOwnerDeath()
    {
        InterruptAttack();
    }

    private AttackStep GetStep(int index)
    {
        if (attackSteps != null && index - 1 < attackSteps.Length && attackSteps[index - 1] != null)
            return attackSteps[index - 1];
        return AttackStep.CreateDefault(index);
    }

    private void PerformAttackDamage(Vector3 direction, AttackStep step)
    {
        if (IsSpawned && !IsServer)
            return;
        int mask = enemyLayer.value == 0 ? Physics.AllLayers : enemyLayer.value;
        float range = step.range > 0f ? step.range : attackRange;
        Collider[] hits = Physics.OverlapSphere(transform.position, range, mask, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            return;
        Transform target = GetClosestTarget(hits, direction);
        if (target == null)
            return;
        PlayerStats targetStats = target.GetComponentInParent<PlayerStats>();
        EffectRunner targetEffects = target.GetComponentInParent<EffectRunner>();
        if (!IsValidTarget(targetStats) || targetEffects == null)
            return;
        CombatResult result = ExecutePipeline(targetStats, targetEffects, step.damageMultiplier);
        OnCombatResolved?.Invoke(result);
    }

    private CombatResult ExecutePipeline(PlayerStats defenderStats, EffectRunner defenderEffects, float damageMultiplier)
    {
        if (defenderStats == null || defenderEffects == null || stats == null || effectRunner == null)
            return new CombatResult { dodged = true };
        float rawDamage = stats.Attack * Mathf.Max(0f, damageMultiplier);
        DefendResult defResult = defenderEffects.ProcessOnHit(rawDamage);
        if (defResult.dodged)
            return new CombatResult { dodged = true };
        float finalDamage = CalculateDamage(rawDamage, defenderStats.Defense, stats.CritRate, stats.CritDamage);
        AttackResult atkResult = effectRunner.ProcessOnAttack(finalDamage);
        ApplyDamage(finalDamage, atkResult, defResult, defenderStats);
        if (atkResult.hasDot)
            defenderEffects.ApplyDamageOverTime(atkResult.dotDamage, atkResult.dotDuration);
        return new CombatResult { finalDamage = finalDamage, extraDamage = atkResult.extraDamage, selfHeal = atkResult.selfHeal, reflectDamage = defResult.reflectDamage, targetDead = defenderStats.IsDead };
    }

    private float CalculateDamage(float raw, int defense, float critRate, float critDamage)
    {
        float reduction = defense / (defense + 100f);
        float damage = raw * (1f - reduction);
        if (UnityEngine.Random.value < critRate)
            damage *= critDamage;
        return Mathf.Max(1f, damage);
    }

    private void ApplyDamage(float damage, AttackResult atkResult, DefendResult defResult, PlayerStats defenderStats)
    {
        bool targetWasAlive = !defenderStats.IsDead;
        defenderStats.ChangeHp(-Mathf.Max(0, Mathf.RoundToInt(damage + atkResult.extraDamage)));
        if (atkResult.selfHeal > 0f)
            stats.ChangeHp((int)atkResult.selfHeal);
        if (defResult.reflectDamage > 0f)
            stats.ChangeHp(-(int)defResult.reflectDamage);
        if (targetWasAlive && defenderStats.IsDead)
        {
            stats.ChangeHp((int)effectRunner.ProcessOnKill());
            QuestTargetIdentity identity = defenderStats.GetComponent<QuestTargetIdentity>();
            if (identity != null && !string.IsNullOrEmpty(identity.TargetKey))
                QuestManager.Publish(QuestEvent.TargetKilled(gameObject, identity.TargetKey));
        }
    }

    private Transform GetClosestTarget(Collider[] hits, Vector3 direction)
    {
        Transform best = null;
        float minDist = float.MaxValue;
        foreach (Collider hit in hits)
        {
            PlayerStats candidate = hit.GetComponentInParent<PlayerStats>();
            if (!IsValidTarget(candidate))
                continue;
            Vector3 toTarget = candidate.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f && Vector3.Dot(direction, toTarget.normalized) < minimumFacingDot)
                continue;
            float distance = toTarget.magnitude;
            if (distance < minDist)
            {
                minDist = distance;
                best = hit.transform;
            }
        }
        return best;
    }

    private bool IsValidTarget(PlayerStats candidate)
    {
        return candidate != null && candidate != stats && !candidate.IsDead && (!candidate.IsSpawned || NetworkObject == null || candidate.NetworkObject == null || candidate.NetworkObject != NetworkObject);
    }

    private Vector3 GetCameraForward()
    {
        if (Camera.main == null)
            return transform.forward;
        Vector3 forward = Camera.main.transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
    }

    private void FaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction);
    }

    private void QueueAttackLunge(Vector3 direction, float distance, float duration)
    {
        distance = distance > 0f ? distance : attackLungeDistance;
        duration = duration > 0f ? duration : attackLungeDuration;
        if (motor == null || distance <= 0f)
            return;
        motor.QueueAttackLunge(direction, distance, duration);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

public struct CombatResult
{
    public bool dodged;
    public float finalDamage;
    public float extraDamage;
    public float selfHeal;
    public float reflectDamage;
    public bool targetDead;
}
