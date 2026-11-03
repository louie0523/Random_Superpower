using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 탭 버튼 모양 — 고른 탭(누를 수 없게 된 버튼)은 강조색 면, 나머지는 보통 면.
/// ShopPanel처럼 '고른 탭 = interactable 끔'으로 상태를 드러내는 창에 붙임 (흐려지는 대신 강조)
/// </summary>
[DisallowMultipleComponent]
public class UiTabLook : MonoBehaviour
{
    public Button Button;
    public Image Face;
    public TMP_Text Label;

    int _last = -1;

    void OnEnable() => _last = -1;

    void LateUpdate()
    {
        if (Button == null || Face == null) return;
        int sel = Button.interactable ? 0 : 1;
        if (sel == _last) return;
        _last = sel;

        Face.color = sel == 1 ? GameUiKit.Mint : GameUiKit.Raised;
        if (Label != null) Label.color = GameUiKit.TextOn(Face.color);
    }
}
