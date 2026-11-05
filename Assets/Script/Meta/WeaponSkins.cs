using UnityEngine;

/// <summary>무기 스킨 적용 — 모델 고르기(통째로 바꾸는 스킨) · 재질 바꾸기</summary>
public static class WeaponSkins
{
    public static WeaponSkinData Get(int skinId)
    {
        var m = GameMeta.Get();
        return m != null && m.Cosmetics != null ? m.Cosmetics.Skin(skinId) : null;
    }

    /// <summary>이 무기로 띄울 모델 프리팹 (스킨이 모델을 바꾸면 그것)</summary>
    public static GameObject ModelFor(EquipmentData weapon, WeaponSkinData skin) =>
        skin != null && skin.ModelPrefab != null && skin.WeaponId == weapon.Id ? skin.ModelPrefab : weapon.ModelPrefab;

    /// <summary>띄운 모델에 재질 바꾸기 (렌더러 순서대로, 빈 칸은 그대로)</summary>
    public static void ApplyMaterials(GameObject model, WeaponSkinData skin)
    {
        if (model == null || skin == null || skin.Materials == null || skin.Materials.Length == 0) return;
        var rs = model.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length && i < skin.Materials.Length; i++)
        {
            var mat = skin.Materials[i];
            if (mat == null) continue;
            var arr = rs[i].sharedMaterials;
            for (int k = 0; k < arr.Length; k++) arr[k] = mat;
            rs[i].sharedMaterials = arr;
        }
    }
}
