using UnityEngine;

namespace ToonVfx
{
    // ⚠ enum은 뒤에만 추가할 것 (중간 삽입 시 기존 에셋 값이 밀림)

    public enum VfxShape
    {
        [InspectorName("평면 카드")] Card,
        [InspectorName("호 (슬래시)")] Arc,
        [InspectorName("링 (충격파)")] Ring,
        [InspectorName("뿌리기 (조각들)")] Burst,
        [InspectorName("기둥 (오라)")] Pillar,
        [InspectorName("속도선")] Streak,
        [InspectorName("잔상 줄 (지나온 경로)")] Trail,
        [InspectorName("원뿔 (돌진 바람)")] Cone,
        // 예정: Mesh
    }

    public enum VfxPlayMode
    {
        [InspectorName("1회")] Once,
        [InspectorName("반복")] Loop,
        [InspectorName("시작 → 반복 → 끝 (버프)")] StartLoopEnd,
    }

    public enum VfxFacing
    {
        [InspectorName("고정 방향")] Fixed,
        [InspectorName("항상 카메라를 봄")] Billboard,
    }

    public enum VfxAppear
    {
        [InspectorName("팍 (번쩍)")] Pop,
        [InspectorName("커지며")] Grow,
        [InspectorName("쓸고 지나감")] Sweep,
        [InspectorName("솟아오름")] Rise,
        [InspectorName("서서히")] Fade,
    }

    public enum VfxHold
    {
        [InspectorName("그대로")] Still,
        [InspectorName("일렁임")] Wobble,
        [InspectorName("회전")] Spin,
        [InspectorName("맥동")] Pulse,
        // 예정: Follow (캐릭터 따라가기)
    }

    public enum VfxVanish
    {
        [InspectorName("끝부터 갉아먹기")] ErodeFromEnd,
        [InspectorName("가운데 구멍")] Hole,
        [InspectorName("얇아지며")] Thin,
        [InspectorName("줄어들며")] Shrink,
        [InspectorName("위로 흩어짐")] ScatterUp,
        [InspectorName("색 식으며")] CoolDown,
        [InspectorName("꼬리가 따라오며")] TailChase,
        [InspectorName("퍼지며 사라짐")] ExpandFade,
    }

    public enum VfxBuiltinMask
    {
        SlashStrip,
        Radial,
        Flame,
        Star,
        Ring,
        Spike,
        Band,
        Rock,
        Smoke,
        PillarFade,
        FlameTongue,    // 세 갈래 카툰 불꽃
        Snowflake,      // 눈송이
        Skull,          // 해골
        ArrowDown,      // 아래 화살표
        Star5,          // 오각별
        Droplet,        // 물방울·핏방울 (뾰족한 쪽이 위)
        TrailFade,      // 잔상 줄 — 폭 가운데가 밝고, 머리(위) 쪽이 밝고 꼬리(아래)로 흐려짐
    }

    public enum VfxBurstShape
    {
        [InspectorName("사방 (구)")] Sphere,
        [InspectorName("위쪽 반구")] Up,
        [InspectorName("앞쪽 원뿔")] Forward,
        [InspectorName("수평 원")] Flat,
    }

    public enum VfxBurstFacing
    {
        [InspectorName("카메라를 봄")] Billboard,
        [InspectorName("날아가는 방향으로 늘어남 (가시)")] Velocity,
    }

    /// <summary>VFX Studio 미리보기에서 이펙트를 놓는 기준점 (게임 재생 위치와는 무관)</summary>
    public enum VfxPreviewAnchor
    {
        [InspectorName("몸 (가슴 높이)")] Chest,
        [InspectorName("발밑")] Feet,
        [InspectorName("앞 (타격 지점)")] Front,
    }

    public enum VfxOwnerFacing
    {
        [InspectorName("레이어 설정 그대로")] Keep,
        [InspectorName("카메라 향함 켜기")] On,
        [InspectorName("카메라 향함 끄기")] Off,
    }

    public enum VfxLayerStyle
    {
        [InspectorName("카툰 (단색·외곽선)")] Toon,
        [InspectorName("빛 (발광·부드러움)")] Glow,
    }
}
