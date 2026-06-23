// 完整改造后的 ItemImporter.cs
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

        string[] lines;
        using (var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            lines = reader.ReadToEnd()
                .Replace("\r", "")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // 读取表头
        string[] headers = lines[0].Split(',');

        string dbPath = "Assets/Item/Database/ItemDatabase.asset";
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        foreach (string oldFile in Directory.GetFiles("Assets/Item/Data", "*.asset"))
            AssetDatabase.DeleteAsset(oldFile);
        db.items.Clear();

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            int id          = int.Parse(cols[0]);
            string name     = cols[1];
            string typeStr  = cols[2];
            int maxStack    = int.Parse(cols[3]);
            string desc     = cols[4];
            Sprite icon     = Resources.Load<Sprite>($"Icons/{cols[5]}");

            // ★ 根据类型创建不同子类
            ItemData item = typeStr switch
            {
                "Weapon"      => ScriptableObject.CreateInstance<WeaponData>(),
                "Equipment"   => ScriptableObject.CreateInstance<ArmorData>(),
                "Consumable"  => ScriptableObject.CreateInstance<ConsumableData>(),
                "Material"    => ScriptableObject.CreateInstance<MaterialData>(),
                "Quest"       => ScriptableObject.CreateInstance<QuestData>(),
                _             => null
            };

            if (item == null)
            {
                Debug.LogWarning($"第{i+1}行: 未知类型 '{typeStr}'，跳过");
                continue;
            }

            // ── 公共字段 ──
            item.itemId          = id;
            item.itemName        = name;
            item.name            = "item"+id;
            item.type            = (ItemData.itemType)Enum.Parse(typeof(ItemData.itemType), typeStr);
            item.maxStack        = maxStack;
            item.itemDescription = desc;
            item.itemIcon        = icon;

            // ── ★ 装备专属字段 ──
            if (item is IEquippable equippable)
            {
                // equipSlot（第 7 列：cols[6]）
                if (cols.Length > 6 && !string.IsNullOrEmpty(cols[6]))
                {
                    var slot = (EquipSlotType)Enum.Parse(typeof(EquipSlotType), cols[6]);
                    // WeaponData 的 equipSlot 由属性 getter 返回 Weapon，不需要设
                    // ArmorData 需要设
                    if (item is ArmorData armor)
                        armor.equipSlot = slot;
                }

                // attributes（第 8~15 列：cols[7]~cols[14]）
                EquipAttributes attr = new EquipAttributes
                {
                    attack           = (int)ParseFloat(cols, 7),    // int
                    defense          = (int)ParseFloat(cols, 8),    // int
                    maxHp            = (int)ParseFloat(cols, 9),    // int
                    maxMp            = (int)ParseFloat(cols, 10),   // int
                    moveSpeedBonus   = ParseFloat(cols, 11),        // float
                    attackSpeedBonus = ParseFloat(cols, 12),        // float
                    critRate         = ParseFloat(cols, 13),        // float
                    critDamage       = ParseFloat(cols, 14),        // float
                };


                if (item is WeaponData w)
                    w.attributes = attr;
                else if (item is ArmorData a)
                    a.attributes = attr;

                // effects（第 16 列：cols[15]，分号分隔效果文件名）
                if (cols.Length > 15 && !string.IsNullOrEmpty(cols[15]))
                {
                    string[] effectNames = cols[15].Split(';');
                    var effectList = new System.Collections.Generic.List<EffectData>();
                    foreach (string eName in effectNames)
                    {
                        var eff = AssetDatabase.LoadAssetAtPath<EffectData>(
                            $"Assets/Item/Data/{eName.Trim()}.asset");
                        if (eff != null) effectList.Add(eff);
                    }
                    if (item is WeaponData w2)
                        w2.effects = effectList.ToArray();
                    else if (item is ArmorData a2)
                        a2.effects = effectList.ToArray();
                }
            }

            string assetPath = $"Assets/Item/Data/item{id}.asset";
            AssetDatabase.CreateAsset(item, assetPath);
            db.items.Add(item);
        }

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"配表导入完成，共 {lines.Length - 1} 行");
    }

    static float ParseFloat(string[] cols, int index)
    {
        if (index >= cols.Length) return 0f;
        float.TryParse(cols[index], out float v);
        return v;
    }
}
