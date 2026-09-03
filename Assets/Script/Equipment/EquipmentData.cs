using UnityEngine;

public enum EquipSlot { Weapon, Armor, Helmet, Boots }
public enum GripType { RightHand, LeftHand, TwoHand }
public enum WeaponClass { Melee, Ranged }

public enum WeaponHitMode
{
    WeaponBox,       // 무기에 붙은 박스 (근접 기본)
    ForwardSphere,   // 카메라 정면 구체 (마법, 범위기)
}


[CreateAssetMenu(menuName = "Game/Equipment")]
public class EquipmentData : ScriptableObject
{
    // ══════════ 공통 ══════════

    [Header("Identity")]
    public int Id;
    public string DisplayName;
    public EquipSlot Slot;
    public Sprite Icon;
    [TextArea(2, 4)] public string Description;

    [Header("Base Stats")]
    public StatEntry[] BaseStats;

    [Header("Options")]
    [Tooltip("강화 옵션 부착 가능 슬롯 수")]
    public int MaxOptionSlots = 3;

    // ══════════ 무기 전용 ══════════

    [Header("─── Weapon: Model ───")]
    public GameObject ModelPrefab;
    public GripType Grip = GripType.RightHand;
    public Vector3 PositionOffset;
    public Vector3 RotationOffset;
    public Vector3 ScaleOverride = Vector3.one;

    [Tooltip("1인칭 전용 오프셋 (0이면 위 값 사용)")]
    public Vector3 FpPositionOffset;
    public Vector3 FpRotationOffset;

    [Header("─── Weapon: Hit Shape ───")]
    public WeaponHitMode HitMode = WeaponHitMode.WeaponBox;

    [Tooltip("무기 기준 판정 박스 중심. Z+ 가 날 방향")]
    public Vector3 HitBoxCenter = new(0f, 0f, 0.6f);

    [Tooltip("판정 박스 크기 (가로, 세로, 길이)")]
    public Vector3 HitBoxSize = new(0.3f, 0.3f, 1.2f);

    [Tooltip("ForwardSphere 전용")]
    public float Range = 3f;
    public float HitRadius = 0.8f;



    [Header("─── Weapon: Combo ───")]
    [Tooltip("배열 길이 1이면 단타 무기")]
    public ComboStep[] Combo = new ComboStep[1];

    [Header("─── Weapon: Ranged ───")]
    [Tooltip("Class가 Ranged일 때만 사용")]
    public GameObject ProjectilePrefab;
    public float ProjectileSpeed = 40f;
    public float ProjectileLifetime = 3f;
    [Tooltip("총구/시위 위치. 모델 프리팹 내부 오브젝트 이름")]
    public string MuzzleNodeName = "Muzzle";

    // ══════════ 편의 ══════════

    public bool IsWeapon => Slot == EquipSlot.Weapon;
    public int ComboLength => Combo == null ? 0 : Combo.Length;

    public ComboStep GetStep(int index)
    {
        if (Combo == null || Combo.Length == 0) return null;
        return Combo[Mathf.Abs(index) % Combo.Length];
    }

    public Vector3 GetPosOffset(bool firstPerson) =>
        firstPerson && FpPositionOffset != Vector3.zero ? FpPositionOffset : PositionOffset;

    public Vector3 GetRotOffset(bool firstPerson) =>
        firstPerson && FpRotationOffset != Vector3.zero ? FpRotationOffset : RotationOffset;

    // ══════════ 중첩 타입 ══════════

    [System.Serializable]
    public struct StatEntry
    {
        public StatType Stat;
        public ModType Type;

        [Tooltip("Flat = 실수치 (+25). Percent = 퍼센트 (25 = +25%). " +
                 "치확/치피/쿨감/흡혈은 Flat만 사용 (0.1 = 10%)")]
        public float Value;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Slot != EquipSlot.Weapon) return;

        if (Combo == null || Combo.Length == 0)
            Combo = new ComboStep[] { new ComboStep() };

        for (int i = 0; i < Combo.Length; i++)
            if (Combo[i] != null && string.IsNullOrEmpty(Combo[i].Name))
                Combo[i].Name = $"{i + 1}타";
    }
#endif
}