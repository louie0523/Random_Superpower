using Fusion;
using UnityEngine;

/// <summary>
/// 복수 표식 (권한자 전용) — 능력이 바뀌어도 남음.
///   복수 시간 안에 대상을 처치 → 버프 (다음에 죽을 때까지)
///   복수 시간이 끝나거나 · 버프 중 죽으면 사라짐
/// </summary>
public class RevengeMark : MonoBehaviour
{
    const int Src = SourceId.MarkBase + 1;

    private AbilityRuntime _ctx;
    private PlayerHealth _health;
    private PlayerStats _stats;
    private PlayerRef _target;
    private float _until;
    private float _bonus;
    private GameObject _fx;
    private bool _buffed;
    private AbilityData _source;
    private string _targetName;
    private HudExtras.Provider _hud;

    public static void Begin(AbilityRuntime ctx, PlayerRef target, float window, float bonusPercent, GameObject fx, AbilityData source = null)
    {
        if (ctx == null || !ctx.HasStateAuthority) return;

        var m = ctx.GetComponent<RevengeMark>();
        if (m == null) m = ctx.gameObject.AddComponent<RevengeMark>();
        else m.Clear();

        m._ctx = ctx;
        m._health = ctx.Health;
        m._stats = ctx.Stats;
        m._target = target;
        m._until = Time.time + window;
        m._bonus = bonusPercent;
        m._fx = fx;
        m._buffed = false;
        m._source = source;
        m._targetName = TargetUtil.NameOf(ctx.Runner, target);

        // 내 화면 HUD — 랜덤 능력 줄 아래 '복수 시간' (능력이 바뀌어도 남음)
        if (ctx.HasInputAuthority)
        {
            m._hud = m.HudRow;
            HudExtras.Add(m._hud);
        }

        m._health.KilledPlayer += m.OnKilled;
        m._health.Died += m.OnDied;

        var comms = PlayerComms.Local;
        if (comms != null) comms.Notice(NoticeKind.Small,
            TargetUtil.Text("notice.revenge.start", "복수 시간! {0}초 안에 {1}을(를) 처치하라", Mathf.RoundToInt(window),
                TargetUtil.NameOf(ctx.Runner, target)),
            Vector3.zero, 0f, ctx.Object.InputAuthority, false);
    }

    void Update()
    {
        if (_ctx == null || _health == null) { Destroy(this); return; }
        if (!_buffed && Time.time > _until) Destroy(this);
    }

    void OnKilled(PlayerRef victim)
    {
        if (_buffed || victim != _target || Time.time > _until) return;

        _buffed = true;
        float before = _stats.Get(StatType.MaxHP);
        _stats.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Percent, _bonus, Src));
        _stats.AddModifier(new StatModifier(StatType.MaxHP, ModType.Percent, _bonus, Src));
        _stats.AddModifier(new StatModifier(StatType.AttackPower, ModType.Percent, _bonus, Src));
        _health.ApplyMaxHpGain(before);

        var comms = PlayerComms.Local;
        if (comms != null)
        {
            comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.revenge.done", "{0}이(가) 복수에 성공했다!", TargetUtil.NameOf(_ctx)));
            if (_fx != null) comms.PlayFx(_fx, _ctx.transform.position + Vector3.up);
        }
    }

    void OnDied(PlayerRef killer, DeathCause cause)
    {
        // 버프 중에 죽음 → 끝. 복수 시간 중에 또 죽음(대상 처치 전) → 복수 시간은 그대로 계속
        if (_buffed) Destroy(this);
    }

    bool HudRow(out HudExtraRow row)
    {
        row = new HudExtraRow
        {
            Key = -9001,
            Style = _source,
            Name = TargetUtil.Text("hud.revenge.name", "복수 시간"),
            Number = HudExtras.Number(_source, 1),
        };
        if (_buffed)
            row.Summary = TargetUtil.Text("hud.revenge.done",
                "복수 성공! 다음에 죽을 때까지 이동 속도 · 최대 체력 · 공격력 +{0}%", Mathf.RoundToInt(_bonus));
        else
        {
            int left = Mathf.CeilToInt(_until - Time.time);
            if (left <= 0) return false;
            row.Summary = TargetUtil.Text("hud.revenge.left",
                "당신을 처치한 {0}을(를) {1}초 이내에 처치하세요", _targetName, left);
        }
        return true;
    }

    void Clear()
    {
        if (_hud != null) { HudExtras.Remove(_hud); _hud = null; }
        if (_stats != null) _stats.RemoveBySource(Src);
        if (_health != null)
        {
            _health.KilledPlayer -= OnKilled;
            _health.Died -= OnDied;
        }
        _buffed = false;
    }

    void OnDestroy() => Clear();
}
