using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 소리 내는 곳(AudioSource)을 믹서 묶음에 저절로 연결 — 기존 코드 수정 없이.
///   Photon Voice Speaker → 보이스 / SoundBusTag → 그 묶음 / 나머지 → 효과음
///   이미 묶음이 정해진 AudioSource는 안 건드림.
/// 0.5초마다 새로 생긴 것 확인 (생성 직후 아주 짧게는 기본 크기로 날 수 있음).
/// 씬에 없어도 저절로 생김.
/// </summary>
public class AudioRouter : MonoBehaviour
{
    static AudioRouter _inst;
    float _next;
    readonly HashSet<int> _done = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("AudioRouter");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<AudioRouter>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(gameObject); return; }
        _inst = this;
        SceneManager.sceneLoaded += OnScene;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnScene;
        if (_inst == this) _inst = null;
    }

    void Start()
    {
        GameAudio.Apply();        // 믹서 값은 Start 이후에 넣어야 먹음
        Route();
    }

    void OnScene(Scene s, LoadSceneMode m)
    {
        _done.Clear();
        _next = 0f;
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        Route();
    }

    void Route()
    {
        if (GameAudio.Mixer == null) return;

        foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (src == null || !_done.Add(src.GetInstanceID())) continue;
            if (src.outputAudioMixerGroup != null) continue;       // 직접 정한 건 그대로

            SoundBus bus = SoundBus.Sfx;
            var tag = src.GetComponentInParent<SoundBusTag>();
            if (tag != null) bus = tag.Bus;
            else if (src.GetComponent<Photon.Voice.Unity.Speaker>() != null) bus = SoundBus.Voice;

            var g = GameAudio.Group(bus);
            if (g != null) src.outputAudioMixerGroup = g;
        }
    }
}
