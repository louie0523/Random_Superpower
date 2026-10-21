using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 맞는 쪽(PlayerHealth.RPC_TakeDamage)에서 최종 피해에 곱하는 배율 규칙.
/// 공격자 · 피격자 둘 다 알아야 정해지는 효과용 — 약화(애교부리기 · 투명 망토), 암살자, 동지 등.
///
/// 규칙은 능력 클래스의 정적 함수로 등록 ([RuntimeInitializeOnLoadMethod]) → 모든 클라이언트에 같은 규칙이 있음.
/// 규칙 안에서는 네트워크로 동기화된 값(상태이상 · 변화 · 능력 ID · 위치)만 읽을 것.
/// 0을 돌려주면 피해 0 (최소 1 피해 규칙도 무시).
/// </summary>
public static class DamageRules
{
    /// <param name="target">맞는 사람 (이 코드는 그 사람의 권한자에서 돔)</param>
    /// <param name="attacker">때린 사람의 플레이어 오브젝트 (환경 피해면 null)</param>
    public delegate float Rule(PlayerHealth target, NetworkObject attacker, DamageInfo info);

    static readonly List<Rule> _rules = new();

    public static void Register(Rule rule)
    {
        if (rule != null && !_rules.Contains(rule)) _rules.Add(rule);
    }

    public static float Multiplier(PlayerHealth target, NetworkObject attacker, DamageInfo info)
    {
        float m = 1f;

        // 약화 — 때린 사람이 약화 상태면 그만큼 덜 들어감 (100 = 피해 없음)
        if (attacker != null && attacker.TryGetComponent<PlayerStatus>(out var st) && st.Has(StatusType.Weaken))
            m *= Mathf.Clamp01(1f - st.Get(StatusType.Weaken) * 0.01f);

        for (int i = 0; i < _rules.Count; i++)
        {
            float r = _rules[i](target, attacker, info);
            if (r >= 0f) m *= r;
        }
        return Mathf.Max(0f, m);
    }
}
