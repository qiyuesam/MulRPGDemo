using UnityEngine;

/// <summary>
/// Animation clips execute events on the Animator GameObject. The combat
/// system lives on the player root, so this component forwards hit events.
/// </summary>
[DisallowMultipleComponent]
public sealed class AttackAnimationEventRelay : MonoBehaviour
{
    private CombatSystem combat;

    private void Awake()
    {
        combat = GetComponentInParent<CombatSystem>();
    }

    public void OnAttackAnimationHit()
    {
        if (combat == null)
            combat = GetComponentInParent<CombatSystem>();
        combat?.OnAttackAnimationHit();
    }
}
