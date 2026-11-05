using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 내 캐릭터 사진 (꾸미기 그대로) — RawImage에. 켜질 때 · 꾸미기가 바뀔 때 다시 넣음.
/// 인벤토리에서 3D로 못 찍을 때 대신 보이는 그림 등
/// </summary>
[RequireComponent(typeof(RawImage))]
public class UiCharacterPhoto : MonoBehaviour
{
    public CharacterPhoto.Frame Frame = CharacterPhoto.Frame.Bust;
    public int Width = 256, Height = 360;

    RawImage _img;

    void OnEnable()
    {
        _img = GetComponent<RawImage>();
        PlayerSave.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => PlayerSave.Changed -= Refresh;

    public void Refresh()
    {
        if (_img == null || !Application.isPlaying) return;
        var t = CharacterPhoto.Get(PlayerSave.Look.PartsCode, Frame, Width, Height);
        _img.texture = t;
        _img.enabled = t != null;
    }
}
