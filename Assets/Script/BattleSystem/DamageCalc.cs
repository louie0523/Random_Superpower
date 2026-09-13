using UnityEngine;

public enum DamageSource
{
    MeleeWeapon,
    RangedWeapon,
    Skill,
    Ultimate,
    Environment,
}

public static class DamageCalc
{
    const float DEF_CONSTANT = 100f;

    /// <summary>공격자 기준 최종 공격력 계산</summary>
    public static float CalcOutgoing(PlayerStats attacker, float baseDamage,
                                     DamageSource source, bool applyCrit, out bool isCrit)
    {
        isCrit = false;
        if (attacker == null) return baseDamage;

        float dmg = baseDamage;

        // 출처별 배율
        switch (source)
        {
            case DamageSource.MeleeWeapon:
                dmg *= attacker.Get(StatType.MeleeDamage);
                break;

            case DamageSource.RangedWeapon:
                dmg *= attacker.Get(StatType.RangedDamage);
                break;

            case DamageSource.Skill:
                dmg *= attacker.Get(StatType.SkillDamage);
                break;

            case DamageSource.Ultimate:
                dmg *= attacker.Get(StatType.SkillDamage);
                dmg *= attacker.Get(StatType.UltimateDamage);
                break;
        }

        // 치명타
        if (applyCrit && Random.value < attacker.Get(StatType.CritChance))
        {
            dmg *= attacker.Get(StatType.CritDamage);
            isCrit = true;
        }

        // 최종 가피증
        dmg *= attacker.Get(StatType.DamageDealt);

        return dmg;
    }

    /// <summary>피격자 기준 최종 피해 계산</summary>
    public static int CalcIncoming(PlayerStats target, float incoming, float defPen = 0f)
    {
        if (target == null) return Mathf.Max(1, Mathf.RoundToInt(incoming));

        float def = Mathf.Max(0f, target.Get(StatType.Defense) * (1f - defPen));
        float afterDef = incoming * (DEF_CONSTANT / (DEF_CONSTANT + def));

        // 최종 가피감
        afterDef *= target.Get(StatType.DamageTaken);

        return Mathf.Max(1, Mathf.RoundToInt(afterDef));
    }
}