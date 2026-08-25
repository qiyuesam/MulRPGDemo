using UnityEngine;

[CreateAssetMenu(fileName = "New Consumable", menuName = "Item/Consumable")]
public class ConsumableData : ItemData
{
    [Header("使用效果")]
    [Min(0)] public int restoreHp;
    [Min(0)] public int restoreMp;

    public bool TryApply(PlayerStats stats)
    {
        if (stats == null || stats.IsDead)
            return false;

        bool canRestoreHp = restoreHp > 0 && stats.CurrentHp < stats.MaxHp;
        bool canRestoreMp = restoreMp > 0 && stats.CurrentMp < stats.MaxMp;
        if (!canRestoreHp && !canRestoreMp)
            return false;

        if (canRestoreHp) stats.ChangeHp(restoreHp);
        if (canRestoreMp) stats.ChangeMp(restoreMp);
        return true;
    }

    void OnValidate()
    {
        type = itemType.Consumable;
        restoreHp = Mathf.Max(0, restoreHp);
        restoreMp = Mathf.Max(0, restoreMp);
    }
}
