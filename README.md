# JBLogLike — 아키텍처 보고서

> 작성 기준일: 2026-04-28  
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
8. [시스템 5 — 렌더링 및 UI](#8-시스템-5--렌더링-및-ui)
9. [성능 전략](#9-성능-전략)
10. [데이터 흐름](#10-데이터-흐름)
11. [확장 포인트](#11-확장-포인트)
12. [개발 현황](#12-개발-현황)

---

## 1. 프로젝트 개요

**JBLogLike**는 Unity 2D Tilemap 기반의 절차적 생성 로그라이크 게임입니다.

| 항목 | 내용 |
|------|------|
| 장르 | 로그라이크 던전 탐색 |
| 시점 | 탑다운 2D |
| 맵 방식 | BSP 알고리즘 절차적 생성 |
| 이동 방식 | 실시간 8방향 이동 + 그리드 충돌 |
| 전투 방식 | 실시간, 패턴 기반 범위 공격 |
| 진행 방식 | 계단을 통한 층 이동 (무한 층 구조) |

---

## 2. 레이어 아키텍처

전체 시스템은 **Clean Architecture** 원칙에 따라 4개 레이어로 분리되어 있습니다.

```
┌──────────────────────────────────────────────────────┐
│  Application Layer (MonoBehaviour)                   │
│  PlayerController · PlayerCombatController           │
│  DungeonManager · LoadingScreenController            │
├──────────────────────────────────────────────────────┤
│  Infrastructure Layer (ScriptableObject Event Bus)   │
│  DungeonEventChannel · CombatEventChannel            │
├──────────────────────────────────────────────────────┤
│  Domain Layer (순수 C# — Unity 의존 없음)             │
│  DungeonData · DungeonGenerator · RoomRegistry       │
│  WeaponData · SkillData · EnemyData                  │
├──────────────────────────────────────────────────────┤
│  Presentation Layer                                  │
│  DungeonTilemapRenderer · DoorController             │
│  EnemyHealthBar                                      │
└──────────────────────────────────────────────────────┘
```

### 핵심 설계 원칙

- **단방향 의존**: 상위 레이어만 하위 레이어를 알고, 역방향 참조 없음
- **이벤트 기반 통신**: 레이어 간 직접 참조 대신 ScriptableObject EventChannel 사용
- **데이터 주입 (ScriptableObject)**: 무기/스킬/적의 수치는 에셋으로 분리, 코드 수정 없이 교체 가능
- **GC 최소화**: 이벤트 인자에 `struct` 사용, 코루틴 캐싱, 1×1 픽셀 스프라이트 static 캐시

---

## 3. 파일 구조

```
Assets/Scripts/
│
├── PlayerController.cs             # 입력·이동·방 감지
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
│   ├── RoomRegistry.cs             # 방 상태 관리 (타입·문 닫힘)
│   └── DungeonTilemapRenderer.cs   # Tilemap 타일 배치
│
├── Combat/
│   ├── IDamageable.cs              # 피해 수신 인터페이스
│   ├── AttackPattern.cs            # 공격 패턴 enum + 좌표 계산기
│   ├── PlayerCombatController.cs   # 플레이어 전투 (HP·MP·공격·스킬)
│   └── CombatEventChannel.cs       # 전투 이벤트 버스 (ScriptableObject)
│
├── Enemy/
│   ├── EnemyController.cs          # 적 HP 관리·피해 수신·사망
│   └── EnemyHealthBar.cs           # 머리 위 체력바 렌더러
│
└── Tool/
    ├── RuntimePerfLogger.cs        # 단계별 성능 타이밍 로거
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

리프 노드 각각에 하나의 방이 배치됩니다.

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

**목적**: 모든 방이 반드시 연결되도록 보장하면서 루프를 확률적으로 추가

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
    (이미 직접 연결된 쌍 제외 → 중복 통로 방지)
    DrawLCorridor(src, k)
```

### 4-5. L자형 통로 생성 (DrawLCorridor)

두 방을 직각 2세그먼트 통로로 연결합니다.

```
수평 연결의 경우 (|dx| >= |dy|):

  src 오른쪽 벽 → far_outside_S (MinStraight 만큼 밖)
                    │ (수평 스텁)
                    └── 코너 지점
                              │ (수직 팔)
  far_outside_E ← dst 왼쪽 벽
  (MinStraight 만큼 밖)

  보장:
    • 각 방 벽에서 코너까지 최소 MinStraight(=2) 칸 이상 직선
    • 수직 팔 길이 < MinStraight 이면 도어 Y 위치를 범위 내 조정
```

### 4-6. 계단 배치 규칙

```
PlaceStairs():
  방 목록을 셔플 (랜덤 순서 탐색)
  각 방의 내부 타일(테두리 1줄 제외) 중:
    - 현재 ROOM 타일
    - 상하좌우 4방향 이웃에 CORRIDOR 없음
  → 조건 충족하는 위치에 STAIR_UP 배치

  최고층에는 STAIR_UP 미배치
```

### 4-7. 타일 타입 상수

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
| `OnRoomEntered` | PlayerController | DoorController, UI |
| `OnNormalRoomEntered` | PlayerController | DoorController |
| `OnSpawnRoomEntered` | PlayerController | — |
| `OnStairRoomEntered` | PlayerController | — |
| `OnFloorChanged` | DungeonManager | PlayerController |

### CombatEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnEnemyKilled(EnemyController)` | EnemyController | 점수UI, 룸클리어 판정 |
| `OnPlayerHpChanged(cur, max)` | PlayerCombatController | HP UI |
| `OnPlayerMpChanged(cur, max)` | PlayerCombatController | MP UI |
| `OnSkillUsed(SkillData)` | PlayerCombatController | 스킬 쿨다운 UI |

### 통신 흐름 예시 (방 진입)

```
PlayerController.CheckRoomEntry()
  → DungeonEventChannel.RaiseRoomEntered(room, isFirstVisit)
      → DoorController.OnNormalRoomEntered()  ← 문 닫기
      → (미래) MonsterSpawner.OnNormalRoomEntered()  ← 몬스터 스폰
      → (미래) HUD.OnRoomEntered()  ← 방 정보 표시
```

---

## 6. 시스템 3 — 플레이어 이동

### 6-1. 구성 요소

| 컴포넌트 | 책임 |
|----------|------|
| `PlayerController` | 입력 읽기, 이동, 방 감지, 계단 상호작용 |
| `PlayerCombatController` | 전투 (같은 GameObject의 별도 컴포넌트) |

### 6-2. 충돌 처리 알고리즘

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

```
collisionRadius = 0.2 (기본값, 0.05~0.49 조정 가능)
코너 오프셋 = tileSize × collisionRadius
```

### 6-3. 방 진입 감지 최적화

```
CheckRoomEntry():
  ① 그리드 좌표가 이전과 동일 → 조기 종료 (같은 타일 내 이동 스킵)
  ② 복도(CORRIDOR) 타일 → 조기 종료
  ③ 방 내부 판정 (테두리 제외):
     gridPos.x > room.X && gridPos.x < room.Right - 1  (and Y 방향)
  ④ 이미 현재 방과 동일 → 조기 종료
  → 이벤트 발행
```

### 6-4. 입력 키 맵

| 키 | 동작 |
|----|------|
| ↑↓←→ | 이동 + Facing 방향 갱신 |
| Z | 계단 상호작용 (0.5초 쿨다운) |
| R | 문 열기 (DoorController 위임) |
| Space | 기본 공격 (PlayerCombatController) |
| 1 / 2 / 3 / 4 | 스킬 슬롯 (PlayerCombatController) |

---

## 7. 시스템 4 — 전투

### 7-1. 구조 개요

```
WeaponData (ScriptableObject)
  ├── damage, attackCooldown
  ├── attackPattern (AttackPatternType)
  ├── patternRange
  ├── bonusAttack, bonusDefense
  └── skills[4] (SkillData[])
         ├── damage, mpCost, cooldown
         └── attackPattern, patternRange

PlayerCombatController
  ├── baseAttack + bonusAttack → TotalAttack
  ├── baseDefense + bonusDefense → TotalDefense
  ├── HP / MP 관리
  ├── Space → TryBasicAttack()
  └── 1~4   → TryUseSkill(index)

IDamageable (interface)
  ├── TakeDamage(int)
  └── IsAlive : bool
        ↑           ↑
  EnemyController  PlayerCombatController
```

### 7-2. 공격 실행 흐름

```
TryBasicAttack() 또는 TryUseSkill(i):
  ① 쿨다운 / MP 확인
  ② AttackPattern.GetTargets(pattern, originGrid, facingDir, range)
       → List<Vector2Int> 반환
  ③ 각 타겟 그리드 좌표 → GridToWorld() → 월드 좌표
  ④ Physics2D.OverlapCircleAll(worldPos, hitRadius)
       → 반경 내 Collider2D 탐색
  ⑤ IDamageable 컴포넌트 보유 오브젝트 → TakeDamage(damage) 호출
```

### 7-3. 공격 패턴 목록

| enum | 설명 | 대상 타일 수 |
|------|------|-------------|
| `Single` | 정면 1칸 | 1 |
| `Cross` | 상하좌우 4방향 | 4 |
| `Diagonal` | 대각선 4방향 | 4 |
| `Circle` | 주변 8칸 전체 | 8 |
| `Line` | 정면 직선 N칸 | patternRange |
| `Cone` | 정면 + 좌우 대각 부채꼴 | 3 |

**Cone 회전 계산:**

```
RotateCW45(v)  = ( clamp(v.x + v.y, -1, 1),  clamp(v.y - v.x, -1, 1) )
RotateCCW45(v) = ( clamp(v.x - v.y, -1, 1),  clamp(v.y + v.x, -1, 1) )

예시 — facing = (0, 1) (위쪽):
  CW45  → (1, 1)   (우상)
  CCW45 → (-1, 1)  (좌상)
  → Cone = 위 + 우상 + 좌상
```

### 7-4. 적 체력 관리

```
EnemyController.TakeDamage(incomingDamage):
  actual = max(1, incomingDamage - data.defense)
  _currentHp -= actual
  _healthBar?.SetHp(_currentHp, data.maxHp)
  if _currentHp == 0 → Die()
    → CombatEventChannel.RaiseEnemyKilled(this)
    → gameObject.SetActive(false)
```

### 7-5. 체력바 렌더링 (EnemyHealthBar)

```
구조:
  Enemy GameObject
  ├── EnemyController
  ├── EnemyHealthBar ← 이것만 추가하면 자동 동작
  │   ├── HPBar_BG    (SpriteRenderer — 배경)
  │   └── HPBar_Fill  (SpriteRenderer — 채움)
  └── SpriteRenderer (적 스프라이트)

SetHp(current, max):
  ratio = current / max
  fillScale.x  = barWidth × ratio
  fillPos.x    = barWidth × (ratio - 1) / 2    ← 왼쪽 앵커
  fillColor    = Lerp(빨강, 초록, ratio)         ← 그라디언트 (선택)
  autoHideTimer = autoHideDelay                 ← N초 후 자동 숨김

픽셀 스프라이트: 1×1 흰색 텍스처 → static 캐시 (적 N마리 공유)
```

---

## 8. 시스템 5 — 렌더링 및 UI

### 8-1. Tilemap 배치 (청크 분할)

층 이동 중 프레임 드랍을 방지하기 위해 Tilemap 배치를 여러 프레임으로 분산합니다.

```
PlaceTilesChunked(data, chunkRows=8):
  전체 행을 chunkRows개 단위로 분할
  각 청크 배치 후 yield return null  ← 프레임 양보
  → 각 프레임당 최대 chunkRows행 처리
```

### 8-2. 층 이동 코루틴 타임라인

```
FloorTransition(targetFloor):
  1. LoadingScreen.Show()     ← 페이드 인
  2. GenerateChunked()        ← 던전 생성 (로딩 화면 뒤에서)
  3. yield return null        ← Unity Tilemap 처리 완료 대기
  4. [선택] GC.Collect()
  5. WaitForSecondsRealtime(settleSeconds)  ← 렌더러 안정화
  6. N 프레임 대기 (settleFrames)
  7. EventChannel.RaiseFloorChanged()  ← 플레이어 스폰 트리거
  8. LoadingScreen.Hide()     ← 페이드 아웃
```

---

## 9. 성능 전략

| 전략 | 적용 위치 | 효과 |
|------|-----------|------|
| `struct` 이벤트 인자 | `RoomEnteredEventArgs` | Heap 할당 없음 |
| YieldInstruction 캐시 | `YieldCache` | 코루틴 GC 감소 |
| 스폰 좌표 캐싱 | `DungeonManager._cachedSpawnPos` | O(1) 조회 |
| 그리드 좌표 변경 시에만 방 감지 | `_lastCheckedGridPos` | Update 부하 감소 |
| 청크 분할 Tilemap 배치 | `PlaceTilesChunked` | 층 이동 시 프레임 유지 |
| static 픽셀 스프라이트 | `EnemyHealthBar.s_Pixel` | 텍스처 1회 생성, N마리 공유 |
| `RuntimePerfLogger` | 각 생성 단계 | 병목 구간 측정 |

---

## 10. 데이터 흐름

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
        └──▶ DungeonTilemapRenderer.PlaceTiles()
                  └── Tilemap에 타일 배치
```

### 전투 데이터 흐름

```
WeaponData (ScriptableObject 에셋)
        │ Inspector 드래그
        ▼
PlayerCombatController
        │ AttackPattern.GetTargets()
        │ Physics2D.OverlapCircleAll()
        ▼
IDamageable.TakeDamage()
        │
        ├──▶ EnemyController
        │         ├── EnemyHealthBar.SetHp()  → SpriteRenderer 갱신
        │         └── CombatEventChannel.RaiseEnemyKilled()
        │
        └──▶ PlayerCombatController (적이 플레이어 공격 시)
                  └── CombatEventChannel.RaisePlayerHpChanged()
```

---

## 11. 확장 포인트

### 새 공격 패턴 추가

```csharp
// AttackPattern.cs
public enum AttackPatternType {
    ...,
    Ring,    // 새 패턴 추가
}

// GetTargets() switch에 case 추가
case AttackPatternType.Ring:
    for (int r = 2; r <= range; r++)
        foreach (var d in s_Cardinals) targets.Add(origin + d * r);
    break;
```

WeaponData / SkillData의 드롭다운에 자동으로 추가됩니다.

### 새 무기 / 스킬 추가

Unity 에디터에서 `Create > JBLogLike > Combat > Weapon` 또는 `Skill`로 에셋 생성 후 수치 입력만 하면 됩니다. 코드 수정 불필요.

### 새 이벤트 추가

`DungeonEventChannel.cs` 또는 `CombatEventChannel.cs`에 `event Action<T>` 선언 및 `Raise()` 메서드 추가. 발행자·구독자 코드는 수정 불필요.

### 새 적 타입 추가

`EnemyData` ScriptableObject 생성 → 프리팹에 `EnemyController` + `EnemyHealthBar` + `Collider2D` 부착 → data 슬롯에 드래그.

---

## 12. 개발 현황

### 완료

| 시스템 | 세부 내용 |
|--------|-----------|
| **던전 생성** | BSP 분할, Prim MST 통로, L자형 통로, 계단 배치 |
| **결정론적 생성** | Seed + Floor 파생 시드 (재현 가능) |
| **층 이동** | 비동기 코루틴 + 청크 Tilemap + 로딩 화면 |
| **플레이어 이동** | 8방향 + 코너 충돌 슬라이딩 |
| **방 진입 감지** | 이벤트 발행, 최초 방문 구분 |
| **문 시스템** | Normal 방 진입 시 문 닫힘 |
| **계단 상호작용** | Z키, 쿨다운 포함 |
| **전투 데이터 구조** | WeaponData, SkillData, EnemyData (ScriptableObject) |
| **공격 패턴 시스템** | 6종 패턴, 데이터 드리븐 |
| **플레이어 전투** | 기본 공격, 스킬 4슬롯, HP/MP 관리 |
| **적 전투** | IDamageable, 방어력 계산, 사망 처리 |
| **적 체력바** | 실시간 갱신, 색상 그라디언트, 자동 숨김 |
| **이벤트 버스** | DungeonEventChannel, CombatEventChannel |
| **성능 모니터링** | RuntimePerfLogger (단계별 ms 측정) |

### 미구현 (다음 단계)

| 항목 | 우선순위 | 비고 |
|------|----------|------|
| 적 AI (이동·공격) | 높음 | EnemyController 기반 확장 |
| 적 스폰 시스템 | 높음 | OnNormalRoomEntered 이벤트 활용 |
| 플레이어 사망 / 게임오버 | 높음 | OnPlayerDied 훅 이미 존재 |
| UI (HP바, MP바, 스킬 쿨다운) | 중간 | CombatEventChannel 이벤트 구독으로 구현 |
| 아이템 / 장비 드랍 | 중간 | OnEnemyKilled 이벤트 활용 |
| 상태이상 시스템 | 낮음 | StatusEffectData ScriptableObject 추가 |
| 세이브 / 로드 | 낮음 | Seed 기반 재현으로 부분 대체 가능 |

---

*본 문서는 현재 master 브랜치 기준이며, 개발 진행에 따라 갱신됩니다.*
