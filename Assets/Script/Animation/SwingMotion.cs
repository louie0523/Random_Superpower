using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SwingMotion
{
    [Tooltip("시간순 키프레임. 최소 2개")]
    public List<PoseKey> Keys = new();

    [Tooltip("전체 진폭 배율")]
    public float Amplitude = 1f;

    public bool IsValid => Keys != null && Keys.Count >= 2;

    /// <summary>t는 0~1</summary>
    public ArmPose Evaluate(float t, ArmPose idle)
    {
        if (!IsValid) return idle;

        t = Mathf.Clamp01(t);

        // 구간 찾기
        for (int i = 0; i < Keys.Count - 1; i++)
        {
            var a = Keys[i];
            var b = Keys[i + 1];
            if (t < a.Time || t > b.Time) continue;

            float span = Mathf.Max(0.0001f, b.Time - a.Time);
            float local = (t - a.Time) / span;
            float eased = b.Ease != null ? b.Ease.Evaluate(local) : local;

            return ArmPose.Lerp(a.Pose, b.Pose, eased);
        }

        return t <= Keys[0].Time ? Keys[0].Pose : Keys[^1].Pose;
    }

    public void SortKeys() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
}