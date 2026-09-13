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

    void Update()
    {
        if (Input.GetKeyDown(skill1)) Pressed[0] = true;
        if (Input.GetKeyDown(skill2)) Pressed[1] = true;

        if (skill3UseRightMouse)
        {
            if (Input.GetMouseButtonDown(1)) Pressed[2] = true;
        }
        else if (Input.GetKeyDown(skill3Alt)) Pressed[2] = true;

        if (Input.GetKeyDown(ultimate)) Pressed[3] = true;

        if (Input.GetKeyDown(randomSkill)) Pressed[4] = true;
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