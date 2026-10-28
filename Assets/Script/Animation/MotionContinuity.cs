using UnityEngine;

/// <summary>
/// '이전 타 위치에서 시작' — 다음 타의 첫 키 자리에 지금(이전 타를 누른 순간) 자세를 넣고,
/// 두 번째 키까지 가면서 원래 모션으로 부드럽게 돌아감. (1타 오른→왼 · 2타 왼→오 같은 이어 베기)
/// 손 · 무기(파츠 포함) · 몸 · 머리 위치/회전/크기에 적용 (손가락 · 카메라는 그대로)
/// </summary>
public sealed class MotionContinuity
{
    ArmPose _off;
    float _start, _end;

    /// <summary>current = 지금 보이는 자세 (이전 타의 마지막 자세). 모션이 없으면 null</summary>
    public static MotionContinuity From(ArmPose current, SwingMotion m)
    {
        if (current == null || m == null || !m.IsValid) return null;
        var k0 = m.Keys[0].Pose;
        if (k0 == null) return null;

        var off = new ArmPose
        {
            RightPos = current.RightPos - k0.RightPos,
            RightRot = Delta(k0.RightRot, current.RightRot),
            LeftPos = current.LeftPos - k0.LeftPos,
            LeftRot = Delta(k0.LeftRot, current.LeftRot),
            WeaponPos = current.WeaponPos - k0.WeaponPos,
            WeaponRot = Delta(k0.WeaponRot, current.WeaponRot),
            WeaponScale = ArmPose.Scl(current.WeaponScale) - ArmPose.Scl(k0.WeaponScale),
            BodyPos = current.BodyPos - k0.BodyPos,
            BodyRot = Delta(k0.BodyRot, current.BodyRot),
            HeadPos = current.HeadPos - k0.HeadPos,
            HeadRot = Delta(k0.HeadRot, current.HeadRot),
        };

        int n = Mathf.Max(current.Parts?.Length ?? 0, k0.Parts?.Length ?? 0);
        for (int j = 0; j < n; j++)
        {
            var e = off.Extra(j);
            e.Pos = current.ExtraPos(j) - k0.ExtraPos(j);
            e.Rot = Delta(k0.ExtraRot(j), current.ExtraRot(j));
            e.Scale = current.PartScale(j + 1) - k0.PartScale(j + 1);
        }

        return new MotionContinuity
        {
            _off = off,
            _start = m.Keys[0].Time,
            _end = m.Keys.Count > 1 ? Mathf.Max(m.Keys[1].Time, m.Keys[0].Time + 0.001f) : 1f,
        };
    }

    /// <summary>t01 = 모션 진행 (0~1). 첫 키 → 두 번째 키 사이에서 차이가 0으로 줄어듦</summary>
    public void Apply(ArmPose p, float t01)
    {
        if (p == null || _off == null) return;
        float u = Mathf.Clamp01((t01 - _start) / (_end - _start));
        float w = 1f - u * u * (3f - 2f * u);
        if (w <= 0.0001f) return;

        p.RightPos += _off.RightPos * w;
        p.RightRot += _off.RightRot * w;
        p.LeftPos += _off.LeftPos * w;
        p.LeftRot += _off.LeftRot * w;
        p.WeaponPos += _off.WeaponPos * w;
        p.WeaponRot += _off.WeaponRot * w;
        p.WeaponScale = ArmPose.Scl(p.WeaponScale) + _off.WeaponScale * w;
        p.BodyPos += _off.BodyPos * w;
        p.BodyRot += _off.BodyRot * w;
        p.HeadPos += _off.HeadPos * w;
        p.HeadRot += _off.HeadRot * w;

        if (_off.Parts != null)
            for (int j = 0; j < _off.Parts.Length; j++)
            {
                var o = _off.Parts[j];
                if (o == null) continue;
                var e = p.Extra(j);
                e.Pos += o.Pos * w;
                e.Rot += o.Rot * w;
                e.Scale = ArmPose.Scl(e.Scale) + o.Scale * w;
            }
    }

    static Vector3 Delta(Vector3 from, Vector3 to) =>
        new(Mathf.DeltaAngle(from.x, to.x), Mathf.DeltaAngle(from.y, to.y), Mathf.DeltaAngle(from.z, to.z));
}
