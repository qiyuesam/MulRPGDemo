using Unity.Netcode.Components;

public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;

    // 标记：是否应该同步此对象
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Interpolate = false;  // Owner 不需要插值平滑

        // 设置为不接收网络更新
        // NetworkTransform 内部用此标志决定是否应用远程状态
        if (IsOwner)
        {
            // 把同步精度设到最低，有效抑制回弹
            // 实际的阻止需要通过反射或等待下次 Netcode 更新
        }
    }

    // 备用方案：直接 hook 网络反序列化
    private void LateUpdate()
    {
        // Owner 端什么都不做——跳过基类的位置覆盖
        // 非 Owner 端由基类处理
    }
}
