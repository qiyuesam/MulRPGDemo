using UnityEngine;

/// <summary>用于普通场景物体的服务器校验任务交互点。</summary>
public class QuestInteractionTarget : MonoBehaviour, IInteractable
{
    [SerializeField] private string targetKey = "interaction_target_1";
    [SerializeField, Min(0.5f)] private float maxInteractDistance = 4.5f;
    [SerializeField] private string interactionMessage = "已交互";

    public string TargetKey => targetKey;
    public float MaxInteractDistance => maxInteractDistance;

    private void OnEnable() => QuestManager.Instance?.RegisterInteractionTarget(this);
    private void Start() => QuestManager.Instance?.RegisterInteractionTarget(this);
    private void OnDisable() => QuestManager.Instance?.UnregisterInteractionTarget(this);

    public bool CanInteract(GameObject interactor)
        => enabled && gameObject.activeInHierarchy && interactor != null &&
           interactor.GetComponent<QuestLog>() != null;

    public void Interact(GameObject interactor)
    {
        QuestLog log = interactor != null ? interactor.GetComponent<QuestLog>() : null;
        log?.RequestRecordInteraction(targetKey);
        if (!string.IsNullOrEmpty(interactionMessage))
            Debug.Log($"[QuestInteraction] {interactionMessage}: {targetKey}", this);
    }

    private void OnValidate()
    {
        targetKey = targetKey?.Trim();
        maxInteractDistance = Mathf.Max(0.5f, maxInteractDistance);
    }
}
