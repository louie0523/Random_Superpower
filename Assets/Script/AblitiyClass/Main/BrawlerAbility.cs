using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Brawler")]
public class BrawlerAbility : AbilityBehaviour
{
    public float MoveBonus = 1.5f;
    public float AttackSpeedBonus = 0.5f;

    private int _src;
    private bool _applied;

    public override void OnEquip()  
    {
        _src = Src(0);      // 기존: SourceId.BuffBase + 50000
        _applied = false;
    }

    public override void OnUnequip() => Remove();

    public override void Tick(float dt)
    {
        var w = Ctx.Equipment?.GetWeapon();
        bool unarmed = w != null && w.IsUnarmed;

        if (unarmed && !_applied) Apply();
        else if (!unarmed && _applied) Remove();
    }

    void Apply()
    {
        Ctx.Stats?.AddModifier(new StatModifier(
            StatType.MoveSpeed, ModType.Flat, MoveBonus, _src));
        Ctx.Stats?.AddModifier(new StatModifier(
            StatType.AttackSpeed, ModType.Flat, AttackSpeedBonus, _src));
        _applied = true;
    }

    void Remove()
    {
        Ctx.Stats?.RemoveBySource(_src);
        _applied = false;
    }
}