using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// (플레이어 프리팹) 내가 연 제단 · 제단 채널.
/// - 서브 제단: 상호작용 키 → 채널 → 끝나면 소울 내고 열림 (개인). 죽어도 유지, 라운드가 바뀌면 초기화
/// - 중앙 제단: 서브 제단 N개를 연 사람만 → 채널(기본 8초) → 끝나면 승리
/// - 채널 중 맞거나 · 죽거나 · 제단에서 멀어지면 끊김. 소울은 끝날 때 냄 (끊기면 안 냄)
/// 권한자 = 이 플레이어 본인.
/// </summary>
public class PlayerAltars : NetworkBehaviour
{
    [Tooltip("상호작용 키 — 상자와 같은 키 권장")]
    [SerializeField] private KeyCode interactKey = KeyCode.R;

    [Tooltip("서브 제단 채널 시간 (초)")]
    [SerializeField] private float subChannel = 2.5f;

    /// <summary>연 서브 제단 (비트 = 서브 제단 번호)</summary>
    [Networked] public int Mask { get; set; }
    [Networked] int RoundSeen { get; set; }
    [Networked] int ChannelRaw { get; set; }            // 0 = 안 함, 아니면 제단 번호 + 1
    [Networked] public TickTimer ChannelTimer { get; set; }
    [Networked] public float ChannelLength { get; set; }
    [Networked] NetworkBool Announced { get; set; }

    static readonly List<PlayerAltars> _all = new();
    public static IReadOnlyList<PlayerAltars> All => _all;

    /// <summary>내 캐릭터의 PlayerAltars (없으면 null — 옵저버 등)</summary>
    public static PlayerAltars Local { get; private set; }

    PlayerHealth _health;
    float _lastHp;
    int _wantStart = -1;      // Update에서 키 → FixedUpdateNetwork에서 처리
    bool _wantCancel;

    // ══════════════════ 조회 ══════════════════

    /// <summary>이 캐릭터의 주인 (더미는 None)</summary>
    public PlayerRef Owner => Object.InputAuthority;

    bool IsMine => HasStateAuthority && Object.InputAuthority != PlayerRef.None
                   && Object.InputAuthority == Runner.LocalPlayer;

    /// <summary>채널 중인 제단 번호 (-1 = 안 함, 99 = 중앙)</summary>
    public int Channel => ChannelRaw - 1;
    public bool IsChannelingCentral => Channel == Altar.CentralIndex;

    public float Progress
    {
        get
        {
            if (Channel < 0 || ChannelLength <= 0f) return 0f;
            float left = ChannelTimer.RemainingTime(Runner) ?? 0f;
            return Mathf.Clamp01(1f - left / ChannelLength);
        }
    }

    public int Count => PopCount(Mask);

    public bool Has(int i) => i >= 0 && i < Altar.MaxSub && (Mask & (1 << i)) != 0;

    public bool CanOpenCentral => Count >= AltarRules.Needed;

    public static PlayerAltars Of(PlayerRef p)
    {
        if (p == PlayerRef.None) return null;
        foreach (var a in _all)
            if (a != null && a.Object != null && a.Object.IsValid && a.Owner == p) return a;
        return null;
    }

    /// <summary>이 사람이 연 서브 제단 수 (순위표 · 시간 끝 판정용)</summary>
    public static int CountOf(PlayerRef p)
    {
        var a = Of(p);
        return a != null ? a.Count : 0;
    }

    static int PopCount(int v)
    {
        uint x = (uint)v;
        int n = 0;
        while (x != 0) { x &= x - 1; n++; }
        return n;
    }

    // ══════════════════ 생성 ══════════════════

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
        _all.Add(this);
        if (IsMine) Local = this;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _all.Remove(this);
        if (Local == this) Local = null;
    }

    // ══════════════════ 입력 · 안내 (내 캐릭터) ══════════════════

    void Update()
    {
        if (Object == null || !Object.IsValid || !IsMine || _health == null) return;

        bool blocked = UIManager.InputBlocked;

        if (Channel >= 0)
        {
            if (!blocked && Input.GetKeyDown(interactKey)) _wantCancel = true;   // 다시 누르면 취소
            return;
        }

        if (_health.IsDead || IsOver()) return;

        var a = FindNear();
        if (a == null) return;

        string key = interactKey.ToString();
        int need = AltarRules.Needed;

        if (a.IsCentral)
        {
            if (!CanOpenCentral)
            {
                AltarHud.Prompt($"중앙 제단 — 제단 {Count}/{need} (더 열어야 함)");
                return;
            }
            int cost = AltarRules.CentralCost;
            AltarHud.Prompt($"[{key}] 중앙 제단 활성화 — {cost} 소울 · {AltarRules.CentralChannel:0}초"
                            + (_health.Soul < cost ? "  (소울 부족)" : ""));
        }
        else
        {
            if (Has(Altar.IndexOf(a)))
            {
                AltarHud.Prompt($"이미 연 제단 ({Count}/{need})");
                return;
            }
            int cost = AltarRules.SubCost;
            AltarHud.Prompt($"[{key}] 제단 활성화 — {cost} 소울"
                            + (_health.Soul < cost ? "  (소울 부족)" : ""));
        }

        if (!blocked && Input.GetKeyDown(interactKey)) _wantStart = Altar.IndexOf(a);
    }

    Altar FindNear()
    {
        Altar best = null;
        float bestD = float.MaxValue;
        Vector3 p = transform.position;
        foreach (var a in Altar.All)
        {
            if (a == null || Altar.IndexOf(a) < 0) continue;
            float d = (a.transform.position - p).sqrMagnitude;
            if (d <= a.Radius * a.Radius && d < bestD) { bestD = d; best = a; }
        }
        return best;
    }

    static bool IsOver()
    {
        var rm = RoundManager.Instance;
        return rm != null && rm.Object != null && rm.Object.IsValid && rm.GameOver;
    }

    // ══════════════════ 처리 (권한자) ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // 새 라운드 → 초기화 (죽는 건 유지)
        var rm = RoundManager.Instance;
        if (rm != null && rm.Object != null && rm.Object.IsValid && rm.RoundIndex != RoundSeen)
        {
            RoundSeen = rm.RoundIndex;
            Mask = 0;
            ChannelRaw = 0;
            Announced = false;
            _wantStart = -1;
            _wantCancel = false;
        }

        if (_wantCancel)
        {
            _wantCancel = false;
            if (Channel >= 0) Cancel("취소함");
        }

        if (_wantStart >= 0)
        {
            int i = _wantStart;
            _wantStart = -1;
            if (Channel < 0 && !IsOver()) TryStart(i);
        }

        if (Channel < 0) return;

        var altar = Altar.ByIndex(Channel);
        if (altar == null || IsOver()) { ChannelRaw = 0; return; }

        if (_health == null || _health.IsDead) { Cancel("죽어서 끊김"); return; }

        float r = altar.Radius * 1.15f;
        if ((transform.position - altar.transform.position).sqrMagnitude > r * r) { Cancel("제단에서 멀어져서 끊김"); return; }

        float hp = _health.HP;
        if (hp < _lastHp - 0.001f) { Cancel("공격받아서 끊김"); return; }
        _lastHp = hp;

        if (ChannelTimer.Expired(Runner)) Complete();
    }

    void TryStart(int i)
    {
        var a = Altar.ByIndex(i);
        if (a == null || _health == null || _health.IsDead) return;

        bool central = a.IsCentral;
        if (!central && Has(i)) return;

        if (central && !CanOpenCentral)
        {
            Toast($"제단을 {AltarRules.Needed - Count}개 더 열어야 해");
            return;
        }

        int cost = central ? AltarRules.CentralCost : AltarRules.SubCost;
        if (_health.Soul < cost)
        {
            Toast($"소울이 부족해 ({_health.Soul}/{cost})");
            return;
        }

        float len = Mathf.Max(0.1f, central ? AltarRules.CentralChannel : subChannel);
        ChannelRaw = i + 1;
        ChannelLength = len;
        ChannelTimer = TickTimer.CreateFromSeconds(Runner, len);
        _lastHp = _health.HP;

        if (central) RPC_Announce(2);
    }

    void Cancel(string why)
    {
        bool central = IsChannelingCentral;
        ChannelRaw = 0;
        Toast(why);
        if (central) RPC_Announce(3);
    }

    void Complete()
    {
        int i = Channel;
        var a = Altar.ByIndex(i);
        ChannelRaw = 0;
        if (a == null) return;

        bool central = a.IsCentral;
        int cost = central ? AltarRules.CentralCost : AltarRules.SubCost;
        if (_health.Soul < cost)
        {
            Toast($"소울이 부족해 ({_health.Soul}/{cost})");
            if (central) RPC_Announce(3);
            return;
        }

        if (cost > 0) _health.RPC_GainSoul(-cost);

        if (central)
        {
            var rm = RoundManager.Instance;
            if (rm != null && rm.Object != null && rm.Object.IsValid) rm.RPC_DeclareWinner(Owner);
            return;
        }

        Mask |= 1 << i;
        int need = AltarRules.Needed;
        Toast($"제단 활성화! ({Count}/{need})");

        if (!Announced && Count >= need)
        {
            Announced = true;
            RPC_Announce(1);
        }
    }

    void Toast(string msg)
    {
        if (IsMine) AltarHud.Toast(msg);
    }

    /// <summary>1 = 권한 획득, 2 = 중앙 채널 시작, 3 = 중앙 채널 끊김</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Announce(byte kind)
    {
        string n = AltarHud.NameOf(Owner);
        switch (kind)
        {
            case 1: AltarHud.Announce($"{n}{AltarHud.IGa(n)} 중앙 제단을 활성화시킬 수 있는 권한을 획득했습니다!"); break;
            case 2: AltarHud.Announce($"{n}{AltarHud.IGa(n)} 중앙 제단을 활성화하고 있습니다!"); break;
            case 3: AltarHud.Announce($"{n}의 중앙 제단 활성화가 끊겼습니다"); break;
        }
    }
}
