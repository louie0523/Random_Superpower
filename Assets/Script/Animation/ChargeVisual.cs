using UnityEngine;

/// <summary>차징 정도에 따라 크기/밝기가 변하는 연출. 무기 모델에 붙임.</summary>
public class ChargeVisual : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("커지고 밝아질 오브젝트")]
    [SerializeField] private Transform target;
    [SerializeField] private Light glow;
    [SerializeField] private ParticleSystem particles;

    [Header("Scale")]
    [SerializeField] private float minScale = 0f;
    [SerializeField] private float maxScale = 0.35f;

    [Header("Light")]
    [SerializeField] private float maxIntensity = 4f;

    [Header("Feel")]
    [Tooltip("차징이 끝났을 때 맥동")]
    [SerializeField] private float pulseSpeed = 8f;
    [SerializeField] private float pulseAmount = 0.08f;

    private float _amount;

    public void SetAmount(float t01)
    {
        _amount = Mathf.Clamp01(t01);
    }

    void LateUpdate()
    {
        float a = _amount;

        Debug.Log($"[Visual] a={a:F2} scale={(target != null ? target.localScale.x : -1f):F3}");

        // 만충이면 살짝 맥동
        float pulse = a >= 0.999f
            ? Mathf.Sin(Time.time * pulseSpeed) * pulseAmount
            : 0f;

        if (target != null)
        {
            float s = Mathf.Lerp(minScale, maxScale, a) * (1f + pulse);
            target.localScale = Vector3.one * s;
            target.gameObject.SetActive(a > 0.01f);
        }

        if (glow != null)
        {
            glow.intensity = maxIntensity * a * (1f + pulse);
            glow.enabled = a > 0.01f;
        }

        if (particles != null)
        {
            var em = particles.emission;
            em.enabled = a > 0.05f;
        }
    }
}