using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

// Assets/Editor 폴더 안에 넣어야 합니다.
// Scene 뷰 위에 오늘 코딩 시간을 항상 띄워줍니다.
// API 키는 Tools > Hackatime 창에서 저장한 것을 같이 씁니다.
[Overlay(typeof(SceneView), "hackatime-today", "Hackatime", true)]
public class HackatimeOverlay : Overlay
{
    const string KeyPref = "Hackatime_ApiKey";
    const string Url = "https://hackatime.hackclub.com/api/hackatime/v1/users/current/statusbar/today";
    const long IntervalMs = 60_000;

    Label label;

    [Serializable] class Response { public Data data; }
    [Serializable] class Data { public GrandTotal grand_total; }
    [Serializable] class GrandTotal { public float total_seconds; public string text; }

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.paddingTop = 2;
        root.style.paddingBottom = 2;

        label = new Label("불러오는 중...");
        label.style.fontSize = 14;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.Add(label);

        // 클릭하면 바로 새로고침
        root.RegisterCallback<MouseDownEvent>(_ => Fetch());

        Fetch();
        root.schedule.Execute(Fetch).Every(IntervalMs);
        return root;
    }

    void Fetch()
    {
        string key = EditorPrefs.GetString(KeyPref, "");
        if (string.IsNullOrEmpty(key))
        {
            label.text = "Tools > Hackatime에서 키 입력";
            return;
        }

        var req = UnityWebRequest.Get(Url);
        req.SetRequestHeader("Authorization", "Bearer " + key);
        req.SendWebRequest().completed += _ =>
        {
            if (label != null)
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var res = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    string t = res?.data?.grand_total?.text;
                    label.text = "오늘 " + (string.IsNullOrEmpty(t) ? "0 mins" : t);
                }
                else
                {
                    label.text = req.responseCode == 401 ? "API 키 오류" : "연결 오류";
                }
            }
            req.Dispose();
        };
    }
}
