using UnityEngine;

public enum DamageSource
{
    MeleeWeapon,
    RangedWeapon,
    Skill,
    Ultimate,
    Environment,
}

/// <summary>
/// 스킬 판정 — 스킬이 무엇으로 때리는지. 근거리면 '근접 무기 피해', 원거리면 '원거리 무기 피해'도 같이 받음
/// (스킬 피해 · 궁극기 피해는 그대로 받고 그 위에 곱함)
/// </summary>
public enum SkillReach
{
    [InspectorName("자동 (무기 판정 스킬이면 든 무기 따라)")] Auto,
    [InspectorName("근거리")] Melee,
    [InspectorName("원거리")] Ranged,
    [InspectorName("기타")] Other,
}

public static class DamageCalc
{
    const float DEF_CONSTANT = 100f;

    /// <summary>공격자 기준 최종 공격력 계산</summary>
    /// <param name="reach">스킬 판정 (스킬 · 궁극기일 때만) — 근거리 = 근접 무기 피해, 원거리 = 원거리 무기 피해도 곱함.
    /// Auto는 여기 오기 전에 정해져 있어야 함 (모르면 기타처럼)</param>
    public static float CalcOutgoing(PlayerStats attacker, float baseDamage,
                                     DamageSource source, bool applyCrit, out bool isCrit,
                                     SkillReach reach = SkillReach.Other)
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
                dmg *= ReachMul(attacker, reach);
                break;

            case DamageSource.Ultimate:
                dmg *= attacker.Get(StatType.SkillDamage);
                dmg *= attacker.Get(StatType.UltimateDamage);
                dmg *= ReachMul(attacker, reach);
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

    /// <summary>스킬 판정 배율 — 근거리 = 근접 무기 피해 · 원거리 = 원거리 무기 피해 · 기타 = 1</summary>
    public static float ReachMul(PlayerStats attacker, SkillReach reach) => reach switch
    {
        SkillReach.Melee => attacker.Get(StatType.MeleeDamage),
        SkillReach.Ranged => attacker.Get(StatType.RangedDamage),
        _ => 1f,
    };

    /// <summary>자동 판정 풀기 — 무기 판정 스킬이면 든 무기 계열, 아니면 기타</summary>
    public static SkillReach Resolve(SkillReach reach, bool weaponHitSkill, EquipmentData heldWeapon)
    {
        if (reach != SkillReach.Auto) return reach;
        if (!weaponHitSkill || heldWeapon == null) return SkillReach.Other;
        return heldWeapon.Class == WeaponClass.Ranged ? SkillReach.Ranged : SkillReach.Melee;
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