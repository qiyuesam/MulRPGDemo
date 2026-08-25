using UnityEngine;

/// <summary>给敌人、NPC 或地点提供稳定的任务目标标识。</summary>
public class QuestTargetIdentity : MonoBehaviour
{
    [SerializeField] private string targetKey;
    public string TargetKey => targetKey;

    private void OnValidate()
    {
        targetKey = targetKey?.Trim();
    }
}
