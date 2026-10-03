using Fusion;
using UnityEngine;

public class PlayerInteract : NetworkBehaviour
{
    [SerializeField] private LayerMask interactMask = ~0;
    [SerializeField] private float searchRadius = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("홀드 요청 재전송 간격 (초). 남이 열다 떼면 이 간격으로 이어받음")]
    [SerializeField] private float sendInterval = 0.3f;

    private bool _holding;
    private LootChest _target;
    private LootChest _sentTo;      // 마지막으로 '누름'을 보낸 상자
    private PlayerHealth _health;
    private float _nextSend;

    public LootChest CurrentTarget => _target;

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (!HasStateAuthority) return;

        // UI가 열리면 누르고 있던 것도 뗀 것으로 처리
        _holding = !UIManager.InputBlocked && Input.GetKey(interactKey);
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

        // 대상이 바뀌면 이전 상자에 '뗌' 전송
        if (_target != null && _target != chest) Release();
        _target = chest;

        if (_target == null) return;
        if (Time.time < _nextSend) return;

        var me = Object.InputAuthority;
        bool mine = _target.Opener == me;

        if (_holding && !mine)
        {
            // 처음 누름, 또는 남이 열고 있다 뗐을 때 이어받기
            _target.RPC_SetHold(me, true);
            _sentTo = _target;
            _nextSend = Time.time + sendInterval;
        }
        else if (!_holding && (mine || _sentTo == _target))
        {
            // 뗌 — 아직 Opener가 안 넘어왔어도(요청이 도착 전) 확실히 취소
            _target.RPC_SetHold(me, false);
            _sentTo = null;
            _nextSend = Time.time + sendInterval;
        }
    }

    void Release()
    {
        if (_sentTo != null)
        {
            _sentTo.RPC_SetHold(Object.InputAuthority, false);
            _sentTo = null;
        }
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