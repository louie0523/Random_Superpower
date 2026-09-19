using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Crab")]
public class CrabAbility : AbilityBehaviour
{
    public override void OnEquip()
    {
        Ctx.MoveLock = AbilityRuntime.MoveLockMode.StrafeOnly;
    }

    public override void OnUnequip()
    {
        Ctx.MoveLock = AbilityRuntime.MoveLockMode.None;
    }
}