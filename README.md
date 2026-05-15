# JBRogLike — 아키텍처 보고서

> 작성 기준일: 2026-05-15  
> 엔진: Unity 2D (Tilemap)  
> 언어: C# (.NET)  
> 현재 브랜치: master

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [레이어 아키텍처](#2-레이어-아키텍처)
3. [파일 구조](#3-파일-구조)
4. [시스템 1 — 던전 생성](#4-시스템-1--던전-생성)
5. [시스템 2 — 이벤트 버스](#5-시스템-2--이벤트-버스)
6. [시스템 3 — 플레이어 이동](#6-시스템-3--플레이어-이동)
7. [시스템 4 — 전투](#7-시스템-4--전투)
8. [시스템 5 — 적 AI](#8-시스템-5--적-ai)
9. [시스템 6 — 방 스폰 및 클리어](#9-시스템-6--방-스폰-및-클리어)
10. [시스템 7 — UI 및 스킬 미리보기](#10-시스템-7--ui-및-스킬-미리보기)
11. [시스템 8 — 렌더링 및 로딩](#11-시스템-8--렌더링-및-로딩)
12. [성능 전략](#12-성능-전략)
13. [데이터 흐름](#13-데이터-흐름)
14. [확장 포인트](#14-확장-포인트)
15. [개발 현황](#15-개발-현황)

---

## 1. 프로젝트 개요

**JBRogLike**는 Unity 2D Tilemap 기반의 절차적 생성 로그라이크 게임입니다.

| 항목 | 내용 |
|------|------|
| 장르 | 로그라이크 던전 탐색 |
| 시점 | 탑다운 2D |
| 맵 방식 | BSP 알고리즘 절차적 생성 |
| 이동 방식 | 실시간 8방향 이동 + 그리드 충돌 + 대시 스킬 |
| 조준 방식 | 8방향 입력 기반 (`AimDirectionUtility`) — 스킬 / 투사체 / 대시 공통 |
| 전투 방식 | 실시간, 패턴 기반 범위 공격 + 스킬 4슬롯 (InstantArea / Projectile / Dash) + 스킬 castDelay·recoveryDelay 중 이동 잠금 |
| 플레이어 상태이상 | 적 공격에서 받는 넉백·슬로우·스턴 (`ApplyEnemyCombatImpact` 단일 진입점, `EnemyAttackImpactData`) |
| 방 타입 | Normal · MonsterDen · Spawn · Stair |
| 적 AI | FSM (Idle → Chase → Attack), A* 경로탐색, Contact/Ranged 행동 분기, Contact Special Attack(Rush/Jump), `isStationary`/`immuneToKnockback` 플래그 |
| 적 전투 | 근접 접촉 피해 + Contact Special(Rush 돌진 / Jump 도약 + 착지 임팩트) + 원거리 투사체 (Single/Burst/Spread/Circle) + 벽 반사 — Rush/Jump/Projectile 은 `EnemyAttackImpactData`(knockback·slow·stun) 적용 |
| 시야 | Fog of War (Bresenham 시야 차단, 미탐사/탐사/현재시야 3단계) |
| 진행 방식 | 계단을 통한 층 이동 (무한 층 구조) |

---

## 2. 레이어 아키텍처

전체 시스템은 **Clean Architecture** 원칙에 따라 4개 레이어로 분리되어 있습니다.

```
┌──────────────────────────────────────────────────────────────┐
│  Application Layer (MonoBehaviour)                           │
│  PlayerController · PlayerInputReader                        │
│  PlayerCombatController · PlayerDashController               │
│  PlayerAnimationController                                   │
│  DungeonManager · FloorTransitionService                     │
│  EnemyBrain · NormalEnemyBrain · RoomSpawner                 │
│  ProjectilePool · ProjectileController                       │
│  FogOfWarController                                          │
│  GameOverFlowController · GameOverSceneReloadRestartHandler  │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure Layer (ScriptableObject Event Bus)           │
│  DungeonEventChannel · CombatEventChannel                    │
├──────────────────────────────────────────────────────────────┤
│  Domain / Pure Service Layer (순수 C# — Unity 의존 없음)      │
│  DungeonData · DungeonGenerator · RoomRegistry               │
│  DungeonQueryService · SpawnPositionService                  │
│  WeaponData · SkillData · EnemyData                          │
│  PlayerResource · AttackPattern · AStarPathfinder            │
│  SkillExecutor · SkillTargetResolver · SkillExecutionContext │
│  SkillSlotRuntime · SkillCooldownController                  │
│  ProjectileFireService · ProjectileFireRequest               │
│  AimDirectionUtility · CombatLayers · CharacterPhysicsSetup  │
│  MovementBlockerQuery · DeterministicSeedUtility · PerfStage │
├──────────────────────────────────────────────────────────────┤
│  Presentation Layer                                          │
│  DungeonTilemapRenderer · DoorController                     │
│  EnemyHealthBar · PlayerStatusBarUI                          │
│  SkillSlotUI · SkillUIManager · SkillRangePreviewer          │
│  PlayerStatusEffectUI · StatusEffectIconView                 │
│  HitFlashFeedback · PlayerInvincibilityFlashFeedback         │
│  EnemyAnimationController · FogVisibilityRenderer            │
│  GameOverUIController                                        │
└──────────────────────────────────────────────────────────────┘
```

### 핵심 설계 원칙

- **단방향 의존**: 상위 레이어만 하위 레이어를 알고, 역방향 참조 없음
- **이벤트 기반 통신**: 레이어 간 직접 참조 대신 ScriptableObject EventChannel 사용
- **데이터 주입 (ScriptableObject)**: 무기/스킬/적의 수치는 에셋으로 분리, 코드 수정 없이 교체 가능
- **FSM 분리**: EnemyBrain의 상태·이동·타겟·액션을 Handler로 분리해 결합도 최소화
- **책임 분리 (SRP)**: DungeonManager의 기능을 서비스 클래스로 추출 (FloorTransitionService, SpawnPositionService, DungeonQueryService)
- **스킬 실행 라우팅**: SkillData.executionType → SkillExecutor 가 InstantArea / Projectile / Dash 분기로 라우팅, MonoBehaviour와 분리된 순수 서비스 계층
- **공유 타겟 해석**: SkillTargetResolver가 미리보기·기본 공격·스킬 모두에 동일한 셀 계산 제공
- **공유 투사체 발사**: ProjectileFireService가 적 원거리·플레이어 스킬 모두에 동일한 패턴(Single/Burst/Spread/Circle) 처리
- **공유 8방향 조준**: AimDirectionUtility가 입력 → 8방향 raw / 정규화 / 그리드 카디널 변환을 단일 책임으로 처리 (스킬·투사체·대시·미리보기 공용)
- **적 공격 임팩트 통합**: `EnemyAttackImpactData`(knockback·slow·stun) 구조로 Rush·Jump·Projectile 의 부가 효과를 동일하게 관리, `PlayerCombatController.ApplyEnemyCombatImpact()` 단일 진입점으로 데미지·넉백·슬로우·스턴 적용
- **런타임 탐색 캐싱**: `DungeonManager` 가 `RoomSpawner` 참조를 SerializeField + 1회 경고로 캐싱, 매 `FindAnyObjectByType` 호출 회피 (다른 컨트롤러도 동일 패턴 사용)
- **GC 최소화**: 이벤트 인자에 `struct` 사용, 코루틴 캐싱, NonAlloc 물리, A* 버퍼 재사용, 스킬 슬롯 / 투사체 / 시야 셀 버퍼 재사용

---

## 3. 파일 구조

```
Assets/Scripts/
│
├── PlayerController.cs             # 입력·이동·방 감지·대시 중 이동 위임
├── PlayerInputReader.cs            # 키보드 입력 단일 집계 (실행 순서 제어)
├── PlayerAnimationController.cs    # 4방향 이동 애니메이션 (MoveX/Y, LastMoveX/Y)
│
├── DungeonManager.cs               # 던전 생애주기 조율 (Facade)
├── DoorController.cs               # 문 열기 위임 (DungeonManager로 라우팅)
│
├── Data/
│   ├── DungeonData.cs              # 타일 그리드 + 방 목록 (Domain)
│   ├── WeaponData.cs               # 무기 ScriptableObject
│   ├── SkillData.cs                # 스킬 ScriptableObject (executionType + Projectile/Dash 필드)
│   ├── SkillExecutionType.cs       # 스킬 실행 라우팅 enum (InstantArea/Projectile/Dash/AreaOverTime/Buff)
│   ├── ProjectileTargetHitMode.cs  # 타깃 적중 정책 enum (DestroyOnHit/Pierce/HitOncePerTarget)
│   └── EnemyData.cs                # 적 ScriptableObject — Contact(+Special Rush/Jump) / Ranged + 투사체 패턴
│                                    #   (EnemySpecialAttackType: None/Rush/Jump + 전용 파라미터 그룹)
│                                    #   (EnemyAttackImpactData struct: knockback/slow/stun — rushImpact/jumpImpact/projectileImpact 공용)
│                                    #   (isStationary: AI 이동/분리/넉백 위치 변화 정지 + Rigidbody FreezeAll, immuneToKnockback: 데미지·상태이상은 적용되나 임펄스만 무시)
│
├── Generate/
│   ├── DungeonGenerator.cs         # BSP + Prim MST 생성 알고리즘 (순수 C#)
│   ├── DungeonTypes.cs             # 공유 타입 (RoomType, RoomInfo+StableRoomKey, DungeonTypeId, DeterministicSeedUtility, 이벤트 인자)
│   ├── DungeonEventChannel.cs      # 던전 이벤트 버스 (ScriptableObject)
│   ├── DungeonQueryService.cs      # 그리드 유틸리티 (IsWalkable, 좌표 변환)
│   ├── SpawnPositionService.cs     # 플레이어 스폰 좌표 계산 서비스
│   ├── FloorTransitionService.cs   # 층 이동 코루틴·로딩 화면·GC 관리
│   ├── RoomRegistry.cs             # 방 상태 관리 (타입·문 닫힘)
│   ├── DungeonTilemapRenderer.cs   # Tilemap 3레이어 배치 (바닥·벽·문)
│   ├── FogOfWarController.cs       # 안개 시야 — Bresenham LoS, 미탐사/탐사/현재시야 3상태
│   ├── RoomFootprintSampler.cs     # 방 overlap 검증용 공통 9-sample 유틸 (PlayerController·DungeonTilemapRenderer 공용)
│   ├── SpawnRegion.cs              # 스폰 지역 플래그 (Dungeon/Forest/Castle)
│   └── RoomSpawner.cs              # 방 진입 시 적 스폰, 방 클리어 감지
│
├── Combat/
│   ├── IDamageable.cs              # 피해 수신 인터페이스
│   ├── AttackPattern.cs            # 공격 패턴 enum + 좌표 계산기 (FillTargets API)
│   ├── AttackExecutor.cs           # 공격 판정·히트 감지·데미지 적용
│   ├── AimDirectionUtility.cs      # 8방향 입력 양자화 + raw/정규화/카디널 변환 (Domain)
│   ├── CombatLayers.cs             # Enemy/Player Layer 캐싱 + ContactFilter2D 공유
│   ├── CharacterPhysicsSetup.cs    # Rigidbody2D + CircleCollider2D 공통 셋업 (Player·Enemy 공유, NoFriction 머터리얼 캐시, 기존 CircleCollider 보존)
│   ├── MovementBlockerQuery.cs     # Player 이동/대시가 `EnemyData.blocksMovement=true` 적과 겹치는지 판정 (Collider2D→EnemyController 캐시)
│   ├── PlayerCombatController.cs   # 플레이어 전투 진입점 (HP·MP·공격·스킬·무적시간·8방향 조준·castDelay/recoveryDelay 잠금)
│   │                               #   + ApplyEnemyCombatImpact(damage, hitDir, knockback, slow, stun) 단일 진입점
│   │                               #   + 슬로우(_enemySlows 강도 최대값) / 스턴(_stunTimer) / 넉백(EnemyKnockbackRoutine → playerMovement.TryApplyExternalDisplacement)
│   │                               #   + IsSlowed/IsStunned/MoveSpeedMultiplier · OnStatusEffectApplied/Ended(PlayerStatusEffectType)
│   ├── PlayerStatusEffectType.cs   # 플레이어 상태이상 enum (Slow, Stun)
│   ├── PlayerResource.cs           # HP·MP 상태 컨테이너 (Domain)
│   ├── PlayerDashController.cs     # 대시 코루틴 — 발자국 검사·외부 무적·path/contact 데미지 분리
│   ├── SkillExecutor.cs            # 스킬 실행 라우팅 (InstantArea/Projectile/Dash 분기)
│   ├── SkillTargetResolver.cs      # 스킬 셀·미리보기 반경·투사체 거리 공통 계산
│   ├── SkillExecutionContext.cs    # 스킬 1회 사용에 필요한 런타임 정보 컨테이너
│   ├── SkillSlotRuntime.cs         # 스킬 슬롯 1칸의 SkillData·쿨다운 상태 (MonoBehaviour 미의존)
│   ├── SkillCooldownController.cs  # 기본 공격 쿨다운만 담당 (스킬 쿨다운은 슬롯 런타임이 보유)
│   ├── ProjectileFireService.cs    # 투사체 발사 패턴 처리 (Single/Burst/Spread/Circle)
│   ├── ProjectileFireRequest.cs    # 투사체 1회 발사 파라미터 (적·플레이어 공용)
│   ├── ProjectileController.cs     # 풀링 발사체 — 벽 반사·관통·파괴, 맵 범위 밖 자동 release, Fog 가시성, 회전 모드 (KeepPrefab/FaceMoveDirection)
│   ├── ProjectilePool.cs           # 투사체 사전 풀링 (SetActive/DisableComponents 모드) — ReleaseAllActiveProjectiles로 층 이동 시 일괄 회수
│   ├── Projectile.cs               # (구) 트리거 기반 발사체 — 호환 유지용
│   ├── HitFlashFeedback.cs         # 피격 시 SpriteRenderer 색상 점멸 (적·플레이어 공용)
│   ├── PlayerInvincibilityFlashFeedback.cs # 무적 시 셰이더 _FlashAmount 보간 (PropertyBlock)
│   └── CombatEventChannel.cs       # 전투 이벤트 버스 (ScriptableObject)
│
├── Visual/
│   └── FogVisibilityRenderer.cs    # FogOfWar visible 상태에 따라 Renderer.enabled 토글 (적·적 투사체 공용)
│
├── Enemy/
│   ├── EnemyController.cs          # 적 HP·피해·사망·상태이상·넉백 벽 클램핑 (Die 시 EnemyBrain.HandleDeathStarted 호출)
│   ├── EnemyBrain.cs               # FSM 조율 추상 + MovementHandler/TargetHandler/ActionHandler
│   │                               #   + EnemySpecialAnimationType(Charge/Rush/Jump/Land) 트리거 라우팅
│   │                               #   + LockSpecialFacing/UnlockSpecialFacing/HandleDeathStarted
│   │                               #   (상태 인스턴스는 EnemyStates.cs에 정의, BossEnemyBrain은 CreateState 오버라이드)
│   ├── NormalEnemyBrain.cs         # 기본 몬스터용 경량 Brain (커스텀 상태 없음)
│   ├── NormalEnemyAI.cs            # [Obsolete] NormalEnemyBrain을 상속만 하는 호환 래퍼 (기존 프리팹 유지용)
│   ├── EnemyStates.cs              # IdleState · ChaseState · AttackState (internal sealed, A* 추격 포함)
│   ├── EnemyMovementHandler.cs     # A* 이동 + 군중 분리 + Ranged 이동 분기 (Chase/Kiting/Random)
│   ├── EnemyTargetHandler.cs       # 플레이어 감지·시야 갱신
│   ├── EnemyActionHandler.cs       # Contact/Ranged 행동 사이클·쿨다운
│   │                               #   + Contact Special Attack 상태머신 (Windup→Rush/Jump→Recovery)
│   │                               #   + Rush 경로 데미지(1회 제한 HashSet) / Jump 착지 임팩트
│   ├── AStarPathfinder.cs          # GC 최소화 A* 탐색기
│   ├── EnemyHealthBar.cs           # 머리 위 체력바 렌더러
│   ├── EnemyAnimationController.cs # 적 이동/공격/사망 애니메이션 + 사격 방향 페이싱
│   │                               #   + Charge/Rush/Jump/Land 트리거 + LockFacing/UnlockFacing (Special 중 페이싱 고정)
│   └── EnemyPoolManager.cs         # 적 오브젝트 풀
│
├── UI/
│   ├── PlayerStatusBarUI.cs        # 플레이어 HP·MP 상태바 (슬라이더 + 텍스트)
│   ├── PlayerStatusEffectUI.cs     # 슬로우/스턴 아이콘 컨테이너 — PlayerCombatController.OnStatusEffectApplied/Ended 구독, RefreshActiveIcons 매 프레임
│   ├── StatusEffectIconView.cs     # 슬롯 1칸 아이콘 뷰 (icon · fill · 남은시간 텍스트)
│   ├── SkillSlotUI.cs              # 스킬 슬롯 1개 렌더링 (아이콘·쿨타임)
│   ├── SkillUIManager.cs           # 4슬롯 초기화·층 변경 갱신
│   ├── SkillRangePreviewer.cs      # Q/W/E/R 미리보기 — InstantArea/Projectile/Dash + 기본공격 홀드
│   ├── GameOverFlowController.cs   # 사망 이벤트 구독 → 지연 후 게임오버 UI 표시
│   ├── GameOverUIController.cs     # 게임오버 UI 페이드 인/아웃·확인 버튼 (UI 참조 누락 시 1회 경고 후 표시 skip)
│   ├── GameOverRestartHandler.cs   # IGameOverRestartHandler 인터페이스
│   └── GameOverSceneReloadRestartHandler.cs # 활성 씬 재로드로 재시작
│
├── Debug/
│   └── RuntimePerfTraceLogger.cs   # 투사체/풀 호출 마이크로 타이밍 트레이스
│
└── Tool/
    ├── RuntimePerfLogger.cs        # 성능 타이밍 로거 (호환 레이어)
    ├── PerfStage.cs                # using-scope 단일 elapsedMs stage 측정 — IsActive false일 때 zero-alloc 패스스루
    ├── YieldCache.cs               # 코루틴 YieldInstruction 캐시
    └── LoadingScreenController.cs  # 층 이동 로딩 화면
```

```
Assets/Editor/                     # Editor-only (런타임 미포함)
├── SkillDataEditor.cs              # SkillData CustomEditor — Basic/InstantArea/Projectile/Dash 섹션 + Reserved foldout + 음수·non-positive 경고
└── EnemyDataEditor.cs              # EnemyData CustomEditor — Basic / Contact + Contact-Special(Rush/Jump 전용 그룹) 또는 (Ranged-Timing + Ranged-Movement + Ranged-Projectile) / Separation-Collision / Reward-Misc / Unhandled 섹션 분기 + 미사용 필드 자동 분리
```

```
Tools/DungeonGenDebug/              # Unity 외부 standalone .NET 콘솔 (DungeonGenerator 검증용)
└── Program.cs                      # seed/floor 별 던전을 그려 corridor carving·MST 연결 디버그 출력
                                    #   --scene-settings 플래그로 실제 씬 설정(120×80, room 10–50) 시뮬레이션
                                    #   RoomPerimeterCorridorScan / CornerDoorwayScan 으로 통로 위반 검출
                                    #   (Unity 측에서 DungeonGenerator.DebugSink + DebugCorridorCarving=true 로 동일 로그 활성 가능)
```

---

## 4. 시스템 1 — 던전 생성

### 4-1. 전체 파이프라인 (7단계)

`DungeonManager.Generate()` 호출 시 다음 순서로 실행됩니다.

```
① BuildSettings       설정 구성 (맵 크기, BSP 깊이, 시드 파생)
        ↓
② GenerateDungeon     그리드 + 방 목록 생성 (DungeonGenerator — 순수 C#)
        ↓
③ BuildRoomInfos      RoomRect → RoomInfo 배열 변환
        ↓
④ DungeonData 생성    그리드 + 방 목록을 Domain 객체로 포장
        ↓
⑤ RoomRegistry.Init   방 타입 감지 (STAIR_UP 포함 여부로 Stair 자동 분류)
        ↓
⑥ ComputeSpawnPos     맵 중앙에 가장 가까운 방 내부 타일 → 캐싱 (O(1) 조회)
                      (SpawnPositionService에 위임)
        ↓
⑦ PlaceTiles          DungeonData → Tilemap 타일 배치 (청크 분할 선택 가능)
```

### 4-2. BSP 공간 분할 알고리즘

**목적**: 맵을 균등한 영역으로 나눠 방들이 겹치거나 치우치지 않도록 배치

```
BspSplit(node, depth):
  if depth >= maxDepth → 종료 (리프 노드)

  if 가로가 훨씬 길면 → 수직 분할
  if 세로가 훨씬 길면 → 수평 분할
  else                  → 50% 확률로 선택

  분할 위치 = [minRoomSize + padding ... 영역 끝 - minRoomSize - padding] 범위에서 랜덤
  좌/우(또는 상/하) 자식 노드 생성 → 재귀 호출
```

```
예시 (BspDepth=4, 80×50):

  전체 맵 80×50
  ├── 좌 40×50
  │   ├── 좌상 40×25 → [방 A]
  │   └── 좌하 40×25 → [방 B]
  └── 우 40×50
      ├── 우상 40×25 → [방 C]
      └── 우하 40×25 → [방 D]
```

### 4-3. 결정론적 시드 파생

같은 시드라도 층마다 다른 지형을 생성합니다.

```csharp
// DungeonSettings.DeriveSeed()
int mixed = (seed ^ (floor * 2654435761u)) * 2246822519u;
return mixed & 0x7FFFFFFF;
```

| 조건 | 결과 |
|------|------|
| 같은 시드 + 같은 층 | 항상 동일한 지형 (재현 가능) |
| 같은 시드 + 다른 층 | 다른 지형 |
| 다른 시드 + 같은 층 | 다른 지형 |

> 방별 적 스폰은 별도의 결정론 경로(`DeterministicSeedUtility.CreateSeed(globalSeed, dungeonType, floor, RoomInfo.StableRoomKey, "enemy_spawn")`)를 사용합니다. `DungeonManager.dungeonType`(`DungeonTypeId`) 으로 같은 시드라도 던전 종류별 스폰 RNG 를 분리할 수 있습니다. 자세한 내용은 [9-1-2. 결정론적 방 스폰 시드](#9-1-2-결정론적-방-스폰-시드-deterministicseedutility) 참조.

### 4-4. 방 연결 알고리즘 (Prim's MST + EXTRA 다중 후보 점수화)

```
ConnectAll():
  connected = { 방0 }
  remaining = { 방1, 방2, ... }

  ── 1단계: MST (isMandatoryEdge=true) ────────────────────
  while remaining이 비지 않을 때:
    connected × remaining 쌍 중 유클리드 거리 최소 → src, dst
    DrawLCorridor(src, dst, mandatory=true, pathBuf)  ← L자형 통로 연결
    connectedPairs.Add((src,dst)) / connected ← dst / remaining ← dst 제거

ConnectExtraCorridors():
  ── 2단계: EXTRA (ExtraConnProb / ExtraCandidateCount) ───
  for attemptIndex in [0 .. roomCount-1]:
    if rng.NextDouble() >= ExtraConnProb → skip attempt
    for 모든 (i,j) 미연결 방 pair:
      pairCandidates = BuildExtraPathCandidatesForPair(...)   ← 최대 ExtraCandidateCount개
        각 후보마다 primary/alternate axis L-path를 emit + 검증
        (interior/perim/perim+1, perimeter-corridor, corner-doorway 충돌 모두 제외)
      score = corridorOverlap * ExtraOverlapScoreWeight
            - pathLength      * ExtraPathLengthPenaltyWeight
            - centerDistanceSq / ExtraCenterDistancePenaltyDivisor
      pairBestCandidates.Add(가장 점수 높은 후보 1개)
    그 attempt에서 가장 점수 좋은 1쌍만 carve, DrawLCorridor(..., mandatory=false)
    carve 성공 시에만 connectedPairs 갱신 (skip 시 connectedPairs 보존)
```

- `DungeonSettings.ExtraCandidateCount` (기본 12): 한 방 pair마다 점수화할 EXTRA 후보 개수
- `DungeonSettings.ExtraConnProb` (기본 0.5): MST 완료 후 각 EXTRA attempt에서 통로 생성을 시도할 확률 (※ 의미가 "두 번째로 가까운 방 추가 연결 확률"에서 attempt 단위 시도 확률로 변경됨)
- `DungeonSettings.ExtraOverlapScoreWeight` (기본 20): 기존 corridor와 겹치는 cell 1개당 후보 점수 보너스 (= 통로 재사용 권장)
- `DungeonSettings.ExtraPathLengthPenaltyWeight` (기본 8): 후보 path cell 1개당 점수 감점
- `DungeonSettings.ExtraCenterDistancePenaltyDivisor` (기본 20): 두 방 중심 거리 제곱 감점의 divisor — 클수록 거리 감점이 약해짐
- `DrawLCorridor`는 `bool`을 반환해 EXTRA가 skip되면 호출자가 `connectedPairs`를 갱신하지 않음 (잘못된 logical 연결 상태 방지)
- 과거에 사용하던 `LongestParallelCorridorRun` 점수 항목은 제거되었습니다 (overlap·length·centerDistance 3축으로 단순화).

### 4-4-1. Corridor Carving 검증 (DrawLCorridor)

`DrawLCorridor`는 L자형 2-segment 통로를 grid에 직접 그리지 않고, 한 번 path 후보를 cell list(`pathBuf`)에 emit해 검증 → carve 순서로 동작합니다.

```
DrawLCorridor(src, dst, isMandatoryEdge, pathBuf) → bool:
  primaryHorizFirst = |dx| >= |dy|

  1) primary axis 로 path 후보 cell 미리 emit
  2) src/dst 가 아닌 다른 방의 interior / perim(=0) / perim+1(벽 옆 1칸) 과 겹치는지 검사
  3) EXTRA(optional)에 한해 추가 검증:
       PathCarvesRoomPerimeter — 방 perimeter 위의 ROOM 셀이 corridor 로 carving 되는지
       PathUsesRoomCornerDoorway — 두 방의 모서리(코너) doorway 를 통로가 횡단하는지
       하나라도 true → 후보 부적합
  4) 겹치면 alternate axis 로 1회 재시도
  5) 둘 다 충돌:
       isMandatoryEdge == true  → connectivity 보장 위해 primary 강제 carve, return true
       isMandatoryEdge == false → 그냥 skip, return false (EXTRA 연결은 포기 가능)
  6) src/dst side 축 범위가 겹치면 동일 door 축을 재사용 (ClampDoorAxis 로 방 범위에 정렬),
     겹치지 않을 때만 기존 MinStraight 보정 적용

재사용 버퍼: pathBuf (List<(int,int)>) — 통로 1회당 0 할당
디버그 hook : DungeonGenerator.DebugCorridorCarving = true + DebugSink 구독 시
              MST/EXTRA 통로마다 src/dst Rect, path 결정 사유, before/after connect-state 스냅샷 로그
              DebugConnectState — connected / remaining / reachable(R0 BFS) 집합 비교로 logical-only / grid-only 불일치 검출
```

### 4-5. 타일 타입 상수

| 값 | 상수 | 의미 |
|----|------|------|
| 0 | EMPTY | 이동 불가 (벽/빈 공간) |
| 1 | ROOM | 방 바닥 |
| 2 | CORRIDOR | 통로 |
| 3 | STAIR_UP | 올라가는 계단 |
| 5 | DOOR_CLOSED | 닫힌 문 |

---

## 5. 시스템 2 — 이벤트 버스

ScriptableObject를 이벤트 버스로 사용합니다. 발행자와 구독자가 서로의 존재를 모릅니다.

### DungeonEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnRoomEntered` | PlayerController | RoomSpawner, FogOfWarController |
| `OnNormalRoomEntered` | PlayerController | — (미사용, 예약) |
| `OnSpawnRoomEntered` | PlayerController | — |
| `OnStairRoomEntered` | PlayerController | — |
| `OnFloorChanged` | DungeonManager | PlayerController, RoomSpawner, SkillUIManager, FogOfWarController |
| `OnRoomDoorsClosed(RoomInfo)` | DungeonManager | FogOfWarController |
| `OnRoomDoorsOpened(RoomInfo)` | DungeonManager | FogOfWarController |

> **참고**: `DoorController`는 이벤트를 구독하지 않습니다. 문 개폐는 `RoomSpawner` → `DungeonManager.CloseCurrentRoomDoors / OpenCurrentRoomDoors`로 직접 호출됩니다. 다만 DungeonManager는 실제 문 상태 전환이 발생한 직후 `OnRoomDoorsClosed` / `OnRoomDoorsOpened`를 발행해, `closedDoorsBlockVision`을 사용하는 FogOfWarController가 즉시 시야를 재계산할 수 있도록 합니다.

### CombatEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnEnemyKilled(EnemyController)` | EnemyController | RoomSpawner (방 클리어 판정) |
| `OnPlayerHpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnPlayerMpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnPlayerDied(PlayerCombatController)` | PlayerCombatController | GameOverFlowController |
| `OnSkillUsed(SkillData)` | PlayerCombatController | SkillSlotUI (쿨다운 표시) |

> 플레이어 상태이상(슬로우/스턴) 알림은 채널이 아닌 `PlayerCombatController` 직접 이벤트로 발행됩니다.
>
> | 이벤트 | 발행자 | 구독자 |
> |--------|--------|--------|
> | `OnStatusEffectApplied(PlayerStatusEffectType)` | PlayerCombatController | PlayerStatusEffectUI |
> | `OnStatusEffectEnded(PlayerStatusEffectType)` | PlayerCombatController | PlayerStatusEffectUI |
>
> UI는 `PlayerCombatController.Active` 정적 참조로 1회 바인딩 후 enable 토글 시 자동 재바인딩(`TryBindCombat`)합니다.

---

## 6. 시스템 3 — 플레이어 이동

### 6-1. 물리 설정 (CharacterPhysicsSetup)

`PlayerController.Start()` / `EnemyController.Awake()`가 모두 `CharacterPhysicsSetup.Configure(go, layerName)` 헬퍼를 호출해 Rigidbody2D + CircleCollider2D 를 동일한 규약으로 자동 셋업합니다 (NoFriction `PhysicsMaterial2D`는 static 캐시 1개를 공유).

| 컴포넌트 | 설정값 |
|---------|-------|
| Rigidbody2D | Dynamic · gravityScale=0 · freezeRotation · Continuous · Interpolate · NoFriction sharedMaterial |
| CircleCollider2D | radius=0.32 · isTrigger=false · NoFriction sharedMaterial |
| 기타 | 동일 GameObject의 모든 BoxCollider2D는 disable, layer 자동 지정 |

### 6-2. 충돌 처리 알고리즘

타일 기반 코너 검사와 물리 기반 최종 안전장치를 함께 사용합니다.

```
MoveWithCollision(input):
  X 이동 시도 → next = pos + (dx, 0)
    CanMoveTo(next) 검사:
      플레이어 경계 사각형의 4 코너 좌표 계산
      각 코너를 그리드 좌표로 변환
      하나라도 IsWalkable == false → 이동 차단
      + MovementBlockerQuery.IsPlayerMovementBlocked(next, radius) → 적 블로커 차단
  Y 이동 시도 → 동일 방식

  → X, Y를 독립 처리하므로 벽에 대해 슬라이딩 이동 가능

  대각선 입력이고 X/Y 모두 차단된 경우:
    TrySlideWithNudge(primaryMove=Y, nudge=-X방향):
      원래 위치에서 주 이동 + 미세 조정 거리를 늘려가며 CanMoveTo 검사
      성공하면 코너 슬라이딩 통과 허용
    실패 시 X 방향도 동일 시도

LateUpdate() — 최종 안전장치:
  CanMoveTo(transform.position) 검사
  통과: _lastSafePosition = 현재 위치 (갱신)
  실패: transform.position = _lastSafePosition (복원)
        Rigidbody velocity 초기화
  → 적과 Rigidbody 충돌로 벽 안에 밀려든 경우 차단
```

### 6-2-1. 적 블로커 차단 (MovementBlockerQuery)

`EnemyData.blocksMovement = true` 로 설정된 적은 플레이어의 일반 이동과 대시 모두를 물리적으로 막습니다. 적 AI 자체의 이동·넉백에는 영향이 없습니다.

```
MovementBlockerQuery.IsPlayerMovementBlocked(worldPos, radius):
  Physics2D.OverlapCircle(worldPos, radius, CombatLayers.EnemyFilter, s_BlockerBuffer)
  히트된 collider → Collider2D→EnemyController 정적 캐시(s_EnemyCache)로 해석
  IsAlive && data.blocksMovement → true 반환

사용처:
  PlayerController.CanMoveTo       — 일반 이동 ⊃ Diagonal slide 후보
  PlayerDashController.IsFootprintWalkable — 대시 경로 / 종착 위치
```

`s_BlockerBuffer`(크기 128) 와 `s_EnemyCache` 는 정적으로 재사용되며 매 호출 0 할당입니다. cache 는 Collider 가 destroy 되면 다음 lookup 시 lazy purge 됩니다.

### 6-3. 방 진입 감지 최적화

```
CheckRoomEntry():
  ① 그리드 좌표가 이전과 동일 → 조기 종료
  ② 복도(CORRIDOR) 타일 → 조기 종료
  ③ 방 내부 판정 (테두리 제외)
  ④ 이미 현재 방과 동일 → 조기 종료
  → 이벤트 발행
```

### 6-4. 입력 키 맵

| 키 | 동작 |
|----|------|
| ↑↓←→ | 이동 + Facing 방향 갱신 + 8방향 조준 raw 입력 |
| Z | 계단 상호작용 (0.5초 쿨다운) |
| F10 | 문 열기 |
| Space | 기본 공격 (홀드 시 범위 미리보기) — Facing 4방향 기준 |
| Q / W / E / R | 스킬 슬롯 1~4 — InstantArea / Projectile / Dash 라우팅 (홀드 시 범위 미리보기) |

### 6-4-1. 적 상태이상에 의한 이동/입력 제어

플레이어가 적 공격으로 상태이상에 걸리면 `PlayerController.Update` / `PlayerCombatController.Update` 가 우선순위에 따라 입력·이동을 가로챕니다.

```
PlayerController.Update 우선순위:
  IsDead          → velocity=0, 입력 무시
  IsTransitioning → 입력 무시 (층 이동 중)
  IsDashing       → CheckRoomEntry 만 수행
  IsStunned       → velocity=0, CheckRoomEntry 만 수행 (이동/방향 전환/스킬 전부 차단)
  BlocksPlayerMovement(스킬 castDelay/recoveryDelay) → velocity=0
  통상 이동: MoveWithCollision(input * _combat.MoveSpeedMultiplier)

MoveSpeedMultiplier:
  IsStunned        → 0          (실제 이동 자체가 Update 단계에서 막힘)
  IsSlowed         → min(activeSlowMultiplier...)   ← _enemySlows 중 가장 강한 감속 적용
  외 기본          → 1
```

스턴 동안 `PlayerCombatController.RefreshAimDirection()` 도 입력을 무시해 8방향 조준 캐시가 갱신되지 않습니다(스턴 직전 방향이 유지). 슬로우는 적의 `EnemyAttackImpactData.slowDuration` 동안만 적용되며, 만료 시 `_enemySlows.RemoveAt` 후 `RecalculateEnemySlowMultiplier`로 즉시 재계산합니다.

### 6-5. 스킬 castDelay / recoveryDelay 중 이동 잠금

`SkillData.castDelay`(선딜레이)와 `SkillData.recoveryDelay`(후딜레이)가 활성인 동안 플레이어는 이동·기본 공격·스킬 입력이 잠깁니다.

```
PlayerCombatController:
  ├── _isSkillCasting  (bool) — SkillCastRoutine 진행 중인지
  ├── _skillRecoveryTimer (float) — recoveryDelay 만료 카운트다운
  ├── IsSkillBusy => _isSkillCasting || _skillRecoveryTimer > 0
  └── BlocksPlayerMovement => IsSkillBusy   ← PlayerController/PlayerAnimationController 가 구독

흐름:
  TryUseSkill(slot):
    castDelay > 0 이면 BeginSkillCast → SkillCastRoutine 로 castDelay 후 ExecuteSkillIfReady
    castDelay == 0 이면 즉시 ExecuteSkillIfReady
  ExecuteSkillIfReady:
    성공 시 SpendMp / slot.StartCooldown / StartSkillRecovery(recoveryDelay)
    실패 가드: IsDead / IsDashing / DungeonManager.IsTransitioning / 슬롯 데이터 불일치
  TickSkillRecovery(dt) — Update에서 매 프레임 감소

게이트(IsSkillBusy 검사):
  Update 기본공격 입력 / TryBasicAttack / TryUseSkill / CanUseSkillSlot
  PlayerController.Update — BlocksPlayerMovement 시 입력 처리 skip
  PlayerAnimationController — BlocksPlayerMovement 시 MoveX/Y 0으로 강제
```

> 사망 / 대시 시작 / 풀링 비활성화 등에서는 `ClearSkillTimingState()`가 진행 중 코루틴을 중단하고 `_skillRecoveryTimer` 를 0 으로 리셋합니다.

> **조준 방향 결정**: 기본 공격(Space)은 `PlayerController.FacingDirection`(이동 키 우선 → 카디널 4방향)을, 스킬·투사체·대시는 `AimDirectionUtility.TryGetEightWayRaw(MoveInput)` 으로 얻은 8방향 raw 입력을 사용합니다. 입력이 비어 있을 때는 `PlayerCombatController._lastAimDirection`(기본값 down)으로 폴백합니다. 미리보기도 동일한 raw 방향을 사용해 실제 발사 결과와 시각이 일치합니다.

---

## 7. 시스템 4 — 전투

### 7-1. 데이터 구조

```
WeaponData (ScriptableObject)
  ├── damage, attackCooldown
  ├── attackPattern (AttackPatternType), patternRange
  ├── basicAttackMultiTarget
  ├── knockbackForce/Duration · slowPercentage/Duration
  ├── canPenetrateWalls
  └── skills[4] (SkillData[])

SkillData (ScriptableObject)
  ├── executionType (SkillExecutionType)  ← InstantArea/Projectile/Dash/AreaOverTime/Buff
  ├── 공통: damage, mpCost, cooldown, castDelay, recoveryDelay
  ├── 공통: isMultiTarget, canPenetrateWalls
  ├── 공통: attackPattern, patternRange, coneHalfAngle
  ├── 공통: knockback/slow 파라미터
  ├── Projectile: prefab, speed, lifetime, count, spreadAngle,
  │              firePattern, wallHitMode, targetHitMode,
  │              maxBounceCount, spawnOffset, burstInterval, burstSpacing
  └── Dash: distance, duration, stopOnWall,
           damageOnPath, damageOnContact, invincibleDuringDash

(Inspector는 SkillDataEditor가 executionType 별로 InstantArea/Projectile/Dash 섹션만 노출,
 AreaOverTime/Buff는 Reserved 안내, 미사용 필드는 Reserved foldout으로 접어둠)

PlayerResource (Domain)
  ├── currentHp, maxHp
  └── currentMp, maxMp

PlayerCombatController
  ├── PlayerResource (HP/MP 상태)
  ├── SkillSlotRuntime[4] (슬롯별 SkillData·쿨다운 상태)
  ├── AttackExecutor / SkillExecutor (스킬 실행 라우팅)
  ├── PlayerDashController (대시 코루틴, RequireComponent)
  ├── PlayerInputReader (RequireComponent)
  ├── HitFlashFeedback (피격 색상 점멸)
  ├── PlayerInvincibilityFlashFeedback (무적 셰이더 플래시)
  ├── damageInvincibleDuration — 피격 후 무적시간 (기본 0.5초)
  ├── _externalInvincibilityCount — 대시·외부 효과 무적 카운터
  ├── _lastAimDirection (Vector2Int) — 8방향 raw 조준 캐시 (입력 없을 때 폴백)
  ├── _isSkillCasting / _skillRecoveryTimer / _skillCastRoutine — 스킬 선/후딜 상태
  ├── IsSkillBusy / BlocksPlayerMovement — 캐스팅·후딜 중 이동·입력 잠금
  ├── 상태이상 (적 공격 → ApplyEnemyCombatImpact 진입):
  │     _enemySlows (List<PlayerSlowEffect>) — 가장 강한 감속을 _enemySlowMultiplier 로 반영
  │     _stunTimer / _stunTotalDurationForUi — Update 단계 TickEnemyStun
  │     _enemyKnockbackRoutine — playerMovement.TryApplyExternalDisplacement 로 매 프레임 변위
  │     IsSlowed / IsStunned / MoveSpeedMultiplier(IsStunned ? 0 : _enemySlowMultiplier)
  │     SlowRemainingTime/Ratio · StunRemainingTime/Ratio — UI 갱신용
  │     OnStatusEffectApplied / OnStatusEffectEnded(PlayerStatusEffectType) 이벤트
  │     ClearEnemyImpactState() — 사망 / OnDisable 시 모든 효과 해제
  ├── CurrentAimDirection / CurrentAimRawDirection — 정규화·raw 8방향 조준
  ├── RefreshAimDirection() — Update 매 프레임 + 스킬 사용 직전 갱신 (스턴 중에는 입력 무시 → 캐시 유지)
  ├── PlayerCombatController.Active (정적) — Projectile 등에서 거리 비교용 캐시 활용
  ├── IsDamageInvincible / HasExternalInvincibility / IsDashing
  ├── IsDead / OnDied(player) — HP 0 도달 시 단발 사망 처리
  ├── BeginExternalInvincibility(visualDuration) / EndExternalInvincibility()
  ├── ApplyEnemyCombatImpact(damage, hitDir, knockback, knockbackDur, slow, slowDur, stunDur)
  │      — 적 Contact Special / Jump 임팩트 / Projectile 적중이 호출하는 단일 진입점
  ├── Die() → CombatEventChannel.RaisePlayerDied()
  ├── Space → TryBasicAttack()
  └── Q/W/E/R → TryUseSkill(index)

SkillSlotRuntime (1슬롯)
  ├── Data (현재 슬롯에 바인드된 SkillData)
  ├── CooldownRemaining
  ├── Bind(data) / TickCooldown(dt) / StartCooldown()
  └── CanUse(availableMp) — 쿨다운+MP 확인

SkillCooldownController
  └── 기본 공격 쿨다운만 담당 (스킬 쿨다운은 SkillSlotRuntime이 보유)
```

### 7-2. 기본 공격 흐름

```
TryBasicAttack():
  ① _cooldownController.IsAttackReady & currentWeapon 확인
  ② SetAttackCooldown(weapon.attackCooldown)
  ③ ResolveTargets(weapon.attackPattern, weapon.patternRange)
       → SkillTargetResolver.ToGridAimDirection(facing)
       → AttackPattern.FillTargets(...)
  ④ AttackExecutor.BeginAttackActivation()
  ⑤ AttackExecutor.ExecuteAttack(
       targets, TotalAttack + weapon.damage,
       weapon.canPenetrateWalls, weapon.basicAttackMultiTarget,
       knockback/slow, hitRadius)
```

### 7-3. 스킬 실행 흐름 — SkillExecutor 라우팅

```
TryUseSkill(slotIndex):
  ① IsDead / IsDashing / IsSkillBusy 가드
  ② 슬롯·쿨다운·MP 확인 (SkillSlotRuntime.CanUse)
  ③ castDelay > 0 → BeginSkillCast → SkillCastRoutine(_isSkillCasting=true)
                    castDelay 만료 후 ExecuteSkillIfReady 호출
     castDelay == 0 → 즉시 ExecuteSkillIfReady

ExecuteSkillIfReady(slotIndex, expectedSkill):
  ① IsDead / IsDashing / DungeonManager.IsTransitioning 가드
  ② slot.Data == expectedSkill / CanUse 재검증 (코루틴 중 슬롯 변경 대응)
  ③ SkillExecutionContext 생성
       (caster, transform, skill, slotIndex, aim, gridFacing,
        TotalAttack, hitRadius)
  ④ SkillExecutor.Execute(context)
       switch (skill.executionType):
         InstantArea  → ExecuteInstantArea()
         Projectile   → ExecuteProjectile()
         Dash         → ExecuteDash()
         AreaOverTime/Buff → 미구현 (경고 로그 1회)
  ⑤ 성공 시 SpendMp / slot.StartCooldown / StartSkillRecovery(recoveryDelay)
            / RaiseSkillUsed

InstantArea:
  SkillTargetResolver.ResolveTargets(context)
    → AttackPattern.FillTargets(skill.attackPattern, origin, gridAim, range, coneHalfAngle)
  AttackExecutor.ExecuteAttack(targets, totalAttack + skill.damage,
    canPenetrateWalls, isMultiTarget, knockback/slow, hitRadius)

Projectile:
  ResolveExecutionDirection(context) →
    AimDirection(8방향 raw → 정규화) 우선, 0이면 GridAimDirection 폴백
  ProjectileFireService.Fire(ProjectileFireRequest):
    Single  → SpawnProjectile 1회
    Spread  → spreadAngle 부채꼴에 N발 균등 분포
    Circle  → 360°를 N등분
    Burst   → 1발 즉시 + (N-1)발을 burstInterval 간격으로 코루틴 발사
  Owner = caster, TargetMode = Enemy
  → ProjectilePool에서 prefab을 가져와 ProjectileController.Initialize(...)
    (knockback/slow 파라미터까지 함께 주입)

Dash:
  PlayerDashController = RequireComponent로 보장된 caster의 컴포넌트
  TryStartDash(direction, distance, duration, stopOnWall,
               invincibleDuringDash, DashDamageRequest{
                 DamageOnPath, DamageOnContact, Damage, HitRadius,
                 KnockbackForce/Duration, SlowPercentage/Duration})
  → direction 은 SkillExecutor가 8방향 raw 조준 기반으로 결정
  → 발자국(4코너) IsWalkable 검사로 destination 결정 (sampleStep ≈ tile×0.25)
  → 무적 옵션 시 BeginExternalInvincibility(duration) → flash 셰이더 진행
  → DashRoutine: Lerp(start, destination, t) 보간 이동
       매 프레임 TryApplyDashPathDamage(prev → current):
         segment 길이 / (HitRadius×0.75) 만큼 보간 샘플링 (최대 16샘플/segment)
         OverlapCircleNonAlloc → _hitEnemiesThisDash로 1회만 히트
       종료 시 TryApplyDashContactDamage(destination):
         최종 위치에서 한 번 더 OverlapCircle (path와 contact는 분리된 플래그)
```

### 7-4. 공격 판정 (AttackExecutor)

```
ExecuteAttack(gridPositions, damage,
              canPenetrateWalls, isMultiTarget,
              knockback/slow, hitRadius):
  Physics2D.OverlapCircleNonAlloc(queryRadius, s_HitBuffer)
  targetGrid ∈ _targetGridSet 필터
  canPenetrateWalls == false → HasWallBetween() 제외
  isMultiTarget: 전체 히트 / false: 최근접 단일 히트
  EnemyController → ApplyCombatImpact(damage, knockback, slow)
  그 외 IDamageable → TakeDamage(damage)
```

**벽 시야 검사 (HasWallBetween)**

```
공격자 그리드 → 대상 그리드까지 Bresenham 선형 보간
중간 타일 중 IsWalkable == false 가 있으면 차단
```

### 7-5. 공격 패턴 목록

| enum | 설명 | 대상 타일 수 |
|------|------|-------------|
| `Single` | 정면 1칸 | 1 |
| `Cross` | 상하좌우 4방향 | 4 |
| `Diagonal` | 대각선 4방향 | 4 |
| `Circle` | 주변 8칸 전체 (체비쇼프 거리) | 8+ |
| `Line` | 정면 직선 N칸 | patternRange |
| `Cone` | 정면 + 좌우 대각 부채꼴 | 3 |

### 7-6. 발사체 — 적·플레이어 공유 파이프라인

플레이어 스킬과 적 원거리 공격 모두 `ProjectileFireService` → `ProjectilePool` → `ProjectileController` 동일 경로를 사용합니다. (구 `Projectile.cs`는 호환 유지용으로 남아 있음)

**ProjectileFireService — 패턴 처리**

```
Fire(ProjectileFireRequest):
  Single → 1발
  Spread → spreadAngle 부채꼴 N발 균등 분포
  Circle → 360°를 N등분
  Burst  → 즉시 1발 + 코루틴으로 (N-1)발을 burstInterval 간격으로 발사
           (CoroutineRunner = caster의 MonoBehaviour)
  CanFire 검사: prefab/origin/caster 살아있음 확인 후 매 발사마다 재검사
  → ProjectilePool.Get → ProjectileController.Initialize(...)
```

**ProjectileController — 비행/충돌**

```
ProjectileController:
  ├── DungeonManager 그리드 IsWalkable 기반 벽 검사 (Physics2D 미사용)
  ├── 매 프레임 IsOutOfDungeonBounds(nextPos) — 맵 범위(InBounds) 밖이면 즉시 Release
  ├── ProjectileWallHitMode: Destroy / PassThrough / Bounce
  ├── ProjectileRotationMode: KeepPrefabRotation / FaceMoveDirection (기본)
  │     FaceMoveDirection — Initialize 시 / 매 비행 프레임 / Bounce 후 RefreshVisualRotation
  │     KeepPrefabRotation — 풀에서 꺼낼 때 prefab localRotation 복원
  ├── TargetMode = Player: 정적 캐시된 PlayerCombatController 거리 비교
  ├── TargetMode = Enemy : Physics2D.OverlapCircle → EnemyController.ApplyCombatImpact
  ├── ProjectileTargetHitMode: DestroyOnHit / Pierce / HitOncePerTarget
  ├── ApplyFogVisibilityForTargetMode():
  │     Enemy projectile(TargetMode=Player)에만 FogVisibilityRenderer enabled
  │     ResetToVisible() + RefreshVisibilityImmediate() 로 풀 재사용 시 잔존 상태 제거
  │     Player projectile(TargetMode=Enemy)는 fog 토글 없이 항상 표시
  ├── PrepareFromPool / HideForPool — 컴포넌트 enabled 토글 + Animator "Fly" 재시작
  │     HideForPool 시 FogVisibilityRenderer.enabled = false 로 fog 평가 정지
  ├── ReleaseForCleanup(reason) — 외부에서 강제 회수 (FloorTransition 등)
  └── lifetime 만료 / 벽 / 적중 / 맵 밖 → Release(reason) → 풀 콜백
        Reason: LifetimeExpired / PlayerHit / EnemyHit / WallHitDestroy /
                BounceLimit / OutOfBounds / FloorTransition / Manual / FallbackDestroy
```

**ProjectilePool — 두 가지 비활성화 모드**

| 모드 | 동작 | 비고 |
|------|------|------|
| `SetActive` | GameObject.SetActive(true/false) 토글 | 기존 Unity 표준 방식 |
| `DisableComponents` | GameObject은 active 유지, ProjectileController/SpriteRenderer/Animator만 enabled 토글 | OnEnable/OnDisable 비용 회피, 기본값 |

- `prewarmEntries`로 프리팹별 사전 풀 생성 수 지정
- Get/Return은 RuntimePerfTraceLogger 활성 시 ProfilerMarker + 마이크로 타이밍 기록

### 7-7. 대시 (PlayerDashController)

```
TryStartDash(caster, direction, distance, duration,
             stopOnWall, invincibleDuringDash, DashDamageRequest):
  ① 진행 중/사망/방향0 가드
  ② TryResolveDestination(start, dir, distance, stopOnWall):
       0.05~타일×0.25 간격으로 IsFootprintWalkable(4코너 IsWalkable) 검사
       stopOnWall = true → 마지막 통과 지점에서 정지
       stopOnWall = false → 막히면 실패
  ③ invincibleDuringDash → caster.BeginExternalInvincibility(duration)
                          → InvincibilityFlashFeedback _FlashAmount 보간
  ④ DashRoutine 코루틴: Lerp(start, destination, t) 이동
       (a) 매 프레임 TryApplyDashPathDamage(prev, current):
             DamageOnPath 일 때만 활성. segment 거리/(hitRadius×0.75)
             단위로 보간 샘플링(최대 16/segment), 각 샘플에서 OverlapCircle
             → _hitEnemiesThisDash HashSet으로 적 1회만 히트
       (b) 종료 후 TryApplyDashContactDamage(destination):
             DamageOnContact 일 때만 최종 위치에서 한 번 더 OverlapCircle
       → ApplyCombatImpact(damage, knockback, slow)
  ⑤ 종료 시 ClearDashInvincibility / ClearDashDamageState

DashDamageRequest 플래그:
  DamageOnPath    — 이동 경로 위 적 모두에 데미지 (segment 보간 샘플링)
  DamageOnContact — 대시 종료 지점에서만 데미지 (최종 프레임)
  두 플래그는 독립이며, 둘 다 켜면 path 적 + 종착 적 모두 처리 (1회 제한)

PlayerController는 IsDashing 동안 Move/입력 처리를 스킵하고
CheckRoomEntry만 호출해 대시 중 방 전환을 감지합니다.
```

---

## 8. 시스템 5 — 적 AI

### 8-1. FSM 구조

```
EnemyBrain (추상)
  ├── TargetHandler   — 플레이어 감지 및 타겟 갱신
  ├── MovementHandler — A* 경로탐색 + 군중 분리 (보간된 분리 벡터) + Ranged 이동(Chase/Kiting/Random)
  └── ActionHandler   — Contact/Ranged 분기 + 쿨다운·선딜·후딜 처리

NormalEnemyBrain (구체)
  └── 상태: Idle → Chase → Attack
       (IdleState/ChaseState/AttackState 는 EnemyStates.cs 의 internal sealed class)
       (보스/에픽은 EnemyBrain.CreateState 오버라이드로 EnemyAIStateId.Phase2/Berserk 등 추가)
```

```
상태 전이:
  Idle  ──(감지 범위 진입)──▶  Chase
  Chase ──(공격 범위 진입)──▶  Attack
  Attack ──(공격 사이클 종료)──▶ Chase
  Chase ──(시야 소실)──────────▶  Idle
```

### 8-2. A* 경로탐색 (AStarPathfinder)

```
FindPath(start, goal, grid):
  OpenSet   = MinHeap<Node>
  ClosedSet = HashSet<Vector2Int>

  g(n) = 시작 ~ n 실제 비용
  h(n) = 맨해튼 거리 (목표까지 추정)
  f(n) = g + h

  → 경로 발견 시 Vector2Int[] 반환 (버퍼 재사용, GC 없음)
```

### 8-3. 이동 최적화

| 전략 | 설명 |
|------|------|
| 직선 시야 확보 시 직접 이동 | Bresenham 그리드 샘플링으로 Physics2D 대체 |
| pathUpdateInterval 주기 갱신 | 매 프레임 A* 재탐색 방지 |
| 군중 분리 벡터 | 인접 적 OverlapCircle(0.1s 캐시) + Lerp 보간으로 지터 감소 — `CombatLayers.EnemyFilter`로 벽/문/투사체 브로드페이즈 컬링 |
| Footprint 4코너 검사 | CollisionFootprintRadius 기준 4코너 IsWalkable 통과 시에만 이동 |
| LateUpdate 위치 복원 | 풋프린트가 벽에 끼면 _lastSafePosition으로 복귀, 이전 프레임과 좌표 동일 시 4-corner 검사 skip |
| Ranged 이동 분기 우선 처리 | `MovementHandler.TryTickRangedMovement` 가 Kiting/Random 활성 시 A*/LOS 흐름을 건너뜀 |

### 8-4. 행동 분기 (EnemyBehaviorType)

> **이동 플래그**: `EnemyData.isStationary = true` 이면 `EnemyBrain.CurrentMoveSpeed = 0`, MovementHandler의 일반 이동·Ranged 이동(Kiting/Random)·idle separation 이 모두 정지하고 `EnemyController`가 `Rigidbody2D.constraints = FreezeAll` 로 잠급니다 (타겟 갱신·공격 사이클은 정상). `immuneToKnockback = true` 이면 `ApplyKnockback`이 즉시 velocity=0 후 return하며, 데미지·슬로우·스턴은 정상 적용됩니다.

`EnemyData.behaviorType`에 따라 ActionHandler가 다른 사이클을 돕니다.

```
Contact (근접):
  Contact Special Attack 진행 중이 아닐 때만 접촉 피해를 적용
  ShouldKeepChasing && Collider 거리 ≤ contactDamageSkin
    → ApplyDamage()  ← 매 프레임 접촉 피해 적용
    (플레이어는 IsDamageInvincible 동안 데미지 무시)
  CanAttack (Contact 분기): specialAttackType ≠ None &&
                           sqrDistance ≤ specialAttackRange² &&
                           attackCooldown ≤ 0 → AttackState 진입 → BeginContactSpecialAttack

Ranged (원거리):
  CanAttack(사거리·쿨다운) → AttackState 진입
  BeginAttack:
    aimDirection = (player - self).normalized
    attackWindup 동안 정지 + Animator AttackTrigger
  TickAttack:
    windup 종료 → FireRangedPattern(aimDirection)
    attackCooldown / attackRecovery 동안 정지
  TickBehavior(Ranged):
    pendingBurstShots > 0 시 burstInterval 마다 FireProjectile()
```

### 8-4-1. Ranged 이동 분기 (RangedMovementType)

`EnemyData.rangedMovementType`로 원거리 적의 추격 동작을 선택합니다.
ChaseState가 매 Tick `MovementHandler.TryTickRangedMovement`를 먼저 호출하고,
true 를 반환하면 LOS/A* 흐름을 건너뜁니다.

| 값 | 동작 | 사용 파라미터 |
|----|------|---------------|
| `Chase`   | 기존 추격 동작과 동일 (LOS 직선 → A*) | — |
| `Kiting`  | `preferredRange` 보다 멀면 접근, `kiteRetreatRange` 안쪽이면 후퇴 | preferredRange, kiteRetreatRange |
| `Random`  | `randomMoveInterval[Min,Max]` 간격으로 `randomMoveRadius` 안의 새 목적지 선택 | randomMoveIntervalMin/Max, randomMoveRadius |

**Kiting 다중 후퇴 방향 (s_KitingRotations)**

후퇴 방향이 막혀 있어도 정지하지 않도록 5단계 우선순위로 시도합니다.

```
away(180°) → away+45° → away-45° → side(+90°) → side(-90°)
```

- 각 후보 방향마다 `data.preferredRange` 만큼의 가상 목적지를 계산하고 `MoveToward`로 1회 이동 시도
- 4-corner footprint 검사를 통과하는 첫 후보를 채택
- 모든 후보가 막히면 `TryApplyIdleSeparationStep` 으로 폴백

**Random 목적지 minR 보호**

`randomMoveRadius` 안의 새 목적지를 뽑을 때 너무 가까운 영역(자기 자신 위)에는 찍히지 않도록 `minR = max(radius * 0.25, footprintRadius + 0.1)` 으로 inner radius를 확보합니다. `randomMoveRadius` 가 footprint 보다도 작은 잘못된 설정이면 random 이동을 안전하게 skip.

### 8-4-2. 정지 상태 separation 보강 (TryApplyIdleSeparationStep)

Kiting의 "적정 거리 도달", Random의 "다음 목적지까지 대기"처럼 **정지하려는 순간**에도 이웃 적과 겹치면 살짝 산개하도록 보정합니다.

```
조건: enableSeparation && _smoothedSeparation.sqrMagnitude >= 0.01 (IdleSeparationActivationSqr)
구현:
  _separationBuffer / s_SeparationFilter / _smoothedSeparation 인프라 재사용 → 0 할당
  separation 방향으로 self+1 의 가상 target 생성 후 MoveToward 1회
효과:
  - 다수의 Ranged 적이 같은 지점에 정지하지 않고 자연 산개
  - LOS/A* 흐름은 그대로 skip (Ranged 분기 안에서 자체 처리)
```

### 8-4-3. Contact Special Attack (Rush / Jump)

`EnemyData.specialAttackType`이 `Rush` 또는 `Jump`인 Contact 적은 일반 접촉 피해 외에 별도 사이클을 실행합니다. ActionHandler 내부 상태머신 `EnemySpecialAttackPhase` (None → Windup → Rush/Jump → Recovery)로 제어되며, 진행 중에는 일반 접촉 피해와 일반 이동이 모두 정지됩니다.

```
공통 흐름 (BeginContactSpecialAttack → TickContactSpecialAttack):
  Windup   — specialAttackWindup 동안 정지, Charge 애니메이션 + 페이싱 잠금
             (Jump는 windup 진입 시 TryResolveJumpTarget 으로 착지점 미리 결정;
              실패하면 CancelContactSpecialAttack 으로 즉시 종료 + 짧은 쿨다운)
  Rush     — 매 프레임 _specialDirection × rushSpeed × dt 만큼 transform 이동
             CanOccupy 실패(벽/막힘) 시 즉시 Recovery 진입
             경로 위 타겟에 rushDamage 적용 — _rushHitTargets(HashSet) 로 1회 제한
             FacingLock 으로 sprite 가 진행 방향 고정
  Jump     — Lerp(start, jumpTargetPosition, t) 보간 비행 (jumpDuration)
             종료 프레임에 위치 스냅 + ApplyJumpImpactDamage(jumpImpactRadius)
             Land 애니메이션 트리거 → Recovery
  Recovery — specialAttackRecovery 동안 정지, 일반 사이클 복귀

쿨다운: StartRush/StartJump 진입 시 _attackCooldownTimer = specialAttackCooldown
        (Cancel 경로는 최소 0.1s 보호값)

사망 시:
  EnemyController.Die() → EnemyBrain.HandleDeathStarted() →
    StopMoving + Action.ResetRuntimeState (specialPhase / timer / hit set 클리어) + UnlockSpecialFacing
```

| 파라미터 (EnemyData) | Rush | Jump |
|---|---|---|
| 공통 (specialAttack*) | Range / Cooldown / Windup / Recovery | 동일 |
| 전용 속도 | `rushSpeed`, `rushDuration` | `jumpDuration` |
| 전용 데미지 | `rushDamage`, `rushHitRadius` | `jumpDamage`, `jumpImpactRadius` |
| 전용 위치 | — | `jumpMaxDistance`, `jumpStayInRoom` |
| 임팩트 | `rushImpact` (EnemyAttackImpactData) | `jumpImpact` (EnemyAttackImpactData) |

> 데미지 값이 0이면 `EnemyData.attack` 이 사용됩니다 (`GetSpecialDamage`).
>
> Rush 경로 데미지 / Jump 착지 임팩트는 `ApplyEnemyImpactToTarget` → `PlayerCombatController.ApplyEnemyCombatImpact` 로 라우팅되어, EnemyAttackImpactData 의 knockback·slow·stun 이 한 번에 적용됩니다 (다른 IDamageable 타깃은 단순히 `TakeDamage(damage)`만 호출).

### 8-5. 원거리 공격 패턴 (ProjectileFirePattern)

| Pattern | 동작 |
|---------|------|
| `Single` | 조준 방향으로 1발 |
| `Burst` | N발을 burstInterval 간격으로 연사 (이동 가능 상태에서 분산 발사) |
| `Spread` | spreadAngle 부채꼴 안에 N발 균등 분포 |
| `Circle` | 360°를 N등분해 전방위 발사 |

투사체는 `ProjectilePool.Instance.Get(prefab)`로 풀에서 꺼내 `Initialize(direction, damage, speed, lifetime, wallHitMode, maxBounceCount, owner)`로 주입합니다.

### 8-6. 투사체 벽 처리 (ProjectileWallHitMode)

```
Destroy:     벽 그리드 진입 시 즉시 Release
PassThrough: IsWalkable 검사 생략, 직선 비행
Bounce:      X/Y 축별로 차단된 축의 방향만 반전
             모서리(둘 다 차단) 시 직진 방향 역전
             maxBounceCount 도달 시 Release
             bounceExitOffset 만큼 진행해 벽 안 끼임 방지
```

플레이어 적중 판정은 `Physics2D` 대신 정적 캐시된 PlayerCombatController의 위치·반경과의 거리 비교(`hitRadius + s_PlayerRadius`)로 처리합니다.

### 8-7. 상태이상 처리 (EnemyController)

| 상태이상 | 처리 |
|--------|------|
| 넉백 | 방향 × 힘 임펄스, `knockbackResistance`로 감쇠. CircleCast + 그리드 IsWalkable 양면 클램핑으로 벽 안 끼임 방지. `immuneToKnockback=true` 이면 즉시 velocity=0 후 무시 |
| 슬로우 | `_activeSlows` 리스트에서 가장 강한 감속만 moveSpeed 승수에 반영, 지속시간 후 자동 제거 |
| 피격 점멸 | `HitFlashFeedback.Play()` — SpriteRenderer 색상 N회 점멸 |

> 적이 플레이어에게 가하는 부가 효과는 `EnemyAttackImpactData`(knockback·slow·stun) 구조로 EnemyData 인스펙터에 노출됩니다. Rush/Jump/Projectile 각자 `rushImpact`/`jumpImpact`/`projectileImpact` 필드를 보유하며 일반 Contact 접촉 피해는 단순 데미지(`TakeDamage(attack)`)만 적용합니다.

**사망 처리 (IsDead → OnDeathFinished)**

```
TakeDamage → HP 0 도달 → Die():
  IsDead = true
  CircleCollider 비활성화 (이후 충돌·접촉 피해 차단)
  ResetStatusEffects() (넉백·슬로우 클리어)
  EnemyBrain.HandleDeathStarted()         ← Special Attack 상태머신 강제 종료, FSM 핸들러 ResetRuntimeState
  EnemyAnimationController.TriggerDeath() → DeathTrigger (Attack/Charge/Rush/Jump/Land trigger 모두 reset)
  CombatEventChannel.RaiseEnemyKilled()  ← 방 클리어 판정 즉시 트리거
  OnDied?.Invoke()
  _deathTimer = EnemyData.deathDelay (기본 0.5초)

Update → TickDeathDelay():
  _deathTimer 만료 → FinishDeath():
    OnDeathFinished?.Invoke()    ← EnemyPoolManager가 풀로 반납
    gameObject.SetActive(false)
```

`EnemyPoolManager`는 `OnDied` 대신 `OnDeathFinished`를 구독합니다. 사망 애니메이션이 끝난 뒤에야 풀로 반납되므로 사망 모션이 잘리지 않습니다.

### 8-8. 적 애니메이션 (EnemyAnimationController)

LateUpdate 기반 위치 변화를 감지해 Animator 파라미터를 자동 갱신합니다.

| 파라미터 | 용도 |
|--------|------|
| `IsMoving` (bool) | 위치 변화 ≥ movementThreshold |
| `MoveX`, `MoveY` (float) | 이동 방향 정규화 벡터 |
| `LastMoveX`, `LastMoveY` (float) | 마지막 이동 방향 (Idle 자세 유지) |
| `AttackTrigger` (trigger) | `PlayAttack(targetPosition)` 호출 시 — 타겟 방향으로 페이싱 후 발동 |
| `ChargeTrigger` / `RushTrigger` / `JumpTrigger` / `LandTrigger` (trigger) | Contact Special Attack 단계별 트리거. 해당 파라미터가 없으면 자동으로 `AttackTrigger` 폴백 (`SetTriggerOrAttack`) |
| `DeathTrigger` (trigger) | 사망 시 Sprite flipX 페이싱 잠금 |

`faceTargetWhileChasing` 옵션을 켜면 EnemyBrain이 매 프레임 `FacePosition(Target)`을 호출해 추격 중에도 항상 타겟을 바라보도록 보정합니다 (근접 적의 추적 방향 안정화). 이때 이동 방향 기반의 자동 페이싱(`faceMoveDirectionWhenMoving`)은 한 프레임 동안 억제됩니다.

`LockFacing(direction)` / `UnlockFacing()` — Contact Special Attack 의 Rush/Jump 동안 sprite 가 진행 방향으로 고정되도록 사용. 잠금 중에는 `faceMoveDirectionWhenMoving` / `faceTargetWhileChasing` 모두 무시됩니다.

ResetAnimationState()에서 `Animator.Rebind()` + `Play("Idle", 0, 0f)`로 풀 재사용 시 잔여 상태를 초기화합니다 (`gameObject.activeInHierarchy`가 false인 경우 Rebind를 건너뜁니다).

---

## 9. 시스템 6 — 방 스폰 및 클리어

### 9-1. 스폰 흐름

```
OnRoomEntered (이벤트 수신):
  ① isFirstVisit == false → 종료 (재진입 시 재스폰 없음)
  ② Room.Type ≠ Normal/MonsterDen → 종료
  ③ CanStartRoomEncounter() 검사:
       플레이어가 방 내부에 있고 (9-포인트 샘플링)
       플레이어가 문 타일과 겹치지 않음
       → 실패 시 _pendingRoomStart에 저장, LateUpdate에서 재시도
  ④ 방 전용 결정론적 RNG 생성:
       roomSeed = FNV-1a(globalSeed, dungeonType, floor, room.StableRoomKey, "enemy_spawn")
       roomRng  = new System.Random(roomSeed)
  ⑤ EnemyPoolManager에서 예산 기반 적 선택 (roomRng.Next 사용)
       (방 면적 × densityFactor × 방 타입 배율)
       (SpawnRegion 비트 필터링, _poolEnemyTable 은 enemyName 기준 정렬 후 선택)
  ⑥ 방 내부 걷기 가능 타일에 스폰
       (테두리 제외, 4-코너 발자국 검사로 벽 끼임 예방)
       (타일 후보는 row-then-column SortSpawnTiles → roomRng Shuffle)
  ⑦ 적 수 > 0 → DungeonManager.CloseCurrentRoomDoors()
     적 수 = 0 → DungeonManager.OpenCurrentRoomDoors()
```

### 9-1-2. 결정론적 방 스폰 시드 (DeterministicSeedUtility)

방마다 결정론적이고 안정적인 적 구성을 보장합니다. 같은 던전 시드 + 같은 층 + 같은 방에 들어가면 항상 동일한 적 조합과 위치가 나옵니다.

```
DeterministicSeedUtility.CreateStableRoomKey(RoomRect):
  FNV-1a 해시(X, Y, W, H, CenterX, CenterY) → 방의 위치·크기 기반 안정 키
  RoomInfo.StableRoomKey 에 던전 생성 시 캐싱
  (RoomSpawner.SameRoom / GetRoomKey 가 이 키로 방 동일성 판정)

DeterministicSeedUtility.CreateSeed(globalSeed, dungeonType, floor, stableRoomKey, domain):
  FNV-1a 해시(long globalSeed, int dungeonType, int floor, int stableRoomKey, string domain)
  → 양수 int 시드 반환

도메인 상수:
  EnemySpawnDomain = "enemy_spawn"
    → RoomSpawner.SpawnEnemiesInRoom 에서 사용
    → 다른 결정론 시스템을 추가할 땐 새 도메인 문자열을 정의해 시드 충돌 방지
```

```
DungeonTypeId (enum):
  Default = 0
  (DungeonManager.dungeonType 가 시드 입력에 포함 — 던전 종류별 RNG 분기 자리)

RoomInfo.StableRoomKey:
  DungeonManager.BuildRoomInfos 가 생성 시 CreateStableRoomKey 로 채움
  Spawn/Stair 자동 분류 후에도 보존
  RoomSpawner._spawnedRoomsByKey / _pendingRoomStart 도 이 키로 식별
```

UnityEngine.Random 대신 per-room `System.Random` 인스턴스를 사용해 다른 시스템(파티클·UI·물리 노이즈)이 RNG 상태를 오염시켜도 스폰 결과가 흔들리지 않습니다.

### 9-1-1. 지연 전투 시작 (Deferred Encounter)

플레이어가 방에 진입할 때 문 타일 위에 걸쳐 있으면 문 닫힘과 충돌이 발생합니다.  
이를 방지하기 위해 `_pendingRoomStart`에 방 정보를 보류하고, `LateUpdate`에서 매 프레임 `CanStartRoomEncounter()`를 재검사해 안전해지면 전투를 시작합니다.

```
LateUpdate():
  _pendingRoomStart가 있으면:
    CanStartRoomEncounter() → true 시 StartRoomEncounter() 실행
```

### 9-2. 방 클리어 판정

```
CheckRoomClear() — 매 프레임 또는 OnEnemyKilled 구독:
  spawned 목록 내 IsAlive인 적이 0 → 방 클리어
  → DungeonManager.OpenDoors(roomId)
  → 중복 판정 방지 플래그 설정
```

### 9-3. 오브젝트 풀 (EnemyPoolManager)

```
Pool<EnemyType>:
  Get()    → 비활성 오브젝트 활성화 또는 새로 생성
  Return() → SetActive(false) + 풀 반환
```

---

## 10. 시스템 7 — UI 및 스킬 미리보기

### 10-1. 플레이어 상태바 (PlayerStatusBarUI)

`CombatEventChannel` 이벤트를 구독해 HP·MP를 실시간으로 표시합니다.

```
PlayerStatusBarUI:
  ├── HP 슬라이더 (Slider) — 수치 비율에 따라 갱신
  ├── MP 슬라이더 (Slider) — 수치 비율에 따라 갱신
  ├── HP 텍스트 (cur / max 형식)
  └── MP 텍스트 (cur / max 형식)

구독 이벤트:
  OnPlayerHpChanged(cur, max) → HP 슬라이더 + 텍스트 갱신
  OnPlayerMpChanged(cur, max) → MP 슬라이더 + 텍스트 갱신
```

### 10-1-1. 플레이어 상태이상 아이콘 UI (PlayerStatusEffectUI)

슬로우·스턴 활성 동안 아이콘과 잔여 시간을 표시합니다.

```
PlayerStatusEffectUI:
  ├── slowIconView / stunIconView (StatusEffectIconView)
  ├── PlayerCombatController.Active 가 준비될 때까지 OnEnable + Update 에서 TryBindCombat
  ├── 구독: OnStatusEffectApplied(Slow/Stun) → SetVisible(true) + MoveToLast
  │         OnStatusEffectEnded(Slow/Stun)  → SetVisible(false)
  └── Update: 활성 상태이면 RefreshIcon → SetTime(remainingTime, ratio)

StatusEffectIconView (슬롯 1개):
  ├── iconImage      — 정적 스프라이트
  ├── fillImage      — fillAmount = remaining / total (역으로 줄어드는 게이지)
  ├── timeText (TMP) — remaining > 0 일 때만 "0.0" 포맷 표시
  └── MoveToLast()   — 새로 활성된 효과를 컨테이너 마지막에 정렬
```

상태이상 발행은 `CombatEventChannel` 이 아니라 `PlayerCombatController` 의 직접 이벤트(`OnStatusEffectApplied`/`Ended`) — UI 한 곳만 구독하면 충분하기 때문입니다.

### 10-2. 스킬 슬롯 UI

```
SkillSlotUI (슬롯 1개):
  ├── 아이콘 Image
  ├── 쿨타임 덮개 Image (fillAmount 0→1)
  └── 남은 시간 Text

SkillUIManager:
  ├── 슬롯 4개 초기화
  ├── OnSkillUsed 이벤트 → 해당 슬롯 쿨다운 시작
  └── OnFloorChanged → WeaponData 교체 시 슬롯 갱신
```

### 10-3. 스킬 범위 미리보기 (SkillRangePreviewer)

Q/W/E/R 홀드 시 스킬 범위, Space 홀드 시 기본 공격 범위를 LineRenderer로 시각화합니다.

```
입력 분기:
  WasSkillPressed(0~3) → 슬롯 미리보기 시작 (기본 공격 미리보기 우선 숨김)
  IsSkillHeld(0~3) == false → 미리보기 숨김
  IsBasicAttackHeld → 슬롯 미리보기가 없을 때만 기본 공격 미리보기

스킬 미리보기 분기 (BuildPreview):
  executionType == Projectile → BuildProjectilePreview
       Single/Burst → 직선
       Spread       → 부채꼴 N갈래
       Circle       → 360° N갈래
       각 라인은 wallHitMode 가 PassThrough 가 아니면 ClipToWall 적용
  executionType == Dash      → BuildDashPreview (직선 + 벽 클리핑)
  그 외 (InstantArea)         → BuildInstantAreaPreview
       Circle / Cone / Line / Single / Cross / Diagonal 6종 다각형

벽 인식 (ClipToWall):
  wallLayer 설정 시 Physics2D.Raycast 우선
  미설정 시 DungeonData.IsWalkable 그리드 샘플링 폴백

재계산 조건:
  슬롯 변경 시 즉시 / FacingDirection 변경 시 (Line/Cone/Single/Projectile/Dash)
```

플레이어가 사망(`PlayerCombatController.IsDead`)한 경우 활성 미리보기를 즉시 숨기고 입력 처리도 중단합니다.

### 10-4. 게임오버 UI 흐름

플레이어 HP가 0이 되면 사망 → 지연 → 게임오버 UI 표시 → 확인 시 씬 재로드 순서로 처리됩니다.

```
PlayerCombatController.TakeDamage()
  → HP 0 도달 → Die()
        IsDead = true
        CombatEventChannel.RaisePlayerDied(this)
                  │
                  ▼
GameOverFlowController.HandlePlayerDied()
  ├── 중복 트리거 차단 (_flowStarted)
  ├── ShowAfterDeathDelay() 코루틴
  │     yield WaitForSeconds(deathUiDelay)  ← 사망 모션 노출 시간
  └── GameOverUIController.Show()
            └── CanvasGroup 페이드 인 (unscaledDeltaTime)

확인 버튼 클릭:
  GameOverFlowController.ConfirmGameOver()
    ├── GameOverUIController.HideImmediate()
    └── IGameOverRestartHandler.RestartAfterGameOver()
          (기본: GameOverSceneReloadRestartHandler → 활성 씬 재로드)
```

**사망 시 입력/이동 차단**

| 컴포넌트 | 처리 |
|---------|------|
| PlayerController | Update 진입 시 `_combat.IsDead`면 Rigidbody velocity 0으로 클리어 후 즉시 반환 |
| PlayerCombatController | Update에서 IsDead면 입력·쿨다운 처리 건너뜀 |
| PlayerAnimationController | `IsDead` Animator 파라미터 true 고정, MoveX/Y 0 |
| SkillRangePreviewer | 활성 미리보기 모두 숨김, 입력 처리 중단 |

`GameOverUIController`는 패널·확인 버튼·이미지를 모두 인스펙터로 사전 연결한다는 전제로 동작합니다. 참조가 비어 있으면 `_warnedMissingReferences` 로 1회 경고를 출력하고 표시를 skip 합니다 (이전 버전의 `BuildDefaultUi()` 런타임 자동 생성 경로는 제거되었습니다). 페이드는 항상 `unscaledDeltaTime` 기반이라 `Time.timeScale=0` 의 일시정지에도 정상 동작합니다.

---

## 11. 시스템 8 — 렌더링·로딩·시야

### 11-0. Fog of War (FogOfWarController)

플레이어 시야 안의 셀만 보이고, 한 번 본 셀은 어두운 톤으로 남기는 3상태 시야 시스템.

```
상태 (셀당):
  미탐사  → unexploredFogTile + unexploredFogColor (기본 검정 불투명)
  탐사됨 → exploredFogTile + exploredFogColor   (기본 검정 반투명)
  현재시야 → 안개 타일 비움 (clear)

LateUpdate 흐름:
  ① 데이터·플레이어 변경 감지 → 필요 시 InitializeForDungeon
  ② 플레이어 그리드가 변하지 않았으면 조기 종료
  ③ RefreshVisibility(playerGrid):
        AddVisionRadiusCells(중심, visionRadius)
          → 원형 거리 + Bresenham HasLineOfSight 체크
          → blockVisionByWalls = true 시 EMPTY/닫힌문에서 차단
        revealCurrentRoom = true 면 현재 방 + 패딩/테두리 셀 추가
        새로 보이는 셀 / 사라진 셀 두 집합으로 SetTiles 1회 배치

GC·성능:
  HashSet 두 개를 swap해서 가비지 없이 visible delta 계산
  TileChangeData[] 크기별 캐시로 SetTiles 인터롭 1회 호출
  closedDoorsBlockVision: 닫힌 문 너머 시야 차단
```

공개 API:
- `IsVisibleCell(Vector2Int gridPos)` — 그리드 좌표가 현재 시야 내인지
- `IsWorldPositionVisible(Vector3 worldPos)` — WorldToGrid 변환 후 시야 검사 (FogVisibilityRenderer 가 매 프레임 호출)
- `ForceRefresh()` / `RequestFullInitialize()` — 외부 강제 갱신 진입점
- `FogOfWarController.Active` (정적) — FogVisibilityRenderer 가 인스턴스 검색 없이 참조

이벤트 구독:
- `DungeonEventChannel.OnFloorChanged` → 다음 LateUpdate 에서 전체 재초기화 요청
- `DungeonEventChannel.OnRoomEntered` → 즉시 ForceRefresh
- `DungeonEventChannel.OnRoomDoorsClosed` / `OnRoomDoorsOpened` → 즉시 ForceRefresh
   (closedDoorsBlockVision = true 일 때 문 개폐 직후 시야 차단/개방을 한 프레임 안에 반영)

### 11-0-1. FogVisibilityRenderer (Visual/)

`Renderer.enabled` 만 토글해 안개 밖 오브젝트를 숨기는 범용 컴포넌트. 게임플레이 로직(AI·콜라이더·데미지·풀)에는 영향을 주지 않습니다. 매 프레임 GetComponent / FindAnyObjectByType / 신규 할당 없이 동작합니다.

```
FogVisibilityRenderer:
  ├── managedRenderers — Awake 시 자식 포함 자동 캐시 (또는 Inspector 명시)
  ├── _rendererInitialEnabled — 프리팹 초기 enabled 상태 보존
  ├── updateInterval (0이면 매 Update / >0이면 그 초마다 평가)
  ├── ResolveVisibility() = FogOfWarController.Active.IsWorldPositionVisible(transform.position)
  ├── ApplyVisibility(visible): Renderer.enabled = visible && initialEnabled
  ├── ResetToVisible() — 풀 재사용 직전 호출 (모든 Renderer를 visible 기준선으로 복원)
  └── RefreshVisibilityImmediate() — pool 직후/Initialize 직후 한 프레임 지연 없이 평가

사용 위치:
  • EnemyPoolManager 가 적 prefab 에 부착해 시야 밖 적 렌더링 차단
  • ProjectileController 가 Enemy projectile(TargetMode=Player)에만 활성화
    → 시야 밖에서 날아오는 투사체를 보이지 않게 처리, 풀 회수 시 enabled=false 로 잔존 평가 차단
```

### 11-1. Tilemap 3레이어 구조

던전 타일맵을 목적에 따라 3개 레이어로 분리합니다.

```
[Layer 0] tilemap (메인)     — 바닥(ROOM) · 통로(CORRIDOR) · 계단(STAIR_UP)
[Layer 1] wallTilemap         — 벽/빈 공간(EMPTY) 전용
                                + TilemapCollider2D 부착 (물리 충돌)
[Layer 2] doorTilemap (상위)  — 닫힌 문만 배치, 열리면 TilemapRenderer 비활성화
```

| 타일 타입 | tilemap | wallTilemap |
|---------|---------|-------------|
| ROOM    | floorTile | null |
| CORRIDOR | corridorTile (없으면 floorTile) | null |
| STAIR_UP | stairUpTile (없으면 floorTile) | null |
| EMPTY   | null | wallTile |

두 버퍼(`tiles[]`, `wallTiles[]`)를 한 패스에서 동시에 채운 뒤 `SetTilesBlock` 2회로 배치합니다.  
문은 `doorTilemap`에 `SetTiles(TileChangeData[], ignoreLockFlags)` 1회 배치 호출 (N→1 interop).

**wallTilemap 물리 콜라이더**: `TilemapCollider2D`를 부착해 Rigidbody2D 기반 충돌을 통해 벽 관통을 물리적으로 차단합니다. 기존 타일 기반 `CanMoveTo()` 검사와 병용합니다.

**doorTilemap 물리 콜라이더**: 닫힌 문 타일에도 `TilemapCollider2D`를 부착해 적이 닫힌 문을 통과하지 못하도록 차단합니다. 문이 열리면 `TilemapRenderer`가 비활성화되고 콜라이더도 함께 효과가 사라집니다.

### 11-1-1. 플레이어 위치 인식 (CanStartRoomEncounter)

`DungeonTilemapRenderer`는 방 전투 시작 안전성을 검사하는 API를 제공합니다.

```
CanStartRoomEncounter(room):
  IsPlayerInsideRoom(room):
    RoomFootprintSampler.FillSamples — 중앙 1 + 8방향 경계 (총 9개)
    중앙이 방 안 → true
    나머지 중 RoomFootprintSampler.Threshold(=3)개 이상 방 안 → true
  IsPlayerOverlappingAnyDoorCell(room):
    방 4면 테두리의 문 후보 타일 각각에 대해
    플레이어 CircleCollider와 타일 AABB 겹침 검사
    → 하나라도 겹치면 false (아직 문 진입 중)
```

> `RoomFootprintSampler`는 `PlayerController.CheckRoomEntry()` 의 후보 방 선정과
> `DungeonTilemapRenderer.IsPlayerInsideRoom()` 에서 같은 9-sample 배치를 공유합니다.
> sample 인덱스 의미는 `0=center, 1=left, 2=right, 3=up, 4=down, 5=down-left, 6=down-right, 7=up-left, 8=up-right` 순서로 고정.

### 11-2. Tilemap 청크 분할 배치

층 이동 중 프레임 드랍을 방지하기 위해 Tilemap 배치를 여러 프레임으로 분산합니다.

```
PlaceTilesChunked(data, chunkRows=8):
  전체 행을 chunkRows개 단위로 분할
  각 청크: tilemap 버퍼 + wallTilemap 버퍼 동시 채움
           SetTilesBlock(bounds, tiles)
           SetTilesBlock(bounds, wallTiles)
  각 청크 배치 후 yield return null  ← 프레임 양보
```

### 11-3. 층 이동 코루틴 타임라인 (FloorTransitionService)

```
FloorTransition(targetFloor):
  0. ProjectilePool.ReleaseAllActiveProjectiles(FloorTransition)
                                   ← 이전 층의 비행 중 투사체 일괄 회수
  1. LoadingScreen.Show()          ← 페이드 인
  2. GenerateChunked()             ← 던전 생성 (로딩 화면 뒤에서)
  3. yield return null             ← Unity Tilemap 처리 완료 대기
  4. [선택] GC.Collect()
  5. WaitForSecondsRealtime()      ← 렌더러 안정화
  6. EventChannel.RaiseFloorChanged()  ← 플레이어 스폰 트리거
  7. LoadingScreen.Hide()          ← 페이드 아웃
```

---

## 12. 성능 전략

| 전략 | 적용 위치 | 효과 |
|------|-----------|------|
| `struct` 이벤트 인자 | `RoomEnteredEventArgs` | Heap 할당 없음 |
| YieldInstruction 캐시 | `YieldCache` | 코루틴 GC 감소 |
| 스폰 좌표 캐싱 | `SpawnPositionService` | O(1) 조회 |
| 그리드 좌표 변경 시에만 방 감지 | `_lastCheckedGridPos` | Update 부하 감소 |
| 청크 분할 Tilemap 배치 | `PlaceTilesChunked` | 층 이동 시 프레임 유지 |
| 타일맵 버퍼 한 패스 채움 | `DungeonTilemapRenderer` | floor+wall 배열을 단일 루프에서 생성 |
| 문 SetTiles 배치 1회 | `CloseDoorsForRoom` / `FlushDoorChanges` | N번 SetColor → 1번 SetTiles (interop N→1) |
| 문 변경 배열 크기별 캐시 | `_doorChangeArraysBySize` | 문 배치마다 배열 할당 없음 |
| static 픽셀 스프라이트 | `EnemyHealthBar.s_Pixel` | 텍스처 1회 생성, N마리 공유 |
| A* 버퍼 재사용 | `AStarPathfinder` | 경로탐색 GC 없음 |
| NonAlloc 물리 | `Physics2D.OverlapCircleNonAlloc` | 전투 판정 GC 없음 |
| Bresenham 직선 시야 | `ChaseState` | Raycast 대신 그리드 샘플링 |
| 오브젝트 풀링 | `EnemyPoolManager` | 적 Instantiate/Destroy 없음 |
| LateUpdate 위치 복원 | `PlayerController` | 물리 충돌 벽 관통 방지 (최종 안전장치) |
| TilemapCollider2D 벽 물리 | `wallTilemap` | Rigidbody2D 레벨의 벽 충돌 추가 안전장치 |
| 지연 전투 시작 | `RoomSpawner._pendingRoomStart` | 문 닫힘 전 플레이어 위치 확인으로 끼임 방지 |
| 9-포인트 방 샘플링 | `DungeonTilemapRenderer` | CircleCollider 반경 기반 정확한 방 내부 판정 |
| 벽 LoS 차단 (공격) | `AttackExecutor.HasWallBetween` | Bresenham 선형 보간으로 벽 너머 공격 방지 |
| 공격 다중/단일 타겟 분리 | `AttackExecutor.isMultiTarget` | 패턴별 최근접 단일 or 전체 히트 선택 |
| 투사체 사전 풀링 | `ProjectilePool.prewarmEntries` | 첫 사격 시 Instantiate 비용 회피 |
| 컴포넌트 비활성화 풀링 | `ProjectilePoolDisableMode.DisableComponents` | SetActive 토글 비용 회피 (OnEnable/OnDisable 미발생) |
| 그리드 IsWalkable 기반 벽 검사 | `ProjectileController.IsWalkPosition` | 투사체 Physics2D 비용 0 |
| 정적 플레이어 캐시 | `ProjectileController.s_PlayerCombat` | 투사체마다 FindAnyObjectByType 호출 회피 |
| 넉백 벽 클램핑 | `EnemyController.ClampKnockbackForceAgainstWall` | CircleCast + 그리드 IsWalkable 양면 검사 |
| 플레이어 피격 무적시간 | `PlayerCombatController.damageInvincibleDuration` | 0.5초 동안 다중 피해 차단 |
| 적 풋프린트 위치 복원 | `EnemyController.LateUpdate` | 물리 푸시로 벽 안에 들어간 적을 _lastSafePosition으로 복귀 |
| Animator 파라미터 사전 캐싱 | `EnemyAnimationController._hasMoveX 등` | 매 프레임 string 비교/탐색 회피 |
| 사망 단발 처리 | `PlayerCombatController.IsDead` / `EnemyController.IsDead` | Die() 중복 호출 차단, 사망 후 데미지·입력·AI 즉시 정지 |
| 적 사망 지연 + Pool 분리 | `EnemyController.deathDelay` + `OnDeathFinished` | 사망 모션 재생 후 풀 반납, 방 클리어는 OnDied로 즉시 |
| 사망 시 콜라이더 비활성 | `EnemyController.Die` | 시체와 추가 접촉 피해/충돌 방지 |
| 게임오버 페이드 (unscaled) | `GameOverUIController.FadeInRoutine` | Time.timeScale=0 일시정지에도 페이드 동작 |
| 추격 중 타겟 페이싱 | `EnemyAnimationController.faceTargetWhileChasing` | 근접 적이 추격 방향 흔들림 없이 항상 타겟을 바라봄 |
| 스킬 슬롯 런타임 분리 | `SkillSlotRuntime` | MonoBehaviour 미의존 — 적·보스 슬롯 재사용 가능, 새 SkillData 바인딩 시 GC 없음 |
| 스킬 타겟 버퍼 재사용 | `SkillTargetResolver._targetBuffer` | 매 프레임 호출되는 미리보기·실행 셀 계산을 단일 List로 처리 |
| 투사체 발사 코루틴 1개 | `ProjectileFireService.FireBurstRoutine` | Burst 패턴이 매 발사마다 새 코루틴을 만들지 않고 1개로 (N-1)발 처리 |
| Fog of War 셀 swap | `FogOfWarController._previousVisibleCells` ↔ `_currentVisibleCells` | 가비지 없이 visible delta 계산, SetTiles 1회로 일괄 적용 |
| Fog Tile 변경 배열 캐시 | `FogOfWarController._tileChangeArraysBySize` | 시야 셀 개수별 배열 1회 할당 후 재사용 |
| Bresenham LoS 차단 | `FogOfWarController.HasLineOfSight` | Raycast 없이 그리드 단위로 벽 너머 시야 차단 |
| 무적 셰이더 PropertyBlock | `PlayerInvincibilityFlashFeedback` | 머티리얼 클로닝 없이 _FlashAmount 보간 (인스턴싱 친화적) |
| 외부 무적 카운터 | `PlayerCombatController._externalInvincibilityCount` | 대시 등 중첩 무적을 부울이 아닌 카운터로 관리 |
| 대시 적중 1회 제한 | `PlayerDashController._hitEnemiesThisDash` | 대시 1회당 적당 1히트 보장 (HashSet) |
| 대시 path/contact 분리 | `DashDamageRequest.DamageOnPath`/`OnContact` | 경로 데미지와 종착 데미지를 독립 플래그로 분리, segment 보간(최대 16샘플) |
| 투사체 맵 범위 가드 | `ProjectileController.IsOutOfDungeonBounds` | 맵 밖으로 나간 투사체를 wall mode와 무관하게 즉시 Release |
| 8방향 조준 통합 | `AimDirectionUtility` | 입력 → raw/정규화/카디널 변환을 단일 유틸로 — GC 없음, 스킬·미리보기·발사 모두 동일 결과 |
| Fog 가시성 렌더러 | `FogVisibilityRenderer` | Renderer.enabled 만 토글해 시야 밖 적·투사체 숨김 (콜라이더·AI 영향 없음) |
| Fog 정적 Active 캐시 | `FogOfWarController.Active` | FogVisibilityRenderer 가 매 프레임 FindAnyObjectByType 없이 참조 |
| 문 개폐 즉시 시야 갱신 | `OnRoomDoorsClosed`/`OnRoomDoorsOpened` → `FogOfWarController.ForceRefresh` | 닫힌 문이 시야 차단 결과로 반영되기까지 한 프레임 지연 없음 |
| Layer 필터 정적 캐싱 | `CombatLayers.EnemyFilter`/`PlayerFilter` | LayerMask 빌드/이름 비교를 1회로 줄이고 OverlapCircle에 공유 |
| 캐릭터 물리 셋업 공통화 | `CharacterPhysicsSetup.Configure` | Player/Enemy 가 동일한 Rigidbody2D/CircleCollider2D 규약 + NoFriction PhysicsMaterial2D static 1개 공유 |
| 스킬 castDelay/recoveryDelay 잠금 | `PlayerCombatController.IsSkillBusy`/`BlocksPlayerMovement` | 캐스팅·후딜 동안 이동·기본공격·스킬 입력 차단 — 코루틴 1개 + float 1개로 처리 |
| 층 이동 시 투사체 일괄 회수 | `ProjectilePool.ReleaseAllActiveProjectiles(FloorTransition)` | 이전 층 비행 중 투사체가 신생 던전에 잔존 / 새 fog 평가에 끼어드는 문제 방지 |
| 투사체 회전 모드 분기 | `ProjectileRotationMode.FaceMoveDirection` | Bounce 후에도 sprite가 진행 방향 유지 — Atan2 1회 / 매 비행 프레임 RefreshVisualRotation |
| PerfStage using-scope | `using (PerfStage.Begin(name))` | `RuntimePerfLogger.IsActive == false` 일 때 zero-alloc passthrough (string 조합/MarkEvent 모두 생략) |
| Corridor carving 사전 검증 | `DrawLCorridor` interior/perim/perim+1 충돌 검사 + alternate axis 재시도 | path 후보를 한 번 emit해서 검증 후 carve — 다른 방을 뚫고 지나가는 통로를 사전에 차단 |
| Kiting 다중 후퇴 방향 | `s_KitingRotations` (away → away±45° → side±90°) | 막혔을 때도 폴백 후보로 후퇴 시도, footprint 통과 첫 후보 채택 |
| Random minR 안전판 | `MovementHandler.TickRandomMovement` | `minR = max(radius*0.25, footprintRadius+0.1)` — 자기 위치 위에 목적지 찍힘 방지 |
| 정지 상태 separation step | `MovementHandler.TryApplyIdleSeparationStep` | Kiting/Random 대기 중에도 이웃이 가까우면 separation 인프라 재사용해 가상 target으로 1회 이동 (0 할당) |
| 결정론적 방 스폰 시드 | `DeterministicSeedUtility.CreateSeed` + `RoomInfo.StableRoomKey` | 방마다 globalSeed/dungeonType/floor/StableRoomKey/domain FNV-1a 해시로 `System.Random` 생성 — UnityEngine.Random 오염에 흔들리지 않음 |
| 스폰 정렬 후 결정론 셔플 | `RoomSpawner.SortSpawnTiles` / `CompareEnemyDataDeterministic` | 타일·EnemyData 후보를 안정 정렬 후 `roomRng.Next` 로 선택, ScriptableObject 로드 순서에 무관한 재현성 |
| 적 블로커 정적 캐시 | `MovementBlockerQuery.s_BlockerBuffer` + `s_EnemyCache` | OverlapCircle 결과를 Collider2D→EnemyController 사전 매핑으로 재사용, 매 이동 검사 0 할당 |
| 분리 벡터 OverlapCircle throttle | `MovementHandler.SeparationQueryInterval=0.1s` | 결과만 캐시, Lerp 평활화는 매 프레임 유지 — N×N OverlapCircle 부담 감소 |
| EnemyController LateUpdate 좌표 skip | `EnemyController.LateUpdate` | 이전 안전 좌표와 동일 시 4-corner footprint 검사 건너뜀 |
| 슬로우 만료 시에만 재계산 | `EnemyController.TickSlowEffects` | Percentage 기반이므로 timer 감소만으로는 강도가 바뀌지 않음 — 만료/추가 시점에만 RecalculateStrongestSlow |
| 기존 CircleCollider 보존 | `CharacterPhysicsSetup.Configure` | Circle 이 이미 있으면 radius/offset/material 을 덮어쓰지 않음 — 프리팹별 충돌 범위 커스터마이즈 가능 |
| EXTRA 통로 다중 후보 점수화 | `BuildExtraPathCandidatesForPair` + `ConnectExtraCorridors` | pair 마다 `ExtraCandidateCount` 후보를 생성·검증한 뒤 `ExtraOverlapScoreWeight`·`ExtraPathLengthPenaltyWeight`·`ExtraCenterDistancePenaltyDivisor` 기반 점수로 가장 깨끗한 1개만 채택 — 외곽 우회·끊긴 통로 발생률 감소 (과거의 parallel run 항목은 단순화로 제거) |
| EXTRA skip 시 connectedPairs 보존 | `DrawLCorridor` 의 bool 반환 + 호출자 분기 | optional skip 시 잘못된 logical 연결 상태 누적 방지 |
| 통로의 방 perimeter/모서리 검증 | `PathCarvesRoomPerimeter` / `PathUsesRoomCornerDoorway` | EXTRA 통로가 다른 방 테두리 ROOM 셀이나 모서리 doorway 를 횡단하지 못하도록 사전 차단 |
| Door 축 ClampDoorAxis | `DrawHorizontalCorridor` / `DrawVerticalCorridor` | 동일 door 축 재사용 시 sy/ey/sx/ex 를 방 범위에 강제 정렬 — 통로가 방 밖으로 새는 케이스 방지 |
| Contact Special Attack 상태머신 | `EnemyActionHandler` `_specialPhase` (None→Windup→Rush/Jump→Recovery) | Rush 경로 1회 제한 (`_rushHitTargets` HashSet), Jump 착지점 사전 결정 (`TryResolveJumpTarget`), 사망 시 `HandleDeathStarted` 로 일괄 정리 |
| Special Attack 페이싱 잠금 | `EnemyAnimationController.LockFacing` / `UnlockFacing` | Rush/Jump 진행 방향으로 sprite 고정, `faceMoveDirection`/`faceTargetWhileChasing` 무시 — Special 종료 시 자동 해제 |
| Special Animator 트리거 폴백 | `SetTriggerOrAttack` + `_hasChargeTrigger`/`Rush`/`Jump`/`Land` 사전 캐싱 | Animator 에 Charge/Rush/Jump/Land 파라미터가 없는 적은 자동으로 AttackTrigger 폴백 — 기존 프리팹 호환 |
| 적 공격 임팩트 통합 진입점 | `PlayerCombatController.ApplyEnemyCombatImpact` + `EnemyActionHandler.ApplyEnemyImpactToTarget` | Rush 경로 / Jump 임팩트 / 적 projectile 적중이 단일 메서드로 데미지·넉백·슬로우·스턴을 일괄 적용 — IDamageable 캐스팅 한 번으로 분기 |
| 적 공격 임팩트 데이터 구조화 | `EnemyAttackImpactData` (knockback·slow·stun) | EnemyData 의 rush/jump/projectile 각 임팩트 그룹이 같은 struct 를 공유, `EffectiveSlowMultiplier` 로 보호 검사 |
| 슬로우 다중 적용 → 최대 강도 1개만 | `PlayerCombatController._enemySlows` + `RecalculateEnemySlowMultiplier` | 여러 적이 동시에 슬로우를 가해도 가장 강한 multiplier 하나만 반영, 만료 시점에만 재계산 |
| 스턴 중 입력·조준 캐시 동결 | `PlayerCombatController.RefreshAimDirection` + `PlayerController.Update` 분기 | 스턴 동안 `_lastAimDirection` 을 유지하고 이동/공격/방향 전환을 차단 |
| 플레이어 넉백 코루틴 1개 | `EnemyKnockbackRoutine` + `PlayerController.TryApplyExternalDisplacement` | 중복 적용 시 이전 코루틴 stop 후 재시작, Lerp 변위는 `MoveWithCollision` 과 동일한 footprint 검사 통과 |
| `isStationary` 적 이동 정지 | `EnemyData.isStationary` + `EnemyBrain.CurrentMoveSpeed=0` + `Rigidbody.FreezeAll` | AI 추적·공격은 정상 동작하나 위치 변화 / Separation / Kiting / Random 모두 zero-cost path |
| `immuneToKnockback` 임펄스 skip | `EnemyController.ApplyKnockback` 게이트 | 데미지·슬로우·스턴은 그대로 적용하고 velocity=0 후 return — clamp/CircleCast 비용 0 |
| RoomSpawner 참조 SerializeField 캐싱 | `DungeonManager.roomSpawner` + `TryGetRoomSpawner` 1회 경고 | `ResetRoomEncounterState` / `ClearPendingRoomStart` 가 매번 `FindAnyObjectByType` 호출하던 비용 제거 |
| Wall 레이어 마스크 캐시 | `CombatLayers.WallMask`/`WallFilter` (`Wall`/`Obstacle` 이름 폴백) | `EnemyController.ClampKnockbackForceAgainstWall` 등이 매 호출마다 `LayerMask.GetMask` 호출 없이 정적 마스크 재사용 |
| GameOver 자동 빌드 fallback 제거 | `GameOverUIController._warnedMissingReferences` | 인스펙터 미설정 시 1회 경고만 출력하고 표시 skip — 런타임에 새 GameObject 생성 코드(~66줄) 삭제 |
| 상태이상 UI 정적 액티브 바인딩 | `PlayerStatusEffectUI.TryBindCombat` + `PlayerCombatController.Active` | 매 프레임 FindAnyObjectByType 없이 OnEnable / 첫 Update 에서 1회 바인딩, OnDisable 시 자동 unsubscribe |

---

## 13. 데이터 흐름

### 던전 생성 데이터 흐름

```
DungeonSettings (설정값)
        │
        ▼
DungeonGenerator.GenerateDungeon()
        │ int[,] grid + RoomRect[]
        ▼
DungeonData (그리드 + 방 목록 보관)
        │
        ├──▶ RoomRegistry.Initialize()
        │         └── 방 타입 결정 (Normal / Stair)
        │
        ├──▶ SpawnPositionService.Compute()
        │         └── 플레이어 스폰 좌표 캐싱
        │
        └──▶ DungeonTilemapRenderer.PlaceTiles()
                  └── Tilemap에 타일 배치
```

### 전투 데이터 흐름

```
WeaponData → PlayerCombatController
                 │ AttackPattern.GetTargets()
                 │ AttackExecutor.Execute()
                 │   Physics2D.OverlapCircleNonAlloc()
                 ▼
           IDamageable.TakeDamage()
                 │
                 ├──▶ EnemyController
                 │         ├── EnemyHealthBar.SetHp()
                 │         └── CombatEventChannel.RaiseEnemyKilled()
                 │                   └──▶ RoomSpawner.CheckRoomClear()
                 │
                 └──▶ PlayerCombatController (적이 공격 시)
                           ├── PlayerResource (HP/MP 갱신)
                           ├── CombatEventChannel.RaisePlayerHpChanged()
                           └── CombatEventChannel.RaisePlayerMpChanged()
                                     └──▶ PlayerStatusBarUI (UI 갱신)
```

### 방 클리어 흐름

```
PlayerController.CheckRoomEntry()
  → DungeonEventChannel.RaiseNormalRoomEntered()
      └──▶ RoomSpawner.OnRoomEntered()
                ├── 적 스폰 (EnemyPoolManager)
                └── DungeonManager.CloseDoors()

EnemyController.Die()
  → CombatEventChannel.RaiseEnemyKilled()
      └──▶ RoomSpawner.OnEnemyKilled()
                └── 모든 적 사망 확인
                      └── DungeonManager.OpenDoors()
```

---

## 14. 확장 포인트

### 새 공격 패턴 추가

```csharp
// AttackPattern.cs
public enum AttackPatternType { ..., Ring }

case AttackPatternType.Ring:
    for (int r = 2; r <= range; r++)
        foreach (var d in s_Cardinals) targets.Add(origin + d * r);
    break;
```

WeaponData / SkillData Inspector 드롭다운에 자동으로 추가됩니다.

### 새 무기 / 스킬 추가

에디터에서 `Create > JBLogLike > Combat > Weapon` 또는 `Skill` 에셋 생성 후 수치 입력. 코드 수정 불필요.

`SkillData.executionType` 으로 실행 형태 선택:
- `InstantArea` — AttackPattern 기반 즉시 범위 공격
- `Projectile` — projectilePrefab + ProjectileFireService 패턴 (Single/Burst/Spread/Circle)
- `Dash` — PlayerDashController 코루틴 (선택적 무적·경로 데미지)
- `AreaOverTime` / `Buff` — enum만 정의, 핸들러 미구현 (확장 자리)

### 새 스킬 실행 타입 추가

```csharp
// SkillExecutionType.cs 에 enum 값 추가 (예: AreaOverTime)
// SkillExecutor.Execute() switch 에 분기 추가
case SkillExecutionType.AreaOverTime:
    return ExecuteAreaOverTime(context);
```

`SkillExecutionContext` 가 caster·aim·grid·totalAttack·hitRadius 를 모두 보유하므로 새 핸들러는 컨텍스트만 받아 실행하면 됩니다.

### 새 적 타입 추가

1. `EnemyData` ScriptableObject 생성 (수치 입력)
   - `behaviorType`: Contact / Ranged
   - Contact 적에 Rush/Jump 특수 공격을 부여하려면 `specialAttackType` 설정 + 전용 파라미터 입력
   - Ranged 적은 `rangedMovementType`(Chase/Kiting/Random) + 투사체 패턴 설정
   - `rushImpact` / `jumpImpact` / `projectileImpact` (`EnemyAttackImpactData`) — knockback / slow / stun 부가 효과 입력
   - 필요 시 이동 플래그 토글: `isStationary` (위치 고정), `immuneToKnockback` (임펄스 무시)
2. 프리팹에 `EnemyController` + `EnemyHealthBar` + `Collider2D` 부착
3. `NormalEnemyBrain` 부착 또는 `EnemyBrain` 상속 후 커스텀 FSM 구현
   - Charge/Rush/Jump/Land Animator 트리거가 없는 프리팹은 자동으로 AttackTrigger 폴백 (호환)
4. `EnemyPoolManager`에 프리팹 등록

### 새 플레이어 상태이상 추가

```csharp
// PlayerStatusEffectType.cs
public enum PlayerStatusEffectType { Slow, Stun, Burn /* 새 항목 */ }

// PlayerCombatController:
//   ① 전용 타이머/리스트 필드와 IsBurning/Remaining/Ratio 프로퍼티 추가
//   ② ApplyEnemyCombatImpact 시그니처 또는 EnemyAttackImpactData 에 burnDuration 추가
//   ③ TickEnemyBurn(deltaTime) 호출 + 만료 시 OnStatusEffectEnded 발행
//   ④ ClearEnemyImpactState 에서 해제
//
// PlayerStatusEffectUI:
//   ⑤ SerializeField StatusEffectIconView burnIconView 추가 + GetView/SyncIcon/RefreshIcon switch 분기
```

### 새 이벤트 추가

`DungeonEventChannel.cs` 또는 `CombatEventChannel.cs`에 `event Action<T>` 선언 + `Raise()` 추가. 발행자·구독자 코드는 수정 불필요.

---

## 15. 개발 현황

### 완료

| 시스템 | 세부 내용 |
|--------|-----------|
| **던전 생성** | BSP 분할, Prim MST 통로, L자형 통로, 계단 배치 |
| **결정론적 생성** | Seed + Floor 파생 시드 (재현 가능) |
| **층 이동** | 비동기 코루틴 + 청크 Tilemap + 로딩 화면 (FloorTransitionService) |
| **플레이어 이동** | 8방향 + 코너 충돌 슬라이딩 + 대각선 자동 슬라이딩(TrySlideWithNudge) |
| **플레이어 충돌 안전장치** | Rigidbody2D 물리 기반 ConfigurePhysics + LateUpdate 위치 복원 |
| **플레이어 입력** | PlayerInputReader 단일 집계, 실행 순서 보장 |
| **방 진입 감지** | 이벤트 발행, 최초 방문 구분 |
| **문 시스템** | 방 진입 시 닫힘, 클리어 시 열림 |
| **계단 상호작용** | Z키, 쿨다운 포함 |
| **전투 데이터 구조** | WeaponData, SkillData, EnemyData (ScriptableObject) |
| **공격 패턴 시스템** | 6종 패턴, 데이터 드리븐 |
| **플레이어 전투** | 기본 공격, 스킬 4슬롯, HP/MP 관리 (PlayerResource) |
| **공격 판정 분리** | AttackExecutor — 히트 감지·데미지 적용 독립 처리 |
| **스킬 실행 라우팅** | SkillExecutor — InstantArea/Projectile/Dash/AreaOverTime/Buff 분기 |
| **스킬 슬롯 런타임 분리** | SkillSlotRuntime — MonoBehaviour 미의존 슬롯 상태(데이터·쿨다운) |
| **스킬 타겟 공통화** | SkillTargetResolver — 미리보기·기본공격·스킬이 동일한 셀 계산 사용 |
| **스킬 실행 컨텍스트** | SkillExecutionContext — caster/aim/grid/totalAttack/hitRadius 일체 전달 |
| **쿨다운 관리** | 기본 공격은 SkillCooldownController, 스킬은 SkillSlotRuntime이 보유 |
| **발사체 시스템** | 직선 이동, 벽/유닛 충돌, 관통 옵션 |
| **투사체 발사 공통화** | ProjectileFireService — 적 원거리·플레이어 스킬이 Single/Burst/Spread/Circle 동일 처리 |
| **투사체 타겟 정책** | ProjectileTargetHitMode — DestroyOnHit / Pierce / HitOncePerTarget |
| **플레이어 투사체 스킬** | SkillData.executionType=Projectile — prefab/속도/수명/패턴/벽반사 인스펙터 설정 |
| **플레이어 대시 스킬** | PlayerDashController — 발자국 검사 이동, 경로 데미지·무적 옵션, 외부 무적 카운터 |
| **외부 무적 시스템** | BeginExternalInvincibility/EndExternalInvincibility — 다중 출처(대시 등) 무적 중첩 처리 |
| **무적 셰이더 플래시** | PlayerInvincibilityFlashFeedback — MaterialPropertyBlock 기반 _FlashAmount 보간 |
| **일반 피격/외부 무적 분리** | HitFlashFeedback(피격 색상) ↔ PlayerInvincibilityFlashFeedback(셰이더) 독립 |
| **투사체/대시 미리보기** | SkillRangePreviewer — Projectile은 발사 패턴별, Dash는 거리 + 벽 클리핑 |
| **기본 공격 미리보기** | Space 홀드 시 무기 attackPattern 시각화 (스킬 미리보기와 우선순위 분리) |
| **Fog of War** | FogOfWarController — 미탐사/탐사/현재시야 3상태, Bresenham LoS, 닫힌 문 시야 차단 |
| **적 전투** | IDamageable, 방어력 계산, 사망 처리 |
| **적 체력바** | 실시간 갱신, 색상 그라디언트, 자동 숨김 |
| **적 AI (FSM)** | Idle/Chase/Attack 상태, A* 경로탐색, 군중 분리 |
| **적 상태이상** | 넉백, 슬로우 (지속시간 기반) |
| **적 스폰 시스템** | 방 진입 트리거, 예산 기반 스폰, 방 클리어 감지 |
| **오브젝트 풀링** | EnemyPoolManager (적 재사용) |
| **HP/MP 상태바 UI** | PlayerStatusBarUI — 슬라이더 + 텍스트, 이벤트 구독 갱신 |
| **스킬 UI** | 4슬롯 아이콘·쿨타임 표시 |
| **스킬 범위 미리보기** | 키 홀드 시 LineRenderer로 공격 범위 시각화 |
| **이벤트 버스** | DungeonEventChannel, CombatEventChannel |
| **던전 서비스 분리** | DungeonQueryService, SpawnPositionService, FloorTransitionService |
| **던전 타일맵 레이어 분리** | 바닥/벽/문 3개 Tilemap 분리, SetTilesBlock 배치 배치 |
| **wallTilemap 물리 콜라이더** | TilemapCollider2D 부착 — Rigidbody2D 레벨 벽 충돌 |
| **MonsterDen 방 타입** | 높은 적 밀도 전투 방, 예산 배율(×2.5) 적용 |
| **지연 전투 시작** | 플레이어가 문 위에 걸친 채 방 진입 시 안전해질 때까지 전투 시작 보류 |
| **방 전투 타이밍 동기화** | `CanStartRoomEncounter` — 9-포인트 샘플링 + 문 타일 겹침 검사 |
| **공격 시야 차단** | `AttackExecutor.HasWallBetween` — 벽 너머 공격 판정 차단 |
| **공격 다중/단일 타겟** | `isMultiTarget` 플래그 — 전체 히트 or 최근접 단일 히트 |
| **공격 상태이상 파라미터** | `ExecuteAttack`에 knockback/slow 파라미터 통합 |
| **원거리 적 AI** | `EnemyBehaviorType.Ranged` — 사거리·선딜·후딜 사이클 + 조준 방향 보정 |
| **투사체 발사 패턴** | Single/Burst/Spread/Circle (projectileCount, spreadAngle, burstInterval) |
| **투사체 풀링** | `ProjectilePool` — 사전 풀링 + DisableComponents 모드로 SetActive 토글 회피 |
| **투사체 벽 처리** | Destroy/PassThrough/Bounce — 벽 반사는 X/Y 축별 분리, maxBounceCount 제한 |
| **투사체 비행 애니메이션** | Animator "Fly" 클립 + ProjectileController.PrepareFromPool |
| **접촉 피해 시스템** | `Collider2D.Distance` 기반 + `contactDamageRadius`/`contactDamageSkin` 폴백 |
| **플레이어 피격 무적시간** | `damageInvincibleDuration` — 다중 피해/접촉 피해 무한 누적 차단 |
| **피격 시각 피드백** | `HitFlashFeedback` — SpriteRenderer 색상 점멸 (적·플레이어 공용) |
| **플레이어 4방향 애니메이션** | `PlayerAnimationController` — MoveX/Y, LastMoveX/Y, IsMoving |
| **적 애니메이션** | `EnemyAnimationController` — LateUpdate 기반 위치 변화 감지, 사격 시 타겟 페이싱 |
| **넉백 벽 클램핑** | `ClampKnockbackForceAgainstWall` — CircleCast + 그리드 IsWalkable 양면 검사 |
| **닫힌 문 물리 충돌** | `doorTilemap` TilemapCollider2D — 적이 닫힌 문 통과 차단 |
| **성능 트레이스 로깅** | `RuntimePerfTraceLogger` — 투사체/풀 호출 마이크로 타이밍 기록 |
| **플레이어 사망 처리** | `IsDead` 단발 처리, 입력·이동·미리보기 차단, `OnDied`/`OnPlayerDied` 이벤트 |
| **게임오버 UI 흐름** | `GameOverFlowController` → 지연 후 `GameOverUIController` 페이드 인 → 확인 시 씬 재로드 |
| ~~**게임오버 UI 자동 빌드**~~ | (제거됨) `BuildDefaultUi` 자동 생성 경로는 삭제되어 이제 인스펙터 참조 누락 시 경고 후 표시 skip |
| **적 사망 지연 처리** | `EnemyData.deathDelay` + `OnDeathFinished` — 사망 모션 종료 후 풀 반납, 방 클리어 판정은 즉시 |
| **추격 중 타겟 페이싱** | `EnemyAnimationController.faceTargetWhileChasing` — 근접 적의 추적 방향 흔들림 보정 |
| **Ranged 이동 분기** | `RangedMovementType.Chase/Kiting/Random` — `MovementHandler.TryTickRangedMovement`가 LOS/A* 흐름 전에 이동을 가로챔 |
| **방 진입 footprint 공통화** | `RoomFootprintSampler` — 9-sample 배치를 PlayerController·DungeonTilemapRenderer가 공유 |
| **8방향 조준 통합** | `AimDirectionUtility` — 입력 → raw/정규화/카디널 변환, 스킬·투사체·대시·미리보기 공유 |
| **대시 path/contact 분리** | `DashDamageRequest.DamageOnPath`/`OnContact` 독립 플래그 — 경로 보간 샘플링 + 종착 별도 판정 |
| **투사체 맵 범위 가드** | `ProjectileController.IsOutOfDungeonBounds` + `ProjectileReleaseReason.OutOfBounds` — 맵 밖 투사체 자동 Release |
| **Fog 가시성 렌더러** | `FogVisibilityRenderer` — Renderer.enabled 토글로 시야 밖 적·적 투사체 시각만 숨김 |
| **적 투사체 Fog 통합** | `ProjectileController.ApplyFogVisibilityForTargetMode` — Enemy projectile에만 fog 토글, 풀 회수 시 잔존 visibility 정리 |
| **문 개폐 이벤트 발행** | `DungeonEventChannel.OnRoomDoorsClosed/Opened` — FogOfWarController가 즉시 시야 재계산 |
| **Combat Layer 정적 캐시** | `CombatLayers` — Enemy/Player ContactFilter2D 공유 |
| **캐릭터 물리 공통화** | `CharacterPhysicsSetup.Configure(go, layer)` — Player/Enemy 동일 Rigidbody2D+CircleCollider2D 규약, NoFriction PhysicsMaterial2D static 캐시 |
| **스킬 castDelay/recoveryDelay** | `PlayerCombatController.IsSkillBusy`/`BlocksPlayerMovement` — 선딜·후딜 동안 이동·기본공격·스킬 입력 잠금 |
| **투사체 회전 모드** | `ProjectileRotationMode` — KeepPrefabRotation / FaceMoveDirection (기본). Bounce 이후에도 sprite가 진행 방향을 향함 |
| **층 이동 시 투사체 정리** | `ProjectilePool.ReleaseAllActiveProjectiles(FloorTransition)` — FloorTransition reason 추가, 이전 층 잔존 투사체 일괄 회수 |
| **Corridor carving 검증** | `DrawLCorridor` interior/perim/perim+1 충돌 검사 + EXTRA 전용 PathCarvesRoomPerimeter / PathUsesRoomCornerDoorway 검증 + primary/alternate axis 재시도 + mandatory(MST) vs optional(EXTRA) 분기 + bool 반환으로 connectedPairs 보존 |
| **Generator 디버그 hook** | `DungeonGenerator.DebugCorridorCarving` + `DebugSink` — MST/EXTRA 통로마다 src/dst Rect 와 path 결정 로그 |
| **DungeonGenDebug 도구** | `Tools/DungeonGenDebug` — Unity 외부 .NET 콘솔로 던전 생성 결과를 standalone 검증 |
| **PerfStage using-scope 측정** | `Tool/PerfStage.cs` — RuntimePerfLogger 비활성 시 zero-alloc passthrough, 활성 시 elapsedMs metadata 자동 기록 |
| **Skill / Enemy CustomEditor** | `Editor/SkillDataEditor` (executionType 별 섹션), `Editor/EnemyDataEditor` (Basic / Contact + Contact-Special(Rush/Jump) 또는 Ranged-Timing/Movement/Projectile / Separation-Collision / Reward-Misc / Unhandled 자동 분기) |
| **Kiting 다중 후퇴 방향** | `s_KitingRotations` 5단계 폴백 (away → away±45° → side±90°) — 후퇴가 막혀도 첫 통과 후보로 이동 |
| **Random 목적지 minR 보호** | `MovementHandler.TickRandomMovement` — `minR=max(radius*0.25, footprintRadius+0.1)` 로 자기 위 목적지 차단 |
| **정지 시 separation step** | `MovementHandler.TryApplyIdleSeparationStep` — Kiting/Random 대기 상태에서도 이웃이 가까우면 산개 |
| **결정론적 방 적 스폰** | `DeterministicSeedUtility` + `RoomInfo.StableRoomKey` + `DungeonManager.dungeonType` — 방별 `System.Random` 으로 적 종류·위치 재현성 보장 |
| **방 적 블로커 시스템** | `EnemyData.blocksMovement` + `MovementBlockerQuery` — `blocksMovement=true` 적이 플레이어 이동/대시를 막음 (AI·넉백 무관) |
| **분리 벡터 throttle** | `MovementHandler.SeparationQueryInterval=0.1s` — OverlapCircle 결과 캐시, Lerp 평활화는 매 프레임 유지 |
| **적 LateUpdate 좌표 skip** | `EnemyController.LateUpdate` — 이전 안전 좌표와 동일하면 4-corner 검사 생략 |
| **슬로우 재계산 만료 시점만** | `EnemyController.TickSlowEffects` — 만료/추가 발생 프레임에만 RecalculateStrongestSlow 호출 |
| **EnemyData 인스펙터 정리** | `EnemyDataEditor` 섹션 재편: Basic / Contact 또는 Ranged-Timing/Movement/Projectile / Separation-Collision / Reward-Misc / Unhandled 자동 분기 |
| **CharacterPhysicsSetup 보존 모드** | 기존 CircleCollider 가 있으면 radius/offset/material 보존 — 프리팹별 충돌 범위 커스터마이즈 가능 |
| **Ranged 적 03/04/05** | `RangedEnemy03/04/05` 프리팹 + RangeEnemy03/04/05.asset 추가 (FirePattern/이동 타입 별 변형) |
| **Contact Special Attack (Rush)** | `EnemySpecialAttackType.Rush` — Windup→Rush→Recovery 상태머신, 경로 위 타겟 1회 데미지(`_rushHitTargets`), 페이싱 잠금, CanOccupy 실패 시 즉시 Recovery |
| **Contact Special Attack (Jump)** | `EnemySpecialAttackType.Jump` — Windup 진입 시 `TryResolveJumpTarget`으로 착지점 사전 결정(`jumpStayInRoom` 옵션), Lerp 보간 비행, 착지 시 `jumpImpactRadius` 임팩트 데미지 + Land 애니메이션 |
| **Contact Special 적 프리팹** | `RushEnemy01`, `JumpEnemy01` 프리팹 + 전용 Animator(Charge/Rush/Jump/Land 트리거) + EnemyData 에셋 |
| **Special Animator 폴백** | `EnemyAnimationController.SetTriggerOrAttack` — Charge/Rush/Jump/Land 파라미터 없는 적은 AttackTrigger 폴백 |
| **Special Facing Lock** | `EnemyAnimationController.LockFacing/UnlockFacing` — Rush/Jump 진행 방향으로 sprite 고정, 자동 페이싱 보정 무시 |
| **사망 시 Brain 정리** | `EnemyBrain.HandleDeathStarted` — Special 상태머신/페이싱 잠금/핸들러 런타임 상태를 사망 즉시 정리, `EnemyController.Die`에서 호출 |
| **EnemyData Contact-Special 인스펙터** | `EnemyDataEditor.DrawContactSpecialSection` — `specialAttackType` 별로 Rush/Jump 전용 필드 그룹만 노출 |
| **EXTRA 통로 다중 후보 점수화** | `BuildExtraPathCandidatesForPair` + `ConnectExtraCorridors` — pair 마다 `ExtraCandidateCount`(기본 12) 후보를 검증·점수화하여 가장 깨끗한 1개 채택 |
| **EXTRA 점수 weight 인스펙터화** | `DungeonManager.extraOverlapScoreWeight`/`extraPathLengthPenaltyWeight`/`extraCenterDistancePenaltyDivisor` — 점수 함수 가중치를 인스펙터로 노출, 과거 `LongestParallelCorridorRun` 항목은 단순화로 제거 |
| **플레이어 상태이상 시스템** | `PlayerStatusEffectType`(Slow/Stun) + `PlayerCombatController.ApplyEnemyCombatImpact` — 적 공격에서 받는 데미지·넉백·슬로우·스턴을 단일 진입점으로 처리, 슬로우는 활성 효과 중 가장 강한 강도만 적용, 스턴 중 이동·방향 전환·스킬 입력 차단 |
| **플레이어 상태이상 아이콘 UI** | `PlayerStatusEffectUI` + `StatusEffectIconView` — 슬로우/스턴 아이콘과 잔여 시간 게이지·텍스트 표시, `OnStatusEffectApplied/Ended` 이벤트 구독 |
| **적 공격 임팩트 데이터화** | `EnemyAttackImpactData`(knockback·slow·stun) struct — `rushImpact`/`jumpImpact`/`projectileImpact` 가 공유, `EnemyActionHandler.ApplyEnemyImpactToTarget` 단일 라우팅 |
| **`isStationary` / `immuneToKnockback` 플래그** | `EnemyData` — 위치 고정 적과 넉백 면역 적 구현 (데미지·상태이상은 그대로 적용) |
| **CombatLayers Wall 마스크 추가** | `WallMask`/`WallFilter`/`HasWallLayer` — `Wall`/`Obstacle` 이름 자동 폴백으로 knockback clamp 등의 LayerMask 호출 정적 캐시화 |
| **RoomSpawner 참조 SerializeField 캐싱** | `DungeonManager.roomSpawner` + `TryGetRoomSpawner` — `FindAnyObjectByType` 제거, 누락 시 1회 경고 |
| **GameOver UI 자동 빌드 제거** | `BuildDefaultUi` 경로 삭제, 인스펙터 미설정 시 1회 경고만 출력하고 표시 skip |
| **EXTRA 통로 외곽 우회 방지** | `DrawLCorridor`가 EXTRA(optional)에서는 primary/alternate 모두 충돌 시 skip + bool 반환 — `connectedPairs` 보존으로 잘못된 logical 상태 누적 차단 |
| **방 perimeter / 모서리 doorway 검증** | `PathCarvesRoomPerimeter`, `PathUsesRoomCornerDoorway` — 통로가 다른 방 테두리 ROOM 셀이나 모서리 doorway 를 횡단하지 못하도록 사전 차단 |
| **DungeonGenDebug `--scene-settings`** | Unity 외부 콘솔에서도 실제 씬 설정(120×80, room 10–50)으로 시뮬레이션 가능, `RoomPerimeterCorridorScan`/`CornerDoorwayScan` 출력 추가 |
| **Generator 디버그 connect-state 로그** | `DebugConnectState` + `DebugReachableRoomsFromR0` — 단계마다 connected/remaining/reachable(BFS) 비교로 logical-only / grid-only 불일치 추적 |
| **DungeonSettings.ExtraCandidateCount** | 인스펙터 `DungeonManager.extraCandidateCount` (기본 12) — pair 당 EXTRA 후보 생성·점수화 개수 노출 |
| **성능 최적화** | NonAlloc 물리, A* 버퍼 재사용, 오브젝트 풀, 청크 로딩, 문 배치 N→1 |

### 미구현 (다음 단계)

| 항목 | 우선순위 | 비고 |
|------|----------|------|
| AreaOverTime 스킬 핸들러 | 중간 | SkillExecutionType enum 자리 마련, SkillExecutor에 분기만 추가하면 됨 |
| Buff 스킬 핸들러 | 중간 | 동일 — caster 자체에 효과를 적용하는 형태 |
| 아이템 / 장비 드랍 | 중간 | OnEnemyKilled 이벤트 활용 |
| 보스 / 에픽 적 패턴 | 중간 | EnemyBrain 상속 + Phase2/Berserk 상태 enum 자리 마련됨 |
| 적 스킬 발사기 통합 | 낮음 | ProjectileFireService를 적 EnemyBrain 액션 핸들러에서도 직접 호출하도록 통합 |
| 상태이상 시스템 확장 | 낮음 | 독, 빙결 등 StatusEffectData 추가 |
| 세이브 / 로드 | 낮음 | Seed 기반 재현으로 부분 대체 가능 |
| 보스 룸 | 낮음 | RoomType.Boss 추가 후 RoomRegistry 확장 |
| MonsterDen 방 타입 등록 | 낮음 | RoomRegistry에서 자동 분류 조건 추가 필요 |
| SkillData dash 툴팁 정정 | 낮음 | `dashDamageOnPath`/`OnContact` 인스펙터 툴팁이 아직 "first-pass implementation shares the same detection path"로 남아 있음 — 실제 구현은 분리됨 |

---

*본 문서는 현재 master 브랜치 기준이며, 개발 진행에 따라 갱신됩니다.*
