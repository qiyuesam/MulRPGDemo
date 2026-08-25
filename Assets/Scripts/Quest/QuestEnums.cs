using System;

public enum QuestStatus
{
    Active,
    ReadyToTurnIn,
    Completed,
    Failed,
    Abandoned
}

public enum QuestObjectiveType
{
    CollectItem,
    KillTarget,
    Interact,
    ReachLocation,
    TalkToNpc,
    UseItem,
    EquipItem
}

public enum QuestEventType
{
    ItemAdded,
    ItemRemoved,
    ItemUsed,
    TargetKilled,
    Interacted,
    LocationReached,
    EquipmentChanged
}

public enum QuestRewardType
{
    Item,
    Experience,
    Flag
}
