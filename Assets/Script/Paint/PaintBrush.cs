using System;
using UnityEngine;

public enum BrushKind { Spray, Marker, Eraser }

/// <summary>
/// 붓 설정 — 타이틀 벽 칠하기 · 에디터 그림 창 · UI 위 칠하기가 같이 씀.
/// 크기는 0~1 (쓰는 곳마다 실제 크기로 바꿈: 벽 = 미터, 그림 = 픽셀)
/// </summary>
[Serializable]
public class PaintBrush
{
    public BrushKind Kind = BrushKind.Spray;
    public Color Color = new(1f, 0.84f, 0.2f, 1f);
    [Range(0f, 1f)] public float Size = 0.35f;
    [Range(0.05f, 1f)] public float Opacity = 1f;

    [Header("보정")]
    [Tooltip("손떨림 보정 — 마우스가 떨려도 선이 부드럽게 (끈으로 끄는 느낌)")]
    public bool Stabilize = true;
    [Range(0f, 1f)] public float StabilizeAmount = 0.5f;
    [Tooltip("스프레이: 멈춰 있던 곳 · 획 끝에서 물감이 흘러내림")]
    public bool Drips = true;
    [Tooltip("칠한 덩어리 둘레에 검은 외곽선 (카툰)")]
    public bool Outline;
    [Tooltip("그리고 잠깐 멈추면 반듯한 직선 · 원으로 (계속 누른 채로 끌면 크기 조절)")]
    public bool Snap = true;

    public bool IsEraser => Kind == BrushKind.Eraser;
    public bool IsSpray => Kind == BrushKind.Spray;

    /// <summary>크기 0~1 → min~max (작은 쪽이 더 촘촘하게)</summary>
    public float Radius(float min, float max) => Mathf.Lerp(min, max, Size * Size);

    public PaintBrush Clone() => (PaintBrush)MemberwiseClone();

    public static readonly Color Ink = new(0.067f, 0.067f, 0.067f, 1f);

    /// <summary>외곽선 두께 (픽셀) — 붓 반지름에 비례 (12%, 1.5 ~ 12)</summary>
    public static float OutlinePx(float radiusPx) => Mathf.Clamp(radiusPx * 0.12f, 1.5f, 12f);

    /// <summary>기본 물감 색 (그래피티 테마)</summary>
    public static readonly Color[] Palette =
    {
        new(0.969f, 0.953f, 0.918f), // 종이 흰색
        new(0.067f, 0.067f, 0.067f), // 먹
        new(1f, 0.84f, 0.2f),        // 노랑
        new(1f, 0.55f, 0.16f),       // 주황
        new(0.93f, 0.22f, 0.24f),    // 빨강
        new(1f, 0.36f, 0.62f),       // 분홍
        new(0.62f, 0.4f, 1f),        // 보라
        new(0.247f, 0.663f, 1f),     // 파랑
        new(0.31f, 0.84f, 0.75f),    // 민트
        new(0.47f, 0.86f, 0.3f),     // 초록
    };
}

/// <summary>붓 자국 한 점 (그림 픽셀 기준)</summary>
public struct PaintDab
{
    public Vector2 P;
    public float R;
    public float A;
    public float Hard;

    public PaintDab(Vector2 p, float r, float a, float hard)
    {
        P = p;
        R = r;
        A = a;
        Hard = hard;
    }
}
