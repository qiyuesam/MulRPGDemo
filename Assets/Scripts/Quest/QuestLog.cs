using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>挂在 Player.prefab 上的个人任务日志。</summary>
public class QuestLog : NetworkBehaviour
{
    [Header("任务数据（为空时从场景 QuestManager 获取）")]
    [SerializeField] private QuestDatabase questDatabase;
    [SerializeField] private ItemDatabase itemDatabase;

    public QuestLogData Data { get; } = new QuestLogData();
    public event Action OnQuestLogChanged;

    private const string SAVE_FILE = "player_quests.sav";
    private const float MIN_REQUEST_INTERVAL = 0.05f;
    private double nextRequestTime;
    private bool eventsBound;

    public bool IsAuthoritative => !IsSpawned || IsServer;
    public QuestDatabase Database => questDatabase != null ? questDatabase : QuestManager.Instance?.Database;
    public ItemDatabase Items => itemDatabase != null ? itemDatabase : QuestManager.Instance?.Items;

    private void Awake()
    {
        Data.Initialize();
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return;

        if (!IsSpawned)
        {
            ResolveDatabases();
            LoadFromDisk();
            BindEvents();
            UIManager.Instance?.BindQuestLog(this);
        }
    }

    public override void OnNetworkSpawn()
    {
        ResolveDatabases();
        UnbindEvents();
        BindEvents();

        if (IsOwner)
        {
            LoadAndApplySave();
            UIManager.Instance?.BindQuestLog(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            SaveToDisk();
        UnbindEvents();
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        UnbindEvents();
        UIManager.Instance?.UnbindQuestLog(this);
    }

    public QuestState GetState(int questId) => Data.GetState(questId);
    public bool HasCompleted(int questId) => Data.HasCompleted(questId);

    public void RequestAcceptQuest(int questId, string giverId)
    {
        if (!IsSpawned)
        {
            AcceptQuestInternal(questId, giverId);
            return;
        }

        if (IsServer)
            AcceptQuestInternal(questId, giverId);
        else if (IsOwner && CanSubmitRequest())
            AcceptQuestServerRpc(questId, giverId ?? string.Empty);
    }

    public void RequestAbandonQuest(int questId)
    {
        if (!IsSpawned)
        {
            AbandonQuestInternal(questId);
            return;
        }

        if (IsServer)
            AbandonQuestInternal(questId);
        else if (IsOwner && CanSubmitRequest())
            AbandonQuestServerRpc(questId);
    }

    public void RequestTurnInQuest(int questId, string giverId)
    {
        if (!IsSpawned)
        {
            TurnInQuestInternal(questId, giverId);
            return;
        }

        if (IsServer)
            TurnInQuestInternal(questId, giverId);
        else if (IsOwner && CanSubmitRequest())
            TurnInQuestServerRpc(questId, giverId ?? string.Empty);
    }

    public void RequestClaimItemSource(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId)) return;
        if (!IsSpawned)
        {
            QuestManager.Instance?.TryClaimItemSource(this, sourceId);
            return;
        }

        if (IsServer)
            QuestManager.Instance?.TryClaimItemSource(this, sourceId);
        else if (IsOwner && CanSubmitRequest())
            ClaimItemSourceServerRpc(sourceId);
    }

    public void RequestRecordInteraction(string targetKey)
    {
        if (string.IsNullOrEmpty(targetKey)) return;
        if (!IsSpawned)
        {
            QuestManager.Instance?.TryRecordInteraction(this, targetKey);
            return;
        }

        if (IsServer)
            QuestManager.Instance?.TryRecordInteraction(this, targetKey);
        else if (IsOwner && CanSubmitRequest())
            RecordInteractionServerRpc(targetKey);
    }

    internal void ApplyEventFromServer(QuestEvent questEvent)
    {
        if (!IsAuthoritative || questEvent.actor != gameObject)
            return;

        bool changed = Data.ApplyEvent(Database, questEvent, notify: false);
        if (questEvent.type == QuestEventType.ItemAdded || questEvent.type == QuestEventType.ItemRemoved)
            changed |= RefreshCollectObjectives(notify: false);

        if (changed)
            NotifyChanged();
    }

    internal bool AcceptQuestInternal(int questId, string giverId)
    {
        if (!ValidateGiver(giverId, questId, out _))
            return false;

        QuestDefinition definition = Database != null ? Database.GetQuest(questId) : null;
        if (!Data.TryAccept(definition, out string error, notify: false))
        {
            Debug.LogWarning($"[QuestLog] 接取任务失败: {error}", this);
            return false;
        }

        RefreshCollectObjectives(notify: false);
        NotifyChanged();
        return true;
    }

    internal bool AbandonQuestInternal(int questId)
    {
        if (!Data.TryAbandon(questId, out string error, notify: false))
        {
            Debug.LogWarning($"[QuestLog] 放弃任务失败: {error}", this);
            return false;
        }

        NotifyChanged();
        return true;
    }

    internal bool TurnInQuestInternal(int questId, string giverId)
    {
        if (!ValidateGiver(giverId, questId, out _))
            return false;

        QuestState state = Data.GetState(questId);
        QuestDefinition definition = Database != null ? Database.GetQuest(questId) : null;
        if (state == null || definition == null || state.status != QuestStatus.ReadyToTurnIn)
            return false;

        if (QuestManager.Instance == null)
        {
            Debug.LogWarning("[QuestLog] 场景缺少 QuestManager", this);
            return false;
        }

        if (!QuestManager.Instance.TryGrantRewards(this, definition, out string error))
        {
            Debug.LogWarning($"[QuestLog] 领取奖励失败: {error}", this);
            return false;
        }

        Data.MarkCompleted(questId, notify: false);
        RefreshCollectObjectives(notify: false);
        NotifyChanged();
        return true;
    }

    public void ForceSave() => SaveToDisk();

    private bool ValidateGiver(string giverId, int questId, out QuestGiver giver)
    {
        if (QuestManager.Instance == null)
        {
            giver = null;
            return false;
        }

        return QuestManager.Instance.ValidateGiver(gameObject, giverId, questId, out giver);
    }

    private bool RefreshCollectObjectives(bool notify)
    {
        Inventory inventory = GetComponent<Inventory>();
        if (inventory == null || Database == null)
            return false;

        bool changed = false;
        foreach (QuestState state in Data.QuestStates)
        {
            if (state == null || state.status == QuestStatus.Completed || state.objectives == null)
                continue;

            QuestDefinition definition = Database.GetQuest(state.questId);
            if (definition == null) continue;

            foreach (QuestObjectiveDefinition definitionObjective in definition.objectives)
            {
                if (definitionObjective == null || definitionObjective.type != QuestObjectiveType.CollectItem)
                    continue;

                int count = inventory.data.CountItem(definitionObjective.targetItemId);
                changed |= Data.SetCollectProgress(state.questId, definitionObjective.objectiveId, count);
            }

        }

        changed |= Data.RefreshReadyStatuses();

        if (changed && notify)
            NotifyChanged();
        return changed;
    }

    private void NotifyChanged()
    {
        Data.NotifyChanged();
    }

    private void BindEvents()
    {
        if (eventsBound) return;
        Data.OnChanged += OnDataChanged;
        eventsBound = true;
    }

    private void UnbindEvents()
    {
        if (!eventsBound) return;
        Data.OnChanged -= OnDataChanged;
        eventsBound = false;
    }

    private void OnDataChanged()
    {
        if (IsAuthoritative)
        {
            if (IsSpawned && IsServer)
            {
                ClientRpcParams targets = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { OwnerClientId }
                    }
                };
                SyncToOwnerClientRpc(Data.ToJson(), targets);
            }
            if (!IsSpawned || IsOwner)
                SaveToDisk();
        }
        OnQuestLogChanged?.Invoke();
    }

    private void ResolveDatabases()
    {
        if (QuestManager.Instance == null) return;
        if (questDatabase == null) questDatabase = QuestManager.Instance.Database;
        if (itemDatabase == null) itemDatabase = QuestManager.Instance.Items;
    }

    private void LoadFromDisk()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (string.IsNullOrEmpty(json)) return;
        if (!Data.TryFromJson(json, Database, out string error, notify: false))
            Debug.LogWarning($"[QuestLog] 忽略无效本地任务存档: {error}", this);
    }

    private void LoadAndApplySave()
    {
        string json = SaveManager.Load(SAVE_FILE);
        if (string.IsNullOrEmpty(json)) return;

        if (IsServer)
        {
            if (!Data.TryFromJson(json, Database, out string error, notify: false))
                Debug.LogWarning($"[QuestLog] Host 任务存档无效: {error}", this);
            else
                NotifyChanged();
        }
        else
        {
            Debug.Log("[QuestLog] 客户端忽略本地任务存档，等待服务器快照");
        }
    }

    private void SaveToDisk()
    {
        if (!IsAuthoritative || (IsSpawned && !IsOwner && !IsServer)) return;
        SaveManager.Save(SAVE_FILE, Data.ToJson());
    }

    private bool CanSubmitRequest()
    {
        double now = Time.unscaledTimeAsDouble;
        if (now < nextRequestTime) return false;
        nextRequestTime = now + MIN_REQUEST_INTERVAL;
        return true;
    }

    [ServerRpc(RequireOwnership = true)]
    private void AcceptQuestServerRpc(int questId, string giverId)
    {
        if (CanSubmitRequest()) AcceptQuestInternal(questId, giverId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void AbandonQuestServerRpc(int questId)
    {
        if (CanSubmitRequest()) AbandonQuestInternal(questId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void TurnInQuestServerRpc(int questId, string giverId)
    {
        if (CanSubmitRequest()) TurnInQuestInternal(questId, giverId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void ClaimItemSourceServerRpc(string sourceId)
    {
        if (CanSubmitRequest()) QuestManager.Instance?.TryClaimItemSource(this, sourceId);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RecordInteractionServerRpc(string targetKey)
    {
        if (CanSubmitRequest()) QuestManager.Instance?.TryRecordInteraction(this, targetKey);
    }

    [ClientRpc]
    private void SyncToOwnerClientRpc(string json, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer || !IsOwner) return;
        if (!Data.TryFromJson(json, Database, out string error, notify: false))
        {
            Debug.LogError($"[QuestLog] 收到无效任务快照: {error}", this);
            return;
        }
        OnQuestLogChanged?.Invoke();
    }
}
