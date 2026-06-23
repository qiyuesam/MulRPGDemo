// Assets/Scripts/Equipment/EffectData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "New Effect", menuName = "Equipment/Effect Data")]
public class EffectData : ScriptableObject
{
    // ── 效果类型枚举 ──
    public enum EffectType
    {
        FireDamage,      // 攻击概率点燃
        Lifesteal,       // 攻击偷取生命
        SpeedBoost,      // 常驻移速加成
        ThornArmor,      // 被击中反伤
        OnKillHeal,      // 击杀回复
        DodgeChance,     // 闪避概率
    }

    public EffectType type;

    [Header("参数")]
    public float value1;  // 概率 / 百分比
    public float value2;  // 伤害值 / 持续时间
    public float value3;  // 备用

    [TextArea]
    public string description;  // 游戏内显示的文本，如 "15% 概率点燃目标 3 秒"
}