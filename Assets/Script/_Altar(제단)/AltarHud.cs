using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 제단 화면 표시 (기본 모양 — 디자인 UI는 나중에 이 자리를 바꿈).
/// - 위: 전체 알림 (권한 획득 · 중앙 채널 시작 · 끊김)
/// - 중앙 제단 위치 표시: 누가 채널 중일 때 모두에게 (진행 % · 거리) / 내가 열 수 있으면 나에게
/// - 아래: 상호작용 안내 · 내 채널 막대 · 짧은 알림
/// 제단이 있는 씬이면 저절로 생김.
/// </summary>
public class AltarHud : MonoBehaviour
{
    static AltarHud _inst;

    struct Msg
    {
        public string Text;
        public float Time;
    }

    const float AnnounceTime = 5f;
    const float ToastTime = 2.5f;

    readonly List<Msg> _announce = new();
    string _toast = "";
    float _toastAt = -99f;
    string _prompt = "";
    int _promptFrame = -10;

    GUIStyle _banner, _marker, _small, _box;

    // ══════════════════ 외부에서 부르는 것 ══════════════════

    public static void Ensure()
    {
        if (_inst != null) return;
        _inst = FindAnyObjectByType<AltarHud>();
        if (_inst != null) return;
        _inst = new GameObject("Altar HUD").AddComponent<AltarHud>();
    }

    /// <summary>전체 알림 (모든 클라이언트에서 각자 부름)</summary>
    public static void Announce(string text)
    {
        Ensure();
        _inst._announce.Add(new Msg { Text = text, Time = Time.unscaledTime });
        if (_inst._announce.Count > 4) _inst._announce.RemoveAt(0);
        Debug.Log($"[Altar] {text}");
    }

    /// <summary>나에게만 짧은 알림</summary>
    public static void Toast(string text)
    {
        Ensure();
        _inst._toast = text ?? "";
        _inst._toastAt = Time.unscaledTime;
    }

    /// <summary>상호작용 안내 — 보여 줄 동안 매 프레임 부름</summary>
    public static void Prompt(string text)
    {
        Ensure();
        _inst._prompt = text ?? "";
        _inst._promptFrame = Time.frameCount;
    }

    public static string NameOf(PlayerRef p)
    {
        var rs = RoomState.Instance;
        if (rs != null) return rs.NameOf(p);
        return $"Player {p.PlayerId}";
    }

    /// <summary>이름 뒤 조사 — 받침 있으면 '이가'(민준이가), 없으면 '가'(루이가), 한글 아니면 '이(가)'</summary>
    public static string IGa(string name)
    {
        if (string.IsNullOrEmpty(name)) return "이(가)";
        char c = name[name.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return "이(가)";
        return (c - 0xAC00) % 28 != 0 ? "이가" : "가";
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
    }

    // ══════════════════ 그리기 ══════════════════

    void OnGUI()
    {
        EnsureStyles();

        float s = Screen.height / 1080f;
        float W = Screen.width / s, H = Screen.height / s;
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        DrawCentralMarker(s, W, H);
        DrawAnnounce(W);
        DrawSelf(W, H);

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    void DrawAnnounce(float W)
    {
        float y = 150f;
        float now = Time.unscaledTime;
        for (int i = 0; i < _announce.Count; i++)
        {
            var m = _announce[i];
            float age = now - m.Time;
            if (age > AnnounceTime) continue;

            float a = age > AnnounceTime - 0.6f ? (AnnounceTime - age) / 0.6f : 1f;
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Box(new Rect(W / 2f - 520f, y, 1040f, 52f), m.Text, _banner);
            y += 58f;
        }
        GUI.color = Color.white;
    }

    void DrawSelf(float W, float H)
    {
        var me = PlayerAltars.Local;

        if (me != null && me.Object != null && me.Object.IsValid && me.Channel >= 0)
        {
            string label = me.IsChannelingCentral ? "중앙 제단 활성화 중..." : "제단 활성화 중...";
            DrawBar(new Rect(W / 2f - 220f, H * 0.62f, 440f, 30f), me.Progress, label);
            GUI.Label(new Rect(W / 2f - 220f, H * 0.62f + 34f, 440f, 26f), "맞거나 멀어지면 끊김 · 다시 누르면 취소", _small);
        }
        else if (_promptFrame >= Time.frameCount - 1 && _prompt.Length > 0)
        {
            GUI.Box(new Rect(W / 2f - 300f, H * 0.66f, 600f, 44f), _prompt, _box);
        }

        if (_toast.Length > 0 && Time.unscaledTime - _toastAt < ToastTime)
            GUI.Box(new Rect(W / 2f - 260f, H * 0.72f, 520f, 40f), _toast, _box);
    }

    void DrawCentralMarker(float s, float W, float H)
    {
        var central = Altar.Central;
        var cam = Camera.main;
        if (central == null || cam == null) return;

        PlayerAltars who = null;
        float best = -1f;
        foreach (var p in PlayerAltars.All)
        {
            if (p == null || p.Object == null || !p.Object.IsValid || !p.IsChannelingCentral) continue;
            float pr = p.Progress;
            if (pr > best) { best = pr; who = p; }
        }

        var me = PlayerAltars.Local;
        bool mine = me != null && me.Object != null && me.Object.IsValid && me.CanOpenCentral;
        if (who == null && !mine) return;

        float dist = Vector3.Distance(cam.transform.position, central.transform.position);
        string label = who != null
            ? $"중앙 제단 · {NameOf(who.Owner)} {Mathf.RoundToInt(best * 100f)}%  ({dist:0}m)"
            : $"중앙 제단  ({dist:0}m)";

        Vector2 pt = ToScreen(cam, central.MarkerPos, s, W, H);
        GUI.Box(new Rect(pt.x - 180f, pt.y - 22f, 360f, 36f), label, _marker);
        if (who != null) DrawBar(new Rect(pt.x - 150f, pt.y + 18f, 300f, 12f), best, "");
    }

    /// <summary>월드 위치 → 화면 (GUI 좌표). 화면 밖 · 뒤면 가장자리에 붙임</summary>
    static Vector2 ToScreen(Camera cam, Vector3 world, float s, float W, float H)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        bool behind = sp.z < 0f;
        if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }

        var pos = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
        var c = new Vector2(W / 2f, H / 2f);
        Vector2 d = pos - c;
        if (d.sqrMagnitude < 1f) d = Vector2.up;

        float hx = W / 2f - 200f, hy = H / 2f - 50f;
        float k = Mathf.Min(hx / Mathf.Max(0.001f, Mathf.Abs(d.x)), hy / Mathf.Max(0.001f, Mathf.Abs(d.y)));
        if (behind || k < 1f) pos = c + d * k;
        return pos;
    }

    void DrawBar(Rect r, float t, string label)
    {
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(t), r.height - 4f), Texture2D.whiteTexture);
        GUI.color = old;
        if (label.Length > 0)
        {
            var st = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), label, st);
            GUI.Label(r, label, _small);
        }
    }

    void EnsureStyles()
    {
        if (_banner != null) return;
        _banner = new GUIStyle(GUI.skin.box) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _banner.normal.textColor = Color.white;
        _marker = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        _marker.normal.textColor = new Color(1f, 0.85f, 0.3f);
        _box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        _box.normal.textColor = Color.white;
        _small = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        _small.normal.textColor = Color.white;
    }
}
