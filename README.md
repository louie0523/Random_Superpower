# (게임 이름) — 랜덤 초능력자 배틀로얄

> 죽을 때마다 초능력이 바뀌는 1인칭 카툰풍 멀티플레이 배틀로얄  
> Unity 6.3 (URP) · Photon Fusion 2 (Shared Mode) · 1인 개발

<img width="1672" height="941" alt="image" src="https://github.com/user-attachments/assets/5fb2760a-f84e-4fcc-8d59-adff66ad9678" />


---

## 한 줄 소개
**소울(Soul)은 돈이자 승리 조건입니다.**  
적을 처치해 소울을 모으고, 그 소울로 상점에서 강해질지 · 끝까지 모아 승리할지 계속 고민하게 만드는 파티형 배틀로얄입니다.  
죽으면 랜덤 능력이 바뀌기 때문에 매 생명마다 다른 플레이가 나옵니다.

## 핵심 재미
- **소울 = 화폐 + 승리 조건** — 쓰면 강해지고, 모으면 이긴다
- **3중 능력 구조** — 라운드 시작 때 고르는 메인 능력(클래스) + 서브 능력 + 죽을 때마다 바뀌는 랜덤 능력
- **엉뚱한 랜덤 능력** — 상대를 껴안고 맵 밖으로 돌진하는 「미래로 가자!」, 맞힌 사람을 동지로 만드는 「탈모빔」, 30번 외우면 용이 되어 3분 버티면 즉시 승리하는 「전설의 용언」, 100m 상공에서 지구를 던지는 「지구 던지기」 등
- **변신** — 물고기 → 용, 체스의 거신병(폰 6개 → 프로모션) 등 모습 · 스탯 · 공격 · 스킬이 통째로 바뀜
- **낙사 · 밀치기 킬** — 넉백 후 낙사하면 민 사람의 처치로 인정

## 주요 시스템
| 분류 | 내용 |
|---|---|
| 전투 | 근접 콤보 · 활/지팡이 차징 · 총(히트스캔/투사체 · 도탄) · 공속/사거리 버프 · 히트스톱 |
| 능력 | 메인/서브/랜덤 능력, 패시브 · 스킬 · 궁극기 개별 레벨업, 스킬 변형(칸 바꿔 끼우기), 플레이어 선택형 스킬 |
| 상태이상 | 둔화 · 화상 · 동상 · 봉인 · 기절 · 중독 · 고스트 · 암전 · 투명 · 언데드 · 저주 · 약화 등 |
| 장비 | 등급(C~S) · 인챈트(구간별 테이블 · 특수 인챈트) · 무기 진화 · 상자 · 상점 |
| 소통 | 텍스트 채팅(경기/팀) · 보이스 채팅(Photon Voice) · 글로벌 알림 메시지 |
| 라운드 | 클래스 선택 → 전투 → 목표 소울 달성 시 승리 → 전부 초기화 후 다음 라운드 |

## 직접 만든 개발 도구 (Unity Editor)
- **Motion Setup** — 1인칭 팔 · 손가락 · 무기 · 몸통 키프레임 모션 편집기 (타임라인, 프리셋, 떨림 · 둥둥 구간, 판정 범위 미리보기, 이벤트 트랙, 무기 파츠)
- **Hand Rig** — 손가락 IK · 그립 자세 편집
- **Rig Animator** — 소환물 · 탈것 · 변신 모델의 본 정리 · IK · 상태 애니메이션
- **VFX Studio** — 카툰풍 스킬 이펙트 제작 툴
- **AlterClipAnimator** — Animator Controller 없이 클립(Mixamo 등)을 상태 이름으로 재생 · 타격 프레임 동기화

## 기술 스택
- Unity 6.3.21f1 · URP · Shader Graph 툰 셰이더
- Photon Fusion 2 — Shared Mode (각 플레이어가 자기 캐릭터의 권한자)
- Photon Voice 2
- Unity Localization (Google Sheets 연동, 다국어 예정)

## 실행 방법
1. Unity **6.3.21f1**로 프로젝트 열기
2. **Photon App ID 발급 후 넣기** (저장소에는 App ID가 들어 있지 않습니다)
   - [Photon Dashboard](https://dashboard.photonengine.com/)에서 Fusion 앱 생성 → App ID 복사
   - Unity: `Tools → Fusion → Fusion Hub` (또는 `PhotonAppSettings` 에셋)에 붙여넣기
   - 보이스를 쓰려면 Voice App ID도 같은 방법으로
3. `Assets/(씬 경로)/(메인 씬).unity` 열고 Play
4. 여러 명 테스트: `Window → Multiplayer → Multiplayer Play Mode`로 플레이어 추가, 또는 빌드 + 에디터

## 조작
| 키 | 동작 |
|---|---|
| WASD · Space | 이동 · 점프 |
| 마우스 왼쪽 | 기본 공격 (원거리: 누르고 떼기) |
| Q · E · R | 스킬 · 스킬 · 궁극기 |
| F (V) | 랜덤 능력 스킬 (조커는 V까지) |
| 1~7 · 휠 | 무기 바꾸기 |
| G | 무기 버리기 |
| Enter · Tab | 채팅 · 채팅 채널 |
| ` 또는 F8 | 개발자 콘솔 (테스트용) |

## 폴더 구조
```
Assets/
 ├ Scripts/        게임 코드 (플레이어 · 능력 · 전투 · UI · 네트워크)
 ├ Editor/         개발 도구 (Motion Setup · Rig Animator · 능력 편집기 등)
 ├ Data/           능력 · 무기 · 인챈트 · 상점 데이터 (ScriptableObject)
 └ ...
```
<!-- 실제 폴더에 맞게 고치기 -->

## 사용한 외부 리소스
| 리소스 | 라이선스 |
|---|---|
| KayKit Character Pack (Kay Lousberg) | CC0 |
| Photon Fusion 2 · Photon Voice 2 | Photon SDK 라이선스 |
| Unity Toon Shader (참고) | Unity 공식 무료 패키지 |
| (Mixamo 애니메이션 등 — 쓴 것 적기) | (각 라이선스) |

## 개발자
- 정루이 — 기획 · 프로그래밍 · 툴 개발 (1인 개발)
- 포트폴리오: https://louie0523.github.io
- 연락: louie07230723@gmail.com

## 라이선스
© 2026 정루이. All Rights Reserved.  
이 저장소는 **심사 · 열람 목적으로만 공개**합니다. 코드 · 리소스의 복제, 수정, 재배포, 상업적 이용을 허락하지 않습니다.  
(외부 리소스는 각 원저작자의 라이선스를 따릅니다.)
