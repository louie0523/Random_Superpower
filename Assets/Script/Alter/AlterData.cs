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

    [Header("스탯 (중첩마다)")]
    public AlterStat[] Stats;

    public string LocalizedName => LocUtil.GetOr($"alter.{Id}.name", LocUtil.Resolve(DisplayName));
    public string LocalizedDesc => LocUtil.GetOr($"alter.{Id}.desc", LocUtil.Resolve(Description));
}