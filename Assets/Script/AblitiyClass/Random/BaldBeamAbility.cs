using Fusion;
using UnityEngine;

/// <summary>
/// 탈모빔 — 스킬(F) '탈모빔' + 살아 있는 동안 '동지' (변화 ComradeAlter — 머리 치장을 없애는 파츠 등, 비워도 됨).
///   동지끼리는 적이어도 서로 주는 피해 0% (스킬 대상 · 밀치기 · 상태이상은 그대로).
///   탈모빔: 손에 힘을 모아 앞으로 광선 → 광선에 닿은 적 한 명이 즉시 '탈모빔' 능력을 얻고 동지가 됨.
///   모든 플레이어가 동지가 되면 글로벌 메시지 → 각자 자기가 동지로 만든 수 × SoulPerConvert 소울 → 전원 사망 (능력이 바뀜).
/// 데이터: 스킬 1개 F · 효과 타이밍 = 판정 프레임(광선이 나가는 순간) · 대상 필요 끄기
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Bald Beam")]
public class BaldBeamAbility : AbilityBehaviour
{
    public int BeamSkill = 0;

    [Header("동지")]
    [Tooltip("동지 겉모습 (머리 치장 없애기 등 — 변화 파츠). 비워도 됨")]
    public AlterData ComradeAlter;

    [Header("탈모빔")]
    [Tooltip("광선 길이 (m)")]
    public float BeamRange = 30f;
    [Tooltip("광선 굵기 — 반지름 (m)")]
    public float BeamRadius = 0.8f;
    [Tooltip("광선을 막는 것 (벽 · 바닥)")]
    public LayerMask WallMask = 1;

    [Header("모두 동지가 되면")]
    [Tooltip("내가 동지로 만든 한 명당 소울")]
    public int SoulPerConvert = 20;
    [Tooltip("메시지가 뜨고 전원 사망까지 (초)")]
    public float EndDelay = 2.5f;
    [Tooltip("최소 인원 — 이 수 이상일 때만 '모두 동지' 판정 (더미 포함)")]
    public int MinPlayers = 2;

    [Header("연출 — 모든 클라이언트")]
    [Tooltip("광선 — LineRenderer가 있으면 시작 · 끝점을 맞춤, 없으면 앞(+Z)으로 길이만큼 늘림")]
    public GameObject BeamFx;
    public float BeamLife = 0.35f;
    public Color BeamColor = new(1f, 0.95f, 0.6f, 1f);
    public GameObject ConvertFx;

    const int EvFrom = 0, EvTo = 1, EvConvert = 2;

    private int _converted;
    private float _checkAt;
    private bool _ending;
    private float _endAt;
    private Vector3 _beamFrom;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == BeamSkill;

    // ══════════════════ 동지 — 맞는 쪽에서 계산 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Register() => DamageRules.Register(Comrades);

    static float Comrades(PlayerHealth target, NetworkObject attacker, DamageInfo info)
    {
        if (attacker == null || target == null) return 1f;
        if (!attacker.TryGetComponent<AbilityRuntime>(out var a) || a.FindAbility<BaldBeamAbility>() == null) return 1f;
        if (!target.TryGetComponent<AbilityRuntime>(out var t) || t.FindAbility<BaldBeamAbility>() == null) return 1f;
        return 0f;          // 동지끼리 — 피해 없음
    }

    public bool IsComrade(PlayerHealth h) => AbilityAim.HasRandom(h, Data != null ? Data.Id : 0);

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _converted = 0;
        _ending = false;
        if (Ctx.HasStateAuthority && ComradeAlter != null)
            Ctx.GetComponent<PlayerAlter>()?.Add(ComradeAlter);
    }

    public override void OnUnequip()
    {
        if (Ctx.HasStateAuthority && ComradeAlter != null)
            Ctx.GetComponent<PlayerAlter>()?.Remove(ComradeAlter);
    }

    // ══════════════════ 탈모빔 ══════════════════

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;

        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 eye = cam.position;
        Vector3 dir = cam.forward;

        float len = BeamRange;
        if (CastUtil.Raycast(eye, dir, BeamRange, WallMask, Ctx.transform.root, out var wall)) len = wall.distance;
        Vector3 end = eye + dir * len;

        // 광선 안의 적 중 가장 가까운 한 명 (이미 동지면 건너뜀)
        PlayerHealth best = null;
        float bestT = float.MaxValue;
        foreach (var h in TargetUtil.AliveEnemies(Ctx))
        {
            if (IsComrade(h)) continue;
            Vector3 c = TargetUtil.Chest(h);
            float t = Vector3.Dot(c - eye, dir);
            if (t < 0f || t > len) continue;
            float off = Vector3.Distance(c, eye + dir * t);
            if (off > BeamRadius + 0.5f) continue;              // 0.5 = 몸 두께
            if (t < bestT) { bestT = t; best = h; }
        }

        // 연출 — 손 쪽에서 시작
        Vector3 from = eye + cam.right * 0.25f - cam.up * 0.2f + dir * 0.4f;
        Broadcast(EvFrom, from);
        Broadcast(EvTo, best != null ? TargetUtil.Chest(best) : end);

        if (best != null) Convert(best);
        return true;
    }

    void Convert(PlayerHealth h)
    {
        if (!h.TryGetComponent<AbilityRuntime>(out var ar)) return;
        ar.RPC_SetRandomAbility(Data.Id);           // 그 사람 쪽에서 랜덤 능력을 '탈모빔'으로 → 바로 동지
        _converted++;
        Broadcast(EvConvert, TargetUtil.Chest(h));

        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.bald.convert", "{0}이(가) {1}을(를) 동지로 만들었다",
                TargetUtil.NameOf(Ctx), TargetUtil.NameOf(h)), true);
    }

    // ══════════════════ 모두 동지 (권한자) ══════════════════

    public override void Tick(float dt)
    {
        if (_ending)
        {
            if (Now < _endAt) return;
            _ending = false;
            int soul = _converted * SoulPerConvert;
            var hp = Ctx.Health;
            if (hp == null || hp.IsDead) return;
            if (soul > 0)
            {
                hp.RPC_GainSoul(soul);
                var comms = PlayerComms.Local;
                if (comms != null)
                    comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.bald.reward", "동지 {0}명 — 소울 +{1}", _converted, soul),
                        Vector3.zero, 0f, Ctx.Object.InputAuthority, false);
            }
            hp.RPC_Sacrifice();                     // 전원 사망 → 능력이 바뀜
            return;
        }

        if (Now < _checkAt) return;
        _checkAt = Now + 0.25f;
        if (!AllComrades(out bool iAmFirst)) return;

        // 모두 동지 — 각자 자기 몫 처리 (조금 기다렸다가 — 다른 동지들도 같은 걸 보게)
        _ending = true;
        _endAt = Now + EndDelay;
        if (iAmFirst)
        {
            var comms = PlayerComms.Local;
            if (comms != null)
                comms.Notice(NoticeKind.Big, TargetUtil.Text("notice.bald.all", "모든 플레이어가 동지가 되었다!"), true);
        }
    }

    /// <summary>살아 있는 모든 플레이어(더미 포함)가 동지인가. 죽은 사람이 있으면 아님. iAmFirst = 메시지를 낼 한 명</summary>
    bool AllComrades(out bool iAmFirst)
    {
        iAmFirst = false;
        var all = TargetUtil.All(true);
        if (all.Count < Mathf.Max(2, MinPlayers)) return false;

        uint min = uint.MaxValue;
        foreach (var h in all)
        {
            if (h == null || h.IsDead || !IsComrade(h)) return false;
            if (h.Object != null && h.Object.IsValid) min = System.Math.Min(min, h.Object.Id.Raw);
        }
        iAmFirst = Ctx.Object != null && Ctx.Object.Id.Raw == min;
        return true;
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        switch (key)
        {
            case EvFrom: _beamFrom = pos; break;
            case EvTo: ShowBeam(_beamFrom, pos); break;
            case EvConvert: Ctx.PlayEffectLocal(ConvertFx, pos); break;
        }
    }

    void ShowBeam(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.05f) return;

        GameObject go;
        LineRenderer lr;
        if (BeamFx != null)
        {
            go = Instantiate(BeamFx, from, Quaternion.LookRotation(d / len));
            lr = go.GetComponentInChildren<LineRenderer>();
            if (lr == null)
            {
                var s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x, s.y, s.z * len);
            }
        }
        else
        {
            go = new GameObject("BaldBeam");
            lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = lr.endWidth = BeamRadius * 0.6f;
            lr.startColor = lr.endColor = BeamColor;
        }
        if (lr != null)
        {
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
        }
        Destroy(go, Mathf.Max(0.05f, BeamLife));
    }

    public override string DescToken(string key, int rank)
    {
        switch (key)
        {
            case "converted": return _converted.ToString();
            case "comrades":
                int n = 0, all = 0;
                foreach (var h in TargetUtil.All(true)) { all++; if (!h.IsDead && IsComrade(h)) n++; }
                return $"{n} / {all}";
        }
        return null;
    }
}
