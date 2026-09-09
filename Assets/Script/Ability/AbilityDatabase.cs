using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Ability Database")]
public class AbilityDatabase : ScriptableObject
{
    [Header("수집 폴더")]
    [SerializeField] private string mainFolder = "Assets/Data/Abilities/Main";
    [SerializeField] private string subFolder = "Assets/Data/Abilities/Sub";
    [SerializeField] private string randomFolder = "Assets/Data/Abilities/Random";

    [Header("분류별 목록")]
    public AbilityData[] MainAbilities;
    public AbilityData[] SubAbilities;
    public AbilityData[] RandomAbilities;

    [Header("통합 목록 (자동 생성)")]
    public AbilityData[] All;

    private Dictionary<int, AbilityData> _lookup;
    private List<AbilityData> _mainPool;
    private List<AbilityData> _randomPool;
    private Dictionary<SubTier, List<AbilityData>> _subPools;

    // ══════════════════ 조회 ══════════════════

    public AbilityData Get(int id)
    {
        if (_lookup == null) BuildCache();
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

    public AbilityData RollMain(System.Random rng = null)
    {
        if (_lookup == null) BuildCache();
        if (_mainPool.Count == 0) return null;

        int i = rng != null ? rng.Next(_mainPool.Count) : Random.Range(0, _mainPool.Count);
        return _mainPool[i];
    }

    public AbilityData RollSub(System.Random rng = null)
    {
        if (_lookup == null) BuildCache();

        float r = rng != null ? (float)rng.NextDouble() : Random.value;

        SubTier tier = r switch
        {
            < 0.60f => SubTier.Silver,
            < 0.90f => SubTier.Gold,
            _ => SubTier.Platinum,
        };

        var pool = GetSubPool(tier);
        if (pool.Count == 0)
        {
            pool = _subPools.Values.FirstOrDefault(p => p.Count > 0);
            if (pool == null || pool.Count == 0) return null;
        }

        int i = rng != null ? rng.Next(pool.Count) : Random.Range(0, pool.Count);
        return pool[i];
    }

    /// <summary>랜덤 능력 추첨. inUse로 인원 제한 반영</summary>
    public AbilityData RollRandom(int excludeId, IReadOnlyList<int> inUse = null)
    {
        if (_lookup == null) BuildCache();
        if (_randomPool.Count == 0) return null;

        var candidates = new List<AbilityData>();
        float totalWeight = 0f;

        foreach (var a in _randomPool)
        {
            if (a.ExcludeFromRoll) continue;
            if (a.Id == excludeId) continue;

            if (a.MaxConcurrent > 0 && inUse != null)
            {
                int count = 0;
                foreach (int id in inUse) if (id == a.Id) count++;
                if (count >= a.MaxConcurrent) continue;
            }

            candidates.Add(a);
            totalWeight += Mathf.Max(0.01f, a.RollWeight);
        }

        // 후보 고갈 시 제한 무시하고 폴백
        if (candidates.Count == 0)
        {
            foreach (var a in _randomPool)
                if (!a.ExcludeFromRoll && a.Id != excludeId)
                {
                    candidates.Add(a);
                    totalWeight += Mathf.Max(0.01f, a.RollWeight);
                }
        }

        if (candidates.Count == 0) return null;

        float r = Random.value * totalWeight;
        foreach (var a in candidates)
        {
            r -= Mathf.Max(0.01f, a.RollWeight);
            if (r <= 0f) return a;
        }

        return candidates[^1];
    }

    public List<AbilityData> GetSubPool(SubTier tier)
    {
        if (_lookup == null) BuildCache();
        return _subPools.TryGetValue(tier, out var p) ? p : new List<AbilityData>();
    }

    public List<AbilityData> GetByTag(AbilityTag tag)
    {
        if (_lookup == null) BuildCache();
        return All.Where(a => a != null && a.Tags != null && a.Tags.Contains(tag)).ToList();
    }

    // ══════════════════ 캐시 ══════════════════

    void BuildCache()
    {
        _lookup = new Dictionary<int, AbilityData>();
        _mainPool = new List<AbilityData>();
        _randomPool = new List<AbilityData>();
        _subPools = new Dictionary<SubTier, List<AbilityData>>();

        foreach (SubTier t in System.Enum.GetValues(typeof(SubTier)))
            _subPools[t] = new List<AbilityData>();

        if (All == null) return;

        foreach (var a in All)
        {
            if (a == null) continue;

            if (!_lookup.TryAdd(a.Id, a))
            {
                Debug.LogError($"[AbilityDB] ID 중복 {a.Id}: {a.name}", a);
                continue;
            }

            switch (a.Kind)
            {
                case AbilityKind.Main: _mainPool.Add(a); break;
                case AbilityKind.Random: _randomPool.Add(a); break;
                default: _subPools[a.Tier].Add(a); break;
            }
        }
    }

    public void InvalidateCache() => _lookup = null;

    // ══════════════════ 에디터 도구 ══════════════════

#if UNITY_EDITOR

    [ContextMenu("① 폴더별 수집 + 통합")]
    public void CollectAll()
    {
        MainAbilities = CollectFrom(mainFolder, AbilityKind.Main);
        SubAbilities = CollectFrom(subFolder, AbilityKind.Sub);
        RandomAbilities = CollectFrom(randomFolder, AbilityKind.Random);

        MergeToAll();

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AbilityDB] 수집 완료 — " +
                  $"메인 {MainAbilities.Length} / 서브 {SubAbilities.Length} / " +
                  $"랜덤 {RandomAbilities.Length} → 전체 {All.Length}");

        Validate();
    }

    AbilityData[] CollectFrom(string folder, AbilityKind expectedKind)
    {
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"[AbilityDB] 폴더 없음: {folder}");
            return new AbilityData[0];
        }

        string[] guids = AssetDatabase.FindAssets(
            $"t:{nameof(AbilityData)}", new[] { folder });

        var found = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AbilityData>)
            .Where(a => a != null)
            .OrderBy(a => a.Id)
            .ToArray();

        // Kind 불일치 경고
        foreach (var a in found)
            if (a.Kind != expectedKind)
                Debug.LogWarning($"[AbilityDB] {a.name}: {folder}에 있는데 " +
                                 $"Kind가 {a.Kind}입니다 (기대: {expectedKind})", a);

        return found;
    }

    [ContextMenu("② 목록만 통합")]
    public void MergeToAll()
    {
        var merged = new List<AbilityData>();

        AddRange(merged, MainAbilities);
        AddRange(merged, SubAbilities);
        AddRange(merged, RandomAbilities);

        All = merged.OrderBy(a => a.Id).ToArray();
        _lookup = null;

        EditorUtility.SetDirty(this);
        Debug.Log($"[AbilityDB] 통합: {All.Length}개");
    }

    static void AddRange(List<AbilityData> dst, AbilityData[] src)
    {
        if (src == null) return;
        foreach (var a in src)
            if (a != null && !dst.Contains(a)) dst.Add(a);
    }

    [ContextMenu("③ 검사")]
    public void Validate()
    {
        if (All == null || All.Length == 0)
        {
            Debug.LogWarning("[AbilityDB] 목록이 비어 있음");
            return;
        }

        var seen = new Dictionary<int, AbilityData>();
        int errors = 0;

        foreach (var a in All)
        {
            if (a == null) { Debug.LogError("[AbilityDB] 빈 슬롯"); errors++; continue; }

            if (a.Id == 0)
            {
                Debug.LogError($"[AbilityDB] {a.name}: ID가 0", a);
                errors++;
            }

            if (seen.TryGetValue(a.Id, out var dup))
            {
                Debug.LogError($"[AbilityDB] ID 중복 {a.Id}: {a.name} ↔ {dup.name}", a);
                errors++;
            }
            else seen[a.Id] = a;

            int band = a.Id / 1000;
            int expected = a.Kind switch
            {
                AbilityKind.Main => 6,
                AbilityKind.Sub => 7,
                AbilityKind.Random => 8,
                _ => -1
            };

            if (band != expected)
            {
                Debug.LogWarning($"[AbilityDB] {a.name}: ID {a.Id}가 " +
                                 $"{a.Kind}({expected}000번대)와 불일치", a);
                errors++;
            }

            if (string.IsNullOrWhiteSpace(a.DisplayName))
            {
                Debug.LogError($"[AbilityDB] {a.name}: 표시 이름 없음", a);
                errors++;
            }

            bool hasContent =
                (a.Passives != null && a.Passives.Length > 0) ||
                (a.Skills != null && a.Skills.Length > 0) ||
                a.Behaviour != null;

            if (!hasContent)
            {
                Debug.LogError($"[AbilityDB] {a.name}: 내용이 비어 있음", a);
                errors++;
            }

            errors += ValidateEffects(a);
            errors += ValidateEvolve(a);
        }

        BuildCache();
        string summary = $"[AbilityDB] 메인 {_mainPool.Count} / 랜덤 {_randomPool.Count}";
        foreach (var kv in _subPools) summary += $" / {kv.Key} {kv.Value.Count}";
        Debug.Log(summary);

        if (errors == 0) Debug.Log($"[AbilityDB] 검사 통과 — {All.Length}개");
        else Debug.LogError($"[AbilityDB] 문제 {errors}건");
    }

    int ValidateEffects(AbilityData a)
    {
        int errors = 0;

        void CheckEffects(string owner, AbilityData.EffectDef[] effects)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (e.Chance <= 0f)
                {
                    Debug.LogError($"[AbilityDB] {a.name} / {owner}: " +
                                   "Chance가 0이라 절대 발동하지 않습니다", a);
                    errors++;
                }
            }
        }

        if (a.Passives != null)
            foreach (var p in a.Passives)
            {
                if (string.IsNullOrWhiteSpace(p.Description))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {p.Name}: 설명 없음", a);
                    errors++;
                }
                CheckEffects(p.Name, p.Effects);
            }

        if (a.Skills != null)
            foreach (var s in a.Skills)
            {
                if (string.IsNullOrWhiteSpace(s.Description))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {s.Name}: 설명 없음", a);
                    errors++;
                }
                CheckEffects(s.Name, s.Effects);
            }

        return errors;
    }

    int ValidateEvolve(AbilityData a)
    {
        if (a.EvolveToId == 0) return 0;

        int errors = 0;
        var target = All.FirstOrDefault(x => x != null && x.Id == a.EvolveToId);

        if (target == null)
        {
            Debug.LogError($"[AbilityDB] {a.name}: 진화 대상 {a.EvolveToId} 없음", a);
            return 1;
        }

        if (!target.ExcludeFromRoll)
        {
            Debug.LogWarning($"[AbilityDB] {target.name}: 진화형인데 " +
                             "ExcludeFromRoll이 꺼져 있음", target);
            errors++;
        }

        if (target.Kind != a.Kind)
        {
            Debug.LogError($"[AbilityDB] {a.name} → {target.name}: Kind 불일치", a);
            errors++;
        }

        if (a.EvolveKillCount <= 0)
        {
            Debug.LogWarning($"[AbilityDB] {a.name}: EvolveKillCount가 0", a);
            errors++;
        }

        return errors;
    }

    [ContextMenu("④ 다음 빈 ID")]
    public void FindNextIds()
    {
        var used = new HashSet<int>(All?.Where(a => a != null).Select(a => a.Id)
                                    ?? Enumerable.Empty<int>());

        int m = 6001; while (used.Contains(m)) m++;
        int s = 7001; while (used.Contains(s)) s++;
        int r = 8001; while (used.Contains(r)) r++;

        Debug.Log($"[AbilityDB] 다음 ID\n  Main: {m}\n  Sub: {s}\n  Random: {r}");
    }

    [ContextMenu("⑤ Chance 0 일괄 수정")]
    public void FixZeroChance()
    {
        int count = 0;

        foreach (var a in All)
        {
            if (a == null) continue;
            bool dirty = false;

            void Fix(AbilityData.EffectDef[] effects)
            {
                if (effects == null) return;
                foreach (var e in effects)
                    if (e.Chance <= 0f) { e.Chance = 1f; dirty = true; count++; }
            }

            if (a.Passives != null) foreach (var p in a.Passives) Fix(p.Effects);
            if (a.Skills != null) foreach (var s in a.Skills) Fix(s.Effects);

            if (dirty) EditorUtility.SetDirty(a);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AbilityDB] Chance 수정: {count}건");
    }

#endif
}