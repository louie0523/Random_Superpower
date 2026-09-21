/// <summary>스탯 수정자의 출처 식별자</summary>
public static class SourceId
{
    // 장비 슬롯
    public const int Weapon = 100001;
    public const int Armor = 100002;
    public const int Helmet = 100003;
    public const int Boots = 100004;

    // 능력
    public const int MainAbility = 100010;
    public const int SubAbility = 100011;
    public const int RandomAbility = 100012;

    // 인챈트 (무기 슬롯별 8칸)
    public const int EnchantBase = 100100;

    // 상점
    public const int ShopBase = 110000; // + 공용 인덱스
    public const int ShopExclusiveBase = 110500; // + 전용 인덱스

    // 임시 효과
    public const int CurseBase = 200000;
    public const int BuffBase = 300000;
    public const int StateBase = 400000;
    public const int AbilityBase = 500000;
}