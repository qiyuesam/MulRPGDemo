using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    bool CanInteract(GameObject interactor);
    void Interact(GameObject interactor);
}

/// <summary>玩家交互入口。只选择半径内最近的有效交互对象。</summary>
public static class InteractionSystem
{
    private const float DefaultRadius = 3f;

    public static bool TryInteract(GameObject interactor, float radius = DefaultRadius)
    {
        if (interactor == null)
            return false;

        Collider[] hits = Physics.OverlapSphere(
            interactor.transform.position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        IInteractable closest = null;
        float closestDistance = float.MaxValue;
        var visited = new HashSet<MonoBehaviour>();

        foreach (Collider hit in hits)
        {
            foreach (MonoBehaviour behaviour in hit.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (!visited.Add(behaviour) || behaviour is not IInteractable interactable ||
                    !interactable.CanInteract(interactor))
                    continue;

                float distance = Vector3.Distance(
                    interactor.transform.position, hit.ClosestPoint(interactor.transform.position));
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = interactable;
                }
            }
        }

        if (closest == null)
            return false;

        closest.Interact(interactor);
        return true;
    }
}

/// <summary>可复用的场景交互触发器，可在 Inspector 中绑定具体业务事件。</summary>
public class InteractionTrigger : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactionMessage = "已交互";
    [SerializeField] private UnityEvent onInteract;

    public bool CanInteract(GameObject interactor)
        => enabled && gameObject.activeInHierarchy && interactor != null;

    public void Interact(GameObject interactor)
    {
        onInteract?.Invoke();
        if (!string.IsNullOrWhiteSpace(interactionMessage))
            Debug.Log($"[Interaction] {interactionMessage}", this);
    }
}
