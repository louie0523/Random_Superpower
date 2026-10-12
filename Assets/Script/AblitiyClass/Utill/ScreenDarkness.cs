using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 본인 화면 어둡게 — 암전(Blind 상태이상) · 그림자 영역 안 시야 축소.
/// 처음 쓸 때 맨 위 캔버스를 스스로 만듦 — 씬에 둘 것 없음.
///   암전: 내 PlayerStatus에 Blind가 걸려 있으면 화면이 거의 까맣게
///   영역: ScreenDarkness.PushZone(세기 0~1)을 매 프레임 부르면 가장자리부터 어두워짐 (안 부르면 풀림)
/// </summary>
public class ScreenDarkness : MonoBehaviour
{
    static ScreenDarkness _inst;

    [Tooltip("암전 최대 불투명도")]
    public float BlindAlpha = 0.97f;
    [Tooltip("영역 — 화면 전체를 이만큼 어둡게")]
    public float ZoneDim = 0.55f;
    public float FadeSpeed = 10f;

    private Image _blind, _vignette, _dim;
    private float _blindA, _zoneA;
    private float _zonePush;

    public static void PushZone(float strength)
    {
        if (!Application.isPlaying) return;
        Ensure();
        _inst._zonePush = Mathf.Max(_inst._zonePush, Mathf.Clamp01(strength));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Ensure()
    {
        if (_inst != null) return;

        var go = new GameObject("ScreenDarkness", typeof(Canvas), typeof(ScreenDarkness));
        DontDestroyOnLoad(go);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;        // 화면 번쩍임(32000) 바로 아래, HUD 위

        _inst = go.GetComponent<ScreenDarkness>();
        _inst._dim = MakeImage(go.transform, "Dim", null);
        _inst._vignette = MakeImage(go.transform, "Vignette", MakeVignette());
        _inst._blind = MakeImage(go.transform, "Blind", null);
    }

    static Image MakeImage(Transform parent, string name, Sprite sprite)
    {
        var o = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)o.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = o.GetComponent<Image>();
        img.sprite = sprite;
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    /// <summary>가운데만 조금 보이고 가장자리가 까만 원형 그라데이션</summary>
    static Sprite MakeVignette()
    {
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.62f, d));
                px[y * N + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
    }

    void LateUpdate()
    {
        var local = AbilityDescTokens.Local;
        var st = local != null ? local.Status : null;
        bool blind = st != null && st.Has(StatusType.Blind);

        float k = 1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime);
        _blindA = Mathf.Lerp(_blindA, blind ? BlindAlpha : 0f, k);
        _zoneA = Mathf.Lerp(_zoneA, _zonePush, k);
        _zonePush = 0f;

        Set(_blind, _blindA);
        Set(_vignette, _zoneA);
        Set(_dim, _zoneA * ZoneDim);
    }

    static void Set(Image img, float a)
    {
        bool on = a > 0.005f;
        if (img.enabled != on) img.enabled = on;
        if (on) img.color = new Color(0f, 0f, 0f, a);
    }
}
