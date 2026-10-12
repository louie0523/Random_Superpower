/// <summary>순서가 저장값·네트워크 인덱스 — 뒤에만 추가</summary>
public enum SkillSlot
{
    Skill1,     // Shift
    Skill2,     // E
    Skill3,     // 우클릭
    Ultimate,   // Q
    Random,     // F
    Random2,    // V — 두 번째 랜덤 능력 (조커)
}

public static class SkillSlotExt
{
    public static string Label(this SkillSlot s) => s switch
    {
        SkillSlot.Skill1 => "SHIFT",
        SkillSlot.Skill2 => "E",
        SkillSlot.Skill3 => "RMB",
        SkillSlot.Ultimate => "Q",
        SkillSlot.Random => "F",
        SkillSlot.Random2 => "V",
        _ => "?"
    };

    public static int Index(this SkillSlot s) => (int)s;
    public const int Count = 6;

    /// <summary>랜덤 능력 칸인가 (F · V)</summary>
    public static bool IsRandom(this SkillSlot s) => s == SkillSlot.Random || s == SkillSlot.Random2;
}