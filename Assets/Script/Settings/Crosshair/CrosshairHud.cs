using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 화면 조준점 — 들고 있는 무기 종류(총 · 활 · 근접)에 맞는 모양으로 그림.
/// 움직이거나 · 공중이거나 · 쏘면 벌어짐 (총 = 실제 탄퍼짐 · 연사 반동, 활 = 덜 당길수록 넓음).
/// 씬에 없어도 저절로 생김. 내 캐릭터가 없거나(로비 · 관전) · 창이 열려 있거나 · 죽었으면 숨김.
/// 예전 조준점(GameHUD 등)이 있으면 그건 꺼 줄 것.
/// </summary>
public class CrosshairHud : MonoBehaviour
{
    static CrosshairHud _inst;

    CrosshairGraphic _g;
    Canvas _canvas;
    float _move, _fire;
    PlayerHealth _health;
    PlayerAttack _healthOf;

    // 벌어짐 세기 (각도, 도) — 보기용
    const float MoveDeg = 2f;          // 최고 속도로 움직일 때
    const float AirDeg = 1.5f;         // 공중
    const float BurstDegPerShot = 0.12f;
    const float BowOpenDeg = 2.5f;     // 활 — 안 당겼을 때
    const float FullSpeed = 7f;        // 이 속도(m/s)면 움직임 벌어짐 최대

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("CrosshairHud", typeof(RectTransform));
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<CrosshairHud>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(gameObject); return; }
        _inst = this;

        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        _canvas.pixelPerfect = true;
        var sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;

        var rt = new GameObject("Crosshair", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        _g = rt.gameObject.AddComponent<CrosshairGraphic>();
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
    }

    void LateUpdate()
    {
        var atk = PlayerAttack.Local;
        if (!CrosshairSettings.Enabled || atk == null || UIManager.InputBlocked || EscMenu.IsOpen || SettingsWindow.IsOpen)
        {
            _g.Hide();
            return;
        }

        if (_healthOf != atk) { _healthOf = atk; _health = atk.GetComponent<PlayerHealth>(); }
        if (_health != null && _health.IsDead) { _g.Hide(); return; }

        var kind = KindOf(atk);
        float fov = Camera.main != null ? Camera.main.fieldOfView : 90f;

        // 움직임
        float moveDeg = 0f;
        var cc = atk.Body;
        if (cc != null)
        {
            var v = cc.velocity;
            v.y = 0f;
            moveDeg = Mathf.Clamp01(v.magnitude / FullSpeed) * MoveDeg;
            if (!cc.isGrounded) moveDeg += AirDeg;
        }

        // 쏘기
        float fireDeg = 0f;
        var w = atk.CurrentWeapon;
        if (kind == CrosshairKind.Gun && w != null)
            fireDeg = atk.GunSpreadDeg(w) + Mathf.Min(atk.BurstShots, 10) * BurstDegPerShot;
        else if (kind == CrosshairKind.Bow)
            fireDeg = (1f - atk.DrawRatio01) * BowOpenDeg;

        float k = 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime);
        _move = Mathf.Lerp(_move, DegToPx(moveDeg, fov), k);
        _fire = Mathf.Lerp(_fire, DegToPx(fireDeg, fov), k);

        _g.Draw(CrosshairSettings.For(kind), _move, _fire);
    }

    /// <summary>각도(도) → 화면 거리 (1080p 픽셀)</summary>
    public static float DegToPx(float deg, float fov) =>
        Mathf.Tan(deg * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 540f;

    public static CrosshairKind KindOf(PlayerAttack atk)
    {
        var w = atk.CurrentWeapon;
        if (atk.ForceMelee || w == null || w.Class != WeaponClass.Ranged) return CrosshairKind.Melee;
        return w.RangedKind == RangedType.Gun ? CrosshairKind.Gun : CrosshairKind.Bow;
    }
}
