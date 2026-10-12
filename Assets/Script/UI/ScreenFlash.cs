using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 번쩍임 (본인 화면 전용). 처음 쓸 때 맨 위 캔버스를 스스로 만듦 — 씬에 따로 둘 것 없음.
/// ScreenFlash.Play(색, 시간)
/// 모션 이벤트(ScreenFlash 타입)는 MotionEventPlayer.OnScreenFlash로 들어옴 — 게임 시작 시 자동 연결
/// </summary>
public class ScreenFlash : MonoBehaviour
{
    static ScreenFlash _inst;

    private Image _img;
    private Color _color;
    private float _dur, _t;

    /// <summary>게임 시작 시 MotionEventPlayer의 화면 번쩍임 신호를 여기로 연결 (씬에 둘 것 없음)</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        MotionEventPlayer.OnScreenFlash -= Play;
        MotionEventPlayer.OnScreenFlash += Play;
    }

    public static void Play(Color color, float duration)
    {
        if (!Application.isPlaying) return;
        if (_inst == null) Create();

        _inst._color = color;
        _inst._dur = Mathf.Max(0.01f, duration);
        _inst._t = 0f;
        _inst._img.enabled = true;
        _inst._img.color = color;
    }

    static void Create()
    {
        var go = new GameObject("ScreenFlash", typeof(Canvas), typeof(ScreenFlash));
        DontDestroyOnLoad(go);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        var imgGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)imgGo.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        _inst = go.GetComponent<ScreenFlash>();
        _inst._img = imgGo.GetComponent<Image>();
        _inst._img.raycastTarget = false;
        _inst._img.enabled = false;
    }

    void Update()
    {
        if (_img == null || !_img.enabled) return;

        _t += Time.unscaledDeltaTime;
        float k = 1f - Mathf.Clamp01(_t / _dur);
        var c = _color;
        c.a *= k * k;                   // 빠르게 빠지는 느낌
        _img.color = c;

        if (k <= 0f) _img.enabled = false;
    }
}