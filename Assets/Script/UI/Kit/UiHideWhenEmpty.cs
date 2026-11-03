using UnityEngine;

/// <summary>글자가 비면 Targets(배경 칩 등)를 숨김 — 둘의 부모에 붙임</summary>
public class UiHideWhenEmpty : MonoBehaviour
{
    public TMPro.TMP_Text Text;
    public GameObject[] Targets;

    void LateUpdate()
    {
        if (Text == null || Targets == null) return;
        bool on = !string.IsNullOrEmpty(Text.text);
        foreach (var t in Targets)
            if (t != null && t.activeSelf != on) t.SetActive(on);
    }
}
