using Fusion;
using UnityEngine;

/// <summary>
/// 물고기 — 이 능력을 가지면 물고기로 변신.
///   땅에 닿을 때마다 위로 튀어 오름 → 주변 적에게 피해, 나는 기절 + 최대 체력 % 고정 피해 (사실상 무방비)
///   튀어 오를 때마다 5% 확률로 '용'으로 변신 → 튀기 끝. 용: 깡스탯(AlterData) · 좌클릭 물어뜯기 · Space 비행(5초)
/// 죽으면 풀리고, 랜덤 능력이 바뀌므로 끝.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Fish")]
public class FishAbility : TransformAbility
{
    [Header("형태")]
    public TransformForm Fish = new() { AttackAnim = "", CanFly = false };
    public TransformForm Dragon = new() { CanFly = true };

    [Header("물고기 — 튀어 오르기")]
    [Tooltip("튀어 오르는 세기 (위쪽 속도 m/초)")]
    public float BounceUp = 7f;
    [Tooltip("튈 때 옆으로 랜덤하게 밀리는 세기 (m/초)")]
    public float BounceSide = 2.5f;
    [Tooltip("착지 때 주변 피해 반경 (m)")]
    public float SplashRadius = 2.5f;
    [Tooltip("착지 때 주변 적 피해")]
    public float SplashDamage = 10f;
    [Tooltip("착지 때 내가 받는 최대 체력 % 고정 피해 (반동)")]
    public float SelfDamagePercent = 5f;
    [Tooltip("착지 때 내가 기절하는 시간 (초) — 다음 착지까지 무방비")]
    public float SelfStun = 1.5f;
    [Tooltip("튈 때마다 용이 될 확률 (0.05 = 5%)")]
    [Range(0f, 1f)] public float DragonChance = 0.05f;
    [Tooltip("물고기가 된 뒤 첫 튀기까지 (초)")]
    public float FirstBounceDelay = 0.5f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject SplashFx;
    public GameObject DragonFx;
    const int EvSplash = 0, EvDragon = 1;

    protected override TransformForm[] Forms => new[] { Fish, Dragon };

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[24];
    private float _bounceAt;
    private bool _waitLand;         // 튀어 오른 뒤 착지 기다리는 중
    private bool _leftGround;
    private float _bouncedAt;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;

    public override void OnEquip()
    {
        base.OnEquip();
        _waitLand = false;
        _bounceAt = Now + FirstBounceDelay;
    }

    public override void OnTrigger(TriggerType t, GameObject other)
    {
        if (t == TriggerType.OnRespawn) { _waitLand = false; _bounceAt = Now + FirstBounceDelay; }
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        var h = Ctx.Health;
        if (h == null || h.IsDead || Movement == null) return;

        // 처음 · 부활 뒤 → 물고기로
        if (Form == null) { BeginForm(Fish); _bounceAt = Now + FirstBounceDelay; }
        if (Form != Fish) return;

        bool grounded = Movement.Grounded;

        if (_waitLand)
        {
            if (!grounded) _leftGround = true;
            // 착지 — 한 번은 떠야 인정 (튄 직후 같은 틱에 바로 '착지'로 보지 않게). 천장 등에 막혀 못 떴으면 잠시 뒤 착지로
            if (grounded && (_leftGround || Now - _bouncedAt > 0.6f))
            {
                _waitLand = false;
                Splash();
            }
            return;
        }

        if (grounded && Now >= _bounceAt) Bounce();
    }

    /// <summary>착지 — 주변 피해 · 내 기절 · 반동 피해</summary>
    void Splash()
    {
        Vector3 p = Ctx.transform.position;
        var me = Ctx.Object.InputAuthority;

        int n = Physics.OverlapSphereNonAlloc(p, SplashRadius, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        var seen = new System.Collections.Generic.HashSet<PlayerHealth>();
        for (int i = 0; i < n; i++)
        {
            var t = _hits[i].GetComponentInParent<PlayerHealth>();
            if (t == null || t.IsDead || !seen.Add(t) || !TargetUtil.IsEnemy(Ctx, t)) continue;
            t.RPC_TakeDamage(DamageInfo.FromPlayerHit(Mathf.RoundToInt(SplashDamage), me, p));
        }

        if (Ctx.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Stun, 1f, SelfStun, me);

        var self = DamageInfo.FromEnvironment(0, p);
        self.MaxHpPercent = SelfDamagePercent;
        self.IgnoreDefense = true;
        self.IsDot = true;
        Ctx.Health.RPC_TakeDamage(self);

        Broadcast(EvSplash, p);
        _bounceAt = Now + 0.1f;
    }

    void Bounce()
    {
        _bounceAt = float.MaxValue;     // 다음 착지 때 다시

        if (Random.value < DragonChance)
        {
            BecomeDragon();
            return;
        }

        Vector2 side = Random.insideUnitCircle * BounceSide;
        Movement.AddKnockback(new Vector3(side.x, BounceUp, side.y));
        _waitLand = true;
        _leftGround = false;
        _bouncedAt = Now;
    }

    void BecomeDragon()
    {
        if (Ctx.TryGetComponent<PlayerStatus>(out var st)) st.Clear(StatusType.Stun);
        BeginForm(Dragon);
        Broadcast(EvDragon, Ctx.transform.position + Vector3.up);

        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.fish.dragon", "물고기 {0}이(가) 용이 되었다!", TargetUtil.NameOf(Ctx)));
    }

    protected override void OnFormEvent(int key, Vector3 pos)
    {
        if (key == EvSplash) Ctx.PlayEffectLocal(SplashFx, pos);
        else if (key == EvDragon) Ctx.PlayEffectLocal(DragonFx, pos);
    }

    public override string DescToken(string key, int rank) =>
        key == "form" ? (Form == Dragon ? "용" : "물고기") : null;
}
