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

    // 상점 (영구)
    public const int ShopPurchase = 100020;

    // 임시 효과 — 베이스에 고유값을 더해서 사용
    public const int CurseBase = 200000;
    public const int BuffBase = 300000;
}