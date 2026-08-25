using UnityEngine;

[CreateAssetMenu(fileName = "New Material", menuName = "Item/Material")]
public class MaterialData : ItemData
{
    void OnValidate() { type = itemType.Material; }
}
