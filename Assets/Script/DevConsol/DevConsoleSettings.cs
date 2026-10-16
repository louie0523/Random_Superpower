using Fusion;
using UnityEngine;

/// <summary>
/// 치트 콘솔 설정 (선택). 만들어서 Resources 폴더에 이름 그대로 "DevConsoleSettings"로 두면 읽음.
/// 없으면 기본값 — ` 키 · F8, 에디터 · Development Build에서만 켜짐.
/// </summary>
[CreateAssetMenu(menuName = "Game/Dev Console Settings", fileName = "DevConsoleSettings")]
public class DevConsoleSettings : ScriptableObject
{
    [Tooltip("콘솔 열기/닫기")]
    public KeyCode ToggleKey = KeyCode.BackQuote;
    [Tooltip("보조 키 (한글 키보드에서 ` 가 안 될 때)")]
    public KeyCode AltToggleKey = KeyCode.F8;

    [Tooltip("정식 빌드(Development Build 아님)에서도 켬 — 출시 전엔 끄기")]
    public bool EnableInReleaseBuild = false;

    [Tooltip("/dummy 로 만들 더미 (NetworkObject 프리팹, Fusion 프리팹 목록에 있어야 함)")]
    public NetworkObject DummyPrefab;

    [Tooltip("글자 크기 배율")]
    [Range(0.6f, 2f)] public float FontScale = 1f;

    static DevConsoleSettings _cached;
    public static DevConsoleSettings Get()
    {
        if (_cached == null) _cached = Resources.Load<DevConsoleSettings>("DevConsoleSettings");
        if (_cached == null) _cached = CreateInstance<DevConsoleSettings>();
        return _cached;
    }
}
