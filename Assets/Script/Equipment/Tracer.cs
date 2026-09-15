using UnityEngine;

public class Tracer : MonoBehaviour
{
    [SerializeField] private LineRenderer line;
    [SerializeField] private float duration = 0.05f;

    private float _t;

    public void Show(Vector3 from, Vector3 to)
    {
        if (line == null) line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        _t = duration;
    }

    void Update()
    {
        _t -= Time.deltaTime;

        float a = Mathf.Clamp01(_t / duration);
        var c = line.startColor;
        c.a = a;
        line.startColor = c;
        line.endColor = c;

        if (_t <= 0f) Destroy(gameObject);
    }
}