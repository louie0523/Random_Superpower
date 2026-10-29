using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵에 놓는 제단. 서브 제단 N개를 소울로 열면(개인) 중앙 제단을 열 수 있고, 중앙 제단을 열면 승리.
/// 네트워크 오브젝트 아님 — 누가 어떤 제단을 열었는지는 각 플레이어의 PlayerAltars에 저장.
/// 서브 제단 번호는 위치 순서로 자동 (모든 클라이언트가 같은 씬이라 같은 번호).
/// </summary>
public class Altar : MonoBehaviour
{
    /// <summary>서브 제단 최대 개수 (번호를 int 하나에 저장)</summary>
    public const int MaxSub = 32;

    /// <summary>중앙 제단 번호</summary>
    public const int CentralIndex = 99;

    [Tooltip("켜면 중앙 제단 (맵에 하나)")]
    [SerializeField] private bool central;

    [Tooltip("이 거리 안에서 상호작용 · 채널 (m)")]
    [SerializeField] private float radius = 3f;

    [Tooltip("위치 표시 높이 (제단 기준 위로 m)")]
    [SerializeField] private float markerHeight = 2.5f;

    [Header("겉모습 (내 기준 — 자식 오브젝트로)")]
    [Tooltip("서브: 아직 안 연 모습 / 중앙: 아직 못 여는 모습")]
    [SerializeField] private GameObject offVisual;

    [Tooltip("서브: 내가 연 모습 / 중앙: 열 수 있는 모습")]
    [SerializeField] private GameObject onVisual;

    public bool IsCentral => central;
    public float Radius => radius;
    public Vector3 MarkerPos => transform.position + Vector3.up * markerHeight;

    static readonly List<Altar> _all = new();
    static readonly List<Altar> _subs = new();
    static Altar _central;
    static bool _dirty = true;

    public static IReadOnlyList<Altar> All => _all;

    // ══════════════════ 등록 ══════════════════

    void OnEnable()
    {
        _all.Add(this);
        _dirty = true;
        AltarHud.Ensure();
    }

    void OnDisable()
    {
        _all.Remove(this);
        _dirty = true;
    }

    static void Rebuild()
    {
        if (!_dirty) return;
        _dirty = false;

        _subs.Clear();
        _central = null;
        foreach (var a in _all)
        {
            if (a == null) continue;
            if (a.central)
            {
                if (_central == null) _central = a;
                else Debug.LogWarning($"[Altar] 중앙 제단이 여러 개 — '{_central.name}'만 씀 ('{a.name}' 무시)");
            }
            else _subs.Add(a);
        }

        // 위치 순서 (cm 단위로 반올림) → 이름
        _subs.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position, pb = b.transform.position;
            int c = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
            if (c != 0) return c;
            c = Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
            if (c != 0) return c;
            c = Mathf.RoundToInt(pa.y * 100f).CompareTo(Mathf.RoundToInt(pb.y * 100f));
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });

        if (_subs.Count > MaxSub)
        {
            Debug.LogWarning($"[Altar] 서브 제단은 {MaxSub}개까지 — 나머지 무시");
            _subs.RemoveRange(MaxSub, _subs.Count - MaxSub);
        }
    }

    public static int SubCount { get { Rebuild(); return _subs.Count; } }

    public static Altar Central { get { Rebuild(); return _central; } }

    public static Altar Sub(int i)
    {
        Rebuild();
        return i >= 0 && i < _subs.Count ? _subs[i] : null;
    }

    public static Altar ByIndex(int i) => i == CentralIndex ? Central : Sub(i);

    /// <summary>서브 = 0~31, 중앙 = 99, 없으면 -1</summary>
    public static int IndexOf(Altar a)
    {
        if (a == null) return -1;
        if (a.central) return a == Central ? CentralIndex : -1;
        Rebuild();
        return _subs.IndexOf(a);
    }

    // ══════════════════ 겉모습 (내 기준) ══════════════════

    void Update()
    {
        if (offVisual == null && onVisual == null) return;

        var me = PlayerAltars.Local;
        bool on;
        if (central) on = me != null && me.CanOpenCentral;
        else on = me != null && me.Has(IndexOf(this));

        if (offVisual != null && offVisual.activeSelf == on) offVisual.SetActive(!on);
        if (onVisual != null && onVisual.activeSelf != on) onVisual.SetActive(on);
    }

    void OnValidate()
    {
        if (offVisual == gameObject || onVisual == gameObject)
            Debug.LogWarning($"[Altar] '{name}': 겉모습 칸에는 자식 오브젝트를 넣어야 해 (자기 자신을 끄면 제단이 멈춤)");
        _dirty = true;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = central ? new Color(1f, 0.8f, 0.1f, 0.9f) : new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.DrawLine(transform.position, MarkerPos);

        if (!Application.isPlaying) _dirty = true;   // 편집 중에 옮기면 번호 다시
        int i = IndexOf(this);
        string label = central ? "중앙 제단" : (i >= 0 ? $"서브 제단 {i}" : "서브 제단 (초과)");
        UnityEditor.Handles.Label(MarkerPos, label);
    }
#endif
}

/// <summary>
/// 제단 규칙 값 — 방에서 들어왔으면 방 설정, 아니면 RoundManager 인스펙터 값.
/// </summary>
public static class AltarRules
{
    static RoundManager RM
    {
        get
        {
            var rm = RoundManager.Instance;
            return rm != null && rm.Object != null && rm.Object.IsValid ? rm : null;
        }
    }

    public static int SubCost
    {
        get { var rm = RM; return RoomRules.SubAltarCost(rm != null ? rm.DefaultSubAltarCost : 35); }
    }

    public static int CentralCost
    {
        get { var rm = RM; return RoomRules.CentralAltarCost(rm != null ? rm.DefaultCentralAltarCost : 20); }
    }

    public static float CentralChannel
    {
        get { var rm = RM; return RoomRules.CentralChannel(rm != null ? rm.DefaultCentralChannel : 8f); }
    }

    /// <summary>중앙 제단을 열려면 필요한 서브 제단 수 (맵의 서브 제단 수를 넘지 않음)</summary>
    public static int Needed
    {
        get
        {
            var rm = RM;
            int subs = Altar.SubCount;
            bool all = RoomRules.AltarsNeedAll(rm != null && rm.DefaultAltarsNeedAll);
            int n = all ? subs : RoomRules.AltarsNeeded(rm != null ? rm.DefaultAltarsNeeded : 4);
            return Mathf.Clamp(n, 0, subs);
        }
    }
}
