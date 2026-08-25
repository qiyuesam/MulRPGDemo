using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class ItemImporter
{
    private const string DatabasePath = "Assets/Item/Database/ItemDatabase.asset";
    private const string DataDirectory = "Assets/Item/Data";
    private const string FallbackIconPath = "Assets/Textures/WhiteFill.png";
    private static readonly Regex ManagedAssetName = new Regex(@"^item(?<id>\d+)\.asset$", RegexOptions.IgnoreCase);

    private sealed class ItemDefinition
    {
        public int id;
        public string itemName;
        public ItemData.itemType itemType;
        public int maxStack;
        public string description;
        public Sprite icon;
        public string iconName;
        public EquipSlotType equipSlot;
        public EquipAttributes attributes;
        public EffectData[] effects = Array.Empty<EffectData>();
        public int restoreHp;
        public int restoreMp;
    }

    [MenuItem("Tools/Import Item csv")]
    public static void Import()
    {
        string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "Tables", "items.csv"));
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
        if (db == null)
        {
            Debug.LogError($"[ItemImporter] 找不到数据库: {DatabasePath}");
            return;
        }
        if (!File.Exists(csvPath))
        {
            Debug.LogError($"[ItemImporter] 配表不存在: {csvPath}");
            return;
        }

        if (!TryParseDefinitions(csvPath, out List<ItemDefinition> definitions, out List<string> errors))
        {
            Debug.LogError("[ItemImporter] 导入已取消，现有资产未修改:\n" + string.Join("\n", errors));
            return;
        }

        Undo.RegisterCompleteObjectUndo(db, "Import Item CSV");
        var importedItems = new List<ItemData>(definitions.Count);
        var expectedIds = new HashSet<int>(definitions.Select(d => d.id));

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (ItemDefinition definition in definitions)
                importedItems.Add(UpdateOrCreateAsset(definition));

            DeleteStaleManagedAssets(expectedIds);
            db.items = importedItems;
            db.Invalidate();
            EditorUtility.SetDirty(db);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int fallbackCount = definitions.Count(d => d.icon != null &&
            AssetDatabase.GetAssetPath(d.icon) == FallbackIconPath);
        string fallbackMessage = fallbackCount > 0
            ? $"；{fallbackCount} 个图标使用通用占位图，请后续放入 Assets/Item/Icons"
            : string.Empty;
        Debug.Log($"[ItemImporter] 导入完成，共 {definitions.Count} 个物品{fallbackMessage}");
    }

    private static bool TryParseDefinitions(string csvPath, out List<ItemDefinition> definitions,
                                            out List<string> errors)
    {
        definitions = new List<ItemDefinition>();
        errors = new List<string>();

        string text;
        try
        {
            using var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            text = reader.ReadToEnd();
        }
        catch (Exception e)
        {
            errors.Add($"读取 CSV 失败: {e.Message}");
            return false;
        }

        List<List<string>> rows = ParseCsv(text, errors);
        if (errors.Count > 0 || rows.Count < 2)
        {
            if (rows.Count < 2) errors.Add("CSV 为空或没有数据行");
            return false;
        }

        var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows[0].Count; i++)
        {
            string name = rows[0][i].Trim();
            if (!string.IsNullOrEmpty(name) && !header.TryAdd(name, i))
                errors.Add($"表头重复: {name}");
        }

        string[] required = { "itemId", "itemName", "itemType", "maxStack", "description", "icon" };
        foreach (string name in required)
            if (!header.ContainsKey(name)) errors.Add($"缺少必需列: {name}");
        if (errors.Count > 0) return false;

        var usedIds = new HashSet<int>();
        for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            int displayRow = rowIndex + 1;
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            if (!TryPositiveInt(Get(row, header, "itemId"), out int id))
            {
                errors.Add($"第 {displayRow} 行 itemId 必须是正整数");
                continue;
            }
            if (!usedIds.Add(id))
            {
                errors.Add($"第 {displayRow} 行 itemId 重复: {id}");
                continue;
            }

            string name = Get(row, header, "itemName").Trim();
            string typeText = Get(row, header, "itemType").Trim();
            if (string.IsNullOrEmpty(name)) errors.Add($"第 {displayRow} 行物品名为空");
            if (!Enum.TryParse(typeText, true, out ItemData.itemType itemType))
            {
                errors.Add($"第 {displayRow} 行物品类型无效: {typeText}");
                continue;
            }
            if (!TryPositiveInt(Get(row, header, "maxStack"), out int maxStack))
            {
                errors.Add($"第 {displayRow} 行 maxStack 必须是正整数");
                continue;
            }

            var definition = new ItemDefinition
            {
                id = id,
                itemName = name,
                itemType = itemType,
                maxStack = maxStack,
                description = Get(row, header, "description").Trim(),
                iconName = Get(row, header, "icon").Trim(),
                attributes = new EquipAttributes
                {
                    attack = ParseInt(row, header, "attack", displayRow, errors),
                    defense = ParseInt(row, header, "defense", displayRow, errors),
                    maxHp = ParseInt(row, header, "maxHp", displayRow, errors),
                    maxMp = ParseInt(row, header, "maxMp", displayRow, errors),
                    moveSpeedBonus = ParseFloat(row, header, "moveSpeedBonus", displayRow, errors),
                    attackSpeedBonus = ParseFloat(row, header, "attackSpeedBonus", displayRow, errors),
                    critRate = ParseFloat(row, header, "critRate", displayRow, errors),
                    critDamage = ParseFloat(row, header, "critDamage", displayRow, errors),
                },
                restoreHp = ParseInt(row, header, "restoreHp", displayRow, errors),
                restoreMp = ParseInt(row, header, "restoreMp", displayRow, errors),
            };

            if (definition.attributes.attack < 0 || definition.attributes.defense < 0 ||
                definition.attributes.maxHp < 0 || definition.attributes.maxMp < 0 ||
                definition.restoreHp < 0 || definition.restoreMp < 0)
                errors.Add($"第 {displayRow} 行属性和恢复值不能为负数");

            if (itemType == ItemData.itemType.Equipment)
            {
                string slotText = Get(row, header, "equipSlot").Trim();
                if (!Enum.TryParse(slotText, true, out definition.equipSlot) ||
                    definition.equipSlot == EquipSlotType.Weapon)
                    errors.Add($"第 {displayRow} 行护甲槽位无效: {slotText}");
                if (maxStack != 1) errors.Add($"第 {displayRow} 行装备 maxStack 必须为 1");
            }
            else if (itemType == ItemData.itemType.Weapon && maxStack != 1)
            {
                errors.Add($"第 {displayRow} 行武器 maxStack 必须为 1");
            }
            else if (itemType == ItemData.itemType.Consumable &&
                     definition.restoreHp == 0 && definition.restoreMp == 0)
            {
                errors.Add($"第 {displayRow} 行消耗品没有任何恢复效果");
            }

            definition.icon = LoadIcon(definition.iconName);
            if (definition.icon == null)
                errors.Add($"第 {displayRow} 行找不到图标且占位图无效: {definition.iconName}");

            string effectsText = Get(row, header, "effects").Trim();
            if (!string.IsNullOrEmpty(effectsText))
            {
                var effects = new List<EffectData>();
                foreach (string effectName in effectsText.Split(';'))
                {
                    string trimmed = effectName.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    string path = $"{DataDirectory}/{trimmed}.asset";
                    var effect = AssetDatabase.LoadAssetAtPath<EffectData>(path);
                    if (effect == null)
                        errors.Add($"第 {displayRow} 行找不到效果资产: {path}");
                    else
                        effects.Add(effect);
                }
                definition.effects = effects.ToArray();
            }

            definitions.Add(definition);
        }

        return errors.Count == 0 && definitions.Count > 0;
    }

    private static ItemData UpdateOrCreateAsset(ItemDefinition definition)
    {
        string path = $"{DataDirectory}/item{definition.id}.asset";
        Type expectedType = GetExpectedType(definition.itemType);
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        bool missingScript = false;
        if (item != null)
        {
            var serialized = new SerializedObject(item);
            SerializedProperty scriptProperty = serialized.FindProperty("m_Script");
            missingScript = scriptProperty == null || scriptProperty.objectReferenceValue == null;
        }

        bool createNew = item == null || item.GetType() != expectedType || missingScript;
        if (createNew)
        {
            AssetDatabase.DeleteAsset(path);
            item = (ItemData)ScriptableObject.CreateInstance(expectedType);
        }
        else
        {
            Undo.RegisterCompleteObjectUndo(item, "Import Item CSV");
        }

        item.name = $"item{definition.id}";
        item.itemId = definition.id;
        item.itemName = definition.itemName;
        item.itemDescription = definition.description;
        item.itemIcon = definition.icon;
        item.type = definition.itemType;
        item.maxStack = definition.maxStack;

        if (item is ArmorData armor)
        {
            armor.equipSlot = definition.equipSlot;
            armor.attributes = definition.attributes;
            armor.effects = definition.effects;
        }
        else if (item is WeaponData weapon)
        {
            weapon.attributes = definition.attributes;
            weapon.effects = definition.effects;
            weapon.maxCombo = Mathf.Clamp(weapon.maxCombo, 1, 5);
            weapon.comboWindowStart = Mathf.Clamp01(weapon.comboWindowStart);
            weapon.comboWindowEnd = Mathf.Clamp(weapon.comboWindowEnd, weapon.comboWindowStart, 1f);
        }
        else if (item is ConsumableData consumable)
        {
            consumable.restoreHp = definition.restoreHp;
            consumable.restoreMp = definition.restoreMp;
        }

        if (createNew)
            AssetDatabase.CreateAsset(item, path);
        EditorUtility.SetDirty(item);
        return item;
    }

    private static void DeleteStaleManagedAssets(HashSet<int> expectedIds)
    {
        if (!AssetDatabase.IsValidFolder(DataDirectory)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData", new[] { DataDirectory }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Match match = ManagedAssetName.Match(Path.GetFileName(path));
            if (match.Success && int.TryParse(match.Groups["id"].Value, out int id) && !expectedIds.Contains(id))
                AssetDatabase.DeleteAsset(path);
        }
    }

    private static Sprite LoadIcon(string iconName)
    {
        if (!string.IsNullOrWhiteSpace(iconName))
        {
            string directPath = $"Assets/Item/Icons/{iconName}.png";
            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(directPath);
            if (direct != null) return direct;

            foreach (string guid in AssetDatabase.FindAssets($"{iconName} t:Sprite", new[] { "Assets/Item/Icons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), iconName,
                                  StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(FallbackIconPath);
    }

    private static Type GetExpectedType(ItemData.itemType type) => type switch
    {
        ItemData.itemType.Weapon => typeof(WeaponData),
        ItemData.itemType.Equipment => typeof(ArmorData),
        ItemData.itemType.Consumable => typeof(ConsumableData),
        ItemData.itemType.Material => typeof(MaterialData),
        ItemData.itemType.Quest => typeof(QuestData),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string Get(List<string> row, Dictionary<string, int> header, string column)
        => header.TryGetValue(column, out int index) && index < row.Count ? row[index] : string.Empty;

    private static int ParseInt(List<string> row, Dictionary<string, int> header, string column,
                                int displayRow, List<string> errors)
    {
        string value = Get(row, header, column).Trim();
        if (string.IsNullOrEmpty(value)) return 0;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;
        errors.Add($"第 {displayRow} 行 {column} 不是有效整数: {value}");
        return 0;
    }

    private static float ParseFloat(List<string> row, Dictionary<string, int> header, string column,
                                    int displayRow, List<string> errors)
    {
        string value = Get(row, header, column).Trim();
        if (string.IsNullOrEmpty(value)) return 0f;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
            !float.IsNaN(parsed) && !float.IsInfinity(parsed))
            return parsed;
        errors.Add($"第 {displayRow} 行 {column} 不是有效数字: {value}");
        return 0f;
    }

    private static bool TryPositiveInt(string value, out int parsed)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static List<List<string>> ParseCsv(string text, List<string> errors)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
                    row = new List<string>();
                    break;
                case '\r': break;
                default: cell.Append(c); break;
            }
        }

        if (quoted) errors.Add("CSV 存在未闭合的引号");
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row);
        }
        return rows;
    }
}
