using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 左侧任务列表、右侧任务详情的 uGUI 任务面板。
/// J 打开任务日志，与任务提供者交互时显示其委托。
/// </summary>
public class QuestUI : MonoBehaviour
{
    private sealed class QuestListItem
    {
        public int questId;
        public bool fromGiver;
        public QuestDefinition definition;
        public QuestState state;
    }

    public static QuestUI Instance { get; private set; }

    [Header("面板")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text giverText;
    [SerializeField] private Button closeButton;

    [Header("左侧任务列表")]
    [SerializeField] private Transform listContentRoot;
    [SerializeField] private QuestEntryUI entryPrefab;

    [Header("右侧任务详情")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private TMP_Text detailTitleText;
    [SerializeField] private TMP_Text detailDescriptionText;
    [SerializeField] private TMP_Text detailObjectivesText;
    [SerializeField] private TMP_Text detailStatusText;
    [SerializeField] private Button detailActionButton;
    [SerializeField] private TMP_Text detailActionButtonText;

    private readonly List<QuestEntryUI> entries = new List<QuestEntryUI>();
    private readonly List<QuestListItem> items = new List<QuestListItem>();
    private QuestLog log;
    private QuestGiver currentGiver;
    private int selectedQuestId = -1;
    private bool selectedFromGiver;
    private bool visible;

    public bool IsVisible => visible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        SetPanelActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.J) || log == null)
            return;

        if (visible)
        {
            Hide();
            return;
        }

        currentGiver = null;
        selectedQuestId = -1;
        Show();
    }

    private void OnDestroy()
    {
        UnsubscribeLog();

        if (closeButton != null)
            closeButton.onClick.RemoveListener(Hide);

        ClearDetailAction();

        if (Instance == this)
            Instance = null;
    }

    public void Bind(QuestLog questLog)
    {
        if (log == questLog)
        {
            if (visible)
                Refresh();
            return;
        }

        UnsubscribeLog();
        log = questLog;
        selectedQuestId = -1;

        if (log == null)
        {
            Hide();
            return;
        }

        log.OnQuestLogChanged += Refresh;

        if (visible)
            Refresh();
    }

    public void Unbind(QuestLog questLog)
    {
        if (log == questLog)
            Bind(null);
    }

    public void ShowGiver(QuestGiver giver, QuestLog questLog)
    {
        Bind(questLog);
        if (log == null)
            return;

        currentGiver = giver;
        selectedQuestId = -1;
        Show();
    }

    public void Hide()
    {
        visible = false;
        currentGiver = null;
        selectedQuestId = -1;
        ClearDetailAction();
        SetPanelActive(false);
        UIManager.Instance?.RefreshCursor();
    }

    public void Refresh()
    {
        ClearEntries();
        items.Clear();

        if (!visible || log == null || listContentRoot == null || entryPrefab == null)
            return;

        UpdateHeader();
        BuildItems();

        if (items.Count == 0)
        {
            QuestEntryUI emptyEntry = Instantiate(entryPrefab, listContentRoot);
            emptyEntry.gameObject.SetActive(true);
            emptyEntry.Configure("暂无任务", string.Empty, null);
            emptyEntry.SetSelected(false);
            entries.Add(emptyEntry);
            HideDetail();
            RebuildListLayout();
            return;
        }

        int selectedIndex = FindSelectedIndex();
        if (selectedIndex < 0)
            selectedIndex = 0;

        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            QuestListItem item = items[i];
            QuestEntryUI entry = Instantiate(entryPrefab, listContentRoot);
            entry.gameObject.SetActive(true);
            entry.Configure(
                GetListTitle(item),
                GetStatusText(item.state != null ? item.state.status : (QuestStatus?)null),
                () => SelectIndex(index));
            entries.Add(entry);
        }

        SelectIndex(selectedIndex);
        RebuildListLayout();
    }

    private void BuildItems()
    {
        var addedQuestIds = new HashSet<int>();

        if (currentGiver != null)
        {
            QuestDefinition[] offered = currentGiver.OfferedQuests;
            if (offered != null)
            {
                foreach (QuestDefinition definition in offered)
                {
                    if (definition == null || !addedQuestIds.Add(definition.questId))
                        continue;

                    items.Add(new QuestListItem
                    {
                        questId = definition.questId,
                        fromGiver = true,
                        definition = definition,
                        state = log.GetState(definition.questId)
                    });
                }
            }
        }

        foreach (QuestState state in log.Data.QuestStates)
        {
            if (state == null || !addedQuestIds.Add(state.questId))
                continue;

            items.Add(new QuestListItem
            {
                questId = state.questId,
                fromGiver = false,
                definition = log.Database != null ? log.Database.GetQuest(state.questId) : null,
                state = state
            });
        }
    }

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= items.Count)
        {
            HideDetail();
            return;
        }

        QuestListItem item = items[index];
        selectedQuestId = item.questId;
        selectedFromGiver = item.fromGiver;

        for (int i = 0; i < entries.Count; i++)
            entries[i].SetSelected(i == index);

        ShowDetail(item);
    }

    private void ShowDetail(QuestListItem item)
    {
        if (detailPanel != null)
            detailPanel.SetActive(true);

        string title = item.definition != null ? item.definition.title : "未知任务";
        string description = item.definition != null
            ? item.definition.description
            : "未找到对应的任务定义。";

        if (detailTitleText != null)
            detailTitleText.text = title;

        if (detailDescriptionText != null)
            detailDescriptionText.text = string.IsNullOrWhiteSpace(description)
                ? "暂无任务描述。"
                : description.Trim();

        if (detailObjectivesText != null)
            detailObjectivesText.text = BuildObjectivesText(item.definition, item.state);

        if (detailStatusText != null)
            detailStatusText.text =
                $"状态：{GetStatusText(item.state != null ? item.state.status : (QuestStatus?)null)}";

        ConfigureDetailAction(item);
    }

    private void ConfigureDetailAction(QuestListItem item)
    {
        ClearDetailAction();

        if (detailActionButton == null)
            return;

        string actionLabel = string.Empty;
        Action action = null;
        QuestState state = item.state;

        bool canRepeat = item.definition != null &&
                         item.definition.repeatable &&
                         state != null &&
                         state.status == QuestStatus.Completed;

        bool canAccept = item.fromGiver &&
                         item.definition != null &&
                         ((state == null &&
                           (!log.HasCompleted(item.questId) || item.definition.repeatable)) ||
                          canRepeat);

        string giverId = currentGiver != null ? currentGiver.GiverId : string.Empty;
        int questId = item.questId;

        if (canAccept)
        {
            actionLabel = "接取任务";
            action = () => log.RequestAcceptQuest(questId, giverId);
        }
        else if (item.fromGiver &&
                 state != null &&
                 state.status == QuestStatus.ReadyToTurnIn)
        {
            actionLabel = "交付并领取奖励";
            action = () => log.RequestTurnInQuest(questId, giverId);
        }
        else if (state != null && state.status == QuestStatus.Active)
        {
            actionLabel = "放弃任务";
            action = () => log.RequestAbandonQuest(questId);
        }

        bool hasAction = action != null;
        detailActionButton.gameObject.SetActive(hasAction);

        if (!hasAction)
            return;

        if (detailActionButtonText != null)
            detailActionButtonText.text = actionLabel;

        detailActionButton.onClick.AddListener(() => action());
    }

    private string BuildObjectivesText(QuestDefinition definition, QuestState state)
    {
        if (definition == null || definition.objectives == null || definition.objectives.Length == 0)
            return "暂无任务目标。";

        var builder = new StringBuilder();

        foreach (QuestObjectiveDefinition objective in definition.objectives)
        {
            if (objective == null)
                continue;

            QuestObjectiveState objectiveState = FindObjectiveState(state, objective.objectiveId);
            int current = objectiveState != null ? objectiveState.current : 0;
            int required = objectiveState != null
                ? objectiveState.required
                : objective.requiredCount;

            string label = !string.IsNullOrWhiteSpace(objective.displayText)
                ? objective.displayText
                : objective.objectiveId;

            builder.Append("• ")
                   .Append(label)
                   .Append("  ")
                   .Append(current)
                   .Append('/')
                   .Append(required)
                   .AppendLine();
        }

        return builder.Length > 0
            ? builder.ToString().TrimEnd()
            : "暂无任务目标。";
    }

    private static QuestObjectiveState FindObjectiveState(QuestState state, string objectiveId)
    {
        if (state == null || state.objectives == null)
            return null;

        foreach (QuestObjectiveState objective in state.objectives)
        {
            if (objective != null && objective.objectiveId == objectiveId)
                return objective;
        }

        return null;
    }

    private int FindSelectedIndex()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].questId == selectedQuestId &&
                items[i].fromGiver == selectedFromGiver)
                return i;
        }

        return -1;
    }

    private void UpdateHeader()
    {
        if (titleText != null)
            titleText.text = currentGiver != null ? "任务" : "任务日志";

        if (giverText == null)
            return;

        bool hasGiver = currentGiver != null;
        giverText.gameObject.SetActive(hasGiver);
        giverText.text = hasGiver
            ? $"任务提供者：{currentGiver.GiverId}"
            : string.Empty;
    }

    private string GetListTitle(QuestListItem item)
    {
        string title = item.definition != null ? item.definition.title : "未知任务";
        return item.fromGiver ? $"[委托] {title}" : title;
    }

    private void Show()
    {
        visible = true;
        SetPanelActive(true);
        Refresh();
        UIManager.Instance?.RefreshCursor();
    }

    private void HideDetail()
    {
        ClearDetailAction();

        if (detailPanel != null)
            detailPanel.SetActive(false);
    }

    private void ClearDetailAction()
    {
        if (detailActionButton != null)
            detailActionButton.onClick.RemoveAllListeners();
    }

    private void ClearEntries()
    {
        foreach (QuestEntryUI entry in entries)
        {
            if (entry == null)
                continue;

            entry.gameObject.SetActive(false);
            Destroy(entry.gameObject);
        }

        entries.Clear();
    }

    private void RebuildListLayout()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContentRoot as RectTransform);
    }

    private void UnsubscribeLog()
    {
        if (log != null)
            log.OnQuestLogChanged -= Refresh;
    }

    private void SetPanelActive(bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }

    private static string GetStatusText(QuestStatus? status)
    {
        if (!status.HasValue)
            return "可接取";

        switch (status.Value)
        {
            case QuestStatus.Active:
                return "进行中";
            case QuestStatus.ReadyToTurnIn:
                return "可交付";
            case QuestStatus.Completed:
                return "已完成";
            case QuestStatus.Failed:
                return "已失败";
            case QuestStatus.Abandoned:
                return "已放弃";
            default:
                return status.Value.ToString();
        }
    }
}

