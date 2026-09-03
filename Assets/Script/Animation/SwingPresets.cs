using UnityEngine;
using System.Collections.Generic;

public static class SwingPresets
{
    public enum Kind
    {
        DiagonalSlash,      // 전방 사선 베기
        HorizontalSweep,    // 수평 휘두르기
        Overhead,           // 위로 들었다 내려찍기
        Thrust,             // 찌르기
        UpperCut,           // 올려베기
        TwoHandSmash,       // 양손 내려찍기
        BackhandReturn,     // 반대 방향 되돌려베기
        SpinSlash,          // 회전 베기
    }

    /// <summary>idle을 기준으로 상대 오프셋을 적용해 모션 생성</summary>
    public static SwingMotion Create(Kind kind, ArmPose idle, bool useLeftHand = false)
    {
        idle ??= new ArmPose();
        var m = new SwingMotion { Keys = new List<PoseKey>() };

        switch (kind)
        {
            case Kind.DiagonalSlash:
                // 오른쪽 위로 들었다 왼쪽 아래로 베기
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.32f, O(+0.12f, +0.28f, -0.12f), R(-35, +42, -45), EaseOut());
                K(m, idle, 0.52f, O(-0.34f, -0.14f, +0.10f), R(+28, -42, +42), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.HorizontalSweep:
                // 오른쪽에서 왼쪽으로 수평
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.30f, O(+0.20f, +0.04f, -0.20f), R(0, +58, -22), EaseOut());
                K(m, idle, 0.50f, O(-0.44f, +0.02f, +0.04f), R(0, -55, +22), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.Overhead:
                // 머리 위로 들었다 정면 아래로
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.38f, O(-0.06f, +0.50f, -0.18f), R(-75, 0, -8), EaseOut());
                K(m, idle, 0.56f, O(-0.06f, -0.26f, +0.12f), R(+62, 0, +4), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.Thrust:
                // 뒤로 당겼다 앞으로 쭉
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.30f, O(+0.02f, -0.03f, -0.24f), R(+6, -5, 0), EaseOut());
                K(m, idle, 0.46f, O(-0.02f, +0.02f, +0.46f), R(-6, +2, 0), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.UpperCut:
                // 아래로 내렸다 위로 올려베기
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.34f, O(+0.06f, -0.28f, -0.14f), R(+52, +12, -18), EaseOut());
                K(m, idle, 0.54f, O(-0.12f, +0.46f, +0.12f), R(-58, -16, +24), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.TwoHandSmash:
                // 양손으로 머리 위 → 정면 아래
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0), null, true);
                K(m, idle, 0.42f, O(-0.02f, +0.56f, -0.22f), R(-82, 0, 0), EaseOut(), true);
                K(m, idle, 0.60f, O(-0.02f, -0.30f, +0.14f), R(+72, 0, 0), Sharp(), true);
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn(), true);
                break;

            case Kind.BackhandReturn:
                // 왼쪽에서 오른쪽으로 (2타용)
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.28f, O(-0.28f, +0.06f, -0.14f), R(0, -55, +26), EaseOut());
                K(m, idle, 0.48f, O(+0.34f, -0.04f, +0.06f), R(0, +52, -26), Sharp());
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;

            case Kind.SpinSlash:
                // 크게 한 바퀴
                K(m, idle, 0.00f, O(0, 0, 0), R(0, 0, 0));
                K(m, idle, 0.25f, O(+0.22f, +0.10f, -0.22f), R(0, +72, -30), EaseOut());
                K(m, idle, 0.45f, O(-0.10f, +0.24f, -0.30f), R(-22, +180, 0), Sharp());
                K(m, idle, 0.65f, O(-0.38f, -0.06f, +0.06f), R(+12, -72, +30));
                K(m, idle, 1.00f, O(0, 0, 0), R(0, 0, 0), EaseIn());
                break;
        }

        // 양손 무기면 전 키의 왼손을 켬
        if (useLeftHand)
            foreach (var k in m.Keys) k.Pose.UseLeftHand = true;

        return m;
    }

    // ══════════ 헬퍼 ══════════

    /// <summary>idle 기준으로 오프셋을 더한 키를 추가</summary>
    static void K(SwingMotion m, ArmPose idle, float time,
                  Vector3 posOffset, Vector3 rotOffset,
                  AnimationCurve ease = null, bool twoHand = false)
    {
        var pose = idle.Clone();

        pose.RightPos = idle.RightPos + posOffset;
        pose.RightRot = idle.RightRot + rotOffset;

        if (twoHand || idle.UseLeftHand)
        {
            pose.UseLeftHand = true;
            // 왼손은 오른손을 살짝 완화해서 따라감
            pose.LeftPos = idle.LeftPos + posOffset * 0.85f;
            pose.LeftRot = idle.LeftRot + rotOffset * 0.85f;
        }

        pose.WeaponPos = Vector3.zero;
        pose.WeaponRot = Vector3.zero;

        m.Keys.Add(new PoseKey
        {
            Time = time,
            Pose = pose,
            Ease = ease ?? AnimationCurve.EaseInOut(0, 0, 1, 1)
        });
    }

    static Vector3 O(float x, float y, float z) => new(x, y, z);
    static Vector3 R(float x, float y, float z) => new(x, y, z);

    static AnimationCurve EaseOut() =>
        new(new Keyframe(0, 0, 0, 2f), new Keyframe(1, 1, 0.2f, 0));
    static AnimationCurve EaseIn() =>
        new(new Keyframe(0, 0, 0, 0.2f), new Keyframe(1, 1, 2f, 0));
    static AnimationCurve Sharp() =>
        new(new Keyframe(0, 0, 0, 0.1f), new Keyframe(1, 1, 3.5f, 0));
}