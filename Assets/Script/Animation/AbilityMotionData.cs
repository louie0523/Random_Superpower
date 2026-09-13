using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Motion")]
public class AbilityMotionData : ScriptableObject, IMotionSource
{
    [Header("Info")]
    public string DisplayName = "일격";
    [TextArea(1, 3)] public string Note;

    [Header("Motion")]
    public ComboStep Step = new();

    [Header("Camera")]
    [Tooltip("시전 중 카메라 롤 (도)")]
    public float CameraRoll;

    [Header("Preview")]
    [Tooltip("툴에서 미리보기용으로 들 무기")]
    public EquipmentData PreviewWeaponData;

    // IMotionSource
    public string SourceName => DisplayName;
    public int ComboLength => 1;
    public ComboStep GetStep(int i) => Step;
    public EquipmentData PreviewWeapon => PreviewWeaponData;
    public bool SupportsMultiStep => false;
    public void SetSteps(ComboStep[] steps) { if (steps != null && steps.Length > 0) Step = steps[0]; }
    public ComboStep[] GetSteps() => new[] { Step };
}