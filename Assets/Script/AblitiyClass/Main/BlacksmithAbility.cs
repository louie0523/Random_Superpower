using UnityEngine;

/// <summary>대장장이 — 무기별 인챈트 한계를 풀어 EnchantRules의 해제 단계(기본 100강)까지</summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Blacksmith")]
public class BlacksmithAbility : AbilityBehaviour
{
    public override void OnEquip()
    {
        Ctx.GetComponent<PlayerEnchant>()?.SetUnlimitedStage(true);
    }

    public override void OnUnequip()
    {
        Ctx.GetComponent<PlayerEnchant>()?.SetUnlimitedStage(false);
    }
}
