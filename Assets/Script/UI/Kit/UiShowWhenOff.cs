using UnityEngine;

/// <summary>
/// 그림(Image)이 꺼져 있을 때만 대신 보이는 것 (스킬 아이콘이 없으면 키 글자 등).
/// Also가 켜져 있으면(빈 칸 표시 등) 안 보임. 둘의 부모에 붙임
/// </summary>
public class UiShowWhenOff : MonoBehaviour
{
    public UnityEngine.UI.Graphic Watch;
    public GameObject Also;
    public GameObject Target;

    void LateUpdate()
    {
        if (Target == null) return;
        bool on = (Watch == null || !Watch.enabled || !Watch.gameObject.activeSelf)
               && (Also == null || !Also.activeSelf);
        if (Target.activeSelf != on) Target.SetActive(on);
    }
}
