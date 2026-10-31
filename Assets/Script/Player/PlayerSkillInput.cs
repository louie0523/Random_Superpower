using UnityEngine;

/// <summary>
/// 스킬 키 입력 → 칸별 눌림 표시 (AbilityRuntime이 틱에서 소비).
/// 키는 설정의 키 설정(KeyBinds)에서. 설정 '스킬 — 뗄 때 쓰기'를 켜면 키를 뗄 때 씀.
/// </summary>
public class PlayerSkillInput : MonoBehaviour
{
    public bool[] Pressed = new bool[SkillSlotExt.Count];

    static readonly (SkillSlot Slot, GameAction Action)[] Map =
    {
        (SkillSlot.Skill1, GameAction.Skill1),
        (SkillSlot.Skill2, GameAction.Skill2),
        (SkillSlot.Skill3, GameAction.Skill3),
        (SkillSlot.Ultimate, GameAction.Ultimate),
        (SkillSlot.Random, GameAction.Random),
        (SkillSlot.Random2, GameAction.Random2),
    };

    // 뗄 때 쓰기 — 누른 게 창이 열리기 전이었는지 (창을 닫다가 떼서 나가는 것 방지)
    readonly bool[] _armed = new bool[SkillSlotExt.Count];

    void Awake()
    {
        // 프리팹에 예전 길이(5)로 저장돼 있어도 칸 수를 맞춤
        if (Pressed == null || Pressed.Length != SkillSlotExt.Count) Pressed = new bool[SkillSlotExt.Count];
    }

    void Update()
    {
        if (UIManager.InputBlocked)
        {
            System.Array.Clear(_armed, 0, _armed.Length);
            return;
        }

        bool onRelease = GameSettings.SkillOnRelease;

        foreach (var (slot, action) in Map)
        {
            int i = (int)slot;
            if (i < 0 || i >= Pressed.Length) continue;

            if (!onRelease)
            {
                if (KeyBinds.Down(action)) Pressed[i] = true;
                continue;
            }

            if (KeyBinds.Down(action)) _armed[i] = true;
            if (_armed[i] && KeyBinds.Up(action))
            {
                _armed[i] = false;
                Pressed[i] = true;
            }
        }
    }

    /// <summary>FixedUpdateNetwork에서 소비</summary>
    public bool Consume(int slot)
    {
        if (slot < 0 || slot >= Pressed.Length) return false;
        bool v = Pressed[slot];
        Pressed[slot] = false;
        return v;
    }

    public void ClearAll()
    {
        for (int i = 0; i < Pressed.Length; i++) Pressed[i] = false;
    }
}
