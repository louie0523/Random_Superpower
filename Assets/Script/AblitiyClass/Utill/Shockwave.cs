using System.Collections.Generic;
using UnityEngine;

/// <summary>쿵! 느낌 — 소리 · 이펙트 · 주변 사람 화면 흔들림 (모든 화면에서 각자)</summary>
[System.Serializable]
public class FeelDef
{
    [Tooltip("이 거리(m) 안의 사람 화면이 흔들림 — 가까울수록 세게. 0이면 안 흔듦")]
    public float ShakeRadius = 12f;
    [Tooltip("흔들림 세기 (FPSCamera.Shake 값 — 일반 타격이 0.15~0.3)")]
    public float ShakeAmount = 0.3f;
    [Tooltip("일으킨 본인 화면은 이 배율로 (0 = 본인은 안 흔들림)")]
    [Range(0f, 1f)] public float ShakeSelf = 0.4f;

    [Header("소리")]
    public AudioClip Sound;
    [Range(0f, 1f)] public float Volume = 1f;
    [Tooltip("이 거리(m)까지 들림 (멀수록 작게)")]
    public float SoundRange = 35f;
    [Range(0f, 0.3f)] public float PitchJitter = 0.06f;

    [Header("이펙트")]
    public GameObject Fx;
    public float FxLife = 2f;
}

/// <summary>쿵! + 주변 적 기절 · 밀치기 · 피해 (권한자) — 거신병 공격 · 도약 · 착지 등</summary>
[System.Serializable]
public class ShockwaveDef
{
    public bool Enabled = true;
    [Tooltip("범위 (m)")]
    public float Radius = 3.5f;
    [Tooltip("중심을 앞으로 (m) — 앞쪽 땅을 내리치면 1~2")]
    public float Forward = 0f;
    [Tooltip("피해 — 공격력 % (0이면 피해 없음 — 무기 판정이 따로 피해를 주면 0 권장)")]
    public float DamageCoef = 0f;
    [Tooltip("기절 (초)")]
    public float Stun = 1f;
    [Tooltip("밀치기 — 바깥 · 위 (m/초)")]
    public float Push = 9f, PushUp = 4f;
    public FeelDef Feel = new();
}

/// <summary>쿵! 실행 도우미</summary>
public static class Shockwave
{
    static readonly Collider[] _hits = new Collider[32];
    static readonly HashSet<PlayerHealth> _seen = new();

    static FPSCamera _cam;
    static Transform _camRoot;

    /// <summary>주인 기준 앞으로 forward m 땅 위치</summary>
    public static Vector3 Center(Transform owner, float forward)
    {
        if (owner == null) return Vector3.zero;
        Vector3 f = owner.forward; f.y = 0f;
        f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        return owner.position + f * forward;
    }

    /// <summary>권한자 — 범위 안 적에게 피해 · 기절 · 밀치기</summary>
    public static void Hit(AbilityRuntime ctx, Vector3 center, ShockwaveDef d)
    {
        if (ctx == null || d == null || !d.Enabled || d.Radius <= 0f) return;
        if (ctx.Object == null || !ctx.Object.IsValid || !ctx.HasStateAuthority) return;

        var me = ctx.Object.InputAuthority;
        var stats = ctx.Stats;
        int n = Physics.OverlapSphereNonAlloc(center, d.Radius, _hits, ctx.HitMask, QueryTriggerInteraction.Ignore);
        _seen.Clear();

        for (int i = 0; i < n; i++)
        {
            var t = _hits[i] != null ? _hits[i].GetComponentInParent<PlayerHealth>() : null;
            if (t == null || t.IsDead || !_seen.Add(t) || !TargetUtil.IsEnemy(ctx, t)) continue;

            if (d.DamageCoef > 0f && stats != null)
            {
                float dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * d.DamageCoef * 0.01f,
                                                    DamageSource.Skill, true, out bool crit);
                var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), me, center, true);
                info.IsCrit = crit;
                t.RPC_TakeDamage(info);
            }

            if (d.Stun > 0f && t.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Stun, 1f, d.Stun, me);

            if ((d.Push > 0f || d.PushUp > 0f) && t.TryGetComponent<PlayerMovement>(out var mv))
            {
                Vector3 away = t.transform.position - center; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : ctx.transform.forward;
                mv.AddKnockback(away * d.Push + Vector3.up * d.PushUp);
            }
        }
    }

    /// <summary>모든 화면 — 소리 · 이펙트 · 내 화면 흔들림 (거리 따라). owner = 일으킨 사람(본인 배율용)</summary>
    public static void Feel(Vector3 center, FeelDef f, Transform owner)
    {
        if (f == null) return;

        if (f.Sound != null) PlaySound(f, center);
        if (f.Fx != null) Object.Destroy(Object.Instantiate(f.Fx, center, Quaternion.identity), Mathf.Max(0.1f, f.FxLife));

        if (f.ShakeAmount <= 0f || f.ShakeRadius <= 0f) return;
        var cam = LocalCamera(out var root);
        if (cam == null || root == null) return;

        float dist = Vector3.Distance(root.position, center);
        if (dist > f.ShakeRadius) return;

        float amt = f.ShakeAmount * Mathf.Lerp(0.3f, 1f, 1f - dist / f.ShakeRadius);
        if (owner != null && owner.root == root) amt *= f.ShakeSelf;
        if (amt > 0.001f) cam.Shake(amt);
    }

    static void PlaySound(FeelDef f, Vector3 pos)
    {
        var go = new GameObject("Shockwave Sound");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = f.Sound;
        src.volume = f.Volume;
        src.pitch = 1f + Random.Range(-f.PitchJitter, f.PitchJitter);
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = Mathf.Max(3f, f.SoundRange);
        src.dopplerLevel = 0f;
        src.Play();
        Object.Destroy(go, f.Sound.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
    }

    /// <summary>내 플레이어의 1인칭 카메라 (흔들기용)</summary>
    static FPSCamera LocalCamera(out Transform root)
    {
        var local = PlayerComms.Local;
        root = local != null ? local.transform.root : null;
        if (root == null) { _cam = null; _camRoot = null; return null; }
        if (_cam == null || _camRoot != root)
        {
            _camRoot = root;
            _cam = root.GetComponentInChildren<FPSCamera>(true);
        }
        return _cam;
    }
}
