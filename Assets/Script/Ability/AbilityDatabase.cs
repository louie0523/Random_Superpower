using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Ability Database")]
public class AbilityDatabase : ScriptableObject
{
    [Header("Auto Collect")]
    [Tooltip("이 폴더 아래를 모두 훑습니다. 예: Assets/Data/Abilities")]
    [SerializeField] private string searchFolder = "Assets/Data/Abilities";

    public AbilityData[] All;

    private Dictionary<int, AbilityData> _lookup;
    private List<AbilityData> _mainPool;
    private Dictionary<SubTier, List<AbilityData>> _subPools;
    private List<AbilityData> _randomPool;

    // ══════════ 조회 ══════════

    public AbilityData Get(int id)
    {
        if (_lookup == null) BuildCache();
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>메인 능력 랜덤 추첨</summary>
    public AbilityData RollMain(System.Random rng = null)
    {
        if (_lookup == null) BuildCache();
        if (_mainPool.Count == 0) return null;

        int i = rng != null
            ? rng.Next(_mainPool.Count)
            : Random.Range(0, _mainPool.Count);
        return _mainPool[i];
    }

    /// <summary>서브 능력 랜덤 추첨 (등급 가중치 적용)</summary>
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
            // 해당 등급이 비었으면 아무거나
            pool = _subPools.Values.FirstOrDefault(p => p.Count > 0);
            if (pool == null || pool.Count == 0) return null;
        }

        int i = rng != null ? rng.Next(pool.Count) : Random.Range(0, pool.Count);
        return pool[i];
    }

    public List<AbilityData> GetSubPool(SubTier tier)
    {
        if (_lookup == null) BuildCache();
        return _subPools.TryGetValue(tier, out var p) ? p : new List<AbilityData>();
    }

    /// <summary>태그로 필터링</summary>
    public List<AbilityData> GetByTag(AbilityTag tag)
    {
        if (_lookup == null) BuildCache();
        return All.Where(a => a != null && a.Tags != null && a.Tags.Contains(tag)).ToList();
    }

    // ══════════ 캐시 ══════════

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

    /// <summary>랜덤 능력 추첨. excludeId와 다른 것을 보장</summary>
    public AbilityData RollRandom(int excludeId = 0)
    {
        if (_lookup == null) BuildCache();
        if (_randomPool.Count == 0) return null;
        if (_randomPool.Count == 1) return _randomPool[0];

        AbilityData pick;
        int guard = 0;
        do
        {
            pick = _randomPool[Random.Range(0, _randomPool.Count)];
        } while (pick.Id == excludeId && ++guard < 20);

        return pick;
    }

    public void InvalidateCache() => _lookup = null;

    // ══════════ 에디터 도구 ══════════

#if UNITY_EDITOR

    [ContextMenu("① 폴더에서 모두 수집")]
    public void CollectFromFolder()
    {
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            Debug.LogError($"[AbilityDB] 폴더 없음: {searchFolder}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets(
            $"t:{nameof(AbilityData)}", new[] { searchFolder });

        All = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AbilityData>)
            .Where(a => a != null)
            .OrderBy(a => a.Id)
            .ToArray();

        _lookup = null;

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AbilityDB] {All.Length}개 수집 완료");
        Validate();
    }

    [ContextMenu("② 검사")]
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
            if (a == null)
            {
                Debug.LogError("[AbilityDB] 빈 슬롯 있음");
                errors++;
                continue;
            }

            // ID
            if (a.Id == 0)
            {
                Debug.LogError($"[AbilityDB] {a.name}: ID가 0 (예약값)", a);
                errors++;
            }

            if (seen.TryGetValue(a.Id, out var dup))
            {
                Debug.LogError($"[AbilityDB] ID 중복 {a.Id}: {a.name} ↔ {dup.name}", a);
                errors++;
            }
            else seen[a.Id] = a;

            // 대역 (메인 6000, 서브 7000)
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
                Debug.LogWarning(
                    $"[AbilityDB] {a.name}: ID {a.Id}가 종류({a.Kind})와 불일치. " +
                    $"{expected}000번대여야 함", a);
                errors++;
            }

            // 필수 표현 요소
            if (a.Icon == null)
            {
                Debug.LogWarning($"[AbilityDB] {a.name}: 아이콘 없음", a);
                errors++;
            }

            if (string.IsNullOrWhiteSpace(a.DisplayName))
            {
                Debug.LogError($"[AbilityDB] {a.name}: 표시 이름 비어있음", a);
                errors++;
            }

            // 내용
            bool hasContent =
                (a.Passives != null && a.Passives.Length > 0) ||
                (a.Skills != null && a.Skills.Length > 0);

            if (!hasContent)
            {
                Debug.LogError($"[AbilityDB] {a.name}: 패시브도 스킬도 없음", a);
                errors++;
            }

            if (a.Skills != null)
            {
                foreach (var s in a.Skills)
                {
                    if (string.IsNullOrWhiteSpace(s.Description))
                    {
                        Debug.LogWarning($"[AbilityDB] {a.name} / {s.Name}: 설명 없음", a);
                        errors++;
                    }

                    // 설명에 {0}이 있는데 Effects가 비었으면 치환 실패
                    if (s.Description != null && s.Description.Contains("{0}") &&
                        (s.Effects == null || s.Effects.Length == 0))
                    {
                        Debug.LogError($"[AbilityDB] {a.name} / {s.Name}: " +
                                       "설명에 {0}이 있는데 Effects가 비었습니다", a);
                        errors++;
                    }
                }
            }

            errors += ValidateDescriptions(a);
        }

        // 풀 요약
        BuildCache();
        string summary = $"[AbilityDB] 메인 {_mainPool.Count}개";
        foreach (var kv in _subPools)
            summary += $" / {kv.Key} {kv.Value.Count}개";
        Debug.Log(summary);

        if (errors == 0) Debug.Log($"[AbilityDB] 검사 통과 — {All.Length}개");
        else Debug.LogError($"[AbilityDB] 문제 {errors}건");
    }

    int ValidateDescriptions(AbilityData a)
    {
        int errors = 0;

        if (a.Passives != null)
        {
            foreach (var p in a.Passives)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name}: 이름 없는 패시브", a);
                    errors++;
                }
                if (string.IsNullOrWhiteSpace(p.Description))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {p.Name}: 설명 없음", a);
                    errors++;
                }
                if (p.Effects == null || p.Effects.Length == 0)
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {p.Name}: 효과 없음", a);
                    errors++;
                }
            }
        }

        if (a.Skills != null)
        {
            foreach (var s in a.Skills)
            {
                if (string.IsNullOrWhiteSpace(s.Name))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name}: 이름 없는 스킬", a);
                    errors++;
                }
                if (string.IsNullOrWhiteSpace(s.Description))
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {s.Name}: 설명 없음", a);
                    errors++;
                }
                if (s.Effects == null || s.Effects.Length == 0)
                {
                    Debug.LogWarning($"[AbilityDB] {a.name} / {s.Name}: 효과 없음", a);
                    errors++;
                }
            }
        }

        return errors;
    }

    [ContextMenu("③ 다음 빈 ID 찾기")]
    public void FindNextIds()
    {
        var used = new HashSet<int>(All?.Where(a => a != null).Select(a => a.Id)
                                    ?? Enumerable.Empty<int>());

        int mainNext = 6001;
        while (used.Contains(mainNext)) mainNext++;

        int subNext = 7001;
        while (used.Contains(subNext)) subNext++;

        Debug.Log($"[AbilityDB] 다음 사용 가능 ID\n  Main: {mainNext}\n  Sub: {subNext}");
    }

    [ContextMenu("④ 추첨 테스트 (20회)")]
    public void TestRoll()
    {
        BuildCache();
        var counts = new Dictionary<string, int>();

        for (int i = 0; i < 20; i++)
        {
            var sub = RollSub();
            string key = sub == null ? "(없음)" : $"{sub.Tier} - {sub.DisplayName}";
            counts.TryGetValue(key, out int c);
            counts[key] = c + 1;
        }

        var sb = new System.Text.StringBuilder("[AbilityDB] 서브 추첨 20회\n");
        foreach (var kv in counts.OrderByDescending(k => k.Value))
            sb.AppendLine($"  {kv.Key}: {kv.Value}");
        Debug.Log(sb.ToString());
    }

#endif
}