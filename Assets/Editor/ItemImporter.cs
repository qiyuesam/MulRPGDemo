using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public class ItemImporter
{
    [MenuItem("Tools/Import Item csv")]
    public static void Import()
    {
        string csvPath = Application.dataPath + "/../Data/Tables/items.csv";
        if (!File.Exists(csvPath))
        {
            Debug.Log($"配表不存在:{csvPath}");
            return;
        }
        // 读文件 + 去 \r + 去空行，一步到位
        string[] lines;
        using (var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            lines = reader.ReadToEnd()
                .Replace("\r", "")                                // ← 清掉 Windows 回车符
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries); // ← 自动跳过空行
        }

        string dbPath="Assets/Item/Database/ItemDatabase.asset";
        var db=AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        foreach (string oldFile in Directory.GetFiles("Assets/Item/Data", "*.asset"))
            AssetDatabase.DeleteAsset(oldFile);
        db.items.Clear();
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            int id=int.Parse(cols[0]);
            string name=cols[1];
            var type=(ItemData.itemType)Enum.Parse(typeof(ItemData.itemType),cols[2]);
            int maxStack=int.Parse(cols[3]);
            string description=cols[4];
            Sprite icon=Resources.Load<Sprite>($"Icons/{cols[5]}");
            
            ItemData item=ScriptableObject.CreateInstance<ItemData>();
            item.itemId = id;
            item.itemName = name;
            item.name = name;
            item.type = type;
            item.maxStack=maxStack;
            item.itemDescription=description;
            item.itemIcon=icon;
            
            string assetPath=$"Assets/Item/Data/{name}.asset";
            AssetDatabase.CreateAsset(item,assetPath);
            db.items.Add(item);
        }
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"配表导入完成，共{lines.Length-1}行");
    }
}
