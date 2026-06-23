using System;

[Serializable]
public struct EquipAttributes
{
    public int attack;
    public int defense;
    public int maxHp;
    public int maxMp;
    public float moveSpeedBonus;
    public float attackSpeedBonus;
    public float critRate;         // 0.15 = 15%
    public float critDamage;       // 1.5 = 150%
    public static EquipAttributes operator +(EquipAttributes a, EquipAttributes b)
    {
        return new EquipAttributes
        {
            attack      = a.attack      + b.attack,
            defense     = a.defense     + b.defense,
            maxHp       = a.maxHp       + b.maxHp,
            maxMp       = a.maxMp       + b.maxMp,
            moveSpeedBonus   = a.moveSpeedBonus   + b.moveSpeedBonus,
            attackSpeedBonus = a.attackSpeedBonus + b.attackSpeedBonus,
            critRate    = a.critRate    + b.critRate,
            critDamage  = a.critDamage  + b.critDamage,
        };
    }

    // 快捷方式：全部为 0 的属性
    public static readonly EquipAttributes Zero = new EquipAttributes();
}
