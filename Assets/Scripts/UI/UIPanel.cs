// Assets/Scripts/UI/UIPanel.cs
using UnityEngine;

/// <summary>
/// 可开关 UI 面板的公共生命周期。
/// 面板只负责「打开/关闭时做什么」，开关状态、cursor、面板栈统一交给 UIManager。
/// </summary>
public abstract class UIPanel : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    /// <summary>ESC 是否可关闭此面板。根菜单类面板应返回 false，避免玩家被关到无面板可用。</summary>
    public virtual bool ClosableWithEscape => true;

    public void Open()
    {
        if (IsOpen)
            return;

        IsOpen = true;
        OnOpen();
        UIManager.Instance?.NotifyPanelOpened(this);
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        OnClose();
        UIManager.Instance?.NotifyPanelClosed(this);
    }

    /// <summary>面板从关闭到打开时调用，子类在此做绑定与刷新。</summary>
    protected abstract void OnOpen();

    /// <summary>面板从打开到关闭时调用，子类在此做解绑与清理。</summary>
    protected abstract void OnClose();
}
