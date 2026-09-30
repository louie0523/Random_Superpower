using Fusion;
using UnityEngine;

public class PlayerInteract : NetworkBehaviour
{
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private float searchRadius = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [Networked] public NetworkBool AllowGunEnchant { get; set; }    

    private bool _holding;
    private LootChest _target;
    private PlayerHealth _health;

    public LootChest CurrentTarget => _target;

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (!HasStateAuthority) return;
        _holding = Input.GetKey(interactKey);
    }


    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health != null && _health.IsDead)
        {
            Release();
            return;
        }

        var chest = FindChest();

        // 대상이 바뀌면 이전 것 취소
        if (_target != null && _target != chest) Release();

        _target = chest;

        //Debug.Log($"[Interact] target={(_target == null ? "NULL" : _target.name)} " +
        //      $"holding={_holding} progress={_target?.Progress:F2}");

        if (_target == null) return;

        if (_holding)
            _target.Hold(Object.InputAuthority, transform.position);
        else
            _target.Cancel(Object.InputAuthority);
    }

    void Release()
    {
        if (_target != null) _target.Cancel(Object.InputAuthority);
        _target = null;
    }

    LootChest FindChest()
    {
        var cols = Physics.OverlapSphere(transform.position, searchRadius,
                                         interactMask, QueryTriggerInteraction.Collide);

        LootChest best = null;
        float bestDist = float.MaxValue;

        foreach (var c in cols)
        {
            var chest = c.GetComponentInParent<LootChest>();
            if (chest == null || chest.IsOpened) continue;

            float d = Vector3.Distance(transform.position, chest.transform.position);
            if (d > chest.InteractRange) continue;

            if (d < bestDist) { bestDist = d; best = chest; }
        }

        return best;
    }
}