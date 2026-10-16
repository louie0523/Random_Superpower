using UnityEngine;

/// <summary>
/// 본 묶음 IK (FABRIK). 끝 본이 target에 닿도록 루트 → 끝 본들을 돌림.
/// 지금 굽은 방향을 유지하며 풀어서 무릎·손가락이 엉뚱하게 꺾이지 않음.
/// 에디터(Rig Animator)와 게임 중(RigAnimator.AimAt) 둘 다 이걸 씀.
/// </summary>
public static class RigIK
{
    static Vector3[] _p = new Vector3[16];
    static float[] _len = new float[16];
    static Quaternion[] _save = new Quaternion[16];

    /// <summary>늘어나기를 켠 IK — 목표가 본 길이의 이 비율보다 멀면 일자로 고정 (경계 떨림 방지)</summary>
    const float StretchStart = 0.985f;

    /// <summary>chain: 루트 → 끝 (중간에 다른 본이 끼어 있어도 됨)</summary>
    /// <param name="maxStretch">1보다 크면 목표가 본 길이보다 멀 때 본을 이 배율까지 늘려서 닿게 함 (고무팔)</param>
    public static void Solve(Transform[] chain, Vector3 target, int iterations = 12, float tolerance = 0.0005f, float maxStretch = 1f)
    {
        if (chain == null) return;
        int n = chain.Length;
        if (n < 2) return;
        for (int i = 0; i < n; i++) if (chain[i] == null) return;

        if (_p.Length < n) { _p = new Vector3[n]; _len = new float[n]; }

        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            _p[i] = chain[i].position;
            if (i > 0) { _len[i - 1] = Vector3.Distance(_p[i - 1], _p[i]); total += _len[i - 1]; }
        }
        if (total < 1e-5f) return;

        // 늘어나기 — 목표까지 (거의) 모자라면 본 간격(위치)을 늘리고 일자로 쭉 뻗음.
        // 다음 프레임엔 애니메이션이 원래 길이로 다시 씀.
        // 끝까지 뻗은 근처에서 FABRIK이 프레임마다 다르게 굽어 떨리던 것 → 그 구간은 늘 일자로 고정
        bool straightOut = false;
        if (maxStretch > 1.0001f)
        {
            float dist = Vector3.Distance(_p[0], target);
            if (dist > total * StretchStart)
            {
                float f = Mathf.Clamp(dist / total, 1f, maxStretch);
                if (f > 1.0001f)
                {
                    for (int i = 1; i < n; i++) chain[i].localPosition *= f;
                    total = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        _p[i] = chain[i].position;
                        if (i > 0) { _len[i - 1] = Vector3.Distance(_p[i - 1], _p[i]); total += _len[i - 1]; }
                    }
                }
                straightOut = true;
            }
        }

        Vector3 root = _p[0];
        Vector3 toTarget = target - root;

        if (straightOut || toTarget.magnitude >= total)
        {
            // 닿지 않음(또는 늘어남) — 쭉 뻗음
            Vector3 dir = toTarget.sqrMagnitude > 1e-10f ? toTarget.normalized : (_p[n - 1] - root).normalized;
            for (int i = 1; i < n; i++) _p[i] = _p[i - 1] + dir * _len[i - 1];
        }
        else
        {
            // 일자로 펴져 있으면 굽힐 방향이 없음 → 살짝 굽혀서 시작 (본의 위쪽으로)
            if (n > 2)
            {
                Vector3 a = (_p[n - 1] - _p[0]).normalized;
                bool straight = true;
                for (int i = 1; i < n - 1; i++)
                    if (Vector3.Cross(a, (_p[i] - _p[0]).normalized).sqrMagnitude > 1e-4f) { straight = false; break; }
                if (straight)
                {
                    Vector3 hint = Vector3.Cross(a, chain[0].right);
                    if (hint.sqrMagnitude < 1e-4f) hint = Vector3.Cross(a, Vector3.right);
                    hint = hint.normalized * total * 0.02f;
                    for (int i = 1; i < n - 1; i++) _p[i] += hint;
                }
            }

            for (int it = 0; it < iterations; it++)
            {
                // 끝 → 루트
                _p[n - 1] = target;
                for (int i = n - 2; i >= 0; i--)
                    _p[i] = _p[i + 1] + (_p[i] - _p[i + 1]).normalized * _len[i];

                // 루트 → 끝
                _p[0] = root;
                for (int i = 1; i < n; i++)
                    _p[i] = _p[i - 1] + (_p[i] - _p[i - 1]).normalized * _len[i - 1];

                if ((_p[n - 1] - target).sqrMagnitude < tolerance * tolerance) break;
            }
        }

        // 위치 → 회전 (루트부터 — 부모가 돌면 자식도 같이 움직이므로 매번 현재 위치로 다시 잼)
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 cur = chain[i + 1].position - chain[i].position;
            Vector3 want = _p[i + 1] - chain[i].position;
            if (cur.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) continue;
            chain[i].rotation = Quaternion.FromToRotation(cur, want) * chain[i].rotation;
        }
    }

    /// <summary>weight만큼만 IK (0 = 원래 자세, 1 = 완전히 목표로)</summary>
    static Vector3[] _savePos = new Vector3[16];

    public static void SolveWeighted(Transform[] chain, Vector3 target, float weight, int iterations = 12, float maxStretch = 1f)
    {
        if (chain == null || chain.Length < 2 || weight <= 0.0001f) return;
        if (weight >= 0.9999f) { Solve(chain, target, iterations, 0.0005f, maxStretch); return; }

        int n = chain.Length;
        if (_save.Length < n) { _save = new Quaternion[n]; _savePos = new Vector3[n]; }
        for (int i = 0; i < n; i++) { _save[i] = chain[i].localRotation; _savePos[i] = chain[i].localPosition; }   // Solve는 _save를 안 건드림

        Solve(chain, target, iterations, 0.0005f, maxStretch);

        for (int i = 0; i < n; i++)
        {
            chain[i].localRotation = Quaternion.Slerp(_save[i], chain[i].localRotation, weight);
            chain[i].localPosition = Vector3.Lerp(_savePos[i], chain[i].localPosition, weight);
        }
    }
}