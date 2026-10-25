using UnityEngine;

/// <summary>지구 던지기의 날아가는 지구 (모든 화면에서 각자 — 연출만)</summary>
public class EarthThrowVisual : MonoBehaviour
{
    Vector3 _from, _to;
    float _time, _t, _spin;
    Vector3 _axis;

    public static void Spawn(GameObject prefab, Vector3 from, Vector3 to, float duration, float size, float spin)
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, from, Quaternion.identity);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Earth";
            var r = go.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (r != null && sh != null) r.material = new Material(sh) { color = new Color(0.2f, 0.45f, 0.95f) };
            go.transform.position = from;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        // 지름 = size (프리팹 크기가 1 기준이 아니어도 렌더러 크기로 맞춤)
        float cur = 1f;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            var b = rs[0].bounds;
            foreach (var x in rs) b.Encapsulate(x.bounds);
            cur = Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        }
        go.transform.localScale *= size / cur;

        var v = go.AddComponent<EarthThrowVisual>();
        v._from = from;
        v._to = to;
        v._time = Mathf.Max(0.1f, duration);
        v._spin = spin;
        v._axis = Random.onUnitSphere;
    }

    void Update()
    {
        _t += Time.deltaTime / _time;
        float k = Mathf.Clamp01(_t);
        float e = k * k;                                    // 점점 빨라짐 (내리꽂는 느낌)
        transform.position = Vector3.LerpUnclamped(_from, _to, e);
        transform.Rotate(_axis, _spin * Time.deltaTime, Space.World);
        if (_t >= 1f) Destroy(gameObject);
    }
}
