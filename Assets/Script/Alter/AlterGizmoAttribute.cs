using System;

/// <summary>변화 설정 창에서 그릴 모양</summary>
public enum AlterGizmoShape
{
    Cone,       // 부채꼴 (Pitch면 3D 원뿔)
    Sphere,     // 구
    Circle,     // 바닥 원 (충격파)
}

/// <summary>모양이 나오는 자리</summary>
public enum AlterGizmoOrigin
{
    Body,       // 플레이어 발밑 + Up · Forward
    Bone,       // 모델의 뼈 (Bone 칸의 이름) — 없으면 Offset 칸 (플레이어 기준 위치)
}

/// <summary>
/// 능력(AbilityBehaviour)에 붙이면 '변화 설정 창'(Tools → Game → 변화 설정)이 그 범위를 변신 모델 위에 그리고
/// 칸 값을 마우스로 고칠 수 있게 함. 게임 동작에는 영향 없음 (에디터 표시용).
/// 문자열 칸은 전부 그 능력의 필드 이름 (중첩은 "LandWave.Radius"처럼 점으로).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class AlterGizmoAttribute : Attribute
{
    public string Label;
    public AlterGizmoShape Shape;

    /// <summary>부채꼴 길이 (m) 필드</summary>
    public string Range;
    /// <summary>부채꼴 각도 (°) 필드</summary>
    public string Angle;
    /// <summary>부채꼴 위아래 허용 높이 (m) 필드 — 수평 부채꼴만</summary>
    public string Height;
    /// <summary>구 · 원 반지름 (m) 필드</summary>
    public string Radius;

    public AlterGizmoOrigin Origin = AlterGizmoOrigin.Body;
    /// <summary>발밑에서 위로 (m)</summary>
    public float Up;
    /// <summary>앞으로 (m) — 고정값</summary>
    public float Forward;
    /// <summary>앞으로 (m) 필드 — 있으면 Forward 대신</summary>
    public string ForwardField;
    /// <summary>뼈 이름 (string) 필드 — Origin = Bone</summary>
    public string Bone;
    /// <summary>뼈가 없을 때 위치 (Vector3, 플레이어 기준) 필드</summary>
    public string Offset;
    /// <summary>시선 위아래를 따라감 (활공 · 브레스)</summary>
    public bool Pitch;

    /// <summary>타이밍 — 상태 이름 (string) 필드. "=Roar"처럼 =로 시작하면 이름 그대로</summary>
    public string State;
    /// <summary>판정 구간 시작 · 끝 프레임 (int) 필드. 둘 다 비우면 그 상태의 Hit Frame 순간, State도 비우면 항상</summary>
    public string StartFrame, EndFrame;
    /// <summary>켜짐 (bool) 필드 — 꺼져 있으면 흐리게</summary>
    public string Enabled;

    /// <summary>색 (#RRGGBB)</summary>
    public string Color = "#FF8040";

    public AlterGizmoAttribute(string label, AlterGizmoShape shape)
    {
        Label = label;
        Shape = shape;
    }
}
