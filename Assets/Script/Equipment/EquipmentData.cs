using UnityEngine.Localization;
using UnityEngine.Localization.SmartFormat.PersistentVariables;
using UnityEngine;

public enum EquipSlot { Weapon, Armor, Helmet, Boots }
public enum GripType { RightHand, LeftHand, TwoHand }
public enum WeaponClass { Melee, Ranged }

public enum RangedType
{
    Bow,        // 시위를 당김
    Staff,      // 차징 후 발사 (시위 없음)
    Gun,        // 즉발
    Thrown,     // 던지기
}
public enum WeaponHitMode
{
    ForwardArc,      // 정면 부채꼴 (근접 기본)
    ForwardSphere,   // 정면 구체 (각도 제한 없음, 범위기)
}

public enum GunFireMode
{
    Hitscan,        // 즉시 판정 (권총, 소총)
    Projectile,     // 투사체 (유탄, 로켓)
}




[CreateAssetMenu(menuName = "Game/Equipment")]
public class EquipmentData : ScriptableObject, IMotionSource
{
    // ══════════ 공통 ══════════

    [Header("Identity")]
    public int Id;
    public string DisplayName;
    public LocalizedString NameEntry;
    public LocalizedString DescEntry;
    public EquipSlot Slot;
    public WeaponClass Class = WeaponClass.Melee;
    public Sprite Icon;

    [Header("태그")]
    [Tooltip("툴팁 하단에 표시할 로컬라이즈 키 목록")]
    public string[] Tags;

    [TextArea(2, 4)] public string Description;

    [Header("Base Stats")]
    public StatEntry[] BaseStats;

    [Header("─── Weapon: 인챈트 ───")]
    [Tooltip("비우면 이 무기는 인챈트 불가")]
    public EnchantTable EnchantTable;

    [Tooltip("인챈트 1회당 스톤 소모량")]
    public int EnchantStoneCost = 1;

    [Tooltip("단계마다 추가되는 스톤 비용")]
    public int EnchantCostPerStage = 0;

    public bool CanEnchant => EnchantTable != null && EnchantTable.MaxStage > 0;
    public int MaxEnchantStage => EnchantTable != null ? EnchantTable.MaxStage : 0;

    public int GetEnchantCost(int currentStage) =>
        Mathf.Max(1, EnchantStoneCost + EnchantCostPerStage * currentStage);



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

    [Header("─── Weapon: Hand Pose ───")]
    [Tooltip("이 무기를 들었을 때의 기본 손 위치/회전. 비우면 FPArmRig의 IdlePose 사용")]
    public bool OverrideIdlePose = false;

    public Vector3 RightHandPos = new(0.22f, -0.18f, 0.42f);
    public Vector3 RightHandRot = new(5f, -8f, 0f);

    public bool UseLeftHand = false;
    public Vector3 LeftHandPos = new(-0.18f, -0.20f, 0.38f);
    public Vector3 LeftHandRot = new(5f, 12f, 0f);

    [Header("─── Weapon: Grip ───")]
    public HandPose RightGrip;
    public HandPose LeftGrip;

    [Header("─── Weapon: Hit Shape ───")]
    public WeaponHitMode HitMode = WeaponHitMode.ForwardArc;

    [Tooltip("사거리 (m)")]
    public float Range = 3f;

    [Tooltip("좌우 판정 각도 (전체각, 도)")]
    [Range(20f, 360f)] public float HitAngle = 120f;

    [Tooltip("최소 사거리. 너무 붙으면 안 맞게 (보통 0)")]
    public float MinRange = 0f;

    [Tooltip("수직 판정 허용 높이 (m). 눈높이 기준 위아래")]
    public float HitHeight = 1.2f;



    [Header("─── Weapon: Combo ───")]
    [Tooltip("배열 길이 1이면 단타 무기")]
    public ComboStep[] Combo = new ComboStep[1];

    [Header("─── Ranged: 공통 ───")]
    [Tooltip("Class가 Ranged일 때만 사용")]
    public RangedType RangedKind = RangedType.Bow;
    public GameObject ProjectilePrefab;
    public float ProjectileLifetime = 3f;
    [Tooltip("총구/시위 위치. 모델 프리팹 내부 오브젝트 이름")]
    public string MuzzleNodeName = "Muzzle";

    [Header("─── Ranged: 차징 ───")]
    [Tooltip("이 시간 미만에 떼면 발사되지 않음")]
    public float MinDrawTime = 0.25f;
    [Tooltip("이 시간에 최대 위력. 더 당겨도 늘지 않음")]
    public float MaxDrawTime = 1.2f;

    [Tooltip("최소 시위 시 피해 배율")]
    public float MinDrawDamageMul = 0.4f;
    [Tooltip("최대 시위 시 피해 배율")]
    public float MaxDrawDamageMul = 2.5f;

    [Tooltip("최소 시위 시 투사체 속도")]
    public float MinDrawSpeed = 25f;
    [Tooltip("최대 시위 시 투사체 속도")]
    public float MaxDrawSpeed = 70f;

    [Header("─── Ranged: 차징 자세 ───")]
    [Tooltip("차징 중 자세를 바꿀지. 끄면 기본 자세 유지")]
    public bool UseChargePose = false;

    [Tooltip("완전히 차징했을 때의 자세")]
    public ArmPose ChargePose = new();

    [Tooltip("자세 보간 곡선. 비우면 선형")]
    public AnimationCurve ChargeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("─── Ranged: 총기 ───")]
    [Tooltip("판정 방식")]
    public GunFireMode FireMode = GunFireMode.Hitscan;

    [Tooltip("최대 탄약. 장전 없음")]
    public int MaxAmmo = 12;

    public float RoundsPerMinute = 500f;
    public bool FullAuto = false;

    [Header("반동")]
    public float RecoilVertical = 2f;
    public float RecoilHorizontal = 0.5f;
    public float RecoilRamp = 1.15f;
    public float RecoilMaxMul = 3f;

    [Header("히트스캔 전용")]
    [Tooltip("최대 사거리 (m)")]
    public float HitscanRange = 200f;
    [Tooltip("예광탄 프리팹")]
    public GameObject TracerPrefab;

    [Tooltip("투사체 속도. 차징 무기는 min/max를 사용")]
    public float ProjectileSpeed = 60f;

    [Header("─── Ranged: 발사 연출 ───")]

    [Tooltip("발사음")]
    public AudioClip FireSound;
    [Range(0f, 1f)] public float FireVolume = 0.8f;
    [Tooltip("피치 랜덤 범위 (±)")]
    [Range(0f, 0.3f)] public float FirePitchVariance = 0.08f;

    [Tooltip("탄약이 없을 때 소리")]
    public AudioClip EmptySound;

    [Header("─── Ranged: 반동 자세 ───")]
    [Tooltip("반동 시 자세를 바꿀지")]
    public bool UseRecoilPose = false;

    [Tooltip("반동이 최대일 때의 자세")]
    public ArmPose RecoilPose = new();

    [Tooltip("반동이 최대까지 오르는 시간 (초)")]
    public float RecoilRiseTime = 0.03f;

    [Tooltip("원래 자세로 돌아오는 시간 (초)")]
    public float RecoilFallTime = 0.12f;

    [Tooltip("연사 시 반동 자세 누적 상한 (1이면 누적 없음)")]
    [Range(1f, 3f)] public float RecoilPoseMaxStack = 1.6f;

    [Header("반동 흔들림")]
    [Tooltip("수평 흔들림 폭 (m)")]
    public float RecoilSwayAmount = 0.015f;

    [Tooltip("수직 흔들림 폭 (m)")]
    public float RecoilSwayVertical = 0.008f;

    [Tooltip("흔들림 속도")]
    public float RecoilSwaySpeed = 30f;

    [Header("─── Ranged: 시위 ───")]
    [Tooltip("시위가 당겨지는 방향. 시위 본의 로컬 기준")]
    public Vector3 StringPullDir = new(0f, 0f, -1f);

    [Tooltip("최대로 당겼을 때 이동 거리 (m)")]
    public float StringPullDistance = 0.35f;

    [Header("─── Ranged: 시위 잡는 손 ───")]
    [Tooltip("시위를 잡았을 때 왼손이 시위 본 기준으로 놓일 오프셋")]
    public Vector3 StringHandOffset;

    [Tooltip("시위를 잡았을 때 왼손 회전")]
    public Vector3 StringHandRot;

    [Tooltip("시위 잡는 손 모양. 비우면 기본 그립 유지")]
    public HandPose StringHandPose;

    [Tooltip("손이 시위로 옮겨가는/돌아오는 시간 (초)")]
    public float StringHandBlendTime = 0.12f;

    [Tooltip("투사체에 작용하는 중력 가속도. 0이면 직선")]
    public float ProjectileGravity = 12f;

    [Tooltip("관통 횟수. 0이면 첫 적중에서 멈춤")]
    [Range(0, 1)] public int PierceCount = 0;

    [Tooltip("관통 시 피해 감소 비율")]
    [Range(0f, 1f)] public float PierceDamageFalloff = 0.3f;

    [Header("─── Weapon: 분류 ───")]
    [Tooltip("맨손 계열. 무기 없이 싸우는 상태")]
    public bool IsUnarmed = false;

    /// <summary>실제 무기를 들고 있는 근접 (맨손 제외)</summary>
    public bool IsArmedMelee => Slot == EquipSlot.Weapon
                             && Class == WeaponClass.Melee
                             && !IsUnarmed;



    // ══════════ 편의 ══════════

    public bool IsWeapon => Slot == EquipSlot.Weapon;
    public int ComboLength => Combo == null ? 0 : Combo.Length;

    public string SourceName => DisplayName;
    public EquipmentData PreviewWeapon => this;
    public bool SupportsMultiStep => true;
    public void SetSteps(ComboStep[] steps) => Combo = steps;
    public ComboStep[] GetSteps() => Combo;

    public ComboStep GetStep(int index)
    {
        if (Combo == null || Combo.Length == 0) return null;
        return Combo[Mathf.Abs(index) % Combo.Length];
    }

    public string GetName() 
    {
        if (!NameEntry.IsEmpty)
        {
            string s = NameEntry.GetLocalizedString();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return DisplayName;
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

    /// <summary>시위 진행도 0~1. 발사 가능 여부는 별도 확인</summary>
    public float GetDrawRatio(float heldTime)
    {
        float span = Mathf.Max(0.01f, MaxDrawTime - MinDrawTime);
        return Mathf.Clamp01((heldTime - MinDrawTime) / span);
    }

    public bool CanRelease(float heldTime) => heldTime >= MinDrawTime;

    public float GetDrawDamageMul(float ratio01) =>
        Mathf.Lerp(MinDrawDamageMul, MaxDrawDamageMul, ratio01);

    public float GetDrawSpeed(float ratio01) =>
        Mathf.Lerp(MinDrawSpeed, MaxDrawSpeed, ratio01);

    public WeaponRequirement ToRequirementFlag()
    {
        if (Slot != EquipSlot.Weapon) return WeaponRequirement.None;
        if (IsUnarmed) return WeaponRequirement.Unarmed;

        if (Class == WeaponClass.Melee) return WeaponRequirement.Melee;

        return RangedKind switch
        {
            RangedType.Bow => WeaponRequirement.Bow,
            RangedType.Staff => WeaponRequirement.Staff,
            RangedType.Gun => WeaponRequirement.Gun,
            RangedType.Thrown => WeaponRequirement.Thrown,
            _ => WeaponRequirement.None,
        };
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