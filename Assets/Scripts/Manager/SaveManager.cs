// Assets/Scripts/Manager/SaveManager.cs
using UnityEngine;
using System.IO;

public static class SaveManager
{
#if UNITY_EDITOR
    private static string SaveDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Saves"));
#else
    private static string SaveDir =>
        Path.Combine(Application.persistentDataPath, "Saves");
#endif

public static void Save(string fileName, string json)
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            string path = Path.Combine(SaveDir, fileName);
            File.WriteAllText(path, json);
            Debug.Log($"[SaveManager] 已保存: {path} ({json.Length} 字节)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] 保存失败: {fileName} - {e.Message}");
        }
    }

public static string Load(string fileName)
    {
        try
        {
            string path = Path.Combine(SaveDir, fileName);
            if (File.Exists(path))
            {
                Debug.Log($"[SaveManager] 已加载: {path}");
                return File.ReadAllText(path);
            }
            Debug.Log($"[SaveManager] 文件不存在: {path}");
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveManager] 加载失败: {fileName} - {e.Message}");
            return null;
        }
    }

    public static void Delete(string fileName)
    {
        string path = Path.Combine(SaveDir, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveManager] 已删除: {path}");
        }
    }
}