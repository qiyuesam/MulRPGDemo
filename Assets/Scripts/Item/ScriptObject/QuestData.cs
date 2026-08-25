using UnityEngine;

[CreateAssetMenu(fileName = "New Quest Item", menuName = "Item/Quest")]
public class QuestData : ItemData
{
    void OnValidate() { type = itemType.Quest; }
}
