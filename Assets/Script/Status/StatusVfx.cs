using System.Collections.Generic;
using ToonVfx;
using UnityEngine;

/// <summary>
/// 상태이상 이펙트 — 플레이어 프리팹에 붙임 (PlayerStatus와 같은 오브젝트나 그 자식).
///   PlayerStatus.Values는 네트워크로 동기화되므로 각 클라이언트가 스스로 켜고 끔 (RPC 없음).
///   본인 캐릭터에는 재생하지 않음 — 1인칭 시야를 가리지 않게. 상태는 화면 UI(아이콘·가장자리 효과)로 표시.
///   이펙트는 '시작 → 반복 → 끝' 모드여야 함 — 풀리면 Stop()으로 사라짐 구간 재생
/// </summary>
public class StatusVfx : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public StatusType Status;
        public VfxEffect Effect;
        [Tooltip("붙을 곳 기준 위치 (플레이어 루트가 발밑이면 0 그대로)")]
        public Vector3 Offset;
    }

    [SerializeField] private PlayerStatus status;
    [Tooltip("이펙트가 따라갈 대상. 비우면 이 오브젝트")]
    [SerializeField] private Transform attach;
    [SerializeField] private Entry[] entries =
    {
        new() { Status = StatusType.Burn },
        new() { Status = StatusType.Frost },
        new() { Status = StatusType.Poison },
        new() { Status = StatusType.Slow },
        new() { Status = StatusType.Stun },
        new() { Status = StatusType.DeepWound },
    };

    [Header("테스트")]
    [Tooltip("켜면 본인 캐릭터에도 보임 (혼자 테스트할 때만)")]
    [SerializeField] private bool showForOwner;

    // 재생 중인 이펙트 — 풀에서 재사용되므로 PlayId로 아직 내 것인지 확인
    private readonly Dictionary<StatusType, (VfxEffect fx, int id)> _active = new();

    void Awake()
    {
        if (status == null) status = GetComponentInParent<PlayerStatus>();
        if (attach == null) attach = transform;
    }

    void OnDisable() => StopAll(true);

    void LateUpdate()
    {
        // 스폰 전·디스폰 중에는 네트워크 값을 읽을 수 없음
        if (status == null || status.Object == null || !status.Object.IsValid)
        {
            StopAll(true);
            return;
        }

        // Shared Mode — 내 캐릭터 = 내가 SA
        if (status.HasInputAuthority && !showForOwner)
        {
            StopAll(true);
            return;
        }

        foreach (var e in entries)
        {
            if (e == null || e.Effect == null) continue;

            bool on = status.Has(e.Status);
            bool mine = _active.TryGetValue(e.Status, out var a) && IsMine(a);

            if (on && (!mine || a.fx.IsStopping))
            {
                // 새로 걸렸거나, 사라지는 중에 다시 걸림 → 새로 재생 (사라지던 건 그대로 마무리)
                var fx = Vfx.Play(e.Effect, attach.TransformPoint(e.Offset), attach.rotation, attach);
                if (fx != null) _active[e.Status] = (fx, fx.PlayId);
            }
            else if (!on && mine && !a.fx.IsStopping)
            {
                a.fx.Stop();        // 사라짐 구간 재생 후 풀로
            }
        }
    }

    static bool IsMine((VfxEffect fx, int id) a) => a.fx != null && a.fx.IsPlaying && a.fx.PlayId == a.id;

    void StopAll(bool immediate)
    {
        if (_active.Count == 0) return;

        foreach (var a in _active.Values)
        {
            if (!IsMine(a)) continue;
            if (immediate) a.fx.StopImmediate();
            else a.fx.Stop();
        }
        _active.Clear();
    }
}
