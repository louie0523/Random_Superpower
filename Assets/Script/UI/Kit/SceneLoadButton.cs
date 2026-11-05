using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>누르면 씬 이동 — 씬 이름이 비면 GameMeta의 타이틀 씬</summary>
[RequireComponent(typeof(Button))]
public class SceneLoadButton : MonoBehaviour
{
    public string Scene;

    void Awake() => GetComponent<Button>().onClick.AddListener(Go);

    void Go()
    {
        string s = Scene;
        if (string.IsNullOrEmpty(s)) s = GameMeta.Get() != null ? GameMeta.Get().TitleScene : "";
        if (!string.IsNullOrEmpty(s)) SceneManager.LoadScene(s);
        else Debug.LogWarning("[씬 이동] 씬 이름이 없어 (GameMeta의 Title Scene)");
    }
}
