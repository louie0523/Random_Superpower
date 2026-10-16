using System;
using System.Reflection;
using Fusion;
using UnityEngine;

/// <summary>
/// 순간이동 직후 1인칭 화면이 버벅이는 것 방지.
/// Shared Mode에서는 내 캐릭터(State Authority)도 NetworkTransform이 틱 사이를 보간해서 그리는데,
/// 순간이동한 틱에 옛 위치 ↔ 새 위치 사이가 한두 프레임 그려지면 카메라(몸의 자식)가 튐.
/// → 순간이동 뒤 몇 틱 동안만 보간을 끄고(DisableSharedModeInterpolation) 다시 원래대로.
/// PlayerHealth.Teleport가 부름. 플레이어에 자동으로 붙음 (프리팹에 넣을 필요 없음).
/// Fusion 버전에 그 설정이 없으면 아무것도 안 함.
/// </summary>
public class TeleportInterpolationFix : MonoBehaviour
{
    [Tooltip("순간이동 뒤 보간을 끄는 틱 수")]
    public int HoldTicks = 3;

    static FieldInfo _flagsField;
    static object _flagValue;
    static bool _looked, _supported;

    private NetworkTransform _nt;
    private NetworkRunner _runner;
    private int _restoreTick = -1;
    private bool _wasSet;

    public static void Apply(GameObject player, NetworkTransform nt, NetworkRunner runner)
    {
        if (player == null || nt == null || runner == null) return;
        if (!Lookup()) return;
        var fix = player.GetComponent<TeleportInterpolationFix>();
        if (fix == null) fix = player.AddComponent<TeleportInterpolationFix>();
        fix.Begin(nt, runner);
    }

    static bool Lookup()
    {
        if (_looked) return _supported;
        _looked = true;
        try
        {
            _flagsField = typeof(NetworkTransform).GetField("ConfigFlags", BindingFlags.Public | BindingFlags.Instance);
            if (_flagsField != null && _flagsField.FieldType.IsEnum)
            {
                _flagValue = Enum.Parse(_flagsField.FieldType, "DisableSharedModeInterpolation");
                _supported = true;
            }
        }
        catch { _supported = false; }
        if (!_supported) Debug.Log("[TeleportFix] 이 Fusion 버전엔 DisableSharedModeInterpolation이 없음 — 건너뜀");
        return _supported;
    }

    void Begin(NetworkTransform nt, NetworkRunner runner)
    {
        if (_restoreTick < 0)
        {
            _nt = nt;
            _wasSet = Has(nt);
            Set(nt, true);
        }
        _runner = runner;
        _restoreTick = runner.Tick + Mathf.Max(1, HoldTicks);
    }

    void LateUpdate()
    {
        if (_restoreTick < 0) return;
        if (_nt == null || _runner == null || !_runner.IsRunning) { _restoreTick = -1; return; }
        if (_runner.Tick < _restoreTick) return;

        Set(_nt, _wasSet);
        _restoreTick = -1;
    }

    static bool Has(NetworkTransform nt)
    {
        long cur = Convert.ToInt64(_flagsField.GetValue(nt));
        long f = Convert.ToInt64(_flagValue);
        return (cur & f) != 0;
    }

    static void Set(NetworkTransform nt, bool on)
    {
        long cur = Convert.ToInt64(_flagsField.GetValue(nt));
        long f = Convert.ToInt64(_flagValue);
        long next = on ? (cur | f) : (cur & ~f);
        _flagsField.SetValue(nt, Enum.ToObject(_flagsField.FieldType, next));
    }
}
