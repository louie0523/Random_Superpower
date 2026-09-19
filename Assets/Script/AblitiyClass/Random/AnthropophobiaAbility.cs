using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Anthropophobia")]
public class AnthropophobiaAbility : AbilityBehaviour
{
    [Header("조건")]
    [Tooltip("이 거리 안에 적이 있으면 카운트 시작")]
    public float DetectRange = 5f;
    [Tooltip("이 시간이 지나면 폭발")]
    public float TriggerTime = 3f;

    [Header("폭발")]
    public float ExplodeRadius = 8f;
    public float Damage = 200f;
    [Tooltip("폭발 후 자신도 사망")]
    public bool KillSelf = true;

    [Header("연출")]
    public GameObject WarningEffect;
    public GameObject ExplodeEffect;

    private float _timer;
    private bool _warned;

    /// <summary>HUD 표시용 (0~1)</summary>
    public float DangerRatio => Mathf.Clamp01(_timer / Mathf.Max(0.01f, TriggerTime));
    public bool IsCounting => _timer > 0f;

    public override void OnEquip()
    {
        _timer = 0f;
        _warned = false;
    }

    public override void Tick(float dt)
    {
        if (Ctx.Health == null || Ctx.Health.IsDead)
        {
            _timer = 0f;
            _warned = false;
            return;
        }

        if (!HasNearbyPlayer())
        {
            _timer = 0f;
            _warned = false;
            return;
        }

        _timer += dt;

        if (!_warned && _timer >= TriggerTime * 0.4f)
        {
            _warned = true;
            Ctx.PlayEffectLocal(WarningEffect, Ctx.transform.position);
        }

        if (_timer >= TriggerTime) Explode();
    }

    bool HasNearbyPlayer()
    {
        var cols = Physics.OverlapSphere(Ctx.transform.position, DetectRange, Ctx.HitMask);

        foreach (var c in cols)
        {
            if (c.gameObject == Ctx.gameObject) continue;
            if (!c.TryGetComponent<PlayerHealth>(out var h)) continue;
            if (h.IsDead) continue;
            return true;
        }
        return false;
    }

    void Explode()
    {
        _timer = 0f;
        _warned = false;

        Vector3 center = Ctx.transform.position;
        var cols = Physics.OverlapSphere(center, ExplodeRadius, Ctx.HitMask);

        foreach (var c in cols)
        {
            if (c.gameObject == Ctx.gameObject) continue;
            if (!c.TryGetComponent<PlayerHealth>(out var h)) continue;
            if (h.IsDead) continue;

            h.RPC_TakeDamage(DamageInfo.FromPlayerHit(
                Mathf.RoundToInt(Damage), Ctx.Object.InputAuthority, center));
        }

        Ctx.PlayEffectLocal(ExplodeEffect, center);

        if (KillSelf && Ctx.Health != null)
            Ctx.Health.RPC_TakeDamage(DamageInfo.FromEnvironment(99999, center));
    }
}