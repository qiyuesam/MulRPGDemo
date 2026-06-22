using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items;

    private Dictionary<int, ItemData> lookup;

    public void Init()
    {
        lookup = new Dictionary<int, ItemData>();
        foreach (var item in items)
        {
            if(item!=null)
                lookup.TryAdd(item.itemId, item);
        }
    }

    public ItemData GetItem(int id)
    {
        if(lookup==null)Init();
        lookup.TryGetValue(id, out ItemData item);
        return item;
    }
    

    private void OnValidate()
    {
        var ids=new HashSet<int>();
        foreach (var item in items)
        {
            if(item==null) continue;
            if(!ids.Add(item.itemId))
                Debug.LogError($"物品 ID 重复{item.itemId}({item.itemName})");
        }
    }
}
