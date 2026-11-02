using UnityEngine;

/// <summary>任务提供者/交付者，复用现有 InteractionSystem。</summary>
public class QuestGiver : MonoBehaviour, IInteractable
{
    [SerializeField] private string giverId = "giver_1";
    [SerializeField] private string interactionMessage = "查看任务";
    [SerializeField] private QuestDefinition[] offeredQuests;

    public string GiverId => giverId;
    public QuestDefinition[] OfferedQuests => offeredQuests;

    private void OnEnable() => QuestManager.Instance?.RegisterGiver(this);
    private void Start() => QuestManager.Instance?.RegisterGiver(this);
    private void OnDisable() => QuestManager.Instance?.UnregisterGiver(this);

    public bool CanInteract(GameObject interactor)
        => enabled && gameObject.activeInHierarchy && interactor != null &&
           !string.IsNullOrEmpty(giverId);

    public void Interact(GameObject interactor)
    {
        QuestLog log = interactor != null ? interactor.GetComponent<QuestLog>() : null;
        if (log == null) return;

        log.RequestRecordInteraction(giverId);
        UIManager.Instance?.ShowGiver(this, log);
        if (UIManager.Instance == null && !string.IsNullOrEmpty(interactionMessage))
            Debug.Log($"[QuestGiver] {interactionMessage}: {giverId}", this);
    }

    public bool OffersQuest(int questId)
    {
        if (offeredQuests == null) return false;
        foreach (QuestDefinition quest in offeredQuests)
            if (quest != null && quest.questId == questId) return true;
        return false;
    }

    private void OnValidate()
    {
        giverId = giverId?.Trim();
    }
}
