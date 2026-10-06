using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상태이상 화면 연출 (로컬 플레이어).
///   EdgeFlames — 화면 가장자리가 일렁이는 프레임 애니메이션 (화상·중독 등)
///                 프레임 이미지를 넣으면 그걸 쓰고, 비우면 코드가 노이즈로 직접 만듦 (에셋 없이 동작)
///   Tint       — 화면 전체를 살짝 물들임 (동상 등)
/// UI 씬 HUD의 전체 화면 RectTransform에 붙이면 레이어(RawImage)를 알아서 만듦.
/// StatusScreenTint를 대체.
/// </summary>
public class StatusScreenEffects : MonoBehaviour
{
    public enum Style { Tint, EdgeFlames }

    [System.Serializable]
    public class Entry
    {
        public StatusType Type;
        public Style Style = Style.EdgeFlames;

        [Tooltip("색과 최대 진하기(알파)")]
        public Color Color = new(1f, 0.45f, 0.1f, 0.855f);

        [Tooltip("직접 만든 프레임 이미지 (가장자리 불꽃 등). 비우면 코드가 생성")]
        public Texture2D[] Frames;

        [Tooltip("초당 프레임")]
        [Min(1)] public float Fps = 12f;

        [Header("생성 — Frames가 비었을 때")]
        [Tooltip("가장자리 두께 (화면 비율)")]
        [Range(0.05f, 0.5f)] public float EdgeWidth = 0.22f;

        [Tooltip("불꽃 무늬 촘촘함")]
        public float NoiseScale = 5f;

        [Tooltip("일렁임 세기")]
        [Range(0f, 1f)] public float Flicker = 0.5f;

        [System.NonSerialized] public Texture2D[] Runtime;
        [System.NonSerialized] public bool Generated;
        [System.NonSerialized] public RawImage Image;
        [System.NonSerialized] public float Alpha;
    }

    [SerializeField] private Entry[] entries =
    {
        new() { Type = StatusType.Burn, Style = Style.EdgeFlames, Color = new Color(1f, 0.45f, 0.1f, 0.85f), Fps = 12f },
        new() { Type = StatusType.Poison, Style = Style.EdgeFlames, Color = new Color(0.45f, 1f, 0.3f, 0.6f), Fps = 8f, Flicker = 0.35f },
        new() { Type = StatusType.Frost, Style = Style.Tint, Color = new Color(0.45f, 0.75f, 1f, 0.22f) },
    };

    [Header("생성 설정")]
    [Tooltip("생성 프레임 해상도. 화면에 늘려 그려지므로 128이면 충분")]
    [SerializeField] private int generatedSize = 128;
    [Tooltip("생성 프레임 수 (마지막 → 처음이 자연스럽게 이어짐)")]
    [SerializeField] private int generatedFrames = 8;

    [SerializeField] private float fadeSpeed = 4f;

    private PlayerStatus _status;

    public void Bind(PlayerStatus status) => _status = status;

    void Awake()
    {
        if (entries == null) return;

        foreach (var e in entries)
        {
            if (e == null) continue;
            e.Image = CreateLayer(e.Type.ToString());

            if (e.Style != Style.EdgeFlames) continue;

            if (e.Frames != null && e.Frames.Length > 0) e.Runtime = e.Frames;
            else
            {
                e.Runtime = Generate(e);
                e.Generated = true;
            }
        }
    }

    RawImage CreateLayer(string label)
    {
        var go = new GameObject($"Status_{label}", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<RawImage>();
        img.raycastTarget = false;
        img.color = Color.clear;
        img.enabled = false;
        return img;
    }

    void Update()
    {
        if (entries == null) return;

        bool valid = _status != null && _status.Object != null && _status.Object.IsValid;

        foreach (var e in entries)
        {
            if (e?.Image == null) continue;

            bool on = valid && _status.Has(e.Type);
            e.Alpha = Mathf.MoveTowards(e.Alpha, on ? 1f : 0f, Time.deltaTime * fadeSpeed);

            bool show = e.Alpha > 0.003f;
            if (e.Image.enabled != show) e.Image.enabled = show;
            if (!show) continue;

            var c = e.Color;
            c.a *= e.Alpha;
            e.Image.color = c;

            if (e.Runtime != null && e.Runtime.Length > 0)
                e.Image.texture = e.Runtime[(int)(Time.time * e.Fps) % e.Runtime.Length];
        }
    }

    /// <summary>
    /// 가장자리 불꽃 프레임 생성. 노이즈를 원 궤도로 움직여 마지막 프레임이 첫 프레임으로 이어지게.
    /// 흰색 + 알파로 만들고 색은 RawImage 색으로 입힘.
    /// </summary>
    Texture2D[] Generate(Entry e)
    {
        int size = Mathf.Clamp(generatedSize, 32, 512);
        int count = Mathf.Max(1, generatedFrames);

        var frames = new Texture2D[count];
        var px = new Color32[size * size];
        float seed = Random.Range(0f, 100f);

        for (int f = 0; f < count; f++)
        {
            float ang = f / (float)count * Mathf.PI * 2f;
            float ox = Mathf.Cos(ang) * 1.5f;
            float oy = Mathf.Sin(ang) * 1.5f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;

                    float d = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float edge = 1f - Mathf.Clamp01(d / e.EdgeWidth);      // 가장자리 1 → 안쪽 0

                    if (edge <= 0f)
                    {
                        px[y * size + x] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    float n = Mathf.PerlinNoise(seed + u * e.NoiseScale + ox, seed + v * e.NoiseScale + oy);
                    float flame = edge * edge * (1f - e.Flicker + e.Flicker * 2f * n);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flame));
                    byte rgb = (byte)(255f * Mathf.Lerp(0.7f, 1f, edge));    // 바깥쪽이 더 밝게

                    px[y * size + x] = new Color32(rgb, rgb, rgb, (byte)(alpha * 255f));
                }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"StatusEdge_{e.Type}_{f}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            frames[f] = tex;
        }

        return frames;
    }

    void OnDestroy()
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null || !e.Generated || e.Runtime == null) continue;
            foreach (var t in e.Runtime) if (t != null) Destroy(t);
        }
    }
}
