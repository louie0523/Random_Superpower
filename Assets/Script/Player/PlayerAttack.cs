using Fusion;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour
{
    [Header("Attack")]
    //[SerializeField] private int damage = 25;
    [SerializeField] private float range = 3f;
    [SerializeField] private float radius = 0.6f;
    [SerializeField] private float cooldown = 0.5f;
    [SerializeField] private LayerMask hitMask;

    [Header("Refs")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPSCamera fpsCamera;

    [Networked] private TickTimer AttackCooldown { get; set; }

    private PlayerHealth _myHealth;
    private PlayerStats _stats;

    public override void Spawned()
    {
        _myHealth = GetComponent<PlayerHealth>();
        _stats = GetComponent<PlayerStats>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (_myHealth.IsDead) return;
        if (!AttackCooldown.ExpiredOrNotRunning(Runner)) return;

        if (Input.GetMouseButton(0))
        {
            float cd = cooldown / Mathf.Max(0.1f, _stats.Get(StatType.AttackSpeed));
            AttackCooldown = TickTimer.CreateFromSeconds(Runner, cd);
            DoAttack();
        }
    }

    int CalcDamage()
    {
        float atk = _stats.Get(StatType.AttackPower);
        bool crit = Random.value < _stats.Get(StatType.CritChance);
        if (crit) atk *= _stats.Get(StatType.CritDamage);
        return Mathf.RoundToInt(atk);
    }

    void DoAttack()
    {
        fpsCamera.FovKick(4f);

        Vector3 origin = cameraRoot.position;
        Vector3 dir = cameraRoot.forward;
        Vector3 center = origin + dir * (range * 0.5f);
        float r = radius + range * 0.3f;

        Collider[] cols = Physics.OverlapSphere(center, r, hitMask);

        foreach (var col in cols)
        {
            GameObject hitObj = col.gameObject;
            if (hitObj == gameObject) continue;

            if (hitObj.TryGetComponent<PlayerHealth>(out var target))
            {
                if (target.IsDead) continue;

                target.RPC_TakeDamage(CalcDamage(), Object.InputAuthority, cameraRoot.position);
                Debug.Log($"[HIT] {hitObj.name}");
                break;
            }
        }
    }



    void OnDrawGizmosSelected()
    {
        if (cameraRoot == null) return;
        Gizmos.color = Color.red;
        Vector3 c = cameraRoot.position + cameraRoot.forward * (range * 0.5f);
        Gizmos.DrawWireSphere(c, radius + range * 0.3f);
    }
}