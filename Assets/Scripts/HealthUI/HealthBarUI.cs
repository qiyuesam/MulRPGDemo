using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private Image hpFill;
    [SerializeField] private Image mpFill;

    private PlayerStats stats;

    void OnEnable()
    {
        UIManager.LocalPlayerChanged += OnPlayerChanged;
        if (UIManager.Instance != null)
            OnPlayerChanged(UIManager.Instance.Player);
    }

    void OnDisable()
    {
        UIManager.LocalPlayerChanged -= OnPlayerChanged;
        Bind(null);
    }

    private void OnPlayerChanged(PlayerContext ctx)
    {
        Bind(ctx.Valid ? ctx.Stats : null);
    }

    public void Bind(PlayerStats s)
    {
        if (stats != null) stats.OnStatsChanged -= Refresh;
        stats = s;
        if (stats != null)
        {
            stats.OnStatsChanged += Refresh;
            Refresh();
        }
    }

    void Refresh()
    {
        if (stats == null) return;
        if (hpFill != null)
            hpFill.fillAmount = stats.MaxHp > 0 ? (float)stats.CurrentHp / stats.MaxHp : 0f;
        if (mpFill != null)
            mpFill.fillAmount = stats.MaxMp > 0 ? (float)stats.CurrentMp / stats.MaxMp : 0f;
    }
}
