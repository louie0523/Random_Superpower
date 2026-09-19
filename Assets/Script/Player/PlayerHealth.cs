using System.Collections.Generic;
using Fusion;
using UnityEngine;
using static UnityEngine.InputSystem.Controls.AxisControl;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private float respawnDelay = 3f;

    [Header("Kill Credit")]
    [Tooltip("어시스트로 인정되는 시간 (초)")]
    [SerializeField] private float assistWindow = 8f;
    [Tooltip("넉백 후 이 시간 안에 낙사하면 막타로 인정")]
    [SerializeField] private float fallCreditWindow = 5f;
    [Tooltip("어시스트 소울 비율")]
    [SerializeField] private float assistSoulRatio = 0.15f;

    [Header("Death Penalty")]
    [Tooltip("플레이어에게 죽었을 때 소울 손실 비율")]
    [SerializeField] private float soulLossByPlayer = 0.35f;
    [Tooltip("자해/환경사 소울 손실 비율")]
    [SerializeField] private float soulLossBySelf = 0.10f;

    [Header("전투 상태")]
    [Tooltip("피해를 주고받은 뒤 전투 중으로 유지되는 시간")]
    [SerializeField] private float combatDuration = 8f;

    [Networked] private TickTimer CombatTimer { get; set; }
    public bool InCombat => CombatTimer.IsRunning && !CombatTimer.Expired(Runner);

    [Networked, OnChangedRender(nameof(OnHPChanged))] public int HP { get; set; }
    [Networked, OnChangedRender(nameof(OnDeadChanged))] public NetworkBool IsDead { get; set; }
    [Networked] public int Soul { get; set; }

    [Networked] public int Kills { get; set; }
    [Networked] public int Assists { get; set; }
    [Networked] public int Deaths { get; set; }

    [Networked] private TickTimer RespawnTimer { get; set; }
    [Networked] private Vector3 LastHitFrom { get; set; }

    /// <summary>최근 나를 때린 사람들 (어시스트 판정용)</summary>
    struct DamageRecord
    {
        public PlayerRef Player;
        public int Amount;
        public float Time;
        public bool CanCauseFall;
    }

    private readonly List<DamageRecord> _recentDamage = new();

    private int _prevHP;
    private PlayerStats _stats;
    private CharacterController _cc;
    private AbilityRuntime _ability;

    [Header("Hit Flash")]
    [Tooltip("셰이더의 색 프로퍼티 이름. 비우면 자동 탐색")]
    [SerializeField] private string colorProperty = "";
    private Renderer _renderer;
    private int _colorId = -1;
    private Color _baseColor = Color.white;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _cc = GetComponent<CharacterController>();
        _ability = GetComponent<AbilityRuntime>();
        _renderer = GetComponentInChildren<Renderer>();
        SetupColorProperty();

        if (HasStateAuthority)
        {
            HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
            Soul = 0;
            IsDead = false;
            _stats.OnStatsChanged += ClampHP;
        }
        _prevHP = HP;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_stats != null) _stats.OnStatsChanged -= ClampHP;
    }

    void ClampHP()
    {
        if (!HasStateAuthority) return;
        if (IsDead) return;

        int max = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        if (HP > max) HP = max;
    }

    void SetupColorProperty()
    {
        if (_renderer == null) return;

        var mat = _renderer.sharedMaterial;
        if (mat == null) return;

        // 후보 이름들
        string[] candidates = string.IsNullOrEmpty(colorProperty)
            ? new[] { "_BaseColor", "_Color", "_MainColor", "_Tint", "_ColorTint" }
            : new[] { colorProperty };

        foreach (var name in candidates)
        {
            if (!mat.HasProperty(name)) continue;

            _colorId = Shader.PropertyToID(name);
            _baseColor = mat.GetColor(_colorId);
            return;
        }

        Debug.LogWarning(
            $"[PlayerHealth] {mat.shader.name}에서 색 프로퍼티를 못 찾았습니다. " +
            "피격 플래시가 비활성화됩니다.", this);
    }

    /// <summary>새 라운드 시작 시 초기화</summary>
    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        Soul = 0;
        Kills = 0;
        Assists = 0;
        Deaths = 0;

        _recentDamage.Clear();

        if (IsDead)
        {
            IsDead = false;
            Respawn();
        }
        else
        {
            HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;


        if (IsDead && RespawnTimer.Expired(Runner)) Respawn();

        // 오래된 피해 기록 정리
        float now = Runner.SimulationTime;
        for (int i = _recentDamage.Count - 1; i >= 0; i--)
            if (now - _recentDamage[i].Time > assistWindow)
                _recentDamage.RemoveAt(i);

        // 낙사 체크
        if (!IsDead && transform.position.y < -20f)
            Die(DeathCause.Environment, PlayerRef.None);
    }

    // ══════════════════ 피해 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_MarkCombat() => MarkCombat();

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(DamageInfo info)
    {
        if (IsDead) return;

        int final = DamageCalc.CalcIncoming(_stats, info.Amount);

        LastHitFrom = info.SourcePos;
        HP -= final;

        // 피해 기록
        if (info.FromPlayer && info.Attacker != PlayerRef.None)
        {
            RecordDamage(info.Attacker, final, info.CanCauseFall);
            MarkCombat();

            // 공격자도 전투 중
            var atk = Runner.GetPlayerObject(info.Attacker);
            if (atk != null && atk.TryGetComponent<PlayerHealth>(out var ah))
                ah.RPC_MarkCombat();
        }

        _ability?.FireTrigger(TriggerType.OnHitTaken, null);

        if (HP <= 0)
        {
            HP = 0;
            Die(info.FromPlayer ? DeathCause.Player : DeathCause.Environment,
                info.Attacker);
        }
    }

    void RecordDamage(PlayerRef attacker, int amount, bool canCauseFall)
    {
        float now = Runner.SimulationTime;

        for (int i = 0; i < _recentDamage.Count; i++)
        {
            if (_recentDamage[i].Player != attacker) continue;

            var r = _recentDamage[i];
            r.Amount += amount;
            r.Time = now;
            r.CanCauseFall = canCauseFall || r.CanCauseFall;
            _recentDamage[i] = r;
            return;
        }

        _recentDamage.Add(new DamageRecord
        {
            Player = attacker,
            Amount = amount,
            Time = now,
            CanCauseFall = canCauseFall,
        });
    }

    // ══════════════════ 사망 ══════════════════

    void Die(DeathCause cause, PlayerRef killer)
    {
        if (IsDead) return;

        IsDead = true;
        Deaths++;
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnDelay);

        // 환경사인데 최근에 넉백당했으면 그 사람에게 킬 인정
        if (cause == DeathCause.Environment)
        {
            var faller = FindFallCredit();
            if (faller != PlayerRef.None)
            {
                cause = DeathCause.Player;
                killer = faller;
            }
        }

        bool byPlayer = cause == DeathCause.Player
                        && killer != PlayerRef.None
                        && killer != Object.InputAuthority;

        if (byPlayer)
        {
            int lost = Mathf.FloorToInt(Soul * soulLossByPlayer);
            Soul -= lost;

            GrantKill(killer, 5 + lost);
            GrantAssists(killer, lost);
        }
        else
        {
            // 자해 / 환경사 — 소울만 소폭 감소, 킬 인정 없음
            int lost = Mathf.FloorToInt(Soul * soulLossBySelf);
            Soul -= lost;
            float delay = byPlayer ? respawnDelay : respawnDelay * 2f;
            RespawnTimer = TickTimer.CreateFromSeconds(Runner, delay);
        }

        CombatTimer = default;

        // 사망 원인과 무관하게 랜덤 능력 재추첨
        _ability?.RerollRandomAbility();
        _ability?.FireTrigger(TriggerType.OnDeath, null);
        _recentDamage.Clear();

    }

    PlayerRef FindFallCredit()
    {
        float now = Runner.SimulationTime;
        PlayerRef best = PlayerRef.None;
        float bestTime = -1f;

        foreach (var r in _recentDamage)
        {
            if (!r.CanCauseFall) continue;
            if (now - r.Time > fallCreditWindow) continue;
            if (r.Time > bestTime) { bestTime = r.Time; best = r.Player; }
        }

        return best;
    }

    void GrantKill(PlayerRef killer, int soul)
    {
        var obj = Runner.GetPlayerObject(killer);
        if (obj == null) return;
        if (!obj.TryGetComponent<PlayerHealth>(out var kh)) return;

        kh.RPC_AddKill(soul);
    }

    void GrantAssists(PlayerRef killer, int lostSoul)
    {
        int assistSoul = Mathf.Max(1, Mathf.RoundToInt(lostSoul * assistSoulRatio));
        float now = Runner.SimulationTime;

        foreach (var r in _recentDamage)
        {
            if (r.Player == killer) continue;
            if (r.Player == Object.InputAuthority) continue;
            if (now - r.Time > assistWindow) continue;

            var obj = Runner.GetPlayerObject(r.Player);
            if (obj == null) continue;
            if (!obj.TryGetComponent<PlayerHealth>(out var ah)) continue;

            ah.RPC_AddAssist(assistSoul);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddKill(int soul)
    {
        Kills++;
        Soul += soul;
        GetComponent<AbilityRuntime>()?.FireTrigger(TriggerType.OnKill, null);
        GetComponent<PlayerEnchant>()?.RPC_AddStones(1);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddAssist(int soul)
    {
        Assists++;
        Soul += soul;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Heal(int amount)
    {
        if (IsDead) return;
        HP = Mathf.Min(HP + amount, Mathf.RoundToInt(_stats.Get(StatType.MaxHP)));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_GainSoul(int amount) => Soul += amount;

    public void SpendSoul(int amount)
    {
        if (!HasStateAuthority) return;
        Soul = Mathf.Max(0, Soul - amount);
    }

    /// <summary>전투 중 상태 갱신</summary>
    public void MarkCombat()
    {
        if (!HasStateAuthority) return;
        CombatTimer = TickTimer.CreateFromSeconds(Runner, combatDuration);
    }

    void Respawn()
    {
        HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        IsDead = false;



        Vector3 pos = new Vector3(Random.Range(-8f, 8f), 1f, Random.Range(-8f, 8f));
        _cc.enabled = false;
        transform.position = pos;
        _cc.enabled = true;

        _ability?.FireTrigger(TriggerType.OnRespawn, null);
    }

    // ---------- 시각 효과 (모든 클라이언트에서 실행) ----------

    void OnHPChanged()
    {
        int delta = _prevHP - HP;
        _prevHP = HP;

        if (delta <= 0) return;

        StopAllCoroutines();
        StartCoroutine(FlashRed());

        if (!HasStateAuthority) return;
        if (!TryGetComponent<FPSCamera>(out var cam)) return;

        // 데미지 비례 강도 (25딜이면 0.5 정도)
        float intensity = Mathf.Clamp01(delta / 50f);

        // 맞은 방향을 내 로컬 기준으로 변환
        Vector3 toAttacker = (LastHitFrom - transform.position).normalized;
        Vector3 local = transform.InverseTransformDirection(toAttacker);

        // 뒤에서 맞으면 화면이 위로, 앞에서 맞으면 아래로
        Vector2 dir = new Vector2(-local.x, local.z);

        cam.HitReact(intensity, dir);
    }

    void OnDeadChanged()
    {
        _renderer.enabled = !IsDead;

        if (_cc != null) _cc.enabled = !IsDead;
    }

    System.Collections.IEnumerator FlashRed()
    {
        if (_renderer == null || _colorId < 0) yield break;

        _renderer.material.SetColor(_colorId, Color.red);
        yield return new WaitForSeconds(0.12f);
        if (_renderer != null) _renderer.material.SetColor(_colorId, _baseColor);
    }
}