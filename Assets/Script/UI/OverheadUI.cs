using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 머리 위 이름 · 체력바 (오버워치식). 씬의 아무 오브젝트(HUD 등)에 하나 붙이면 화면 캔버스를 스스로 만듦.
///   체력바: 그 플레이어가 전투 중일 때만 (피해를 주고받은 뒤 몇 초)
///   이름:   가까이 있을 때만 (기본 10m)
///   크기:   거리와 상관없이 항상 같음 · 벽 뒤에 있으면 숨김 (설정)
///   내 캐릭터는 안 그림. 더미도 그림
/// 최대 체력은 PlayerVitals(플레이어 프리팹)에서 — 없으면 권한자 화면에서만 정확함
/// </summary>
public class OverheadUI : MonoBehaviour
{
    [Header("보이는 조건")]
    [Tooltip("이름이 보이는 거리 (m)")]
    [SerializeField] private float nameDistance = 10f;
    [Tooltip("체력바가 보이는 최대 거리 (m)")]
    [SerializeField] private float barDistance = 60f;
    [Tooltip("벽 뒤에 있으면 숨김")]
    [SerializeField] private bool hideBehindWalls = true;
    [Tooltip("가리는 것으로 볼 레이어 (장애물 · 바닥) — 플레이어 레이어는 빼기")]
    [SerializeField] private LayerMask obstacleMask = 1;
    [Tooltip("아웃라인이 벽 너머로 보이는 대상(쉐도우 워커 궁 등)은 벽 뒤여도 보임")]
    [SerializeField] private bool showWhenXRay = true;

    [Header("위치")]
    [Tooltip("발 기준 높이 (m) — 탈것 높이는 자동으로 더함")]
    [SerializeField] private float height = 2.25f;
    [Tooltip("화면에서 위로 더 띄우기 (px)")]
    [SerializeField] private float screenOffset = 6f;

    [Header("모양")]
    [SerializeField] private Vector2 barSize = new(96f, 9f);
    [SerializeField] private Color barBack = new(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color barFill = new(0.95f, 0.25f, 0.22f, 1f);
    [SerializeField] private Color barLow = new(1f, 0.55f, 0.15f, 1f);
    [SerializeField] private float nameSize = 18f;
    [SerializeField] private Color nameColor = Color.white;
    [Tooltip("이름에 쓸 글꼴 (비우면 TMP 기본)")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("다른 UI보다 뒤에 그려지게 (낮을수록 뒤)")]
    [SerializeField] private int sortingOrder = -10;

    /// <summary>이름 정하기 — 닉네임 시스템이 생기면 여기에 연결</summary>
    public static Func<NetworkObject, string> NameProvider;

    class Plate
    {
        public PlayerHealth H;
        public PlayerVitals V;
        public PlayerAlter A;
        public NetworkObject O;
        public RectTransform Root;
        public TextMeshProUGUI Name;
        public RectTransform Bar;
        public RectTransform Fill;
        public Image FillImg;
        public float LosTimer;
        public bool Visible = true;
    }

    private readonly Dictionary<PlayerHealth, Plate> _plates = new();
    private RectTransform _canvas;
    private float _scanTimer;
    private Camera _cam;
    private float _camTimer;

    void Awake()
    {
        var go = new GameObject("OverheadUI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortingOrder;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 1f;
        _canvas = (RectTransform)go.transform;
    }

    void LateUpdate()
    {
        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f) { _scanTimer = 1f; Scan(); }

        var cam = ActiveCamera();
        if (cam == null) { foreach (var p in _plates.Values) Show(p, false); return; }
        Vector3 camPos = cam.transform.position;

        foreach (var p in _plates.Values)
        {
            if (p.H == null || p.O == null || !p.O.IsValid) { Show(p, false); continue; }
            if (p.O.HasInputAuthority || p.H.IsDead) { Show(p, false); continue; }
            if (InvisibleVisual.IsInvisible(p.H)) { Show(p, false); continue; }

            Vector3 world = p.H.transform.position + Vector3.up * (height + (p.A != null ? p.A.Lift : 0f));
            float dist = Vector3.Distance(camPos, world);

            bool showBar = p.H.InCombat && dist <= barDistance;
            bool showName = dist <= nameDistance;
            if (!showBar && !showName) { Show(p, false); continue; }

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) { Show(p, false); continue; }

            // 벽 뒤 확인 — 대상마다 0.15초에 한 번
            if (hideBehindWalls)
            {
                p.LosTimer -= Time.deltaTime;
                if (p.LosTimer <= 0f)
                {
                    p.LosTimer = 0.15f;
                    Vector3 dir = world - camPos;
                    p.Visible = !Physics.Raycast(camPos, dir.normalized, dir.magnitude - 0.3f, obstacleMask, QueryTriggerInteraction.Ignore);
                }
                bool xray = showWhenXRay && p.H.GetComponentInParent<PlayerOutline>() is { } ol && ol.isActiveAndEnabled && IsXRay(ol);
                if (!p.Visible && !xray) { Show(p, false); continue; }
            }

            // 암흑 시야 — 어둠 속 대상은 이름 · 체력바도 숨김 (벽 너머 아웃라인 대상은 예외)
            if (!ScreenDarkness.CanSee(p.H.transform.position + Vector3.up) && !(p.H.GetComponentInParent<PlayerOutline>() is { } ol2 && ol2.IsXRayNow))
            { Show(p, false); continue; }

            Show(p, true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, sp, null, out var local);
            p.Root.anchoredPosition = local + Vector2.up * screenOffset;

            p.Name.gameObject.SetActive(showName);
            if (showName) p.Name.text = NameOf(p.O);

            p.Bar.gameObject.SetActive(showBar);
            if (showBar)
            {
                int max = p.V != null && p.V.MaxHp > 0 ? p.V.MaxHp : MaxFromStats(p.H);
                float r = max > 0 ? Mathf.Clamp01(p.H.HP / (float)max) : 0f;
                p.Fill.anchorMax = new Vector2(r, 1f);
                p.FillImg.color = r < 0.3f ? barLow : barFill;
            }
            // 체력바가 없으면 이름을 바 자리까지 내림
            p.Name.rectTransform.anchoredPosition = new Vector2(0f, showBar ? barSize.y + 2f : 0f);
        }
    }

    static bool IsXRay(PlayerOutline ol) => ol.IsXRayNow;

    static int MaxFromStats(PlayerHealth h)
    {
        var st = h.GetComponent<PlayerStats>();
        return st != null ? Mathf.RoundToInt(st.Get(StatType.MaxHP)) : 0;
    }

    string NameOf(NetworkObject o)
    {
        if (NameProvider != null)
        {
            string n = NameProvider(o);
            if (!string.IsNullOrEmpty(n)) return n;
        }
        var runner = o.Runner;
        if (runner != null && o.InputAuthority != PlayerRef.None && runner.GetPlayerObject(o.InputAuthority) == o)
            return LocUtil.Get("ui.score.player", o.InputAuthority.PlayerId);
        return o.name.Replace("(Clone)", "").Trim();
    }

    // ══════════════════ 대상 목록 ══════════════════

    void Scan()
    {
        var found = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        var alive = new HashSet<PlayerHealth>(found);

        var gone = new List<PlayerHealth>();
        foreach (var kv in _plates) if (kv.Key == null || !alive.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var h in gone)
        {
            if (_plates[h].Root != null) Destroy(_plates[h].Root.gameObject);
            _plates.Remove(h);
        }

        foreach (var h in found)
            if (h != null && !_plates.ContainsKey(h)) _plates[h] = Make(h);
    }

    Plate Make(PlayerHealth h)
    {
        var p = new Plate
        {
            H = h,
            V = h.GetComponent<PlayerVitals>(),
            A = h.GetComponent<PlayerAlter>(),
            O = h.GetComponent<NetworkObject>(),
        };

        var root = new GameObject("Plate_" + h.name, typeof(RectTransform));
        p.Root = (RectTransform)root.transform;
        p.Root.SetParent(_canvas, false);
        p.Root.sizeDelta = new Vector2(barSize.x, barSize.y);
        p.Root.pivot = new Vector2(0.5f, 0f);

        // 체력바
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        p.Bar = (RectTransform)bar.transform;
        p.Bar.SetParent(p.Root, false);
        p.Bar.anchorMin = new Vector2(0.5f, 0f);
        p.Bar.anchorMax = new Vector2(0.5f, 0f);
        p.Bar.pivot = new Vector2(0.5f, 0f);
        p.Bar.sizeDelta = barSize;
        var bg = bar.GetComponent<Image>();
        bg.color = barBack;
        bg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        p.Fill = (RectTransform)fill.transform;
        p.Fill.SetParent(p.Bar, false);
        p.Fill.anchorMin = Vector2.zero;
        p.Fill.anchorMax = Vector2.one;
        p.Fill.offsetMin = new Vector2(1.5f, 1.5f);
        p.Fill.offsetMax = new Vector2(-1.5f, -1.5f);
        p.FillImg = fill.GetComponent<Image>();
        p.FillImg.color = barFill;
        p.FillImg.raycastTarget = false;

        // 이름
        var name = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        p.Name = name.GetComponent<TextMeshProUGUI>();
        var nr = p.Name.rectTransform;
        nr.SetParent(p.Root, false);
        nr.anchorMin = new Vector2(0.5f, 0f);
        nr.anchorMax = new Vector2(0.5f, 0f);
        nr.pivot = new Vector2(0.5f, 0f);
        nr.sizeDelta = new Vector2(240f, nameSize + 8f);
        if (font != null) p.Name.font = font;
        p.Name.fontSize = nameSize;
        p.Name.alignment = TextAlignmentOptions.Bottom;
        p.Name.color = nameColor;
        p.Name.raycastTarget = false;
        p.Name.outlineWidth = 0.22f;
        p.Name.outlineColor = new Color32(0, 0, 0, 200);
        p.Name.textWrappingMode = TextWrappingModes.NoWrap;

        Show(p, false);
        return p;
    }

    static void Show(Plate p, bool on)
    {
        if (p.Root != null && p.Root.gameObject.activeSelf != on) p.Root.gameObject.SetActive(on);
    }

    // ══════════════════ 카메라 ══════════════════

    /// <summary>지금 화면을 그리는 카메라 (1인칭 · 3인칭 어느 쪽이든). URP 오버레이 카메라는 제외</summary>
    Camera ActiveCamera()
    {
        _camTimer -= Time.deltaTime;
        if (_cam != null && _cam.isActiveAndEnabled && _camTimer > 0f) return _cam;
        _camTimer = 0.5f;

        Camera best = null;
        foreach (var c in Camera.allCameras)
        {
            if (c == null || !c.isActiveAndEnabled || c.targetTexture != null) continue;
            var data = c.GetUniversalAdditionalCameraData();
            if (data != null && data.renderType != CameraRenderType.Base) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        _cam = best != null ? best : Camera.main;
        return _cam;
    }
}