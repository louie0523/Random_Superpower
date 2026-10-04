using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 내 화면 전체에 상태이상 색을 살짝 덮음 (동상 = 푸르게 등).
/// UI 씬 HUD에 전체 화면 Image 하나 두고 연결. UIManager가 로컬 플레이어에 바인딩.
/// </summary>
public class StatusScreenTint : MonoBehaviour
{
    [System.Serializable]
    public class Tint
    {
        public StatusType Type;
        [Tooltip("알파가 덮는 진하기. 위에 있는 항목이 우선")]
        public Color Color = new(0.45f, 0.75f, 1f, 0.22f);
    }

    [Tooltip("전체 화면 Image. Raycast Target 꺼둘 것")]
    [SerializeField] private Image overlay;
    [SerializeField] private Tint[] tints =
    {
        new() { Type = StatusType.Frost, Color = new Color(0.45f, 0.75f, 1f, 0.22f) },
    };
    [SerializeField] private float fadeSpeed = 4f;

    private PlayerStatus _status;

    public void Bind(PlayerStatus status) => _status = status;

    void Awake()
    {
        if (overlay != null)
        {
            overlay.raycastTarget = false;
            overlay.color = Color.clear;
            overlay.enabled = false;
        }
    }

    void Update()
    {
        if (overlay == null) return;

        Color target = Color.clear;

        if (_status != null && _status.Object != null && _status.Object.IsValid && tints != null)
            foreach (var t in tints)
                if (t != null && _status.Has(t.Type)) { target = t.Color; break; }

        Color c = Color.Lerp(overlay.color, target, Time.deltaTime * fadeSpeed);
        overlay.color = c;
        overlay.enabled = c.a > 0.003f;
    }
}
