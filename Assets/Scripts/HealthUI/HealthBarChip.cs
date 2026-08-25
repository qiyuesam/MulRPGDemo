// Assets/Scripts/HealthUI/HealthBarChip.cs
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

public class HealthBarChip : MonoBehaviour
{
    public IObjectPool<HealthBarChip> Pool { get; set; }

    [SerializeField] private float lifetime = 2f;        // 存活时间，到点回池
    [SerializeField] private float fallSpeed = 60f;      // 下落速度（像素/秒）
    [SerializeField] private float fadeDuration = 0.5f;  // 淡出耗时（秒）

    private Image chipImage;
    private float timer;

    void Awake()
    {
        chipImage = GetComponent<Image>();
    }

    void OnEnable()
    {
        timer = lifetime;

        // 每次取出重置为不透明
        if (chipImage != null)
        {
            Color c = chipImage.color;
            c.a = 1f;
            chipImage.color = c;
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;

        // ★ 下落（累加，不是赋值）
        transform.localPosition += Vector3.down * fallSpeed * Time.deltaTime;

        // ★ 淡出只发生在生命周期末段，而不是启用后立即消失
        if (chipImage != null)
        {
            float elapsed = lifetime - timer;                 // 已存活时间
            float fadeStart = Mathf.Max(0f, lifetime - fadeDuration);
            float alpha = elapsed <= fadeStart
                ? 1f
                : 1f - Mathf.Clamp01((elapsed - fadeStart) / Mathf.Max(0.001f, fadeDuration));
            Color c = chipImage.color;
            c.a = alpha;
            chipImage.color = c;
        }

        if (timer <= 0f)
            Pool?.Release(this);
    }

    public void Setup(float width, float height)
    {
        var rt = (RectTransform)transform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
    }
}
