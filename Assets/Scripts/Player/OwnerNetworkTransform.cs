using Unity.Netcode.Components;

/// <summary>
/// Compatibility component kept on the prefab for existing scenes. Logical
/// transform replication is now performed by PlayerMotor's compact state
/// snapshots, so all legacy NetworkTransform axes are disabled at runtime.
/// </summary>
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;

    protected override void Awake()
    {
        base.Awake();
        DisableTransformReplication();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        DisableTransformReplication();
    }

    private void DisableTransformReplication()
    {
        SyncPositionX = false;
        SyncPositionY = false;
        SyncPositionZ = false;
        SyncRotAngleX = false;
        SyncRotAngleY = false;
        SyncRotAngleZ = false;
        SyncScaleX = false;
        SyncScaleY = false;
        SyncScaleZ = false;
        Interpolate = false;
    }
}
