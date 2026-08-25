using UnityEngine;

/// <summary>服务器或本地权威检测的任务区域。</summary>
[RequireComponent(typeof(Collider))]
public class QuestLocation : MonoBehaviour
{
    [SerializeField] private string locationKey = "location_1";

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        QuestLog log = other.GetComponentInParent<QuestLog>();
        if (log == null || !log.IsAuthoritative)
            return;

        QuestManager.Publish(QuestEvent.LocationReached(log.gameObject, locationKey));
    }

    private void OnValidate()
    {
        locationKey = locationKey?.Trim();
        Collider trigger = GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;
    }
}
