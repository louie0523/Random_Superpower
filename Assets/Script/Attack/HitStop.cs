using UnityEngine;

/// <summary>타격감용 시각 정지. timeScale은 건드리지 않는다 (네트워크 영향).</summary>
public static class HitStop
{
    static WeaponAnimator _local;

    /// <summary>로컬 플레이어 스폰 시 등록</summary>
    public static void Register(WeaponAnimator anim) => _local = anim;
    public static void Unregister(WeaponAnimator anim)
    {
        if (_local == anim) _local = null;
    }

    public static void Do(float duration)
    {
        if (duration <= 0f) return;
        if (_local == null) return;
        _local.Freeze(duration);
    }
}