using UnityEngine;

/// <summary>装备面板骨架。装备槽目前已并入背包面板，此面板留作独立装备界面扩展。</summary>
public class EquipmentUI : UIPanel
{
    [SerializeField] private GameObject panel;

    protected override void OnOpen()
    {
        if (panel != null)
            panel.SetActive(true);
    }

    protected override void OnClose()
    {
        if (panel != null)
            panel.SetActive(false);
    }
}
