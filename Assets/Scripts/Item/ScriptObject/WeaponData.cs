using System;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon", menuName = "Item/Weapon")]
public class WeaponData : ItemData, IEquippable
{
    [Header("武器属性")]
    public EquipAttributes attributes;
    public EffectData[] effects;

    [Header("连段")]
    [Min(1)] public int maxCombo = 5;
    [Range(0f, 1f)] public float comboWindowStart = 0.7f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.9f;
    public AttackStep[] steps;
    public AnimatorOverrideController animatorOverride;

    public EquipSlotType EquipSlot => EquipSlotType.Weapon;
    public EquipAttributes Attributes => attributes;
    public EffectData[] Effects => effects;

    void OnValidate()
    {
        type = itemType.Weapon;
        maxStack = 1;
        maxCombo = Mathf.Clamp(maxCombo, 1, 5);
        comboWindowStart = Mathf.Clamp01(comboWindowStart);
        comboWindowEnd = Mathf.Clamp(comboWindowEnd, comboWindowStart, 1f);
        if (steps != null && steps.Length > maxCombo)
            System.Array.Resize(ref steps, maxCombo);
        if (steps != null)
        {
            foreach (AttackStep step in steps)
                step?.Sanitize();
        }
    }
}

[Serializable]
public class AttackStep
{
    [Min(0.05f)] public float duration = 0.9f;
    [Range(0f, 1f)] public float hitTime = 0.35f;
    [Range(0f, 1f)] public float attackEndTime = 0.95f;
    public bool useCustomComboWindow;
    [Range(0f, 1f)] public float comboWindowStart = 0.7f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.9f;
    [Min(0f)] public float damageMultiplier = 1f;
    [Min(0f)] public float range = 0f;
    [Min(0f)] public float lungeDistance = 0f;
    [Min(0f)] public float lungeDuration = 0f;
    [Min(0f)] public float cooldown = 0.5f;

    public void Sanitize()
    {
        duration = Mathf.Max(0.05f, duration);
        hitTime = Mathf.Clamp01(hitTime);
        attackEndTime = Mathf.Clamp(attackEndTime, hitTime, 1f);
        comboWindowStart = Mathf.Clamp01(comboWindowStart);
        comboWindowEnd = Mathf.Clamp(comboWindowEnd, comboWindowStart, attackEndTime);
        damageMultiplier = Mathf.Max(0f, damageMultiplier);
        range = Mathf.Max(0f, range);
        lungeDistance = Mathf.Max(0f, lungeDistance);
        lungeDuration = Mathf.Max(0f, lungeDuration);
        cooldown = Mathf.Max(0f, cooldown);
    }

    public static AttackStep CreateDefault(int index)
    {
        float clipDuration;
        switch (index)
        {
            case 1: clipDuration = 2.2667f; break;
            case 2: clipDuration = 2.3667f; break;
            case 3: clipDuration = 2.7f; break;
            case 4: clipDuration = 2.3f; break;
            case 5: clipDuration = 3.2667f; break;
            default: clipDuration = 2.3f; break;
        }
        return new AttackStep
        {
            duration = clipDuration,
            hitTime = index >= 4 ? 0.45f : 0.35f,
            damageMultiplier = 1f + (index - 1) * 0.1f,
            cooldown = 0.5f
        };
    }
}
