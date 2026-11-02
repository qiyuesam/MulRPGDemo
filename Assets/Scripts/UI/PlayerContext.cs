// Assets/Scripts/UI/PlayerContext.cs
using UnityEngine;

/// <summary>
/// 本地玩家的 UI 相关组件聚合。spawn 时由 UIManager 构造并统一下发，
/// 面板与 HUD 通过 UIManager.LocalPlayerChanged 订阅，不再各自 FindObjectsOfType。
/// </summary>
public readonly struct PlayerContext
{
    public readonly Inventory Inventory;
    public readonly Equipment Equipment;
    public readonly QuestLog QuestLog;
    public readonly PlayerStats Stats;

    public PlayerContext(Inventory inventory)
    {
        Inventory = inventory;
        Equipment = inventory != null ? inventory.GetComponent<Equipment>() : null;
        QuestLog = inventory != null ? inventory.GetComponent<QuestLog>() : null;
        Stats = inventory != null ? inventory.GetComponent<PlayerStats>() : null;
    }

    public bool Valid => Inventory != null;
}
