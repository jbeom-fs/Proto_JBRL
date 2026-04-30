# JBRogLike — 아키텍처 보고서

> 작성 기준일: 2026-04-30  
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
| 적 AI | FSM (Idle → Chase → Attack), A* 경로탐색 |
| 진행 방식 | 계단을 통한 층 이동 (무한 층 구조) |

---

## 2. 레이어 아키텍처

전체 시스템은 **Clean Architecture** 원칙에 따라 4개 레이어로 분리되어 있습니다.

```
┌──────────────────────────────────────────────────────────────┐
│  Application Layer (MonoBehaviour)                           │
│  PlayerController · PlayerInputReader                        │
│  PlayerCombatController · SkillCooldownController            │
│  DungeonManager · FloorTransitionService                     │
│  EnemyBrain · NormalEnemyBrain · RoomSpawner                 │
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
│
├── DungeonManager.cs               # 던전 생애주기 조율 (Facade)
├── DoorController.cs               # 문 개폐 제어
│
├── Data/
│   ├── DungeonData.cs              # 타일 그리드 + 방 목록 (Domain)
│   ├── WeaponData.cs               # 무기 ScriptableObject
│   ├── SkillData.cs                # 스킬 ScriptableObject
│   └── EnemyData.cs                # 적 ScriptableObject
│
├── Generate/
│   ├── DungeonGenerator.cs         # BSP + Prim MST 생성 알고리즘 (순수 C#)
│   ├── DungeonTypes.cs             # 공유 타입 (RoomType, RoomInfo, 이벤트 인자)
│   ├── DungeonEventChannel.cs      # 던전 이벤트 버스 (ScriptableObject)
│   ├── DungeonSettings.cs          # 던전 생성 설정값
│   ├── DungeonQueryService.cs      # 그리드 유틸리티 (IsWalkable, 좌표 변환)
│   ├── SpawnPositionService.cs     # 플레이어 스폰 좌표 계산 서비스
│   ├── FloorTransitionService.cs   # 층 이동 코루틴·로딩 화면·GC 관리
│   ├── RoomRegistry.cs             # 방 상태 관리 (타입·문 닫힘)
│   ├── DungeonTilemapRenderer.cs   # Tilemap 타일 배치
│   ├── SpawnRegion.cs              # 스폰 지역 플래그 (Dungeon/Forest/Castle)
│   └── RoomSpawner.cs              # 방 진입 시 적 스폰, 방 클리어 감지
│
├── Combat/
│   ├── IDamageable.cs              # 피해 수신 인터페이스
│   ├── AttackPattern.cs            # 공격 패턴 enum + 좌표 계산기
│   ├── AttackExecutor.cs           # 공격 판정·히트 감지·데미지 적용
│   ├── PlayerCombatController.cs   # 플레이어 전투 진입점 (HP·MP·공격·스킬)
│   ├── SkillCooldownController.cs  # 기본 공격·스킬 4슬롯 쿨다운 관리
│   ├── PlayerResource.cs           # HP·MP 상태 컨테이너 (Domain)
│   ├── Projectile.cs               # 직선 이동 발사체
│   └── CombatEventChannel.cs       # 전투 이벤트 버스 (ScriptableObject)
│
├── Enemy/
│   ├── EnemyController.cs          # 적 HP 관리·피해 수신·사망·상태이상
│   ├── EnemyBrain.cs               # FSM 조율 추상 클래스 (+ Handler 분리)
│   ├── NormalEnemyBrain.cs         # 기본 몬스터용 FSM 구현
│   ├── NormalEnemyAI.cs            # 적 전투 AI (공격 실행)
│   ├── ChaseState.cs               # A* 기반 추격 상태
│   ├── AStarPathfinder.cs          # GC 최소화 A* 탐색기
│   ├── EnemyHealthBar.cs           # 머리 위 체력바 렌더러
│   └── EnemyPoolManager.cs         # 적 오브젝트 풀
│
├── UI/
│   ├── PlayerStatusBarUI.cs        # 플레이어 HP·MP 상태바 (슬라이더 + 텍스트)
│   ├── SkillSlotUI.cs              # 스킬 슬롯 1개 렌더링 (아이콘·쿨타임)
│   ├── SkillUIManager.cs           # 4슬롯 초기화·층 변경 갱신
│   └── SkillRangePreviewer.cs      # Q/W/E/R 스킬 범위 미리보기 (LineRenderer)
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
| `OnRoomEntered` | PlayerController | DoorController, RoomSpawner |
| `OnNormalRoomEntered` | PlayerController | DoorController, RoomSpawner |
| `OnSpawnRoomEntered` | PlayerController | — |
| `OnStairRoomEntered` | PlayerController | — |
| `OnFloorChanged` | DungeonManager | PlayerController, SkillUIManager |

### CombatEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnEnemyKilled(EnemyController)` | EnemyController | RoomSpawner (방 클리어 판정) |
| `OnPlayerHpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnPlayerMpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
| `OnSkillUsed(SkillData)` | PlayerCombatController | SkillSlotUI (쿨다운 표시) |

---

## 6. 시스템 3 — 플레이어 이동

### 6-1. 충돌 처리 알고리즘

실제 물리 엔진 대신 타일 기반 코너 검사를 사용합니다.

```
MoveWithCollision(input):
  X 이동 시도 → next = pos + (dx, 0)
    CanMoveTo(next) 검사:
      플레이어 경계 사각형의 4 코너 좌표 계산
      각 코너를 그리드 좌표로 변환
      하나라도 IsWalkable == false → 이동 차단
  Y 이동 시도 → 동일 방식

  → X, Y를 독립 처리하므로 벽에 대해 슬라이딩 이동 가능
```

### 6-2. 방 진입 감지 최적화

```
CheckRoomEntry():
  ① 그리드 좌표가 이전과 동일 → 조기 종료
  ② 복도(CORRIDOR) 타일 → 조기 종료
  ③ 방 내부 판정 (테두리 제외)
  ④ 이미 현재 방과 동일 → 조기 종료
  → 이벤트 발행
```

### 6-3. 입력 키 맵

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
  ③ AttackExecutor.Execute():
       Physics2D.OverlapCircleNonAlloc(worldPos, hitRadius)
       IDamageable.TakeDamage(damage) 호출
  ④ CombatEventChannel 이벤트 발행
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

### 7-4. 발사체 (Projectile)

```
Projectile:
  ├── 직선 이동 (direction × speed)
  ├── 벽 충돌 → 파괴 (canPenetrateWalls=false 시)
  ├── 유닛 충돌 → IDamageable.TakeDamage() → 파괴
  └── maxDistance 초과 → 자동 파괴
```

---

## 8. 시스템 5 — 적 AI

### 8-1. FSM 구조

```
EnemyBrain (추상)
  ├── TargetHandler   — 플레이어 감지 및 타겟 갱신
  ├── MovementHandler — A* 경로탐색 + 군중 분리
  └── ActionHandler   — 공격 실행

NormalEnemyBrain (구체)
  └── 상태: Idle → Chase → Attack
```

```
상태 전이:
  Idle  ──(감지 범위 진입)──▶  Chase
  Chase ──(공격 범위 진입)──▶  Attack
  Attack ──(범위 이탈)────────▶  Chase
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
| 군중 분리 벡터 | 인접 적과의 겹침 방지 |
| 벽 넉백 제약 | 이동 전 IsWalkable 검사 |

### 8-4. 상태이상 처리 (EnemyController)

| 상태이상 | 처리 |
|--------|------|
| 넉백 | 방향 × 힘으로 위치 오프셋, 벽 충돌 시 차단 |
| 슬로우 | moveSpeed 승수 감소, 지속시간 후 자동 해제 |

---

## 9. 시스템 6 — 방 스폰 및 클리어

### 9-1. 스폰 흐름

```
OnNormalRoomEntered (이벤트 수신):
  ① isFirstVisit == false → 종료 (재진입 시 재스폰 없음)
  ② EnemyPoolManager에서 예산 기반 적 선택
  ③ 방 내부 랜덤 위치에 스폰 (플레이어 주변 제외)
  ④ 문 닫기 (DungeonManager.CloseDoors)
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

---

## 11. 시스템 8 — 렌더링 및 로딩

### 11-1. Tilemap 청크 분할 배치

층 이동 중 프레임 드랍을 방지하기 위해 Tilemap 배치를 여러 프레임으로 분산합니다.

```
PlaceTilesChunked(data, chunkRows=8):
  전체 행을 chunkRows개 단위로 분할
  각 청크 배치 후 yield return null  ← 프레임 양보
```

### 11-2. 층 이동 코루틴 타임라인 (FloorTransitionService)

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
| static 픽셀 스프라이트 | `EnemyHealthBar.s_Pixel` | 텍스처 1회 생성, N마리 공유 |
| A* 버퍼 재사용 | `AStarPathfinder` | 경로탐색 GC 없음 |
| NonAlloc 물리 | `Physics2D.OverlapCircleNonAlloc` | 전투 판정 GC 없음 |
| Bresenham 직선 시야 | `ChaseState` | Raycast 대신 그리드 샘플링 |
| 오브젝트 풀링 | `EnemyPoolManager` | 적 Instantiate/Destroy 없음 |

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
| **플레이어 이동** | 8방향 + 코너 충돌 슬라이딩 |
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
| **성능 최적화** | NonAlloc 물리, A* 버퍼 재사용, 오브젝트 풀, 청크 로딩 |

### 미구현 (다음 단계)

| 항목 | 우선순위 | 비고 |
|------|----------|------|
| 플레이어 사망 / 게임오버 화면 | 높음 | OnPlayerDied 훅 이미 존재 |
| 아이템 / 장비 드랍 | 중간 | OnEnemyKilled 이벤트 활용 |
| 원거리 적 AI | 중간 | EnemyBrain 상속 + Projectile 조합 |
| 상태이상 시스템 확장 | 낮음 | 독, 빙결 등 StatusEffectData 추가 |
| 세이브 / 로드 | 낮음 | Seed 기반 재현으로 부분 대체 가능 |
| 보스 룸 | 낮음 | RoomType.Boss 추가 후 RoomRegistry 확장 |

---

*본 문서는 현재 master 브랜치 기준이며, 개발 진행에 따라 갱신됩니다.*
