using UnityEngine;

/// <summary>
/// 내게 걸린 상태이상 아이콘 줄 (로컬 플레이어 HUD).
/// 정의된 상태이상마다 아이콘 하나를 미리 만들어 두고, 걸렸을 때만 켬.
/// </summary>
public class StatusIconBar : MonoBehaviour
{
    [System.Serializable]
    public class IconDef
    {
        public StatusType Type;
        public Sprite Icon;
        public Color Color = Color.white;
    }

    [Tooltip("아이콘이 놓일 부모 (Horizontal Layout Group)")]
    [SerializeField] private Transform parent;
    [SerializeField] private StatusIconView iconPrefab;
    [SerializeField] private IconDef[] icons;

    private PlayerStatus _status;
    private StatusIconView[] _views;
    private float[] _peak;

    public void Bind(PlayerStatus status) => _status = status;

    void Awake()
    {
        int n = icons?.Length ?? 0;
        _views = new StatusIconView[n];
        _peak = new float[n];

        if (iconPrefab == null || parent == null) return;

        for (int i = 0; i < n; i++)
        {
            var v = Instantiate(iconPrefab, parent);
            v.Setup(icons[i].Icon, icons[i].Color);
            v.gameObject.SetActive(false);
            _views[i] = v;
        }
    }

    void Update()
    {
        if (_views == null) return;

        bool valid = _status != null && _status.Object != null && _status.Object.IsValid;

        for (int i = 0; i < _views.Length; i++)
        {
            var v = _views[i];
            if (v == null) continue;

            var type = icons[i].Type;
            bool has = valid && _status.Has(type);

            if (v.gameObject.activeSelf != has) v.gameObject.SetActive(has);
            if (!has) { _peak[i] = 0f; continue; }

            float remain = _status.Remaining(type);
            if (remain > _peak[i]) _peak[i] = remain;      // 새로 걸리거나 갱신되면 기준을 다시

            int stacks = type == StatusType.Poison ? _status.PoisonStacks : 0;
            v.Set(remain, _peak[i] > 0f ? remain / _peak[i] : 0f, stacks);
        }
    }
}
