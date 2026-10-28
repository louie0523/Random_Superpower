using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// Assets/Editor 폴더 안에 넣어야 합니다.
// 메뉴: Tools > Hackatime
public class HackatimeWindow : EditorWindow
{
    const string KeyPref = "Hackatime_ApiKey"; // API 키는 프로젝트가 아니라 내 PC(EditorPrefs)에 저장됨 → 깃에 안 올라감
    const string Url = "https://hackatime.hackclub.com/api/hackatime/v1/users/current/statusbar/today";
    const double IntervalSeconds = 60;

    string apiKey;
    string todayText = "-";
    string status = "API 키를 입력하세요";
    double nextFetch;
    UnityWebRequest req;
    bool showSettings;
    GUIStyle bigStyle;

    [Serializable] class Response { public Data data; }
    [Serializable] class Data { public GrandTotal grand_total; }
    [Serializable] class GrandTotal { public float total_seconds; public string text; }

    [MenuItem("Tools/Hackatime")]
    static void Open() => GetWindow<HackatimeWindow>("Hackatime");

    void OnEnable()
    {
        apiKey = EditorPrefs.GetString(KeyPref, "");
        showSettings = string.IsNullOrEmpty(apiKey);
        nextFetch = 0;
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        req?.Dispose();
        req = null;
    }

    void Tick()
    {
        // 요청 진행 중이면 끝났는지 확인
        if (req != null)
        {
            if (!req.isDone) return;

            if (req.result == UnityWebRequest.Result.Success)
            {
                var res = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                string t = res?.data?.grand_total?.text;
                todayText = string.IsNullOrEmpty(t) ? "0 mins" : t;
                status = "갱신: " + DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                status = req.responseCode == 401
                    ? "API 키가 틀렸어요 (401)"
                    : "오류: " + req.error;
            }

            req.Dispose();
            req = null;
            Repaint();
            return;
        }

        if (string.IsNullOrEmpty(apiKey)) return;
        if (EditorApplication.timeSinceStartup < nextFetch) return;

        nextFetch = EditorApplication.timeSinceStartup + IntervalSeconds;
        req = UnityWebRequest.Get(Url);
        req.SetRequestHeader("Authorization", "Bearer " + apiKey);
        req.SendWebRequest();
    }

    void OnGUI()
    {
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter
            };
        }

        GUILayout.Space(6);
        GUILayout.Label("오늘 코딩 시간", EditorStyles.centeredGreyMiniLabel);
        GUILayout.Label(todayText, bigStyle, GUILayout.Height(36));
        GUILayout.Label(status, EditorStyles.centeredGreyMiniLabel);

        GUILayout.Space(4);
        if (GUILayout.Button("지금 새로고침")) nextFetch = 0;

        showSettings = EditorGUILayout.Foldout(showSettings, "설정");
        if (showSettings)
        {
            apiKey = EditorGUILayout.PasswordField("API Key", apiKey);
            if (GUILayout.Button("저장"))
            {
                apiKey = apiKey.Trim();
                EditorPrefs.SetString(KeyPref, apiKey);
                nextFetch = 0;
                status = "불러오는 중...";
            }
        }
    }
}
