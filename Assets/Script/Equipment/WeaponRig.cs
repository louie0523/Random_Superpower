using UnityEngine;

public enum ProjectilePreviewMode
{
    None,           // 미리보기 없음
    WhileDrawing,   // 활 — 시위를 당기는 동안만 (쏘면 사라지고 다음 차징 때 다시)
    WhileLoaded,    // RPG 등 — 탄약이 있는 동안 (쏘면 잠깐 사라졌다가 탄약이 남아 있으면 다시)
}

/// <summary>무기 모델 프리팹에 붙는 부착점 정의. 참조는 프리팹 내부로 한정.</summary>
public class WeaponRig : MonoBehaviour
{
    [Header("공통")]
    [Tooltip("무기 끝. 트레일/이펙트 기준")]
    public Transform Tip;

    [Tooltip("양손 무기일 때 왼손이 붙을 지점")]
    public Transform LeftGrip;

    [Header("원거리")]
    [Tooltip("투사체가 나가는 위치")]
    public Transform Muzzle;
    public MuzzleFlash MuzzleFlash;

    [Header("활")]
    [Tooltip("당겨지는 시위 본")]
    public Transform StringBone;

    [Header("투사체 미리보기")]
    [Tooltip("무기에 붙어 보이는 화살·탄두 모델 (프리팹 안의 자식).\n" +
             "활은 StringBone의 자식으로 두면 시위를 당기는 만큼 같이 움직임.\n" +
             "위치·회전은 프리팹에서 직접 맞추면 됨")]
    public GameObject ProjectileVisual;

    public ProjectilePreviewMode PreviewMode = ProjectilePreviewMode.None;

    [Tooltip("활: 이만큼 이상 당겼을 때부터 보임 (0~1)")]
    [Range(0f, 1f)] public float ShowFromDraw = 0.02f;

    [Tooltip("장전형: 쏜 뒤 다시 보이기까지 (초) — 다음 탄을 끼우는 느낌")]
    [Min(0f)] public float ReloadShowDelay = 0.5f;

    private float _draw;
    private bool _loaded = true;
    private bool _firedHold;        // 활: 쏜 직후 시위가 돌아오는 동안 다시 보이지 않게
    private float _hiddenUntil;     // 장전형: 쏜 직후 잠깐 숨김

    void Awake() => Refresh();
    void OnEnable() => Refresh();

    void Update()
    {
        // 장전형은 숨김 시간이 끝나는 순간 다시 판정
        if (PreviewMode == ProjectilePreviewMode.WhileLoaded && _hiddenUntil > 0f && Time.time >= _hiddenUntil)
        {
            _hiddenUntil = 0f;
            Refresh();
        }
    }

    /// <summary>활 — FPArmRig.SetDrawAmount가 매번 넘겨줌 (모션 편집 창 미리보기에서도 동작)</summary>
    public void OnDraw(float amount)
    {
        _draw = amount;
        if (_firedHold && amount < ShowFromDraw) _firedHold = false;
        Refresh();
    }

    /// <summary>발사 순간 — 실제 투사체로 교체되므로 숨김. 본인·다른 사람 화면 모두</summary>
    public void OnFired()
    {
        _firedHold = true;
        if (Application.isPlaying) _hiddenUntil = Time.time + ReloadShowDelay;
        Refresh();
    }

    /// <summary>장전 상태 — 매 프레임 탄약 유무로 넘겨도 됨 (바뀔 때만 실제로 켜고 끔)</summary>
    public void SetLoaded(bool loaded)
    {
        if (_loaded == loaded) return;
        _loaded = loaded;
        Refresh();
    }

    void Refresh()
    {
        if (ProjectileVisual == null) return;

        bool show = PreviewMode switch
        {
            ProjectilePreviewMode.WhileDrawing => !_firedHold && _draw >= ShowFromDraw,
            ProjectilePreviewMode.WhileLoaded => _loaded && (_hiddenUntil <= 0f || Time.time >= _hiddenUntil),
            _ => false,
        };

        if (ProjectileVisual.activeSelf != show) ProjectileVisual.SetActive(show);
    }

    /// <summary>편집 창 미리보기용 — 모드와 상관없이 강제로 보이기/숨기기</summary>
    public void ForcePreview(bool show)
    {
        if (ProjectileVisual != null) ProjectileVisual.SetActive(show);
    }
}