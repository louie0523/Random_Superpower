public struct StatModifier
{
    public StatType Stat;
    public ModType Type;
    public float Value;
    public int SourceId;

    public StatModifier(StatType stat, ModType type, float value, int sourceId = 0)
    {
        Stat = stat; Type = type; Value = value; SourceId = sourceId;
    }
}