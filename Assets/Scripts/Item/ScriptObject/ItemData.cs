using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Item/New Item")]
public class ItemData : ScriptableObject
{
    public int itemId;
    public string itemName;
    [TextArea]
    public string itemDescription;
    public Sprite itemIcon;

    public enum itemType
    {
        Consumable,
        Equipment,    // 装备（武器、防具）
        Material,   // ← 新增
        Quest       // ← 新增
    }
    public itemType type;
    public int maxStack=99;
    public GameObject itemPrefab;
    
}
