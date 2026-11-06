using UnityEngine;

/// <summary>
/// 게임 밖(타이틀 · 꾸미기 · 스킨 · 도감 · 프로필)에서 쓰는 데이터 모음 — Resources/GameMeta 하나.
/// 만들기: Project 창 우클릭 → Create → Game/Meta/Game Meta → 이름 그대로 'GameMeta'로 Resources 폴더에
/// </summary>
[CreateAssetMenu(menuName = "Game/Meta/Game Meta", fileName = "GameMeta")]
public class GameMeta : ScriptableObject
{
    [Header("데이터")]
    public AbilityDatabase Abilities;
    public EquipmentDatabase Equipment;
    public CosmeticDatabase Cosmetics;
    public GachaTable Gacha;
    public RewardRules Rewards;

    [Header("캐릭터")]
    [Tooltip("기본 캐릭터 모델 (모델만 — Animator + 메시. 스크립트 없는 프리팹)\n" +
             "타이틀 · 꾸미기 · 대기실 사진 · 인벤토리 대체 그림이 이걸 씀. 꾸미기 부위는 이 위에 뼈마다 붙음")]
    public GameObject CharacterModel;
    [Tooltip("타이틀 · 꾸미기에서 틀 애니메이터 (비우면 모델에 들어 있는 것)")]
    public RuntimeAnimatorController IdleAnimator;
    [Tooltip("타이틀 · 꾸미기 · 사진에 띄울 때 크기 (1 = 모델 그대로). 사진은 크기에 맞춰 찍혀서 영향 없음")]
    [Range(0.05f, 3f)] public float CharacterScale = 0.5f;

    [Header("씬")]
    public string TitleScene = "Title";
    public string LobbyScene = "Lobby";

    [Header("게임 이름 (타이틀 로고 글자)")]
    public string GameTitle = "정루이 멀티";

    static GameMeta _cur;

    public static GameMeta Get()
    {
        if (_cur == null) _cur = Resources.Load<GameMeta>("GameMeta");
        return _cur;
    }
}
