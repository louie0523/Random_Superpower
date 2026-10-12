using UnityEngine;

public class PlayerSkillInput : MonoBehaviour
{
    public bool[] Pressed = new bool[SkillSlotExt.Count];

    [Header("Keys")]
    [SerializeField] private KeyCode skill1 = KeyCode.LeftShift;
    [SerializeField] private KeyCode skill2 = KeyCode.E;
    [SerializeField] private KeyCode ultimate = KeyCode.Q;
    [SerializeField] private bool skill3UseRightMouse = true;
    [SerializeField] private KeyCode skill3Alt = KeyCode.LeftControl;
    [SerializeField] private KeyCode randomSkill = KeyCode.F;
    [Tooltip("두 번째 랜덤 능력 (조커)")]
    [SerializeField] private KeyCode randomSkill2 = KeyCode.V;

    void Awake()
    {
        // 프리팹에 예전 길이(5)로 저장돼 있어도 칸 수를 맞춤
        if (Pressed == null || Pressed.Length != SkillSlotExt.Count) Pressed = new bool[SkillSlotExt.Count];
    }

    void Update()
    {
        if (UIManager.InputBlocked) return;

        if (Input.GetKeyDown(skill1)) Pressed[(int)SkillSlot.Skill1] = true;
        if (Input.GetKeyDown(skill2)) Pressed[(int)SkillSlot.Skill2] = true;

        if (skill3UseRightMouse)
        {
            if (Input.GetMouseButtonDown(1)) Pressed[(int)SkillSlot.Skill3] = true;
        }
        else if (Input.GetKeyDown(skill3Alt)) Pressed[(int)SkillSlot.Skill3] = true;

        if (Input.GetKeyDown(ultimate)) Pressed[(int)SkillSlot.Ultimate] = true;
        if (Input.GetKeyDown(randomSkill)) Pressed[(int)SkillSlot.Random] = true;
        if (Input.GetKeyDown(randomSkill2)) Pressed[(int)SkillSlot.Random2] = true;
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