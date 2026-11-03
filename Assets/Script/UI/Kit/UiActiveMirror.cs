using UnityEngine;

/// <summary>
/// 글자 뒤 배경처럼 '다른 오브젝트가 켜져 있을 때만' 보여야 하는 것들을 따라 켜고 끔.
/// (코드가 글자 오브젝트만 켜고 끄는 칸 — 배경은 글자 앞 형제로 두고, 이걸 둘의 부모에 붙임)
/// </summary>
public class UiActiveMirror : MonoBehaviour
{
    public GameObject Source;
    public GameObject[] Targets;

    void LateUpdate()
    {
        if (Source == null || Targets == null) return;
        bool on = Source.activeSelf;
        foreach (var t in Targets)
            if (t != null && t.activeSelf != on) t.SetActive(on);
    }
}
