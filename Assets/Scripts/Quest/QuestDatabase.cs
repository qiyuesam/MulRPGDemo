using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "QuestDatabase", menuName = "Quest/Quest Database")]
public class QuestDatabase : ScriptableObject
{
    public List<QuestDefinition> quests = new List<QuestDefinition>();

    private Dictionary<int, QuestDefinition> lookup;

    public IEnumerable<QuestDefinition> All => quests ?? (IEnumerable<QuestDefinition>)System.Array.Empty<QuestDefinition>();

    public void Init()
    {
        lookup = new Dictionary<int, QuestDefinition>();
        if (quests == null)
            quests = new List<QuestDefinition>();

        foreach (QuestDefinition quest in quests)
        {
            if (quest == null) continue;
            if (!lookup.ContainsKey(quest.questId))
                lookup.Add(quest.questId, quest);
        }
    }

    public QuestDefinition GetQuest(int questId)
    {
        if (questId <= 0) return null;
        if (lookup == null) Init();
        lookup.TryGetValue(questId, out QuestDefinition quest);
        return quest;
    }

    public void Invalidate() => lookup = null;

    private void OnValidate()
    {
        var ids = new HashSet<int>();
        if (quests == null)
        {
            quests = new List<QuestDefinition>();
            lookup = null;
            return;
        }

        foreach (QuestDefinition quest in quests)
        {
            if (quest == null) continue;
            if (!ids.Add(quest.questId))
                Debug.LogError($"任务 ID 重复: {quest.questId} ({quest.title})", this);

            if (!quest.IsValid(out string error))
                Debug.LogError($"任务 {quest.questId} 无效: {error}", quest);
        }

        lookup = null;
    }
}
