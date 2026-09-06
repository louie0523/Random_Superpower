using UnityEngine;

/// <summary>모션 편집 툴이 다룰 수 있는 대상</summary>
public interface IMotionSource
{
    string SourceName { get; }
    int ComboLength { get; }
    ComboStep GetStep(int index);

    /// <summary>프리뷰에 띄울 무기</summary>
    EquipmentData PreviewWeapon { get; }

    /// <summary>콤보 추가/삭제 지원 여부</summary>
    bool SupportsMultiStep { get; }
    void SetSteps(ComboStep[] steps);
    ComboStep[] GetSteps();
}