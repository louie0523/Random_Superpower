using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Gunsmith")]
public class GunsmithAbility : AbilityBehaviour
{
    public override void OnEquip()
    {
        Ctx.GetComponent<PlayerEnchant>()?.SetGunEnchant(true);
    }

    public override void OnUnequip()
    {
        Ctx.GetComponent<PlayerEnchant>()?.SetGunEnchant(false);
    }
}