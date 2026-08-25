using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "QuestDefinition", menuName = "Quest/Quest Definition")]
public class QuestDefinition : ScriptableObject
{
    [Header("基础信息")]
    [Min(1)] public int questId = 1;
    [Min(1)] public int version = 1;
    public string title = "新任务";

    [TextArea(2, 6)]
    public string description;

    public bool repeatable;
    public int[] prerequisiteQuestIds;

    [Header("目标与奖励")]
    public QuestObjectiveDefinition[] objectives;
    public QuestRewardDefinition[] rewards;

    public bool IsValid(out string error)
    {
        error = null;
        if (questId <= 0)
        {
            error = "任务 ID 必须大于 0";
            return false;
        }

        if (objectives == null || objectives.Length == 0)
        {
            error = "任务至少需要一个目标";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < objectives.Length; i++)
        {
            QuestObjectiveDefinition objective = objectives[i];
            if (objective == null)
            {
                error = $"目标 {i} 为空";
                return false;
            }

            string objectiveId = objective.objectiveId?.Trim();
            if (string.IsNullOrEmpty(objectiveId) || !ids.Add(objectiveId))
            {
                error = $"目标 {i} 的 objectiveId 为空或重复";
                return false;
            }

            if (objective.requiredCount <= 0)
            {
                error = $"目标 {objectiveId} 的数量必须大于 0";
                return false;
            }
        }

        if (rewards != null)
        {
            for (int i = 0; i < rewards.Length; i++)
            {
                QuestRewardDefinition reward = rewards[i];
                if (reward == null)
                {
                    error = $"奖励 {i} 为空";
                    return false;
                }

                if (reward.type == QuestRewardType.Item &&
                    (reward.itemId <= 0 || reward.count <= 0))
                {
                    error = $"物品奖励 {i} 的物品 ID 或数量无效";
                    return false;
                }
            }
        }

        return true;
    }

    public QuestObjectiveDefinition FindObjective(string objectiveId)
    {
        if (objectives == null || string.IsNullOrEmpty(objectiveId))
            return null;

        foreach (QuestObjectiveDefinition objective in objectives)
        {
            if (objective != null && objective.objectiveId == objectiveId)
                return objective;
        }

        return null;
    }

    private void OnValidate()
    {
        version = Mathf.Max(1, version);
        if (objectives != null)
        {
            foreach (QuestObjectiveDefinition objective in objectives)
            {
                if (objective == null) continue;
                objective.objectiveId = objective.objectiveId?.Trim();
                objective.requiredCount = Mathf.Max(1, objective.requiredCount);
            }
        }

        if (rewards != null)
        {
            foreach (QuestRewardDefinition reward in rewards)
            {
                if (reward == null) continue;
                reward.itemId = Mathf.Max(0, reward.itemId);
                reward.count = Mathf.Max(0, reward.count);
                reward.experience = Mathf.Max(0, reward.experience);
                reward.flag = reward.flag?.Trim();
            }
        }
    }
}

[Serializable]
public class QuestObjectiveDefinition
{
    [Tooltip("存档使用的稳定 ID，修改目标顺序时不要改变它")]
    public string objectiveId = "objective_1";

    public QuestObjectiveType type;

    [Tooltip("CollectItem、UseItem、EquipItem 使用物品 ID")]
    public int targetItemId;

    [Tooltip("击杀目标、交互对象或地点使用稳定字符串")]
    public string targetKey;

    [Min(1)] public int requiredCount = 1;

    [Tooltip("交付任务时是否从背包扣除收集物品")]
    public bool consumeOnTurnIn;

    [TextArea(1, 3)] public string displayText;
}

[Serializable]
public class QuestRewardDefinition
{
    public QuestRewardType type = QuestRewardType.Item;
    public int itemId;
    [Min(1)] public int count = 1;
    [Min(0)] public int experience;
    public string flag;
}
