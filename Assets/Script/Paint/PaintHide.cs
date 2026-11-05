using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 칠하기 모드에서 숨는 소품 — 안 보이고 · 안 칠해지고 · 붓을 막지도 않음.
/// 타이틀 씬 소품(상자 · 간판 등) 루트에 붙이면 자식까지 전부
/// </summary>
public class PaintHide : MonoBehaviour
{
    readonly List<Renderer> _renderers = new();
    readonly List<Collider> _colliders = new();

    void OnEnable()
    {
        PaintMode.Changed += Apply;
        if (PaintMode.On) Apply(true);
    }

    void OnDisable()
    {
        PaintMode.Changed -= Apply;
        Apply(false);
    }

    void Apply(bool hide)
    {
        if (hide)
        {
            _renderers.Clear();
            _colliders.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; _renderers.Add(r); }
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c.enabled) { c.enabled = false; _colliders.Add(c); }
        }
        else
        {
            foreach (var r in _renderers) if (r != null) r.enabled = true;
            foreach (var c in _colliders) if (c != null) c.enabled = true;
            _renderers.Clear();
            _colliders.Clear();
        }
    }
}
