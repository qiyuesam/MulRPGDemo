using System;
using UnityEngine;

[Serializable]
public struct QuestEvent
{
    public QuestEventType type;
    public GameObject actor;
    public int itemId;
    public int amount;
    public string targetKey;

    public static QuestEvent ItemAdded(GameObject actor, int itemId, int amount)
    {
        return new QuestEvent
        {
            type = QuestEventType.ItemAdded,
            actor = actor,
            itemId = itemId,
            amount = Mathf.Max(0, amount)
        };
    }

    public static QuestEvent ItemUsed(GameObject actor, int itemId)
    {
        return new QuestEvent
        {
            type = QuestEventType.ItemUsed,
            actor = actor,
            itemId = itemId,
            amount = 1
        };
    }

    public static QuestEvent TargetKilled(GameObject actor, string targetKey)
    {
        return new QuestEvent
        {
            type = QuestEventType.TargetKilled,
            actor = actor,
            targetKey = targetKey,
            amount = 1
        };
    }

    public static QuestEvent Interacted(GameObject actor, string targetKey)
    {
        return new QuestEvent
        {
            type = QuestEventType.Interacted,
            actor = actor,
            targetKey = targetKey,
            amount = 1
        };
    }

    public static QuestEvent LocationReached(GameObject actor, string targetKey)
    {
        return new QuestEvent
        {
            type = QuestEventType.LocationReached,
            actor = actor,
            targetKey = targetKey,
            amount = 1
        };
    }
}
