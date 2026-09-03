using UnityEngine;
using System.Collections;

public class HitStop : MonoBehaviour
{
    static HitStop _inst;
    static Coroutine _routine;

    void Awake() { _inst = this; }

    public static void Do(float duration)
    {
        if (_inst == null) return;
        if (_routine != null) _inst.StopCoroutine(_routine);
        _routine = _inst.StartCoroutine(_inst.Run(duration));
    }

    IEnumerator Run(float d)
    {
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(d);
        Time.timeScale = 1f;
        _routine = null;
    }
}