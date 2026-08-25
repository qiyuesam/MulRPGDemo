using UnityEngine;

/// <summary>可复用的场景物品来源，物品发放始终由本地权威或服务器执行。</summary>
public class QuestItemSource : MonoBehaviour, IInteractable
{
    [SerializeField] private string sourceId = "item_source_1";
    [SerializeField] private int itemId = 11;
    [SerializeField, Min(1)] private int count = 1;
    [SerializeField, Min(0.5f)] private float maxInteractDistance = 4.5f;
    [SerializeField] private string interactionMessage = "获得物品";

    public string SourceId => sourceId;
    public int ItemId => itemId;
    public int Count => count;
    public float MaxInteractDistance => maxInteractDistance;

    private void OnEnable() => QuestManager.Instance?.RegisterItemSource(this);
    private void Start() => QuestManager.Instance?.RegisterItemSource(this);
    private void OnDisable() => QuestManager.Instance?.UnregisterItemSource(this);

    public bool CanInteract(GameObject interactor)
    {
        if (!enabled || !gameObject.activeInHierarchy || interactor == null)
            return false;
        return interactor.GetComponent<QuestLog>() != null;
    }

    public void Interact(GameObject interactor)
    {
        QuestLog log = interactor != null ? interactor.GetComponent<QuestLog>() : null;
        log?.RequestClaimItemSource(sourceId);
    }

    internal bool GrantTo(QuestLog log)
    {
        if (log == null || !log.IsAuthoritative)
            return false;

        Inventory inventory = log.GetComponent<Inventory>();
        ItemDatabase database = QuestManager.Instance != null ? QuestManager.Instance.Items : null;
        if (inventory == null || database == null || database.GetItem(itemId) == null ||
            !inventory.data.CanAddItem(database, itemId, count))
            return false;

        inventory.TryAddItem(itemId, count);
        if (!string.IsNullOrEmpty(interactionMessage))
            Debug.Log($"[QuestItemSource] {interactionMessage}: item={itemId}, count={count}", this);
        return true;
    }

    private void OnValidate()
    {
        sourceId = sourceId?.Trim();
        itemId = Mathf.Max(1, itemId);
        count = Mathf.Max(1, count);
        maxInteractDistance = Mathf.Max(0.5f, maxInteractDistance);
    }
}
