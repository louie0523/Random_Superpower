using UnityEngine;

/// <summary>
/// 헤드샷 판정 · 배율 (총 · 투사체 무기).
///  - 판정 (때린 쪽 권한자에서):
///    1) 맞은 콜라이더에 <see cref="HeadZone"/>이 붙어 있으면 머리 (프리팹 머리 콜라이더 · 변화 피격 범위의 '머리' 체크)
///    2) 변화(변신) 피격 범위인데 HeadZone이 없으면 몸
///    3) 그 밖 — 맞은 높이가 <see cref="PlayerHealth.HeadBottomY"/> 위면 머리 (캐릭터 컨트롤러 위쪽 20% · 최소 0.3m)
///  - 배율: 무기 <see cref="EquipmentData.HeadshotMul"/> (0 = 기본: 총 2 · 활/지팡이/투척 1.5 · -1 = 헤드샷 없음)
///          + 때린 사람 스탯 <see cref="StatType.HeadshotDamage"/> (0.25 = +0.25배)
///  - 감소: 맞는 쪽에서 <see cref="StatType.HeadshotReduction"/> (투구 · 0.2 = 헤드샷 피해 20% 감소, 몸샷보다 아래로는 안 내려감)
///  - 갑옷: <see cref="StatType.BodyDamageReduction"/> (0.15 = 받는 피해 15% 감소) — 헤드샷이 아닐 때만 (<see cref="BodyArmor"/>)
/// </summary>
public static class Headshot
{
    public const float GunMul = 2f;
    public const float ProjectileMul = 1.5f;

    /// <summary>이 무기의 기본 헤드샷 배율 — 0이면 헤드샷 없음 (근접 · 맨손)</summary>
    public static float BaseMul(EquipmentData w)
    {
        if (w == null || w.Class != WeaponClass.Ranged) return 0f;
        if (w.HeadshotMul < 0f) return 0f;
        if (w.HeadshotMul > 0f) return w.HeadshotMul;
        return w.RangedKind == RangedType.Gun ? GunMul : ProjectileMul;
    }

    /// <summary>때린 사람 스탯까지 더한 배율 — 0이면 헤드샷 없음</summary>
    public static float MulFor(EquipmentData w, PlayerStats attacker)
    {
        float m = BaseMul(w);
        if (m <= 0f) return 0f;
        if (attacker != null) m += attacker.Get(StatType.HeadshotDamage);
        return Mathf.Max(1f, m);
    }

    /// <summary>맞은 곳이 머리인가</summary>
    public static bool IsHead(Collider col, Vector3 point, PlayerHealth target)
    {
        if (col == null || target == null) return false;

        if (col.GetComponent<HeadZone>() != null) return true;

        // 변화 피격 범위 (모양이 자유라 높이로 못 정함) — 머리 체크한 것만
        var p = col.transform.parent;
        if (p != null && p.name == AlterHitboxes.ContainerName) return false;

        return point.y >= target.HeadBottomY;
    }

    /// <summary>소환물(조커 분신 등) — HeadZone이거나, 콜라이더 위쪽 20%(최소 0.3m)</summary>
    public static bool IsHead(Collider col, Vector3 point)
    {
        if (col == null) return false;
        if (col.GetComponent<HeadZone>() != null) return true;
        var b = col.bounds;
        return point.y >= b.max.y - Mathf.Max(0.3f, b.size.y * 0.2f);
    }

    /// <summary>갑옷 감소 최대치 (갑옷 · 인챈트를 다 합쳐도 이만큼까지만)</summary>
    public const float MaxBodyReduction = 0.8f;

    /// <summary>맞는 쪽 — 갑옷(받는 피해 감소) 배율. 헤드샷이면 1 (갑옷이 안 막음)</summary>
    public static float BodyArmor(float headshotMul, PlayerStats target)
    {
        if (headshotMul > 1f || target == null) return 1f;
        float red = Mathf.Clamp(target.Get(StatType.BodyDamageReduction), 0f, MaxBodyReduction);
        return 1f - red;
    }

    /// <summary>맞는 쪽 — 투구 감소를 넣은 최종 배율 (1 이상)</summary>
    public static float Incoming(float mul, PlayerStats target)
    {
        if (mul <= 1f) return 1f;
        float red = target != null ? Mathf.Clamp01(target.Get(StatType.HeadshotReduction)) : 0f;
        return Mathf.Max(1f, mul * (1f - red));
    }
}
