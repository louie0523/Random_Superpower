using Fusion;
using ToonVfx;
using UnityEngine;

/// <summary>
/// 피격 이펙트 — 플레이어(·더미) 프리팹 루트에 붙임.
/// PlayerHealth가 피해를 받으면 RPC로 모든 클라이언트에서 Play(때린 위치, 치명타) 호출.
/// 맞은 사람 본인 화면에선 기본으로 안 보임 (1인칭 시야를 가리니까). 3인칭 중이면 보임.
/// </summary>
public class HitVfx : MonoBehaviour
{
    [Header("이펙트 (VFX Studio 프리팹)")]
    [SerializeField] private VfxEffect normalHit;
    [Tooltip("비우면 일반 이펙트")]
    [SerializeField] private VfxEffect critHit;

    [Header("위치")]
    [Tooltip("발 기준 가슴 높이 (m)")]
    [SerializeField] private float chestHeight = 1.2f;
    [Tooltip("몸 중심에서 때린 쪽으로 이만큼 앞 (몸 표면)")]
    [SerializeField] private float surfaceOffset = 0.35f;
    [SerializeField] private float scale = 1f;
    [SerializeField] private float critScale = 1.3f;

    [Header("본인")]
    [Tooltip("맞은 사람 본인 화면에도 보이기 (1인칭에서 시야를 가릴 수 있음)")]
    [SerializeField] private bool showForSelf;

    private NetworkObject _no;
    private ThirdPersonView _tpv;
    private PlayerAlter _alter;

    void Awake()
    {
        _no = GetComponent<NetworkObject>();
        _tpv = GetComponent<ThirdPersonView>();
        _alter = GetComponent<PlayerAlter>();
    }

    public void Play(Vector3 from, bool crit)
    {
        var prefab = crit && critHit != null ? critHit : normalHit;
        if (prefab == null) return;

        bool self = _no != null && _no.HasInputAuthority;
        if (self && !showForSelf && (_tpv == null || !_tpv.IsThirdPerson)) return;

        float lift = _alter != null ? _alter.Lift : 0f;       // 말 위면 그만큼 위
        Vector3 chest = transform.position + Vector3.up * (chestHeight + lift);

        Vector3 dir = from - chest;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        dir.Normalize();

        Vector3 pos = chest + dir * surfaceOffset;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);   // 때린 쪽을 바라봄

        Vfx.Play(prefab, pos, rot, transform, new VfxPlayOptions
        {
            Scale = crit ? critScale * scale : scale,
            IsOwner = self,
        });
    }
}
