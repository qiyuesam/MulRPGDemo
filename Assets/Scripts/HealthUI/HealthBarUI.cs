using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private Image hpFill;
    [SerializeField] private Image mpFill;
    
    private PlayerStats stats;

    void Start()
    {
        Bind(FindPlayerStats());
    }

    void Update()
    {
        if (stats == null)
            Bind(FindPlayerStats());
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
        {
            hpFill.fillAmount=stats.MaxHp>0?(float)stats.CurrentHp/stats.MaxHp:0f;
        }

        if (mpFill != null)
        {
            mpFill.fillAmount=stats.MaxMp>0?(float)stats.CurrentMp/stats.MaxMp:0f;
        }
           
    }

    void OnDestroy()
    {
        if (stats!=null) stats.OnStatsChanged -= Refresh;
    }

    PlayerStats FindPlayerStats()
    {
        foreach (var s in FindObjectsOfType<PlayerStats>())
        {
            if(!s.IsSpawned||s.IsOwner)
                return s;
        }
        return null;
    }
}
