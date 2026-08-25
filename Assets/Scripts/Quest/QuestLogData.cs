using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class QuestObjectiveState
{
    public string objectiveId;
    public int current;
    public int required;

    public bool IsComplete => current >= required;
}

[Serializable]
public class QuestState
{
    public int questId;
    public int definitionVersion;
    public QuestStatus status;
    public int repeatCount;
    public QuestObjectiveState[] objectives;

    public bool AllObjectivesComplete
    {
        get
        {
            if (objectives == null || objectives.Length == 0)
                return false;
            foreach (QuestObjectiveState objective in objectives)
            {
                if (objective == null || !objective.IsComplete)
                    return false;
            }
            return true;
        }
    }
}

[Serializable]
public class QuestLogData
{
    public const int MAX_ACTIVE_QUESTS = 20;
    public const int MAX_TOTAL_QUESTS = 128;
    public const int MAX_JSON_LENGTH = 32768;

    public QuestState[] quests;
    public int[] completedQuestIds;
    public string[] flags;

    public event Action OnChanged;

    [Serializable]
    private class SaveData
    {
        public int schemaVersion = 1;
        public QuestState[] quests;
        public int[] completedQuestIds;
        public string[] flags;
    }

    public void Initialize()
    {
        if (quests == null) quests = Array.Empty<QuestState>();
        if (completedQuestIds == null) completedQuestIds = Array.Empty<int>();
        if (flags == null) flags = Array.Empty<string>();
    }

    public IReadOnlyList<QuestState> QuestStates
    {
        get
        {
            Initialize();
            return quests;
        }
    }

    public QuestState GetState(int questId)
    {
        Initialize();
        foreach (QuestState state in quests)
        {
            if (state != null && state.questId == questId)
                return state;
        }
        return null;
    }

    public bool HasCompleted(int questId)
    {
        Initialize();
        foreach (int id in completedQuestIds)
            if (id == questId) return true;
        return false;
    }

    public bool HasFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return false;
        Initialize();
        foreach (string value in flags)
            if (value == flag) return true;
        return false;
    }

    public bool TryAccept(QuestDefinition definition, out string error, bool notify = true)
    {
        error = null;
        Initialize();

        if (definition == null || !definition.IsValid(out error))
            return false;
        QuestState existingState = GetState(definition.questId);
        int nextRepeatCount = 0;
        bool replaceCompletedRepeat = false;
        if (existingState != null && definition.repeatable && existingState.status == QuestStatus.Completed)
        {
            nextRepeatCount = existingState.repeatCount + 1;
            replaceCompletedRepeat = true;
        }
        else if (existingState != null)
        {
            error = "任务已经在任务日志中";
            return false;
        }
        if (HasCompleted(definition.questId) && !definition.repeatable)
        {
            error = "任务已经完成且不可重复";
            return false;
        }
        if (ActiveQuestCount >= MAX_ACTIVE_QUESTS)
        {
            error = "任务日志已满";
            return false;
        }

        if (definition.prerequisiteQuestIds != null)
        {
            foreach (int prerequisite in definition.prerequisiteQuestIds)
            {
                if (prerequisite > 0 && !HasCompleted(prerequisite))
                {
                    error = $"前置任务未完成: {prerequisite}";
                    return false;
                }
            }
        }

        var state = new QuestState
        {
            questId = definition.questId,
            definitionVersion = definition.version,
            status = QuestStatus.Active,
            repeatCount = nextRepeatCount,
            objectives = new QuestObjectiveState[definition.objectives.Length]
        };

        for (int i = 0; i < definition.objectives.Length; i++)
        {
            QuestObjectiveDefinition objective = definition.objectives[i];
            state.objectives[i] = new QuestObjectiveState
            {
                objectiveId = objective.objectiveId,
                current = 0,
                required = Mathf.Max(1, objective.requiredCount)
            };
        }

        if (replaceCompletedRepeat)
            RemoveState(definition.questId);

        var next = new QuestState[quests.Length + 1];
        Array.Copy(quests, next, quests.Length);
        next[next.Length - 1] = state;
        quests = next;
        if (notify) OnChanged?.Invoke();
        return true;
    }

    public bool TryAbandon(int questId, out string error, bool notify = true)
    {
        error = null;
        QuestState state = GetState(questId);
        if (state == null || state.status != QuestStatus.Active)
        {
            error = "任务不存在或当前不能放弃";
            return false;
        }

        RemoveState(questId);
        if (notify) OnChanged?.Invoke();
        return true;
    }

    public bool ApplyEvent(QuestDatabase database, QuestEvent questEvent, bool notify = true)
    {
        Initialize();
        bool changed = false;

        foreach (QuestState state in quests)
        {
            if (state == null || state.status != QuestStatus.Active)
                continue;

            QuestDefinition definition = database != null ? database.GetQuest(state.questId) : null;
            if (definition == null || state.objectives == null)
                continue;

            foreach (QuestObjectiveState progress in state.objectives)
            {
                QuestObjectiveDefinition objective = progress != null
                    ? definition.FindObjective(progress.objectiveId)
                    : null;
                if (progress == null || objective == null || progress.IsComplete)
                    continue;
                if (!MatchesEvent(objective, questEvent))
                    continue;

                int amount = Mathf.Max(1, questEvent.amount);
                int next = Mathf.Clamp(progress.current + amount, 0, progress.required);
                if (next != progress.current)
                {
                    progress.current = next;
                    changed = true;
                }
            }

            if (state.AllObjectivesComplete && state.status == QuestStatus.Active)
            {
                state.status = QuestStatus.ReadyToTurnIn;
                changed = true;
            }
        }

        if (changed && notify) OnChanged?.Invoke();
        return changed;
    }

    public bool SetCollectProgress(int questId, string objectiveId, int count)
    {
        QuestState state = GetState(questId);
        if (state == null || state.status == QuestStatus.Completed || state.objectives == null)
            return false;

        foreach (QuestObjectiveState objective in state.objectives)
        {
            if (objective == null || objective.objectiveId != objectiveId)
                continue;
            int next = Mathf.Clamp(count, 0, objective.required);
            if (objective.current == next)
                return false;
            objective.current = next;
            return true;
        }
        return false;
    }

    public bool MarkCompleted(int questId, bool notify = true)
    {
        QuestState state = GetState(questId);
        if (state == null || state.status != QuestStatus.ReadyToTurnIn)
            return false;

        state.status = QuestStatus.Completed;
        AddCompletedId(questId);
        if (notify) OnChanged?.Invoke();
        return true;
    }

    public bool RefreshReadyStatuses()
    {
        Initialize();
        bool changed = false;
        foreach (QuestState state in quests)
        {
            if (state == null) continue;
            bool complete = state.AllObjectivesComplete;
            if (complete && state.status == QuestStatus.Active)
            {
                state.status = QuestStatus.ReadyToTurnIn;
                changed = true;
            }
            else if (!complete && state.status == QuestStatus.ReadyToTurnIn)
            {
                state.status = QuestStatus.Active;
                changed = true;
            }
        }
        return changed;
    }

    public void RemoveState(int questId)
    {
        Initialize();
        var remaining = new List<QuestState>(quests.Length);
        foreach (QuestState state in quests)
        {
            if (state != null && state.questId != questId)
                remaining.Add(state);
        }
        quests = remaining.ToArray();
    }

    public void AddFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag) || HasFlag(flag)) return;
        var next = new string[flags.Length + 1];
        Array.Copy(flags, next, flags.Length);
        next[next.Length - 1] = flag;
        flags = next;
    }

    public void NotifyChanged() => OnChanged?.Invoke();

    public int ActiveQuestCount
    {
        get
        {
            Initialize();
            int count = 0;
            foreach (QuestState state in quests)
            {
                if (state != null &&
                    (state.status == QuestStatus.Active || state.status == QuestStatus.ReadyToTurnIn))
                    count++;
            }
            return count;
        }
    }

    public string ToJson()
    {
        Initialize();
        return JsonUtility.ToJson(new SaveData
        {
            quests = quests,
            completedQuestIds = completedQuestIds,
            flags = flags
        });
    }

    public bool TryFromJson(string json, QuestDatabase database, out string error, bool notify = true)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MAX_JSON_LENGTH)
        {
            error = "任务存档为空或超过长度限制";
            return false;
        }

        SaveData save;
        try
        {
            save = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception exception)
        {
            error = $"任务 JSON 解析失败: {exception.Message}";
            return false;
        }

        if (save == null || save.quests == null || save.completedQuestIds == null || save.flags == null ||
            save.quests.Length > MAX_TOTAL_QUESTS || save.completedQuestIds.Length > MAX_TOTAL_QUESTS)
        {
            error = "任务存档结构无效";
            return false;
        }

        var questIds = new HashSet<int>();
        foreach (QuestState state in save.quests)
        {
            if (state == null || state.questId <= 0 || !questIds.Add(state.questId))
            {
                error = "任务存档包含空任务或重复任务";
                return false;
            }

            QuestDefinition definition = database != null ? database.GetQuest(state.questId) : null;
            if (definition == null || state.objectives == null ||
                state.objectives.Length != definition.objectives.Length)
            {
                error = $"任务 {state.questId} 不存在或目标数量不匹配";
                return false;
            }

            var objectiveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuestObjectiveState progress in state.objectives)
            {
                QuestObjectiveDefinition objective = progress != null
                    ? definition.FindObjective(progress.objectiveId)
                    : null;
                if (progress == null || objective == null || !objectiveIds.Add(progress.objectiveId) ||
                    progress.required != objective.requiredCount ||
                    progress.current < 0 || progress.current > progress.required)
                {
                    error = $"任务 {state.questId} 的目标进度无效";
                    return false;
                }
            }
        }

        quests = save.quests;
        completedQuestIds = save.completedQuestIds;
        flags = save.flags;
        if (notify) OnChanged?.Invoke();
        return true;
    }

    private void AddCompletedId(int questId)
    {
        if (HasCompleted(questId)) return;
        var next = new int[completedQuestIds.Length + 1];
        Array.Copy(completedQuestIds, next, completedQuestIds.Length);
        next[next.Length - 1] = questId;
        completedQuestIds = next;
    }

    private static bool MatchesEvent(QuestObjectiveDefinition objective, QuestEvent questEvent)
    {
        if (objective == null) return false;
        switch (objective.type)
        {
            case QuestObjectiveType.KillTarget:
                return questEvent.type == QuestEventType.TargetKilled &&
                       SameKey(objective.targetKey, questEvent.targetKey);
            case QuestObjectiveType.Interact:
            case QuestObjectiveType.TalkToNpc:
                return questEvent.type == QuestEventType.Interacted &&
                       SameKey(objective.targetKey, questEvent.targetKey);
            case QuestObjectiveType.ReachLocation:
                return questEvent.type == QuestEventType.LocationReached &&
                       SameKey(objective.targetKey, questEvent.targetKey);
            case QuestObjectiveType.UseItem:
                return questEvent.type == QuestEventType.ItemUsed &&
                       objective.targetItemId == questEvent.itemId;
            case QuestObjectiveType.EquipItem:
                return questEvent.type == QuestEventType.EquipmentChanged &&
                       (objective.targetItemId <= 0 || objective.targetItemId == questEvent.itemId);
            default:
                return false;
        }
    }

    private static bool SameKey(string expected, string actual)
    {
        return !string.IsNullOrEmpty(expected) &&
               string.Equals(expected.Trim(), actual?.Trim(), StringComparison.Ordinal);
    }
}
