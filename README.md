# JBRogLike — 아키텍처 보고서

> 작성 기준일: 2026-05-06  
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
| 이동 방식 | 실시간 8방향 이동 + 그리드 충돌 |
| 전투 방식 | 실시간, 패턴 기반 범위 공격 + 스킬 4슬롯 |
| 방 타입 | Normal · MonsterDen · Spawn · Stair |
| 적 AI | FSM (Idle → Chase → Attack), A* 경로탐색, Contact/Ranged 행동 분기 |
| 적 전투 | 근접 접촉 피해 + 원거리 투사체 (Single/Burst/Spread/Circle) + 벽 반사 |
| 진행 방식 | 계단을 통한 층 이동 (무한 층 구조) |

---

## 2. 레이어 아키텍처

전체 시스템은 **Clean Architecture** 원칙에 따라 4개 레이어로 분리되어 있습니다.

```
┌──────────────────────────────────────────────────────────────┐
│  Application Layer (MonoBehaviour)                           │
│  PlayerController · PlayerInputReader                        │
│  PlayerCombatController · SkillCooldownController            │
│  PlayerAnimationController                                   │
│  DungeonManager · FloorTransitionService                     │
│  EnemyBrain · NormalEnemyBrain · RoomSpawner                 │
│  ProjectilePool · ProjectileController                       │
│  GameOverFlowController · GameOverSceneReloadRestartHandler  │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure Layer (ScriptableObject Event Bus)           │
│  DungeonEventChannel · CombatEventChannel                    │
├──────────────────────────────────────────────────────────────┤
│  Domain Layer (순수 C# — Unity 의존 없음)                     │
│  DungeonData · DungeonGenerator · RoomRegistry               │
│  DungeonQueryService · SpawnPositionService                  │
│  WeaponData · SkillData · EnemyData                          │
│  PlayerResource · AttackPattern · AStarPathfinder            │
├──────────────────────────────────────────────────────────────┤
│  Presentation Layer                                          │
│  DungeonTilemapRenderer · DoorController                     │
│  EnemyHealthBar · PlayerStatusBarUI                          │
│  SkillSlotUI · SkillUIManager · SkillRangePreviewer          │
│  HitFlashFeedback · EnemyAnimationController                 │
│  GameOverUIController                                        │
└──────────────────────────────────────────────────────────────┘
```

### 핵심 설계 원칙

- **단방향 의존**: 상위 레이어만 하위 레이어를 알고, 역방향 참조 없음
- **이벤트 기반 통신**: 레이어 간 직접 참조 대신 ScriptableObject EventChannel 사용
- **데이터 주입 (ScriptableObject)**: 무기/스킬/적의 수치는 에셋으로 분리, 코드 수정 없이 교체 가능
- **FSM 분리**: EnemyBrain의 상태·이동·타겟·액션을 Handler로 분리해 결합도 최소화
- **책임 분리 (SRP)**: DungeonManager의 기능을 서비스 클래스로 추출 (FloorTransitionService, SpawnPositionService, DungeonQueryService)
- **GC 최소화**: 이벤트 인자에 `struct` 사용, 코루틴 캐싱, NonAlloc 물리, A* 버퍼 재사용

---

## 3. 파일 구조

```
Assets/Scripts/
│
├── PlayerController.cs             # 입력·이동·방 감지
├── PlayerInputReader.cs            # 키보드 입력 단일 집계 (실행 순서 제어)
├── PlayerAnimationController.cs    # 4방향 이동 애니메이션 (MoveX/Y, LastMoveX/Y)
│
├── DungeonManager.cs               # 던전 생애주기 조율 (Facade)
├── DoorController.cs               # 문 열기 위임 (DungeonManager로 라우팅)
│
├── Data/
│   ├── DungeonData.cs              # 타일 그리드 + 방 목록 (Domain)
│   ├── WeaponData.cs               # 무기 ScriptableObject
│   ├── SkillData.cs                # 스킬 ScriptableObject
│   └── EnemyData.cs                # 적 ScriptableObject (Contact/Ranged + 투사체 패턴)
│
├── Generate/
│   ├── DungeonGenerator.cs         # BSP + Prim MST 생성 알고리즘 (순수 C#)
│   ├── DungeonTypes.cs             # 공유 타입 (RoomType, RoomInfo, 이벤트 인자)
│   ├── DungeonEventChannel.cs      # 던전 이벤트 버스 (ScriptableObject)
│   ├── DungeonQueryService.cs      # 그리드 유틸리티 (IsWalkable, 좌표 변환)
│   ├── SpawnPositionService.cs     # 플레이어 스폰 좌표 계산 서비스
│   ├── FloorTransitionService.cs   # 층 이동 코루틴·로딩 화면·GC 관리
│   ├── RoomRegistry.cs             # 방 상태 관리 (타입·문 닫힘)
│   ├── DungeonTilemapRenderer.cs   # Tilemap 3레이어 배치 (바닥·벽·문)
│   ├── SpawnRegion.cs              # 스폰 지역 플래그 (Dungeon/Forest/Castle)
│   └── RoomSpawner.cs              # 방 진입 시 적 스폰, 방 클리어 감지
│
├── Combat/
│   ├── IDamageable.cs              # 피해 수신 인터페이스
│   ├── AttackPattern.cs            # 공격 패턴 enum + 좌표 계산기
│   ├── AttackExecutor.cs           # 공격 판정·히트 감지·데미지 적용
│   ├── PlayerCombatController.cs   # 플레이어 전투 진입점 (HP·MP·공격·스킬·무적시간)
│   ├── SkillCooldownController.cs  # 기본 공격·스킬 4슬롯 쿨다운 관리
│   ├── PlayerResource.cs           # HP·MP 상태 컨테이너 (Domain)
│   ├── HitFlashFeedback.cs         # 피격 시 SpriteRenderer 색상 점멸 (적·플레이어 공용)
│   ├── Projectile.cs               # (구) 플레이어 스킬용 트리거 기반 발사체
│   ├── ProjectileController.cs     # (신) 적 원거리용 풀링 발사체 — 벽 반사·관통·파괴
│   ├── ProjectilePool.cs           # 투사체 사전 풀링 (SetActive/DisableComponents 모드)
│   └── CombatEventChannel.cs       # 전투 이벤트 버스 (ScriptableObject)
│
├── Enemy/
│   ├── EnemyController.cs          # 적 HP·피해·사망·상태이상·넉백 벽 클램핑
│   ├── EnemyBrain.cs               # FSM 조율 추상 + MovementHandler/TargetHandler/ActionHandler
│   │                               #   (Idle/Attack 상태 nested + 원거리 BeginAttack/TickAttack)
│   ├── NormalEnemyBrain.cs         # 기본 몬스터용 경량 Brain (커스텀 상태 없음)
│   ├── NormalEnemyAI.cs            # (호환 유지용 보조 컴포넌트)
│   ├── ChaseState.cs               # A* 기반 추격 상태
│   ├── AStarPathfinder.cs          # GC 최소화 A* 탐색기
│   ├── EnemyHealthBar.cs           # 머리 위 체력바 렌더러
│   ├── EnemyAnimationController.cs # 적 이동/공격/사망 애니메이션 + 사격 방향 페이싱
│   └── EnemyPoolManager.cs         # 적 오브젝트 풀
│
├── UI/
│   ├── PlayerStatusBarUI.cs        # 플레이어 HP·MP 상태바 (슬라이더 + 텍스트)
│   ├── SkillSlotUI.cs              # 스킬 슬롯 1개 렌더링 (아이콘·쿨타임)
│   ├── SkillUIManager.cs           # 4슬롯 초기화·층 변경 갱신
│   ├── SkillRangePreviewer.cs      # Q/W/E/R 스킬 범위 미리보기 (LineRenderer)
│   ├── GameOverFlowController.cs   # 사망 이벤트 구독 → 지연 후 게임오버 UI 표시
│   ├── GameOverUIController.cs     # 게임오버 UI 빌드·페이드 인/아웃·확인 버튼
│   ├── GameOverRestartHandler.cs   # IGameOverRestartHandler 인터페이스
│   └── GameOverSceneReloadRestartHandler.cs # 활성 씬 재로드로 재시작
│
├── Debug/
│   └── RuntimePerfTraceLogger.cs   # 투사체/풀 호출 마이크로 타이밍 트레이스
│
└── Tool/
    ├── RuntimePerfLogger.cs        # 성능 타이밍 로거 (호환 레이어)
    ├── YieldCache.cs               # 코루틴 YieldInstruction 캐시
    └── LoadingScreenController.cs  # 층 이동 로딩 화면
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

### 4-4. 방 연결 알고리즘 (Prim's MST + 추가 연결)

```
ConnectAll():
  connected = { 방0 }
  remaining = { 방1, 방2, ... }

  while remaining이 비지 않을 때:
    ── MST 단계 ────────────────────────────────────────
    connected × remaining 쌍 중 유클리드 거리 최소 → src, dst
    DrawLCorridor(src, dst)       ← L자형 통로 연결
    connected ← dst 추가, remaining ← dst 제거

    ── 추가 연결 단계 (ExtraConnProb 확률) ─────────────
    src 기준 dst를 제외한 가장 가까운 방 k 탐색
    DrawLCorridor(src, k)
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
| `OnRoomEntered` | PlayerController | RoomSpawner |
| `OnNormalRoomEntered` | PlayerController | — (미사용, 예약) |
| `OnSpawnRoomEntered` | PlayerController | — |
| `OnStairRoomEntered` | PlayerController | — |
| `OnFloorChanged` | DungeonManager | PlayerController, RoomSpawner, SkillUIManager |

> **참고**: `DoorController`는 이벤트를 구독하지 않습니다. 문 개폐는 `RoomSpawner` → `DungeonManager.CloseCurrentRoomDoors / OpenCurrentRoomDoors`로 직접 호출됩니다.

### CombatEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnEnemyKilled(EnemyController)` | EnemyController | RoomSpawner (방 클리어 판정) |
| `OnPlayerHpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnPlayerMpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnPlayerDied(PlayerCombatController)` | PlayerCombatController | GameOverFlowController |
| `OnSkillUsed(SkillData)` | PlayerCombatController | SkillSlotUI (쿨다운 표시) |

---

## 6. 시스템 3 — 플레이어 이동

### 6-1. 물리 설정 (ConfigurePhysics)

`Start()`에서 Rigidbody2D와 CircleCollider2D를 코드로 자동 설정합니다.

| 컴포넌트 | 설정값 |
|---------|-------|
| Rigidbody2D | Dynamic · gravityScale=0 · Continuous · Interpolate · NoFriction |
| CircleCollider2D | radius=0.32 · isTrigger=false |

### 6-2. 충돌 처리 알고리즘

타일 기반 코너 검사와 물리 기반 최종 안전장치를 함께 사용합니다.

```
MoveWithCollision(input):
  X 이동 시도 → next = pos + (dx, 0)
    CanMoveTo(next) 검사:
      플레이어 경계 사각형의 4 코너 좌표 계산
      각 코너를 그리드 좌표로 변환
      하나라도 IsWalkable == false → 이동 차단
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
| ↑↓←→ | 이동 + Facing 방향 갱신 |
| Z | 계단 상호작용 (0.5초 쿨다운) |
| F10 | 문 열기 |
| Space | 기본 공격 |
| Q / W / E / R | 스킬 슬롯 1~4 (홀드 시 범위 미리보기) |

---

## 7. 시스템 4 — 전투

### 7-1. 데이터 구조

```
WeaponData (ScriptableObject)
  ├── damage, attackCooldown
  ├── attackPattern (AttackPatternType)
  ├── patternRange
  └── skills[4] (SkillData[])
         ├── damage, mpCost, cooldown
         ├── attackPattern, patternRange
         └── canPenetrateWalls

PlayerResource (Domain)
  ├── currentHp, maxHp
  └── currentMp, maxMp

PlayerCombatController
  ├── PlayerResource 참조 (HP/MP 상태)
  ├── SkillCooldownController 참조 (쿨다운 관리)
  ├── HitFlashFeedback 참조 (피격 시 색상 점멸)
  ├── damageInvincibleDuration — 피격 후 무적시간 (기본 0.5초)
  ├── IsDamageInvincible: 피격 시 데미지 무시
  ├── IsDead / OnDied(player) — HP 0 도달 시 단발 사망 처리
  ├── Die() → CombatEventChannel.RaisePlayerDied()
  ├── Space → TryBasicAttack()
  └── Q/W/E/R → TryUseSkill(index)

SkillCooldownController
  ├── 기본 공격 쿨다운
  └── 스킬 슬롯 4개 쿨다운 (코루틴 기반)
```

### 7-2. 공격 실행 흐름

```
TryBasicAttack() / TryUseSkill(i):
  ① 쿨다운 / MP 확인 (SkillCooldownController)
  ② AttackPattern.GetTargets() → List<Vector2Int>
  ③ AttackExecutor.BeginAttackActivation()  ← 히트셋·버퍼 초기화
  ④ AttackExecutor.ExecuteAttack(
       gridPositions, damage,
       canPenetrateWalls, isMultiTarget,
       knockbackForce, knockbackDuration,
       slowPercentage, slowDuration,
       hitRadius):
         Physics2D.OverlapCircle(queryRadius, s_HitBuffer)
         targetGrid ∈ _targetGridSet 필터
         canPenetrateWalls == false → HasWallBetween() 제외
         isMultiTarget: 전체 히트 / false: 최근접 단일 히트
         EnemyController → ApplyCombatImpact(damage, knockback, slow)
         그 외 IDamageable → TakeDamage(damage)
  ⑤ CombatEventChannel 이벤트 발행
```

**벽 시야 검사 (HasWallBetween)**

```
공격자 그리드 → 대상 그리드까지 Bresenham 선형 보간
중간 타일 중 IsWalkable == false 가 있으면 차단
```

### 7-3. 공격 패턴 목록

| enum | 설명 | 대상 타일 수 |
|------|------|-------------|
| `Single` | 정면 1칸 | 1 |
| `Cross` | 상하좌우 4방향 | 4 |
| `Diagonal` | 대각선 4방향 | 4 |
| `Circle` | 주변 8칸 전체 (체비쇼프 거리) | 8+ |
| `Line` | 정면 직선 N칸 | patternRange |
| `Cone` | 정면 + 좌우 대각 부채꼴 | 3 |

### 7-4. 발사체

프로젝트에는 두 종류의 발사체가 공존합니다.

**Projectile.cs (구 — 플레이어 스킬용)**

```
Projectile (Trigger Collider 기반):
  ├── 직선 이동 (Rigidbody2D linearVelocity)
  ├── OnTriggerEnter2D → wallLayerMask / unitLayerMask 분기
  ├── 벽 충돌 → 파괴 (canPenetrateWalls=false 시)
  ├── 유닛 충돌 → IDamageable.TakeDamage() → 파괴
  └── maxRange 초과 → 자동 파괴
```

**ProjectileController.cs (신 — 적 원거리용, 풀링 대응)**

```
ProjectileController:
  ├── DungeonManager 그리드 IsWalkable 기반 벽 검사 (Physics2D 미사용)
  ├── ProjectileWallHitMode: Destroy / PassThrough / Bounce
  ├── 플레이어 적중: 정적 캐시된 PlayerCombatController 위치 + 반경 비교
  ├── lifetime 만료 / 벽 / 플레이어 적중 시 Release(Reason)
  └── ProjectilePool가 Release 콜백을 받아 비활성화 후 풀로 반납
```

**ProjectilePool — 두 가지 비활성화 모드**

| 모드 | 동작 | 비고 |
|------|------|------|
| `SetActive` | GameObject.SetActive(true/false) 토글 | 기존 Unity 표준 방식 |
| `DisableComponents` | GameObject은 active 유지, ProjectileController/SpriteRenderer/Animator만 enabled 토글 | OnEnable/OnDisable 비용 회피, 기본값 |

- `prewarmEntries`로 프리팹별 사전 풀 생성 수 지정
- Get/Return은 RuntimePerfTraceLogger 활성 시 ProfilerMarker + 마이크로 타이밍 기록

---

## 8. 시스템 5 — 적 AI

### 8-1. FSM 구조

```
EnemyBrain (추상)
  ├── TargetHandler   — 플레이어 감지 및 타겟 갱신
  ├── MovementHandler — A* 경로탐색 + 군중 분리 (보간된 분리 벡터)
  └── ActionHandler   — Contact/Ranged 분기 + 쿨다운·선딜·후딜 처리

NormalEnemyBrain (구체)
  └── 상태: Idle → Chase → Attack (Idle/Attack은 EnemyBrain의 nested sealed class)
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
| 군중 분리 벡터 | 인접 적 OverlapCircle + Lerp 보간으로 지터 감소 |
| Footprint 4코너 검사 | CollisionFootprintRadius 기준 4코너 IsWalkable 통과 시에만 이동 |
| LateUpdate 위치 복원 | 풋프린트가 벽에 끼면 _lastSafePosition으로 복귀 |

### 8-4. 행동 분기 (EnemyBehaviorType)

`EnemyData.behaviorType`에 따라 ActionHandler가 다른 사이클을 돕니다.

```
Contact (근접):
  ShouldKeepChasing && Collider 거리 ≤ contactDamageSkin
    → ApplyDamage()  ← 매 프레임 접촉 피해 적용
    (플레이어는 IsDamageInvincible 동안 데미지 무시)

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
| 넉백 | 방향 × 힘 임펄스, `knockbackResistance`로 감쇠. CircleCast + 그리드 IsWalkable 양면 클램핑으로 벽 안 끼임 방지 |
| 슬로우 | `_activeSlows` 리스트에서 가장 강한 감속만 moveSpeed 승수에 반영, 지속시간 후 자동 제거 |
| 피격 점멸 | `HitFlashFeedback.Play()` — SpriteRenderer 색상 N회 점멸 |

**사망 처리 (IsDead → OnDeathFinished)**

```
TakeDamage → HP 0 도달 → Die():
  IsDead = true
  CircleCollider 비활성화 (이후 충돌·접촉 피해 차단)
  ResetStatusEffects() (넉백·슬로우 클리어)
  EnemyAnimationController.TriggerDeath() → DeathTrigger
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
| `DeathTrigger` (trigger) | 사망 시 Sprite flipX 페이싱 잠금 |

`faceTargetWhileChasing` 옵션을 켜면 EnemyBrain이 매 프레임 `FacePosition(Target)`을 호출해 추격 중에도 항상 타겟을 바라보도록 보정합니다 (근접 적의 추적 방향 안정화). 이때 이동 방향 기반의 자동 페이싱(`faceMoveDirectionWhenMoving`)은 한 프레임 동안 억제됩니다.

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
  ④ EnemyPoolManager에서 예산 기반 적 선택
       (방 면적 × densityFactor × 방 타입 배율)
       (SpawnRegion 비트 필터링)
  ⑤ 방 내부 걷기 가능 타일에 스폰
       (테두리 제외, 4-코너 발자국 검사로 벽 끼임 예방)
  ⑥ 적 수 > 0 → DungeonManager.CloseCurrentRoomDoors()
     적 수 = 0 → DungeonManager.OpenCurrentRoomDoors()
```

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

Q/W/E/R 키를 누르고 있는 동안 공격 범위를 LineRenderer로 시각화합니다.

```
키 홀드 감지 → GetTargets() → 그리드 좌표 목록
  ├── 벽 인식: IsWalkable 검사로 벽 뒤 타일 제외
  └── LineRenderer로 각 타일 테두리 표시
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

`GameOverUIController`는 인스펙터에 UI가 비어 있으면 `BuildDefaultUi()`로 패널·이미지·확인 버튼을 런타임 자동 생성합니다 (Time.timeScale에 영향받지 않도록 unscaled 페이드 사용).

---

## 11. 시스템 8 — 렌더링 및 로딩

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
    9-포인트 샘플링 (중앙 1 + 8방향 경계)
    중앙이 방 안 → true
    나머지 중 ROOM_ENTRY_SAMPLE_THRESHOLD(=3)개 이상 방 안 → true
  IsPlayerOverlappingAnyDoorCell(room):
    방 4면 테두리의 문 후보 타일 각각에 대해
    플레이어 CircleCollider와 타일 AABB 겹침 검사
    → 하나라도 겹치면 false (아직 문 진입 중)
```

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

### 새 적 타입 추가

1. `EnemyData` ScriptableObject 생성 (수치 입력)
2. 프리팹에 `EnemyController` + `EnemyHealthBar` + `Collider2D` 부착
3. `NormalEnemyBrain` 부착 또는 `EnemyBrain` 상속 후 커스텀 FSM 구현
4. `EnemyPoolManager`에 프리팹 등록

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
| **쿨다운 관리** | SkillCooldownController — 기본 공격·스킬 4슬롯 쿨다운 분리 |
| **발사체 시스템** | 직선 이동, 벽/유닛 충돌, 관통 옵션 |
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
| **게임오버 UI 자동 빌드** | `GameOverUIController.BuildDefaultUi` — 인스펙터 미설정 시 패널·이미지·확인 버튼 런타임 생성 |
| **적 사망 지연 처리** | `EnemyData.deathDelay` + `OnDeathFinished` — 사망 모션 종료 후 풀 반납, 방 클리어 판정은 즉시 |
| **추격 중 타겟 페이싱** | `EnemyAnimationController.faceTargetWhileChasing` — 근접 적의 추적 방향 흔들림 보정 |
| **성능 최적화** | NonAlloc 물리, A* 버퍼 재사용, 오브젝트 풀, 청크 로딩, 문 배치 N→1 |

### 미구현 (다음 단계)

| 항목 | 우선순위 | 비고 |
|------|----------|------|
| 아이템 / 장비 드랍 | 중간 | OnEnemyKilled 이벤트 활용 |
| 보스 / 에픽 적 패턴 | 중간 | EnemyBrain 상속 + Phase2/Berserk 상태 enum 자리 마련됨 |
| 상태이상 시스템 확장 | 낮음 | 독, 빙결 등 StatusEffectData 추가 |
| 세이브 / 로드 | 낮음 | Seed 기반 재현으로 부분 대체 가능 |
| 보스 룸 | 낮음 | RoomType.Boss 추가 후 RoomRegistry 확장 |
| MonsterDen 방 타입 등록 | 낮음 | RoomRegistry에서 자동 분류 조건 추가 필요 |

---

*본 문서는 현재 master 브랜치 기준이며, 개발 진행에 따라 갱신됩니다.*
