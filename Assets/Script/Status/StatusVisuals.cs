using UnityEngine;

/// <summary>
/// 상태이상 연출 — 걸린 동안 지정한 오브젝트(파티클 등)를 켬.
/// 플레이어 프리팹에 붙이고, 이펙트는 몸 아래 자식으로.
/// 모든 클라이언트에서 동작 (남이 봐도 보임).
/// </summary>
public class StatusVisuals : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public StatusType Type;
        public GameObject Visual;

        [Tooltip("본인 화면에서도 켤지. 1인칭에선 몸에 붙은 이펙트가 시야를 가릴 수 있어 기본 꺼짐")]
        public bool ShowForSelf = false;
    }

    [SerializeField] private PlayerStatus status;
    [SerializeField] private Entry[] entries;

    void Awake()
    {
        if (status == null) status = GetComponentInParent<PlayerStatus>();

        if (entries != null)
            foreach (var e in entries)
                if (e?.Visual != null) e.Visual.SetActive(false);
    }

    void Update()
    {
        if (status == null || status.Object == null || !status.Object.IsValid) return;
        if (entries == null) return;

        bool self = status.HasInputAuthority;

        foreach (var e in entries)
        {
            if (e?.Visual == null) continue;

            bool on = status.Has(e.Type) && (!self || e.ShowForSelf);
            if (e.Visual.activeSelf != on) e.Visual.SetActive(on);
        }
    }
}
