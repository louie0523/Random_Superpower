/// <summary>
/// 게임 코드가 방 설정을 읽는 곳. 방이 없으면(게임 씬에서 바로 실행) 기존 값(fallback)을 그대로 씀.
/// 예) refillTime → RoomRules.ChestRefill(refillTime)
/// 제단 값은 AltarRules에서 RoundManager 인스펙터 값과 합쳐서 씀.
/// </summary>
public static class RoomRules
{
    static RoomState R => RoomState.Instance;

    /// <summary>방(대기실)을 거쳐 들어온 게임인지</summary>
    public static bool Active => R != null;

    public static int StartSoul(int fallback)
    {
        var r = R;
        return r != null ? r.StartSoul : fallback;
    }

    /// <summary>라운드 시간(초). 0 = 무제한</summary>
    public static int RoundSeconds(int fallback)
    {
        var r = R;
        return r != null ? r.RoundSeconds : fallback;
    }

    public static float ChestRefill(float fallback)
    {
        var r = R;
        return r != null ? r.ChestRefillSeconds : fallback;
    }

    // ── 제단 ──

    public static int SubAltarCost(int fallback)
    {
        var r = R;
        return r != null ? r.SubAltarCost : fallback;
    }

    public static int CentralAltarCost(int fallback)
    {
        var r = R;
        return r != null ? r.CentralAltarCost : fallback;
    }

    public static int AltarsNeeded(int fallback)
    {
        var r = R;
        return r != null ? r.AltarsNeeded : fallback;
    }

    public static bool AltarsNeedAll(bool fallback)
    {
        var r = R;
        return r != null ? (bool)r.AltarsNeedAll : fallback;
    }

    public static float CentralChannel(float fallback)
    {
        var r = R;
        return r != null ? r.CentralChannelSeconds : fallback;
    }

    // ── 기타 ──

    /// <summary>낙사로 죽는지 (false면 부활 지점으로 옮기기만)</summary>
    public static bool FallKills
    {
        get
        {
            var r = R;
            return r == null || r.FallRuleValue == (int)FallRule.Death;
        }
    }

    public static bool TeamMode
    {
        get
        {
            var r = R;
            return r != null && r.TeamMode;
        }
    }

    /// <summary>클래스 · 랜덤 능력 밴 목록에 있는지</summary>
    public static bool IsBanned(int abilityId)
    {
        var r = R;
        return r != null && r.IsBanned(abilityId);
    }
}
