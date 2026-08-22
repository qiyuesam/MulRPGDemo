// Assets/Scripts/HealthUI/HpDrainTest.cs
using UnityEngine;

/// <summary>
/// 测试用：每秒随机扣减 1%~10% 的最大生命值，用于验证血条 UI 和掉血方块。
/// 挂到场景里任意 GameObject 上即可。
/// 注意：联机模式下只有服务器能改血量（ChangeHp 内部有 IsServer 判断），
///      纯客户端运行此脚本不会掉血；本地/主机模式正常。
/// </summary>
public class HpDrainTest : MonoBehaviour
{
    [Tooltip("每秒扣减最大生命值的百分比下限，0.01 = 1%")]
    [SerializeField] private float minDrainPercent = 0.01f;
    [Tooltip("每秒扣减最大生命值的百分比上限，0.10 = 10%")]
    [SerializeField] private float maxDrainPercent = 0.10f;

    private PlayerStats stats;
    private float timer;

    void Start()
    {
        stats = FindLocalStats();
    }

    void Update()
    {
        // 玩家可能是点按钮/连接后才生成的，补找一次
        if (stats == null)
        {
            stats = FindLocalStats();
            return;
        }

        timer += Time.deltaTime;
        if (timer >= 0.5f)
        {
            timer -= 0.5f;

            // 每秒随机掉 1% ~ 10% 最大血量（至少扣 1 点）
            float percent = Random.Range(minDrainPercent, maxDrainPercent);
            int drain = Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * percent));
            stats.ChangeHp(-drain);
        }
    }

    PlayerStats FindLocalStats()
    {
        foreach (var s in FindObjectsOfType<PlayerStats>())
            if (!s.IsSpawned || s.IsOwner)
                return s;
        return null;
    }
}
