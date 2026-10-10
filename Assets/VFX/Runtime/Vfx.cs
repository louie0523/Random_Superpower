using System.Collections.Generic;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>재생 옵션 — 0이나 null은 '기본값'</summary>
    public struct VfxPlayOptions
    {
        /// <summary>재생 속도 (공속 등). 0 = 1</summary>
        public float Speed;
        /// <summary>크기 배율. 0 = 1</summary>
        public float Scale;
        /// <summary>색 교체 (같은 이펙트를 화상·동상 버전으로). null = 이펙트 기본</summary>
        public VfxPalette Palette;
        /// <summary>판정 사거리(m) — 0보다 크면 호 반지름을 여기에 맞춤</summary>
        public float FitRange;
        /// <summary>판정 각도(°) — 0보다 크면 호 각도를 여기에 맞춤</summary>
        public float FitAngle;
        /// <summary>본인(1인칭) 화면에서 재생 — 이펙트의 본인 크기 배율·숨김 적용</summary>
        public bool IsOwner;
    }

    /// <summary>
    /// 게임에서 이펙트를 재생하는 입구. 풀링 — 끝난 이펙트는 꺼서 보관했다가 다시 씀.
    ///   Vfx.Play(prefab, pos, rot)                    월드에 고정
    ///   Vfx.Play(prefab, pos, rot, follow: hand)      손을 따라감
    /// 반복 이펙트는 반환값을 들고 있다가 Stop() — 풀로 돌아감
    /// </summary>
    public static class Vfx
    {
        static readonly Dictionary<VfxEffect, Stack<VfxEffect>> Pools = new();
        static Transform _root;
        static Transform _holding;

        /// <summary>항상 꺼져 있는 보관함 — 여기서 Instantiate 하면 OnEnable이 안 불림</summary>
        static Transform Holding
        {
            get
            {
                if (_holding == null)
                {
                    var go = new GameObject("Holding");
                    go.SetActive(false);
                    go.transform.SetParent(Root, false);
                    _holding = go.transform;
                }
                return _holding;
            }
        }

        static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("[VFX Pool]");
                    Object.DontDestroyOnLoad(go);
                    _root = go.transform;
                }
                return _root;
            }
        }

        public static VfxEffect Play(VfxEffect prefab, Vector3 pos, Quaternion rot, Transform follow = null) =>
            Play(prefab, pos, rot, follow, default);

        public static VfxEffect Play(VfxEffect prefab, Vector3 pos, Quaternion rot, Transform follow, in VfxPlayOptions opt)
        {
            if (prefab == null || !Application.isPlaying) return null;
            if (opt.IsOwner && prefab.HideForOwner) return null;

            var fx = Take(prefab);
            var t = fx.transform;

            t.SetParent(follow != null ? follow : Root, false);
            t.SetPositionAndRotation(pos, rot);

            float scale = (opt.Scale > 0f ? opt.Scale : 1f) * (opt.IsOwner ? prefab.OwnerScale : 1f);
            t.localScale = prefab.transform.localScale * scale;
            if (follow != null)
            {
                // 부모 크기에 영향받지 않게 — 월드 크기를 프리팹 그대로 유지
                var ls = follow.lossyScale;
                t.localScale = new Vector3(
                    t.localScale.x / Mathf.Max(ls.x, 1e-4f),
                    t.localScale.y / Mathf.Max(ls.y, 1e-4f),
                    t.localScale.z / Mathf.Max(ls.z, 1e-4f));
            }

            fx.gameObject.SetActive(true);
            fx.Play(opt);
            return fx;
        }

        static VfxEffect Take(VfxEffect prefab)
        {
            if (Pools.TryGetValue(prefab, out var stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled != null) return pooled;      // 씬 전환 등으로 지워진 건 건너뜀
                }
            }

            // 꺼진 부모 아래에 만들어 OnEnable의 자동 재생을 막음 (프리팹 에셋 자체는 건드리지 않음)
            var fx = Object.Instantiate(prefab, Holding);
            fx.name = prefab.name;
            fx.PlayOnEnable = false;
            fx.SourcePrefab = prefab;
            fx.Finished += Return;
            fx.gameObject.SetActive(false);
            fx.transform.SetParent(Root, false);
            return fx;
        }

        static void Return(VfxEffect fx)
        {
            if (fx == null || fx.SourcePrefab == null) return;

            // 따라가던 대상(플레이어)이 꺼지는 중이면 끄기·부모 바꾸기를 할 수 없음 — 렌더러는 이미 숨겼으니
            // 그대로 두고 다음에 꺼낼 때 옮김. 대상이 삭제되면 같이 사라지고, 풀에서는 null로 걸러짐
            var parent = fx.transform.parent;
            if (parent == null || parent.gameObject.activeInHierarchy)
            {
                fx.gameObject.SetActive(false);
                fx.transform.SetParent(Root, false);
            }

            if (!Pools.TryGetValue(fx.SourcePrefab, out var stack))
                Pools[fx.SourcePrefab] = stack = new Stack<VfxEffect>();
            stack.Push(fx);
        }

        /// <summary>미리 만들어 둠 — 첫 재생 때 생성 비용으로 끊기지 않게 (라운드 시작 등)</summary>
        public static void Prewarm(VfxEffect prefab, int count)
        {
            if (prefab == null || !Application.isPlaying) return;
            for (int i = 0; i < count; i++) Return(Take(prefab));
        }
    }
}
