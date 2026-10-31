using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 크기 설정을 이 캔버스에 적용 — HUD 캔버스(Canvas Scaler가 있는 오브젝트)에 붙이기만 하면 됨.
/// Scale With Screen Size면 기준 해상도를, Constant Pixel Size면 Scale Factor를 바꿈.
/// 설정 창 · 메뉴 캔버스에는 붙이지 말 것.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class HudScaler : MonoBehaviour
{
    CanvasScaler _sc;
    Vector2 _baseRes;
    float _baseFactor;
    float _applied = -1f;

    void Awake()
    {
        _sc = GetComponent<CanvasScaler>();
        _baseRes = _sc.referenceResolution;
        _baseFactor = _sc.scaleFactor;
    }

    void OnEnable() => GameSettings.Changed += Apply;
    void OnDisable() => GameSettings.Changed -= Apply;
    void Start() => Apply();

    void Apply()
    {
        float s = GameSettings.HudScale;
        if (Mathf.Approximately(s, _applied) || _sc == null) return;
        _applied = s;

        if (_sc.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            _sc.referenceResolution = _baseRes / Mathf.Max(0.1f, s);      // 기준이 작아지면 UI가 커짐
        else
            _sc.scaleFactor = _baseFactor * s;
    }
}
