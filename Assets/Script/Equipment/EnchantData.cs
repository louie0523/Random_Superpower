using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enchant")]
public class EnchantData : ScriptableObject
{
    public int Id;
    public string DisplayName;
    [TextArea(1, 2)] public string Description;
    public Color TierColor = Color.white;

    [Tooltip("적용할 스탯 수정자들")]
    public EquipmentData.StatEntry[] Stats;

    [Tooltip("추첨 가중치")]
    public float RollWeight = 1f;
}