using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>게임 동작 — 키 설정에서 바꿀 수 있는 것</summary>
public enum GameAction
{
    Attack,
    Skill1,
    Skill2,
    Skill3,
    Ultimate,
    Random,
    Random2,
    Interact,
    Chat,
    Scoreboard,

    // 3단계 — 저장 이름이 enum 이름이라 순서는 아무렇게나 바꿔도 됨
    MoveForward,
    MoveBack,
    MoveLeft,
    MoveRight,
    Jump,
    Drop,
    Weapon1,
    Weapon2,
    Weapon3,
    Weapon4,
    Weapon5,
    Weapon6,
    Weapon7,
    AbilityPanel,
    Inventory,
    Shop,
    ShieldCharge,       // 소울 쉴드 충전 (누르고 있기)
}

/// <summary>
/// 키 설정 (이 PC에 저장). 코드는 Input.GetKeyDown(KeyCode.E) 대신 KeyBinds.Down(GameAction.Skill2)처럼.
/// 마우스 버튼도 키로 (Mouse0 = 왼쪽, Mouse1 = 오른쪽, Mouse3 · 4 = 옆 버튼).
/// 이미 다른 동작에 쓰는 키를 넣으면 두 동작의 키를 서로 바꿈.
/// </summary>
public static class KeyBinds
{
    const string P = "key.";

    static readonly Dictionary<GameAction, KeyCode> Defaults = new()
    {
        { GameAction.Attack, KeyCode.Mouse0 },
        { GameAction.Skill1, KeyCode.LeftShift },
        { GameAction.Skill2, KeyCode.E },
        { GameAction.Skill3, KeyCode.Mouse1 },
        { GameAction.Ultimate, KeyCode.Q },
        { GameAction.Random, KeyCode.F },
        { GameAction.Random2, KeyCode.V },
        { GameAction.Interact, KeyCode.R },
        { GameAction.Chat, KeyCode.Return },
        { GameAction.Scoreboard, KeyCode.Tab },

        { GameAction.MoveForward, KeyCode.W },
        { GameAction.MoveBack, KeyCode.S },
        { GameAction.MoveLeft, KeyCode.A },
        { GameAction.MoveRight, KeyCode.D },
        { GameAction.Jump, KeyCode.Space },
        { GameAction.Drop, KeyCode.G },
        { GameAction.Weapon1, KeyCode.Alpha1 },
        { GameAction.Weapon2, KeyCode.Alpha2 },
        { GameAction.Weapon3, KeyCode.Alpha3 },
        { GameAction.Weapon4, KeyCode.Alpha4 },
        { GameAction.Weapon5, KeyCode.Alpha5 },
        { GameAction.Weapon6, KeyCode.Alpha6 },
        { GameAction.Weapon7, KeyCode.Alpha7 },
        { GameAction.AbilityPanel, KeyCode.F1 },
        { GameAction.Inventory, KeyCode.I },
        { GameAction.Shop, KeyCode.B },
        { GameAction.ShieldCharge, KeyCode.C },
    };

    /// <summary>설정 창 '조작' 탭에 나오는 순서</summary>
    public static readonly (GameAction Action, string Label)[] Labels =
    {
        (GameAction.MoveForward, "앞으로"),
        (GameAction.MoveBack, "뒤로"),
        (GameAction.MoveLeft, "왼쪽"),
        (GameAction.MoveRight, "오른쪽"),
        (GameAction.Jump, "점프"),
        (GameAction.Attack, "기본 공격"),
        (GameAction.Skill1, "스킬 1"),
        (GameAction.Skill2, "스킬 2"),
        (GameAction.Skill3, "스킬 3"),
        (GameAction.Ultimate, "궁극기"),
        (GameAction.Random, "랜덤 능력"),
        (GameAction.Random2, "랜덤 능력 2 (조커)"),
        (GameAction.Interact, "상호작용 (제단 · 상자 · 줍기)"),
        (GameAction.Drop, "무기 버리기"),
        (GameAction.ShieldCharge, "소울 쉴드 충전 (누르고 있기)"),
        (GameAction.Weapon1, "무기 1번"),
        (GameAction.Weapon2, "무기 2번"),
        (GameAction.Weapon3, "무기 3번"),
        (GameAction.Weapon4, "무기 4번"),
        (GameAction.Weapon5, "무기 5번"),
        (GameAction.Weapon6, "무기 6번"),
        (GameAction.Weapon7, "무기 7번"),
        (GameAction.Inventory, "인벤토리"),
        (GameAction.Shop, "상점"),
        (GameAction.AbilityPanel, "능력 창"),
        (GameAction.Chat, "채팅"),
        (GameAction.Scoreboard, "순위표"),
    };

    /// <summary>무기 슬롯 번호(0부터) → 동작. 7칸 넘으면 Weapon7</summary>
    public static GameAction WeaponSlot(int i) => GameAction.Weapon1 + Mathf.Clamp(i, 0, 6);

    /// <summary>이동 축 (-1 · 0 · 1) — 키 두 개 다 누르면 0</summary>
    public static float Axis(GameAction neg, GameAction pos) => (Held(pos) ? 1f : 0f) - (Held(neg) ? 1f : 0f);

    static readonly Dictionary<GameAction, KeyCode> _cache = new();

    public static KeyCode Get(GameAction a)
    {
        if (_cache.TryGetValue(a, out var k)) return k;
        k = (KeyCode)PlayerPrefs.GetInt(P + a, (int)Defaults[a]);
        _cache[a] = k;
        return k;
    }

    public static void Set(GameAction a, KeyCode k)
    {
        var old = Get(a);
        if (old == k) return;

        // 같은 키를 쓰던 동작이 있으면 서로 바꿈
        foreach (GameAction other in Enum.GetValues(typeof(GameAction)))
            if (other != a && Get(other) == k) Store(other, old);

        Store(a, k);
        GameSettings.NotifyChanged();
    }

    static void Store(GameAction a, KeyCode k)
    {
        PlayerPrefs.SetInt(P + a, (int)k);
        _cache[a] = k;
    }

    /// <summary>기본 키 그대로인가</summary>
    public static bool IsDefault(GameAction a) => Defaults.TryGetValue(a, out var d) && Get(a) == d;

    public static void ResetAll()
    {
        foreach (var kv in Defaults) Store(kv.Key, kv.Value);
        GameSettings.NotifyChanged();
    }

    public static bool Down(GameAction a) => Input.GetKeyDown(Get(a));
    public static bool Held(GameAction a) => Input.GetKey(Get(a));
    public static bool Up(GameAction a) => Input.GetKeyUp(Get(a));

    /// <summary>화면에 보일 키 이름 (Mouse0 → 좌클릭 등). 번역 키: key.name.{KeyCode} (예: key.name.Mouse0)</summary>
    public static string Name(KeyCode k)
    {
        string ko = k switch
        {
            KeyCode.Mouse0 => "좌클릭",
            KeyCode.Mouse1 => "우클릭",
            KeyCode.Mouse2 => "휠 클릭",
            KeyCode.Mouse3 => "마우스 옆 1",
            KeyCode.Mouse4 => "마우스 옆 2",
            KeyCode.LeftShift => "왼쪽 Shift",
            KeyCode.RightShift => "오른쪽 Shift",
            KeyCode.LeftControl => "왼쪽 Ctrl",
            KeyCode.LeftAlt => "왼쪽 Alt",
            _ => null,
        };
        if (ko != null) return SetLoc.T("key.name." + k, ko);

        return k switch
        {
            KeyCode.Return => "Enter",
            KeyCode.Space => "Space",
            KeyCode.Escape => "Esc",
            KeyCode.UpArrow => "↑",
            KeyCode.DownArrow => "↓",
            KeyCode.LeftArrow => "←",
            KeyCode.RightArrow => "→",
            KeyCode.BackQuote => "`",
            >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
            >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => SetLoc.F("key.name.Keypad", "숫자패드 {0}", (int)(k - KeyCode.Keypad0)),
            _ => k.ToString(),
        };
    }

    public static string Name(GameAction a) => Name(Get(a));
}
