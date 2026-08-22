using UnityEngine;
using UnityEngine.Pool;
public class ChipPool : MonoBehaviour
{
    [SerializeField] HealthBarChip chipPrefab;
    [SerializeField] private RectTransform fillRect;
    
    ObjectPool<HealthBarChip> chipPool;
    
    private PlayerStats stats;
    private int lastHp;


    void Awake()
    {
        chipPool = new ObjectPool<HealthBarChip>(
            createFunc: CreateHealthBarChip,
            actionOnGet: b => b.gameObject.SetActive(true),
            actionOnRelease: b => b.gameObject.SetActive(false),
            actionOnDestroy: b => Destroy(b.gameObject),
            collectionCheck: true,
            defaultCapacity: 20,
            maxSize: 100
        );
    }

    HealthBarChip CreateHealthBarChip()
    {
        HealthBarChip chip = Instantiate(chipPrefab,transform);
        chip.Pool=chipPool;
        return chip;
    }

    void Start() => Bind(FindLocalStats());

    void Update()
    {
        if (stats == null) Bind(FindLocalStats());   // 玩家后生成时补找
    }

    void Bind(PlayerStats s)
    {
        if (stats != null) stats.OnStatsChanged -= OnHpChanged;
        stats = s;
        if (stats != null)
        {
            lastHp = stats.CurrentHp;
            stats.OnStatsChanged += OnHpChanged;
        }
    }

    void OnHpChanged()
    {
        if (stats == null) return;
        int cur = stats.CurrentHp;
        if (cur < lastHp)               // 只掉血才掉方块
            HeathDrop(lastHp - cur);
        lastHp = cur;
    }

    public void HeathDrop(float damage)
    {
        if (fillRect == null || stats == null) return;

        float maxHp = stats.MaxHp;
        float curHp = stats.CurrentHp;
        float barWidth  = fillRect.rect.width;    // 血条总长
        float barHeight = fillRect.rect.height;

        // 方块长度 = 伤害 / 最大生命值 × 血条总长
        float chipWidth = (damage / maxHp) * barWidth;
        // 生成位置 = 伤害结算后红条最右端
        float spawnX = (curHp / maxHp) * barWidth;

        HealthBarChip chip = chipPool.Get();
        chip.Setup(chipWidth, barHeight);

        // Fill 本地坐标 → 世界 → chip 父物体(ChipPool)本地坐标
        Vector3 worldPos = fillRect.TransformPoint(new Vector3(spawnX, 0f, 0f));
        chip.transform.localPosition = transform.InverseTransformPoint(worldPos);
    }

    void OnDestroy()
    {
        if (stats != null) stats.OnStatsChanged -= OnHpChanged;
    }

    PlayerStats FindLocalStats()
    {
        foreach (var s in FindObjectsOfType<PlayerStats>())
            if (!s.IsSpawned || s.IsOwner)
                return s;
        return null;
    }
}
