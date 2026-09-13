public enum SkillSlot
{
    Skill1,     // Shift
    Skill2,     // E
    Skill3,     // 우클릭
    Ultimate,   // Q
    Random,     // F
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
        _ => "?"
    };

    public static int Index(this SkillSlot s) => (int)s;
    public const int Count = 5;   // 5
}