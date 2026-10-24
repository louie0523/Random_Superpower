using UnityEngine;

/// <summary>순서가 저장값 — 뒤에만 추가</summary>
public enum AlterAttach
{
    Body,       // 몸통 (FPArmRig.Body) — 날개, 꼬리, 등 장비
    Head,       // 머리 (FPArmRig.Head) — 뿔, 후광, 가면
    RightHand,  // 오른손 — 발톱, 손 장갑
    LeftHand,   // 왼손
    Weapon,     // 든 무기 끝 (무기 바꾸면 따라 옮겨감)
    Root,       // 플레이어 루트 — 발밑 오라 등
    Bone,       // 이름으로 찾은 오브젝트 (BoneName)
}

[System.Serializable]
public class AlterStat
{
    public StatType Stat;
    public ModType Type;
    public float Value;
}

/// <summary>
/// 변화 하나 — 몸에 붙는 파츠 + 스탯. 여러 개 동시에 가질 수 있음.
/// 같은 Group의 변화는 하나만 (새로 붙이면 기존 것을 교체) — 예) 날개 종류
/// </summary>
[CreateAssetMenu(menuName = "Game/Alter")]
public class AlterData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("11000번대 권장")]
    public int Id;
    public string DisplayName;
    public Sprite Icon;
    [TextArea(1, 3)] public string Description;

    [Header("파츠")]
    [Tooltip("붙일 모델. 비우면 스탯만 (보이지 않는 변화)")]
    public GameObject Prefab;
    public AlterAttach Attach = AlterAttach.Body;
    [Tooltip("Attach = Bone일 때 찾을 오브젝트 이름 (플레이어 아래에서 검색)")]
    public string BoneName;

    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.one;

    [Header("탈것")]
    [Tooltip("올라타는 높이 (m). 0이 아니면 플레이어 몸·머리·카메라·피격 판정이 이만큼 올라감 — 말 등.\n" +
             "말 파츠 자체는 Attach = Root(발밑)에 붙여야 땅에 남음 (Body에 붙이면 같이 올라감).\n" +
             "여러 개면 가장 큰 값")]
    [Min(0f)] public float Lift = 0f;

    [Header("방향")]
    [Tooltip("내 시선(마우스) 방향을 따라 돌지 않고, 움직이는 방향(WASD)을 바라봄 — 말 등.\n" +
             "멈추면 마지막 방향 그대로. 회전(Rotation) 값은 모델 앞을 맞추는 보정으로 그대로 적용")]
    public bool FaceMoveDirection = false;
    [Tooltip("이동 방향으로 도는 빠르기")]
    [Min(0.1f)] public float FaceTurnSpeed = 8f;

    [Header("보이기")]
    [Tooltip("본인 화면에서도 보임. 날개·뿔처럼 시야를 가리는 건 끄고, 손 파츠는 켜기")]
    public bool ShowForSelf = false;
    [Tooltip("본인 화면에서 안 보일 때도 그림자는 남김")]
    public bool SelfShadow = true;

    [Header("중첩")]
    [Tooltip("같은 변화를 몇 개까지. 1 = 중복 불가")]
    [Min(1)] public int MaxStack = 1;
    [Tooltip("중첩마다 파츠를 이만큼 옮겨서 하나 더 (0이면 파츠는 하나만, 스탯만 쌓임)")]
    public Vector3 StackOffset;

    [Tooltip("같은 그룹은 하나만 — 새로 붙이면 기존 것을 교체. 비우면 그룹 없음")]
    public string Group;

    [Header("변신")]
    [Tooltip("켜면 '변신' — 몸 · 손 · 무기를 숨기고 이 모델만 보임. 무기 · 스킬 잠금 · 공격 대체는 변신 능력(TransformAbility)이 함")]
    public bool IsTransform;
    [Tooltip("변신 중 내 화면을 3인칭으로")]
    public bool TransformThirdPerson = true;
    public ThirdPersonSettings TransformCamera = new();

    [Header("변신 — 기본 공격 · 스킬")]
    [Tooltip("붙어 있는 동안 기존 스킬 · 궁극기 사용 불가 (랜덤 능력 스킬은 가능)")]
    public bool LockSkills;
    [Tooltip("켜면 붙어 있는 동안 좌클릭 = 아래 '공격' (물어뜯기 등). 무기 스탯 그대로, 원거리 무기도 근접으로")]
    public bool OverrideAttack;
    [Tooltip("공격 — 길이 · 판정 시간(HitStart) · 범위(판정 덮어쓰기) · 피해 배율. 모델(DragonWave)은 이 판정 시간에 맞춰 입을 닫음")]
    public ComboStep Attack = DefaultBite();
    [Tooltip("(선택) 모션 에셋 — 넣으면 위 '공격' 대신 이걸 씀")]
    public AbilityMotionData AttackMotion;
    [Tooltip("공격할 때 모델이 재생할 상태 (DragonWave = Attack)")]
    public string AttackAnim = "Attack";
    [Tooltip("공격 판정 순간에 쿵 — 앞쪽 범위 기절 · 밀치기 · 주변 화면 흔들림 (거신병 내려찍기 등). 끄면 없음")]
    public ShockwaveDef AttackWave = new() { Enabled = false, Radius = 3.5f, Forward = 1.8f, Stun = 0.8f, Push = 8f, PushUp = 3.5f };
    [Tooltip("변화 기본 공격 동안 이동 · 점프 금지 (내려찍기 등 발이 묶이는 공격)")]
    public bool RootDuringAttack = false;
    [Tooltip("변화 기본 공격 내부 쿨타임 (초, 공격 시작부터). 0 = 없음")]
    [Min(0f)] public float AttackCooldown = 0f;
    [Tooltip("붙어 있는 동안 점프 세기 배율 (1 = 그대로 · 1.4면 높이 약 2배)")]
    [Min(0.1f)] public float JumpMultiplier = 1f;
    [Tooltip("내려올 때 중력 배율 (올라갈 땐 그대로). 1 = 그대로 · 2 = 2배 빠르게 떨어짐")]
    [Min(0.1f)] public float FallGravity = 1f;
    [Tooltip("땅에 있을 때만 기본 공격 (공중에선 좌클릭 무시)")]
    public bool GroundOnlyAttack = false;

    /// <summary>이 변화의 기본 공격 (없으면 null)</summary>
    public ComboStep AttackStep =>
        AttackMotion != null && AttackMotion.Step != null ? AttackMotion.Step : (OverrideAttack ? Attack : null);

    static ComboStep DefaultBite() => new()
    {
        Name = "물어뜯기",
        Duration = 0.6f,
        HitStart = 0.5f,
        HitEnd = 0.62f,
        CancelPoint = 0.9f,
        OverrideHitShape = true,
        HitMode = WeaponHitMode.ForwardArc,
        Range = 4f,
        HitAngle = 90f,
        HitHeight = 2.5f,
        DamageMultiplier = 1.5f,
        MaxTargets = 3,
        HitStopDuration = 0.07f,
        CameraShake = 0.4f,
    };

    [Header("피격 범위")]
    [Tooltip("변신 모델의 피격 콜라이더 — 비우면 없음 (원래 몸 판정만).\n" +
         "기준: 붙는 자리(Root면 발밑) · 플레이어 방향 · m. 플레이 중 값을 바꾸면 바로 반영 (에디터)")]
    public AlterHitbox[] Hitboxes;

    [Header("스탯 (중첩마다)")]
    public AlterStat[] Stats;

    public string LocalizedName => LocUtil.GetOr($"alter.{Id}.name", LocUtil.Resolve(DisplayName));
    public string LocalizedDesc => LocUtil.GetOr($"alter.{Id}.desc", LocUtil.Resolve(Description));
}