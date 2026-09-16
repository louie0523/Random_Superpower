using UnityEngine;

/// <summary>총구 화염. 스프라이트를 순서대로 재생하고 사라진다.</summary>
public class MuzzleFlash : MonoBehaviour
{
    [SerializeField] private SpriteRenderer target;
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float frameTime = 0.02f;
    [SerializeField] private bool randomRoll = true;
    [SerializeField] private float minScale = 0.85f;
    [SerializeField] private float maxScale = 1.15f;
    [SerializeField] private Light flashLight;
    [SerializeField] private float lightIntensity = 8f;

    private float _t = -1f;
    private float _total;

    void Awake()
    {
        if (target == null) target = GetComponentInChildren<SpriteRenderer>();
        SetVisible(false);
    }

    /// <summary>발사할 때마다 호출. 재생 중이어도 처음부터 다시 시작</summary>
    public void Play()
    {
        _t = 0f;
        _total = Mathf.Max(0.01f, frames.Length * frameTime);

        if (randomRoll)
            transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        transform.localScale = Vector3.one * Random.Range(minScale, maxScale);

        if (frames.Length > 0 && target != null) target.sprite = frames[0];
        SetVisible(true);
    }

    void Update()
    {
        if (_t < 0f) return;

        _t += Time.deltaTime;

        if (_t >= _total)
        {
            _t = -1f;
            SetVisible(false);
            return;
        }

        int i = Mathf.Clamp((int)(_t / frameTime), 0, frames.Length - 1);
        if (target != null) target.sprite = frames[i];

        if (flashLight != null)
            flashLight.intensity = lightIntensity * (1f - _t / _total);
    }

    void SetVisible(bool on)
    {
        if (target != null) target.enabled = on;
        if (flashLight != null) flashLight.enabled = on;
    }
}