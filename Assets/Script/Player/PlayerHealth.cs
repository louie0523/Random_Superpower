using Fusion;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private int maxHP = 100;
    [SerializeField] private float respawnDelay = 3f;

    [Networked, OnChangedRender(nameof(OnHPChanged))]
    public int HP { get; set; }

    [Networked, OnChangedRender(nameof(OnDeadChanged))]
    public bool IsDead { get; set; }

    [Networked] public int Soul { get; set; }
    [Networked] private TickTimer RespawnTimer { get; set; }
    [Networked] private Vector3 LastHitFrom { get; set; }

    private int _prevHP;
    private Renderer _renderer;
    private Color _baseColor;
    private CharacterController _cc;
    private PlayerStats _stats;

    public override void Spawned()
    {
        _renderer = GetComponent<Renderer>();
        _baseColor = _renderer.material.color;
        _cc = GetComponent<CharacterController>();
        _stats = GetComponent<PlayerStats>();

        if (HasStateAuthority)
        {
            HP = maxHP;
            Soul = 0;
            IsDead = false;
        }
        _prevHP = HP;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (IsDead && RespawnTimer.Expired(Runner))
            Respawn();
    }

    // ---------- 데미지 ----------

    /// <summary>공격자가 호출. 실제 실행은 피격자 쪽에서.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(int damage, PlayerRef attacker, Vector3 attackerPos)
    {
        if (IsDead) return;

        float def = _stats.Get(StatType.Defense);
        int final = Mathf.Max(1, Mathf.RoundToInt(damage * (100f / (100f + def))));

        LastHitFrom = attackerPos;
        HP -= final;

        if (HP <= 0)
        {
            HP = 0;
            Die(attacker);
        }
    }

    void Die(PlayerRef killer)
    {
        IsDead = true;
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnDelay);

        // 소울 35% 손실 → 킬러에게
        int lost = Mathf.FloorToInt(Soul * 0.35f);
        Soul -= lost;

        var killerObj = Runner.GetPlayerObject(killer);
        if (killerObj != null &&
            killerObj.TryGetComponent<PlayerHealth>(out var kh))
        {
            kh.RPC_GainSoul(5 + lost);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_GainSoul(int amount)
    {
        Soul += amount;
    }

    void Respawn()
    {
        HP = maxHP;
        IsDead = false;

        Vector3 pos = new Vector3(Random.Range(-8f, 8f), 1f, Random.Range(-8f, 8f));

        // CharacterController는 켜진 상태로 위치를 바꾸면 씹힙니다
        _cc.enabled = false;
        transform.position = pos;
        _cc.enabled = true;
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
    }

    System.Collections.IEnumerator FlashRed()
    {
        _renderer.material.color = Color.red;
        yield return new WaitForSeconds(0.12f);
        _renderer.material.color = _baseColor;
    }
}