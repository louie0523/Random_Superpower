using System;

/// <summary>칠하기 모드 켜짐 · 꺼짐 — 소품(PaintHide) · 캐릭터가 듣고 숨음</summary>
public static class PaintMode
{
    public static bool On { get; private set; }
    public static event Action<bool> Changed;

    public static void Set(bool on)
    {
        if (On == on) return;
        On = on;
        Changed?.Invoke(on);
    }
}
