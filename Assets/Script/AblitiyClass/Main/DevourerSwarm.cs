using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 탐식자 궁극기 '굶주린 괴수'의 괴수들 — 모든 클라이언트가 각자 그림 (네트워크 오브젝트 아님, WeaponOrbit과 같은 방식).
///   평소: 주인 둘레를 원으로 둘러싸고 천천히 돎 (살짝 위아래로 떠다님) — Idle
///   SetTarget(번호, 대상): 그 괴수가 대상에게 날아감 · null이면 제자리로 돌아옴 — 날아가는 동안 Run
///   SetTarget(번호, 대상, true): 대상 둘레를 맴돎 — 다시 물 수 있을 때까지 기다리는 느낌
///   Attack(번호): 공격 모션 시작 (RigAnimator의 AttackState)
///   Bite(번호): 무는 순간 — 공격 모션이 없는 모델이면 살짝 커졌다 작아짐
/// 누구를 물지 · 피해는 DevourerAbility(권한자)가 정하고, 다른 화면엔 이벤트로 알림.
/// 괴수 모델은 앞이 +Z (파랑 화살표)가 되게 만들 것
/// </summary>
public class DevourerSwarm : MonoBehaviour
{
    [Tooltip("둘레 반지름 (m)")] public float Radius = 2.2f;
    [Tooltip("발 기준 높이 (m)")] public float Height = 1.6f;
    [Tooltip("도는 빠르기 (°/초)")] public float Spin = 25f;
    [Tooltip("위아래로 떠다니는 폭 (m)")] public float Bob = 0.15f;
    [Tooltip("날아가는 속도 (m/초)")] public float Speed = 14f;
    [Tooltip("대상의 어디를 무는지 — 발 기준 높이 (m)")] public float AimHeight = 1.1f;
    [Tooltip("대상 앞 이만큼에서 멈춤 (m) — 몸 속으로 파고들지 않게")] public float StopDistance = 0.9f;
    [Tooltip("공격 모션 상태 이름 (RigAnimData)")] public string AttackState = "Attack";
    [Tooltip("대상 둘레를 맴도는 반지름 (m)")] public float ProwlRadius = 2.5f;
    [Tooltip("맴도는 빠르기 (°/초)")] public float ProwlSpin = 45f;

    class Beast
    {
        public GameObject Go;
        public RigAnimator Anim;
        public Vector3 Scale;
        public Vector3 Pos, Vel;
        public Transform Target;
        public bool Prowl;
        public float ProwlAngle;    // 대상 둘레 각도 (°)
        public float ProwlDir = 1f; // 도는 방향 (±1)
        public float NextFlip;      // 가끔 방향을 바꿈
        public float Seed;
        public float Grow;      // 생길 때 0 → 1
        public float Punch;     // 물 때 (RigAnimator가 없을 때)
    }

    readonly List<Beast> _beasts = new();
    Transform _owner;
    PlayerHealth _ownerHealth;
    float _angle;
    bool _hidden;

    public int Count => _beasts.Count;

    public static DevourerSwarm Create(Transform owner, PlayerHealth health, GameObject prefab, int count)
    {
        var go = new GameObject("DevourerSwarm");
        var s = go.AddComponent<DevourerSwarm>();
        s._owner = owner;
        s._ownerHealth = health;
        s._angle = owner != null ? owner.eulerAngles.y : 0f;
        for (int i = 0; i < count; i++) s.AddBeast(prefab);
        return s;
    }

    void AddBeast(GameObject prefab)
    {
        GameObject m;
        if (prefab != null) m = Instantiate(prefab, transform);
        else
        {
            // 프리팹이 없을 때 임시 모양 (테스트용)
            m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.transform.SetParent(transform, false);
            m.transform.localScale = Vector3.one * 0.45f;
            var r = m.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.35f, 0.08f, 0.1f);
        }
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in m.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

        var b = new Beast
        {
            Seed = Random.value * 100f,
            Go = m,
            Anim = m.GetComponentInChildren<RigAnimator>(),
            Scale = m.transform.localScale,
            Pos = _owner != null ? _owner.position + Vector3.up * Height : transform.position,
        };
        m.transform.position = b.Pos;
        m.transform.localScale = Vector3.zero;      // 커지면서 나타남
        _beasts.Add(b);
    }

    // ══════════════════ 외부에서 ══════════════════

    /// <summary>지금 그 괴수의 위치 (권한자가 '물 수 있는 거리인지' 확인할 때)</summary>
    public Vector3 Position(int i) =>
        i >= 0 && i < _beasts.Count ? _beasts[i].Pos : (_owner != null ? _owner.position : transform.position);

    /// <summary>대상에게 날아감 · null이면 제자리로</summary>
    public void SetTarget(int i, Transform t, bool prowl = false)
    {
        if (i < 0 || i >= _beasts.Count) return;
        var b = _beasts[i];
        bool startProwl = prowl && t != null && (!b.Prowl || b.Target != t);
        b.Target = t;
        b.Prowl = prowl && t != null;
        if (startProwl)
        {
            // 지금 있는 쪽에서부터 돌기 시작 (대상을 가로지르지 않게)
            Vector3 d = b.Pos - t.position;
            b.ProwlAngle = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            b.ProwlDir = Random.value < 0.5f ? -1f : 1f;
            b.NextFlip = Time.time + Random.Range(1.5f, 3.5f);
        }
    }

    RigAnimator FirstAnim()
    {
        foreach (var b in _beasts) if (b.Anim != null && b.Anim.Data != null) return b.Anim;
        return null;
    }

    /// <summary>공격 모션 길이 (초, 재생 속도 반영) — 모션이 없으면 0</summary>
    public float AttackLength
    {
        get
        {
            var a = FirstAnim();
            var st = a != null ? a.Data.Find(AttackState) : null;
            if (st == null) return 0f;
            return st.Length / Mathf.Max(0.01f, st.Speed * a.SpeedMul);
        }
    }

    /// <summary>공격 모션 시작</summary>
    public void Attack(int i)
    {
        if (i < 0 || i >= _beasts.Count) return;
        var b = _beasts[i];
        if (b.Anim != null && b.Anim.Has(AttackState)) b.Anim.Play(AttackState, AttackLength);   // 반복 상태여도 한 번 길이만
    }

    /// <summary>무는 순간 — 공격 모션이 없으면 살짝 커졌다 작아짐</summary>
    public void Bite(int i)
    {
        if (i < 0 || i >= _beasts.Count) return;
        var b = _beasts[i];
        if (b.Anim == null || !b.Anim.Has(AttackState)) b.Punch = 1f;
    }

    // ══════════════════ 매 프레임 ══════════════════

    Vector3 Home(int i)
    {
        int n = Mathf.Max(1, _beasts.Count);
        float a = (_angle + i * 360f / n) * Mathf.Deg2Rad;
        float bob = Mathf.Sin(Time.time * 2f + i * 1.7f) * Bob;
        return _owner.position + new Vector3(Mathf.Cos(a) * Radius, Height + bob, Mathf.Sin(a) * Radius);
    }

    void LateUpdate()
    {
        if (_owner == null) { Destroy(gameObject); return; }

        bool dead = _ownerHealth != null && _ownerHealth.IsDead;
        if (dead != _hidden)
        {
            _hidden = dead;
            foreach (var b in _beasts) if (b.Go != null) b.Go.SetActive(!dead);
        }
        if (dead) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        _angle += Spin * dt;

        for (int i = 0; i < _beasts.Count; i++)
        {
            var b = _beasts[i];
            if (b.Go == null) continue;

            bool chase = b.Target != null && b.Target.gameObject.activeInHierarchy;
            Vector3 aim = chase ? b.Target.position + Vector3.up * AimHeight : Vector3.zero;
            Vector3 goal;
            bool prowl = chase && b.Prowl;
            if (prowl)
            {
                // 대상 둘레를 어슬렁 — 반지름 · 높이가 조금씩 출렁이고 가끔 방향을 바꿈
                if (Time.time >= b.NextFlip)
                {
                    b.ProwlDir = -b.ProwlDir;
                    b.NextFlip = Time.time + Random.Range(1.5f, 3.5f);
                }
                b.ProwlAngle += b.ProwlDir * ProwlSpin * (0.7f + 0.3f * Mathf.Sin(Time.time * 1.1f + b.Seed)) * dt;
                float r = ProwlRadius + Mathf.Sin(Time.time * 0.9f + b.Seed) * 0.5f;
                float a = b.ProwlAngle * Mathf.Deg2Rad;
                float hy = AimHeight + 0.3f + Mathf.Sin(Time.time * 1.6f + b.Seed * 2f) * 0.3f;
                goal = b.Target.position + new Vector3(Mathf.Cos(a) * r, hy, Mathf.Sin(a) * r);
            }
            else if (chase)
            {
                // 대상 앞 StopDistance에서 멈춤 (괴수 쪽에서 다가간 방향)
                Vector3 back = b.Pos - aim;
                back.y = 0f;
                if (back.sqrMagnitude < 0.0001f) back = _owner.position - aim;
                back.y = 0f;
                goal = aim + (back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.back) * StopDistance;
            }
            else goal = Home(i);
            Vector3 prev = b.Pos;

            float dist = Vector3.Distance(b.Pos, goal);
            bool flying = dist > (prowl ? 1.5f : chase ? 0.3f : 1.5f);          // 날아가는 중 (Run) · 아니면 Idle
            if (!flying)
                b.Pos = Vector3.Lerp(b.Pos, goal, 1f - Mathf.Exp((prowl ? -4f : -8f) * dt));   // 제자리 · 대상 앞 · 맴돌기 — 부드럽게
            else
                b.Pos = Vector3.MoveTowards(b.Pos, goal, Speed * dt);

            b.Vel = Vector3.Lerp(b.Vel, (b.Pos - prev) / dt, 1f - Mathf.Exp(-12f * dt));

            // 방향 — 대상이 있으면 대상 쪽, 아니면 움직이는 쪽, 거의 멈췄으면 바깥쪽
            Vector3 look;
            if (chase) { look = aim - b.Pos; look.y = 0f; }
            else
            {
                look = b.Vel;
                look.y *= 0.3f;
                if (look.sqrMagnitude < 0.25f) { look = b.Pos - _owner.position; look.y = 0f; }
            }
            if (look.sqrMagnitude > 0.0001f)
            {
                var want = Quaternion.LookRotation(look.normalized, Vector3.up);
                b.Go.transform.rotation = Quaternion.Slerp(b.Go.transform.rotation, want, 1f - Mathf.Exp(-10f * dt));
            }
            b.Go.transform.position = b.Pos;

            b.Grow = Mathf.MoveTowards(b.Grow, 1f, dt * 4f);
            b.Punch = Mathf.MoveTowards(b.Punch, 0f, dt * 6f);
            float s = Mathf.SmoothStep(0f, 1f, b.Grow) * (1f + Mathf.Sin(b.Punch * Mathf.PI) * 0.3f);
            b.Go.transform.localScale = b.Scale * s;

            if (b.Anim != null)
            {
                // 날아갈 때 Run (RunThreshold 이상으로 넘겨서 Walk 말고 Run) · 제자리 · 대상 앞에선 Idle
                // 맴돌 땐 실제 속도대로 (Walk가 있으면 Walk, 없으면 느린 Run)
                float run = b.Anim.Data != null ? b.Anim.Data.RunThreshold : 0f;
                float spd = b.Vel.magnitude;
                if (prowl && !flying) { b.Anim.Moving = spd > 1.2f; b.Anim.MoveSpeed = Mathf.Min(spd, run * 0.8f); }
                else { b.Anim.Moving = flying; b.Anim.MoveSpeed = flying ? Mathf.Max(spd, run) : 0f; }
            }
        }
    }
}