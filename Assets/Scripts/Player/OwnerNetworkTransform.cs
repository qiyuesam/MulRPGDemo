using Unity.Netcode.Components;

public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;//意味着非服务端也能控制该五道题
}