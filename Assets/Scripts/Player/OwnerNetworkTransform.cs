using Unity.Netcode.Components;

public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;

    // 标记：是否应该同步此对象
public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsOwner)
            Interpolate = false;  // 仅 Owner 不需要插值平滑，非 Owner 保留插值
    }

    // 备用方案：直接 hook 网络反序列化

}
