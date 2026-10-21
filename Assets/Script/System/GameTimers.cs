using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 'N초 뒤에 이것'. 능력이 바뀌거나(사망 시 랜덤 능력 교체) 사라져도 예약은 남음 — 데스노트 · 필연 등.
/// 이 화면에서만 돎 (네트워크 아님) → 권한자 로직에서만 쓸 것.
/// </summary>
public class GameTimers : MonoBehaviour
{
    struct Job
    {
        public float At;
        public Action Act;
    }

    static GameTimers _inst;
    readonly List<Job> _jobs = new();
    readonly List<Job> _due = new();

    static GameTimers Inst
    {
        get
        {
            if (_inst == null)
            {
                var go = new GameObject("GameTimers");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<GameTimers>();
            }
            return _inst;
        }
    }

    public static void After(float seconds, Action act)
    {
        if (act == null) return;
        Inst._jobs.Add(new Job { At = Time.time + Mathf.Max(0f, seconds), Act = act });
    }

    void Update()
    {
        if (_jobs.Count == 0) return;

        float now = Time.time;
        _due.Clear();
        for (int i = _jobs.Count - 1; i >= 0; i--)
            if (now >= _jobs[i].At) { _due.Add(_jobs[i]); _jobs.RemoveAt(i); }

        for (int i = _due.Count - 1; i >= 0; i--)
        {
            try { _due[i].Act(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
