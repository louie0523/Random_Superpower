using UnityEngine;

public enum DamageNumberKind
{
    Normal,     // 일반
    Crit,       // 치명타
    True,       // 고정 피해 (심판, 혈격 등)
    Burn,       // 화상 틱
    Poison,     // 중독 틱
    Miss,       // 회피
}

/// <summary>
/// 파티클 기반 데미지 숫자 (1인칭).
///  - 숫자 0~9마다 파티클 시스템 하나. 각 시스템은 Texture Sheet Animation = Sprites로
///    그 숫자의 변형 4개를 등록하고 Start Frame을 0~3.99 랜덤 → 찍을 때마다 변형이 랜덤
///  - 거리에 비례해 키워서 화면상 크기가 일정
///  - 대상 머리 위에서, 연타 시 겹치지 않게 살짝 흩뿌림
///  - 숫자 전체가 같은 속도로 떠오름 (글자끼리 흩어지지 않음)
/// 로컬 플레이어 화면에서만 동작. 공격자에게만 보이도록 PlayerHealth가 그 사람에게만 RPC.
/// </summary>
public class DamageParticleManager : MonoBehaviour
{
    public static DamageParticleManager Instance { get; private set; }

    [System.Serializable]
    public class Style
    {
        public DamageNumberKind Kind;
        public Color Color = Color.white;
        [Tooltip("기본 크기 대비 배율")]
        public float Scale = 1f;
    }

    [Header("파티클")]
    [Tooltip("숫자 0~9 순서")]
    [SerializeField] private ParticleSystem[] digitSystems = new ParticleSystem[10];
    [Tooltip("MISS 글자 하나")]
    [SerializeField] private ParticleSystem missSystem;

    [Header("크기 — 거리와 상관없이 화면상 일정")]
    [Tooltip("1m 거리에서 글자 높이(m). 거리에 비례해 커짐")]
    [SerializeField] private float heightPerMeter = 0.045f;
    [SerializeField] private float minHeight = 0.12f;
    [SerializeField] private float maxHeight = 2.5f;

    [Tooltip("숫자 한 칸 가로/세로 비 (24/32)")]
    [SerializeField] private float digitAspect = 0.75f;
    [Tooltip("MISS 가로/세로 비 (72/32)")]
    [SerializeField] private float missAspect = 2.25f;
    [Tooltip("글자 간격 — 글자 폭 대비 (1 = 딱 붙음)")]
    [SerializeField] private float spacing = 0.85f;

    [Header("위치 · 움직임")]
    [Tooltip("대상 발 위치 기준 위로 (m) — 머리 위")]
    [SerializeField] private float headOffset = 2.0f;
    [Tooltip("연타 시 겹치지 않게 흩뿌리는 정도 (글자 높이 배수)")]
    [SerializeField] private float jitter = 0.6f;
    [Tooltip("떠오르는 속도 (m/s, 거리 보정 전)")]
    [SerializeField] private float riseSpeed = 0.8f;

    [Header("종류별 색 · 크기 — 텍스처의 흰 부분에 곱해짐")]
    [SerializeField] private Style[] styles =
    {
        new() { Kind = DamageNumberKind.Normal, Color = Color.white, Scale = 1f },
        new() { Kind = DamageNumberKind.Crit,   Color = new Color(1f, 0.85f, 0.2f), Scale = 1.4f },
        new() { Kind = DamageNumberKind.True,   Color = new Color(0.75f, 0.5f, 1f), Scale = 1.1f },
        new() { Kind = DamageNumberKind.Burn,   Color = new Color(1f, 0.55f, 0.2f), Scale = 0.8f },
        new() { Kind = DamageNumberKind.Poison, Color = new Color(0.5f, 0.95f, 0.4f), Scale = 0.8f },
        new() { Kind = DamageNumberKind.Miss,   Color = Color.white, Scale = 1f },
    };

    private Camera _cam;
    private ParticleSystem.EmitParams _ep;
    private readonly int[] _digits = new int[10];

    void Awake()
    {
        Instance = this;
        _ep = new ParticleSystem.EmitParams();

        // 가로·세로를 따로 주려면 3D Start Size가 켜져 있어야 함
        foreach (var ps in digitSystems) Enable3DSize(ps);
        Enable3DSize(missSystem);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static void Enable3DSize(ParticleSystem ps)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startSize3D = true;
    }

    /// <summary>UIManager.BindLocalPlayer가 플레이어 카메라를 넘김</summary>
    public void SetCamera(Camera cam) => _cam = cam;

    Style GetStyle(DamageNumberKind k)
    {
        if (styles != null)
            foreach (var s in styles)
                if (s != null && s.Kind == k) return s;
        return null;
    }

    /// <param name="targetPos">맞은 대상의 발 위치</param>
    public void Show(Vector3 targetPos, int amount, DamageNumberKind kind)
    {
        var cam = _cam != null ? _cam : Camera.main;
        if (cam == null) return;
        if (kind != DamageNumberKind.Miss && amount <= 0) return;

        var style = GetStyle(kind);
        float scale = style != null ? style.Scale : 1f;
        _ep.startColor = style != null ? style.Color : Color.white;

        Transform ct = cam.transform;
        Vector3 right = ct.right;
        Vector3 up = ct.up;

        Vector3 pos = targetPos + Vector3.up * headOffset;
        float dist = Vector3.Distance(ct.position, pos);
        float h = Mathf.Clamp(dist * heightPerMeter, minHeight, maxHeight) * scale;

        // 연타 시 겹치지 않게
        pos += (right * Random.Range(-1f, 1f) + up * Random.Range(-0.4f, 0.6f)) * (h * jitter);

        // 숫자 전체가 같이 떠오르게 (거리 보정 — 멀어도 화면에서 같은 속도)
        _ep.velocity = Vector3.up * (riseSpeed * h / Mathf.Max(0.01f, heightPerMeter));

        if (kind == DamageNumberKind.Miss)
        {
            if (missSystem == null) return;
            _ep.position = pos;
            _ep.startSize3D = new Vector3(h * missAspect, h, 1f);
            missSystem.Emit(_ep, 1);
            return;
        }

        // 자릿수 분해
        int n = 0, v = amount;
        while (v > 0 && n < _digits.Length)
        {
            _digits[n++] = v % 10;
            v /= 10;
        }

        float w = h * digitAspect;
        float step = w * spacing;
        Vector3 start = pos - right * ((n - 1) * step * 0.5f);

        _ep.startSize3D = new Vector3(w, h, 1f);

        for (int i = 0; i < n; i++)
        {
            int d = _digits[n - 1 - i];
            var ps = d < digitSystems.Length ? digitSystems[d] : null;
            if (ps == null) continue;

            _ep.position = start + right * (i * step);
            ps.Emit(_ep, 1);
        }
    }
}
