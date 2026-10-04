using UnityEditor;
using UnityEngine;

/// <summary>
/// 스탯 단위가 의심스러운 값을 프로젝트 전체에서 찾음.
/// 규칙: Flat = 스탯 실제 값에 그대로 더함 / Percent = 퍼센트 숫자
///   배율 스탯(피해 계열, 기본 1.0) Flat 0.25 = +25%p
///   비율 스탯(치확/치피/쿨감/흡혈) Flat만, 0.1 = 10%
/// 콘솔 로그를 클릭하면 해당 에셋으로 이동.
/// </summary>
public static class StatUnitChecker
{
    [MenuItem("Tools/검사/스탯 단위 검사")]
    public static void Run()
    {
        int found = 0;

        // ── 능력 ──
        foreach (var a in LoadAll<AbilityData>("t:AbilityData"))
        {
            if (a.Passives != null)
                foreach (var p in a.Passives)
                    found += CheckEffects(a, $"패시브 '{p?.Name}'", p?.Effects);

            if (a.Skills != null)
                foreach (var s in a.Skills)
                    found += CheckEffects(a, $"스킬 '{s?.Name}'", s?.Effects);
        }

        // ── 장비 ──
        foreach (var e in LoadAll<EquipmentData>("t:EquipmentData"))
            if (e.BaseStats != null)
                foreach (var s in e.BaseStats)
                    found += Check(e, "BaseStats", s.Stat, s.Type, new[] { s.Value });

        // ── 상점 ──
        foreach (var item in LoadAll<ShopItemData>("t:ShopItemData"))
            if (item.Kind == ShopItemKind.Stat)
                found += Check(item, "ValuePerLevel", item.Stat, item.ModType, item.ValuePerLevel);

        // ── 인챈트 ──
        foreach (var en in LoadAll<EnchantData>("t:EnchantData"))
            if (en.Stats != null)
                foreach (var s in en.Stats)
                    found += Check(en, "Stats", s.Stat, s.Type, new[] { s.Value });

        if (found == 0)
            Debug.Log("[스탯 단위] ✓ 의심되는 값 없음");
        else
            Debug.LogWarning($"[스탯 단위] ⚠ {found}건 — 위 로그를 클릭하면 에셋으로 이동합니다");
    }

    static int CheckEffects(Object asset, string where, AbilityData.EffectDef[] effects)
    {
        if (effects == null) return 0;

        int n = 0;
        foreach (var e in effects)
            if (e != null && e.Effect == EffectType.StatMod)
                n += Check(asset, where, e.Stat, e.ModType, e.Values);
        return n;
    }

    static int Check(Object asset, string where, StatType stat, ModType mod, float[] values)
    {
        if (values == null || values.Length == 0) return 0;

        string head = $"[스탯 단위] {asset.name} / {where} / {stat} {mod}";

        if (mod == ModType.Percent && IsRatio(stat))
        {
            Debug.LogWarning($"{head}: 비율 스탯은 Percent가 무시됩니다 → Flat으로 (0.1 = 10%)", asset);
            return 1;
        }

        foreach (var v in values)
        {
            float a = Mathf.Abs(v);

            if (mod == ModType.Flat && IsMultiplier(stat) && a >= 1f)
            {
                Debug.LogWarning($"{head} = {v}: 배율이 +{v * 100f:0}%p 오릅니다. " +
                                 "0.25 = +25%p 단위가 맞나요?", asset);
                return 1;
            }

            if (mod == ModType.Flat && IsRatio(stat) && a >= 1f)
            {
                Debug.LogWarning($"{head} = {v}: +{v * 100f:0}% 입니다. 0.1 = 10% 단위가 맞나요?", asset);
                return 1;
            }

            if (mod == ModType.Percent && a > 0f && a < 1f)
            {
                Debug.LogWarning($"{head} = {v}: +{v}% (아주 작음). Percent는 25 = +25% 단위입니다.", asset);
                return 1;
            }
        }

        return 0;
    }

    static bool IsMultiplier(StatType s) =>
        s == StatType.DamageDealt || s == StatType.DamageTaken ||
        s == StatType.MeleeDamage || s == StatType.RangedDamage ||
        s == StatType.SkillDamage || s == StatType.UltimateDamage;

    static bool IsRatio(StatType s) =>
        s == StatType.CritChance || s == StatType.CritDamage ||
        s == StatType.CooldownReduction || s == StatType.LifeSteal;

    static System.Collections.Generic.IEnumerable<T> LoadAll<T>(string filter) where T : Object
    {
        foreach (var guid in AssetDatabase.FindAssets(filter))
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) yield return asset;
        }
    }
}
