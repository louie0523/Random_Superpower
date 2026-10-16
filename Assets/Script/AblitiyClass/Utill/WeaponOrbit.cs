using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무구의 달인 — 들고 있지 않은 무기들이 머리 위를 돔 (모든 클라이언트가 각자 그림, 네트워크 오브젝트 아님).
/// 무기 목록은 PlayerEquipment(네트워크)를 매 프레임 읽어서 맞춤. 총기는 안 떠다님.
/// Fly(슬롯, 목표) — 그 무기가 목표까지 날아갔다가 (돌아오기면) 제자리로.
/// </summary>
public class WeaponOrbit : MonoBehaviour
{
    public PlayerEquipment Equip;
    public PlayerHealth Health;

    [Tooltip("머리 위 원 반지름 (m)")]
    public float Radius = 1.3f;
    [Tooltip("발 기준 높이 (m)")]
    public float Height = 2.1f;
    [Tooltip("도는 속도 (°/초)")]
    public float Spin = 50f;
    public float Bob = 0.1f;
    [Tooltip("떠다니는 무기 크기 배율")]
    public float ModelScale = 0.8f;
    [Tooltip("떠다닐 때 날 끝은 위 — 바깥쪽으로 기울이는 각도 (°, 음수면 안쪽)")]
    public float TipLean = 25f;
    [Tooltip("날아갈 때 날 끝이 목표에 닿게 모델을 뒤로 당김 (끄면 손잡이 위치가 목표)")]
    public bool TipLeads = true;

    class View
    {
        public int WeaponId;
        public GameObject Go;
        public Vector3 Pos;
        public bool Flying;
        public bool Returning;
        public bool ComeBack;
        public Vector3 Target;
        public float Speed;
        public WeaponAxis.Info Axis;
        public Vector3 Scale;
        public float Lead;      // 0 = 손잡이 기준, 1 = 날 끝 기준 (날아갈 때)
        public float SpinAngle;
    }

    private readonly View[] _views = new View[PlayerEquipment.WeaponSlots];
    private readonly List<int> _slots = new();
    private bool _hidden;

    public static bool CanOrbit(EquipmentData w) =>
        w != null && !(w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun);

    /// <summary>지금 떠다니는 무기의 슬롯 번호들 (들고 있는 것·빈 칸·총기 제외)</summary>
    public List<int> OrbitingSlots(List<int> into)
    {
        into.Clear();
        if (Equip == null || Equip.Database == null) return into;

        for (int i = 0; i < Equip.UsableSlots; i++)
        {
            if (i == Equip.ActiveWeaponSlot) continue;
            int id = Equip.GetWeaponIdAt(i);
            if (id == 0) continue;
            if (CanOrbit(Equip.Database.Get(id))) into.Add(i);
        }
        return into;
    }

    public Vector3 SlotPosition(int slot)
    {
        var v = slot >= 0 && slot < _views.Length ? _views[slot] : null;
        return v != null && v.Go != null ? v.Go.transform.position : transform.position + Vector3.up * Height;
    }

    public bool IsFlying(int slot) => slot >= 0 && slot < _views.Length && _views[slot] != null && _views[slot].Flying;

    /// <summary>그 무기가 target까지 날아감. comeBack이면 닿은 뒤 제자리로</summary>
    public void Fly(int slot, Vector3 target, float speed, bool comeBack)
    {
        if (slot < 0 || slot >= _views.Length || _views[slot] == null || _views[slot].Go == null) return;
        var v = _views[slot];
        v.Flying = true;
        v.Returning = false;
        v.ComeBack = comeBack;
        v.Target = target;
        v.Speed = Mathf.Max(1f, speed);
    }

    void LateUpdate()
    {
        if (Equip == null) { Destroy(gameObject); return; }

        bool dead = Health != null && Health.IsDead;
        if (dead != _hidden)
        {
            _hidden = dead;
            foreach (var v in _views)
                if (v?.Go != null) v.Go.SetActive(!dead);
        }

        Sync();
        if (dead) return;

        int n = _slots.Count;
        for (int k = 0; k < n; k++)
        {
            int slot = _slots[k];
            var v = _views[slot];
            if (v?.Go == null) continue;

            Vector3 home = HomePos(k, n);
            var t = v.Go.transform;
            float dt = Time.deltaTime;

            if (v.Flying)
            {
                Vector3 goal = v.Returning ? home : v.Target;
                Vector3 to = goal - v.Pos;
                float step = v.Speed * dt;

                if (to.magnitude <= step)
                {
                    v.Pos = goal;
                    if (v.Returning || !v.ComeBack) { v.Flying = false; v.Returning = false; }
                    else v.Returning = true;
                }
                else
                {
                    v.Pos += to.normalized * step;
                    // 날아가는 쪽으로 날 끝 (돌아올 때도 진행 방향으로). 넓은 면은 위
                    v.SpinAngle += v.Axis.Spin * dt;
                    if (v.Axis.Style == ThrowStyle.Tumble)
                    {
                        // 날을 세워 앞뒤로 빙글빙글
                        Vector3 d = to.normalized;
                        Vector3 up = Mathf.Abs(d.y) > 0.98f ? Vector3.forward : Vector3.up;
                        t.rotation = Quaternion.LookRotation(d, up) * Quaternion.AngleAxis(v.SpinAngle, Vector3.right) * WeaponAxis.Normalize(v.Axis, 90f);
                    }
                    else
                    {
                        float extra = v.Axis.Style == ThrowStyle.Drill ? v.SpinAngle : 0f;
                        var want = WeaponAxis.Aim(v.Axis, to.normalized, Vector3.up, extra);
                        t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-25f * dt));
                    }
                }
            }
            else
            {
                v.Pos = Vector3.Lerp(v.Pos, home, 1f - Mathf.Exp(-10f * dt));
                Vector3 outward = (home - transform.position); outward.y = 0f;
                if (outward.sqrMagnitude > 0.001f)
                {
                    outward.Normalize();
                    // 날 끝은 위, 바깥으로 TipLean만큼 기울임. 넓은 면은 바깥을 봄
                    Vector3 tip = Quaternion.AngleAxis(TipLean, Vector3.Cross(Vector3.up, outward)) * Vector3.up;
                    var want = WeaponAxis.Aim(v.Axis, tip, outward);
                    t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-12f * dt));
                }
            }

            // 날아갈 때는 날 끝이 경로 위에 오게 — 모델 원점(손잡이)을 뒤로
            v.Lead = Mathf.MoveTowards(v.Lead, TipLeads && v.Flying ? 1f : 0f, dt * 6f);
            Vector3 anchor = v.Axis.Style == ThrowStyle.Tumble ? v.Axis.Center : v.Axis.TipPoint;      // 빙글빙글은 가운데 기준
            Vector3 lead = v.Lead > 0.001f ? t.rotation * Vector3.Scale(v.Scale, anchor) * v.Lead : Vector3.zero;
            t.position = v.Pos - lead;
        }
    }

    Vector3 HomePos(int k, int n)
    {
        float a = (Time.time * Spin + k * 360f / Mathf.Max(1, n)) * Mathf.Deg2Rad;
        float bob = Mathf.Sin(Time.time * 2f + k) * Bob;
        return transform.position + new Vector3(Mathf.Cos(a) * Radius, Height + bob, Mathf.Sin(a) * Radius);
    }

    /// <summary>장비 목록에 맞춰 모델 만들기·지우기</summary>
    void Sync()
    {
        OrbitingSlots(_slots);

        for (int i = 0; i < _views.Length; i++)
        {
            bool want = _slots.Contains(i);
            int id = want ? Equip.GetWeaponIdAt(i) : 0;
            var v = _views[i];

            if (v != null && (v.WeaponId != id || !want))
            {
                if (v.Go != null) Destroy(v.Go);
                _views[i] = v = null;
            }
            if (!want || v != null) continue;

            var data = Equip.Database != null ? Equip.Database.Get(id) : null;
            if (data == null || data.ModelPrefab == null) continue;

            var go = Instantiate(data.ModelPrefab, transform);
            go.name = $"Orbit_{data.name}";
            var ax = WeaponAxis.Get(data.ModelPrefab, go);
            go.transform.localScale = data.ScaleOverride * ModelScale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            go.SetActive(!_hidden);

            _views[i] = new View
            {
                WeaponId = id,
                Go = go,
                Pos = transform.position + Vector3.up * Height,
                Axis = ax,
                Scale = go.transform.lossyScale
            };
        }
    }

    void OnDestroy()
    {
        foreach (var v in _views) if (v?.Go != null) Destroy(v.Go);
    }
}