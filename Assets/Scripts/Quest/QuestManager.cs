using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景级任务服务。它只路由权威事件和校验场景交互，不保存玩家个人进度。
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    [Header("任务数据")]
    [SerializeField] private QuestDatabase questDatabase;
    [SerializeField] private ItemDatabase itemDatabase;

    private readonly Dictionary<string, QuestGiver> givers =
        new Dictionary<string, QuestGiver>(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestItemSource> itemSources =
        new Dictionary<string, QuestItemSource>(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestInteractionTarget> interactionTargets =
        new Dictionary<string, QuestInteractionTarget>(StringComparer.Ordinal);

    public QuestDatabase Database => questDatabase;
    public ItemDatabase Items => itemDatabase;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        questDatabase?.Init();
        itemDatabase?.Init();
        RebuildGiverRegistry();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Publish(QuestEvent questEvent)
    {
        Instance?.RouteEvent(questEvent);
    }

    public QuestDefinition GetQuest(int questId)
        => questDatabase != null ? questDatabase.GetQuest(questId) : null;

    public void RegisterGiver(QuestGiver giver)
    {
        if (giver == null || string.IsNullOrEmpty(giver.GiverId)) return;
        givers[giver.GiverId] = giver;
    }

    public void UnregisterGiver(QuestGiver giver)
    {
        if (giver == null || string.IsNullOrEmpty(giver.GiverId)) return;
        if (givers.TryGetValue(giver.GiverId, out QuestGiver registered) && registered == giver)
            givers.Remove(giver.GiverId);
    }

    public bool TryGetGiver(string giverId, out QuestGiver giver)
    {
        if (!string.IsNullOrEmpty(giverId) && givers.TryGetValue(giverId, out giver) &&
            giver != null && giver.isActiveAndEnabled)
            return true;

        giver = null;
        return false;
    }

    public void RegisterItemSource(QuestItemSource source)
    {
        if (source == null || string.IsNullOrEmpty(source.SourceId)) return;
        itemSources[source.SourceId] = source;
    }

    public void UnregisterItemSource(QuestItemSource source)
    {
        if (source == null || string.IsNullOrEmpty(source.SourceId)) return;
        if (itemSources.TryGetValue(source.SourceId, out QuestItemSource registered) && registered == source)
            itemSources.Remove(source.SourceId);
    }

    public bool TryClaimItemSource(QuestLog log, string sourceId)
    {
        if (log == null || !log.IsAuthoritative || string.IsNullOrEmpty(sourceId) ||
            !itemSources.TryGetValue(sourceId, out QuestItemSource source) || source == null ||
            !source.isActiveAndEnabled)
            return false;

        float distance = source.MaxInteractDistance;
        if ((source.transform.position - log.transform.position).sqrMagnitude > distance * distance)
            return false;

        return source.GrantTo(log);
    }

    public bool ValidateGiver(GameObject interactor, string giverId, int questId, out QuestGiver giver,
                              float maxDistance = 4.5f)
    {
        if (!ValidateGiver(interactor, giverId, out giver, maxDistance) || !giver.OffersQuest(questId))
            return false;

        return true;
    }

    public bool ValidateGiver(GameObject interactor, string giverId, out QuestGiver giver,
                              float maxDistance = 4.5f)
    {
        if (!TryGetGiver(giverId, out giver) || interactor == null)
            return false;
        return (giver.transform.position - interactor.transform.position).sqrMagnitude <=
               maxDistance * maxDistance;
    }

    public void RegisterInteractionTarget(QuestInteractionTarget target)
    {
        if (target == null || string.IsNullOrEmpty(target.TargetKey)) return;
        interactionTargets[target.TargetKey] = target;
    }

    public void UnregisterInteractionTarget(QuestInteractionTarget target)
    {
        if (target == null || string.IsNullOrEmpty(target.TargetKey)) return;
        if (interactionTargets.TryGetValue(target.TargetKey, out QuestInteractionTarget registered) &&
            registered == target)
            interactionTargets.Remove(target.TargetKey);
    }

    public bool TryRecordInteraction(QuestLog log, string targetKey)
    {
        if (log == null || !log.IsAuthoritative || string.IsNullOrEmpty(targetKey))
            return false;

        bool valid = ValidateGiver(log.gameObject, targetKey, out _);
        if (!valid && interactionTargets.TryGetValue(targetKey, out QuestInteractionTarget target) &&
            target != null && target.isActiveAndEnabled)
        {
            float distance = target.MaxInteractDistance;
            valid = (target.transform.position - log.transform.position).sqrMagnitude <= distance * distance;
        }

        if (!valid) return false;
        log.ApplyEventFromServer(QuestEvent.Interacted(log.gameObject, targetKey));
        return true;
    }

    public bool TryGrantRewards(QuestLog log, QuestDefinition definition, out string error)
    {
        error = null;
        if (log == null || definition == null)
        {
            error = "任务或玩家无效";
            return false;
        }

        Inventory inventory = log.GetComponent<Inventory>();
        if (inventory == null || itemDatabase == null)
        {
            error = "玩家缺少背包或物品数据库";
            return false;
        }

        var itemRewards = new List<QuestRewardDefinition>();
        if (definition.rewards != null)
        {
            foreach (QuestRewardDefinition reward in definition.rewards)
            {
                if (reward == null) continue;
                if (reward.type == QuestRewardType.Item)
                {
                    if (itemDatabase.GetItem(reward.itemId) == null || reward.count <= 0)
                    {
                        error = $"奖励物品无效: {reward.itemId}";
                        return false;
                    }
                    itemRewards.Add(reward);
                }
            }
        }

        var consumedItems = new List<QuestRewardDefinition>();
        // 先验证所有奖励，之后才做扣除与发放，避免部分完成。
        QuestState questState = log.Data.GetState(definition.questId);
        if (questState == null || questState.status != QuestStatus.ReadyToTurnIn)
        {
            error = "任务状态不存在或尚未完成";
            return false;
        }

        foreach (QuestObjectiveDefinition objective in definition.objectives)
        {
            if (objective == null || objective.type != QuestObjectiveType.CollectItem ||
                !objective.consumeOnTurnIn || objective.targetItemId <= 0)
                continue;

            int required = Mathf.Max(0, objective.requiredCount);
            if (inventory.data.CountItem(objective.targetItemId) < required)
            {
                error = $"缺少任务物品: {objective.targetItemId}";
                return false;
            }

            consumedItems.Add(new QuestRewardDefinition
            {
                type = QuestRewardType.Item,
                itemId = objective.targetItemId,
                count = required
            });
        }

        if (!CanFitRewards(inventory.data, consumedItems, itemRewards))
        {
            error = "背包空间不足，无法领取任务奖励";
            return false;
        }

        inventory.data.BeginBatch();
        try
        {
            foreach (QuestObjectiveDefinition objective in definition.objectives)
            {
                if (objective == null || objective.type != QuestObjectiveType.CollectItem ||
                    !objective.consumeOnTurnIn || objective.targetItemId <= 0)
                    continue;
                inventory.data.RemoveItemById(objective.targetItemId, objective.requiredCount);
            }

            foreach (QuestRewardDefinition reward in itemRewards)
                inventory.data.AddItem(itemDatabase, reward.itemId, reward.count);
        }
        finally
        {
            inventory.data.EndBatch();
        }

        foreach (QuestRewardDefinition reward in definition.rewards ?? Array.Empty<QuestRewardDefinition>())
        {
            if (reward == null) continue;
            if (reward.type == QuestRewardType.Flag && !string.IsNullOrEmpty(reward.flag))
                log.Data.AddFlag(reward.flag);
            // 当前 PlayerStats 没有经验字段，经验奖励先保留在定义中并记录日志。
            if (reward.type == QuestRewardType.Experience && reward.experience > 0)
                Debug.Log($"[Quest] 任务 {definition.questId} 经验奖励待接入: {reward.experience}");
        }

        return true;
    }

    private bool CanFitRewards(InventoryData inventory,
                               List<QuestRewardDefinition> consumes,
                               List<QuestRewardDefinition> rewards)
    {
        string snapshot = inventory.ToJson();
        var simulation = new InventoryData();
        simulation.Initialize();
        if (!simulation.TryFromJson(snapshot, itemDatabase, out _, notify: false))
            return false;

        foreach (QuestRewardDefinition consume in consumes)
        {
            if (!simulation.RemoveItemById(consume.itemId, consume.count))
                return false;
        }

        foreach (QuestRewardDefinition reward in rewards)
        {
            if (!simulation.CanAddItem(itemDatabase, reward.itemId, reward.count))
                return false;
            if (!simulation.AddItem(itemDatabase, reward.itemId, reward.count))
                return false;
        }
        return true;
    }

    private void RouteEvent(QuestEvent questEvent)
    {
        if (questEvent.actor == null) return;
        QuestLog log = questEvent.actor.GetComponent<QuestLog>();
        if (log == null || !log.IsAuthoritative)
            return;
        log.ApplyEventFromServer(questEvent);
    }

    private void RebuildGiverRegistry()
    {
        givers.Clear();
        QuestGiver[] sceneGivers = FindObjectsOfType<QuestGiver>(true);
        foreach (QuestGiver giver in sceneGivers)
            RegisterGiver(giver);

        itemSources.Clear();
        QuestItemSource[] sceneSources = FindObjectsOfType<QuestItemSource>(true);
        foreach (QuestItemSource source in sceneSources)
            RegisterItemSource(source);

        interactionTargets.Clear();
        QuestInteractionTarget[] sceneTargets = FindObjectsOfType<QuestInteractionTarget>(true);
        foreach (QuestInteractionTarget target in sceneTargets)
            RegisterInteractionTarget(target);
    }
}
