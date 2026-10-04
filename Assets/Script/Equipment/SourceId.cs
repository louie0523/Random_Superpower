/// <summary>
/// 스탯 수정자의 출처 식별자. 같은 출처의 수정자는 RemoveBySource로 한 번에 지운다.
/// 0은 쓰지 않는다 (어디서도 지워지지 않아 영구 잔류).
/// 라운드 시작 시 PlayerStats.ClearAll()로 전부 비운 뒤 각 시스템이 다시 건다.
/// </summary>
public static class SourceId
{
    // ── 장비 (PlayerEquipment) — 슬롯당 1개 ──
    public const int Weapon = 100001;
    public const int Armor = 100002;
    public const int Helmet = 100003;
    public const int Boots = 100004;

    // ── 능력 패시브 StatMod (AbilityRuntime) — 데이터에서 자동 ──
    public const int MainAbility = 100010;
    public const int SubAbility = 100011;
    public const int RandomAbility = 100012;

    // ── 인챈트 (PlayerEnchant) — 든 무기의 인챈트 전체를 이 번호 하나로 ──
    public const int EnchantBase = 100100;

    // ── 상점 (PlayerShop) ──
    public const int ShopBase = 110000;            // + 공용 인덱스 (0~15)
    public const int ShopExclusiveBase = 110500;   // + 전용 인덱스 (0~7)

    // ── 임시 효과 ──
    public const int CurseBase = 200000;   // 예약: 남에게 거는 디버프 (2-5)
    public const int BuffBase = 300000;    // 그 자체 = 영구 버프, +1 ~ +99999 = 임시 버프 순환
    public const int StateBase = 400000;   // + 상태 ID (100000 미만, 보통 능력 ID와 같게)
    public const int AbilityBase = 500000; // + 능력ID × 10 + n (n = 0~9). AbilityBehaviour.Src(n)으로 사용
    public const int StatusBase = 600000;  // + StatusType — 둔화·동상의 스탯 감소
}