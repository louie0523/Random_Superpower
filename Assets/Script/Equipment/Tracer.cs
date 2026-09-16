using UnityEngine;

public class Tracer : MonoBehaviour
{
    [SerializeField] private LineRenderer line;
    [SerializeField] private float duration = 0.05f;
    private Transform _followStart;
    private Vector3 _end;

    private float _t = -1f;

    public void Show(Vector3 from, Vector3 to, Transform followStart = null)
    {
        if (line == null) line = GetComponent<LineRenderer>();

        _followStart = followStart;
        _end = to;

        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        _t = duration;
    }

    void Update()
    {
        if (_t < 0f) return;
        _t -= Time.deltaTime;

        // 시작점은 총구를 따라가고, 끝점은 고정
        if (_followStart != null)
            line.SetPosition(0, _followStart.position);

        float a = Mathf.Clamp01(_t / duration);
        var c = line.startColor;
        c.a = a;
        line.startColor = c;
        line.endColor = c;

        if (_t <= 0f) Destroy(gameObject);
    }
}