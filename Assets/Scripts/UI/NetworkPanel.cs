// Assets/Scripts/UI/NetworkPanel.cs
using UnityEngine;

/// <summary>
/// 网络模式选择菜单（NetPanel）。作为主菜单在游戏启动时打开，
/// 选择模式后关闭，网络关停后重新打开。ESC 不可关闭（它是 UI 栈的根）。
/// </summary>
public class NetworkPanel : UIPanel
{
    public override bool ClosableWithEscape => false;

    void Start()
    {
        // 作为主菜单：游戏启动时自动打开
        Open();
    }

    protected override void OnOpen()
    {
        gameObject.SetActive(true);
    }

    protected override void OnClose()
    {
        gameObject.SetActive(false);
    }
}
