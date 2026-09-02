using UnityEngine;

public enum EquipSlot { Weapon, Armor, Helmet, Boots }

[CreateAssetMenu(menuName = "Game/Equipment")]
public class EquipmentData : ScriptableObject
{
    [Header("Identity")]
    public int Id;                  // 네트워크로 보낼 값
    public string DisplayName;
    public EquipSlot Slot;
    [TextArea] public string Description;

    [Header("Base Stats")]
    public StatEntry[] BaseStats;

    [Header("Options")]
    public int MaxOptionSlots = 3;

    [System.Serializable]
    public struct StatEntry
    {
        public StatType Stat;
        public ModType Type;

        [Tooltip("Flat = 실수치 (+25). Percent = 퍼센트 (25 = +25%). " +
                 "치명타/쿨감/흡혈은 Flat만 사용 (0.1 = 10%)")]
        public float Value;
    }
}