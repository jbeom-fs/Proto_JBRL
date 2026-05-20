# HANDOFF_Proto_JBRL_2026-05-20

> 목적: 이 문서는 새 채팅방이나 다른 작업자가 **문서만 보고 현재 Proto_JBRL / JBRogLike 프로젝트 구조, 최근 변경사항, 핵심 설계 결정, 남은 작업, 검증 포인트**를 이해하고 이어서 개발할 수 있도록 작성한 인수인계 문서다.  
> 기준일: 2026-05-20  
> 프로젝트: Unity 2D Roguelite `Proto_JBRL / JBRogLike`

---

## 0. 현재 프로젝트 한 줄 요약

`Proto_JBRL / JBRogLike`는 **마을에서 시작해 절차 생성 던전에 진입하고, 방 단위 전투/탐험/아이템/Elite 방/미니맵/텔레포트 시스템을 사용하는 Unity 2D 실시간 로그라이트** 프로젝트다.

최근 큰 축은 다음과 같다.

- 시작 위치를 던전이 아니라 **Town**으로 변경.
- 목적지 기반 **TeleportDestinationDatabase** 구조 도입.
- Town / Dungeon 모두 표시 가능한 **Minimap 이중 모드** 구현.
- 5, 15, 25층 등 특정 층에 **Elite Room / Elite Door / Elite Key** 구조 추가.
- Elite Key를 즉시 지급하지 않고 **ItemDatabase + EnemyInventory + DroppedItem** 기반으로 드롭/획득하도록 변경.
- 플레이어 입력 키를 하드코딩에서 **PlayerInputKeySettings ScriptableObject** 기반으로 분리해 키 커스텀 준비 완료.

---

## 1. 작업/코딩 원칙

이 프로젝트에서 앞으로 코드를 수정할 때 반드시 지켜야 할 기준이다.

### 1.1 기본 원칙

- 기존 동작을 불필요하게 바꾸지 않는다.
- 임시 하드코딩보다 확장 가능한 구조를 우선한다.
- 기능 추가 전에 기존 책임 경계를 확인한다.
- Inspector에서 사용자가 입력한 값을 자동으로 고치지 않는다.
  - 잘못된 값은 **warning만 출력**하고 값은 유지한다.
- Play Mode 검증 항목을 항상 보고서에 포함한다.
- `git diff --check`, `dotnet build Assembly-CSharp.csproj`, 필요 시 `Assembly-CSharp-Editor.csproj` 검증을 포함한다.

### 1.2 성능/GC 원칙

- per-frame allocation 금지.
- LINQ 남용 금지.
- 반복 `Find`, `GetComponent`, `AddComponent` fallback 금지.
- Physics2D 반복 쿼리 최소화.
- `ContactFilter2D`, static buffer, cached reference를 우선한다.
- RuntimePerfLogger/RuntimePerfTraceLogger는 OFF 상태에서 string concat, ToString, full scan, GetInvocationList 등 숨은 비용이 발생하지 않도록 guard한다.

### 1.3 Unity/Scene 원칙

- ScriptableObject가 scene object를 직접 참조하는 구조는 피한다.
- scene object 참조가 필요한 경우:
  - scene component registry 패턴
  - root id 기반 registry
  - 또는 Inspector 직접 연결
  을 사용한다.
- YAML 직접 수정이 필요한 경우 Unity Editor에서 한 번 열어 serialization 확인 필요.

---

## 2. 현재 핵심 구조 개요

```text
Main Scene
├─ Managers / Systems
│  ├─ DungeonManager
│  ├─ TownDungeonTransitionManager
│  ├─ RoomSpawner
│  ├─ EnemyPoolManager
│  ├─ DropItemSpawner
│  ├─ MinimapController
│  └─ 기타 UI / Input / Fog 시스템
│
├─ TownRoot
│  ├─ WalkTileMap
│  ├─ WallTileMap
│  ├─ LocationRoot
│  └─ TilemapMinimapSource
│
├─ DungeonRoot
│  ├─ FloorTilemap / CorridorTilemap / WallTilemap / DoorTilemap 등
│  ├─ FogTilemap
│  └─ Dungeon 관련 Tilemap Renderer 대상
│
├─ Player
│  ├─ PlayerController
│  ├─ PlayerCombatController
│  ├─ PlayerDashController
│  ├─ PlayerInputReader
│  └─ PlayerEliteKeyInventory
│
└─ Canvas
   ├─ Status Bar
   │  ├─ ItemSlot
   │  │  └─ KeyIcon
   │  └─ BuffSlot
   └─ Minimap
```

---

## 3. 절차 생성 던전 구조

### 3.1 DungeonManager

주요 책임:

- DungeonSettings 구성.
- DungeonGenerator 호출.
- DungeonData 생성 및 보관.
- Tilemap 렌더링 요청.
- Floor transition.
- Town/Dungeon 전환 시 dungeon runtime 상태 정리와 연계.
- 현재 `currentStageRegion`을 DungeonData에 전달.

중요 사항:

- `currentStageRegion`은 이제 자동 보정하지 않는다.
- `SpawnRegion.None` 또는 복합 flag여도 값을 변경하지 않고 warning만 출력한다.
- SpawnRegion은 현재 **지형 생성에는 영향 없음**.
- SpawnRegion은 현재 **enemy spawn filter / enemy spawn seed domain**에만 영향이 있다.

### 3.2 DungeonGenerator

현재 핵심 흐름:

1. BSP 기반 room 생성.
2. MST 통로 생성.
3. Elite Room 선정이 필요한 층이면 MST leaf room 기반으로 Elite Room 선정.
4. EXTRA 통로 생성.
5. Stair 배치.
6. DungeonData로 변환.

### 3.3 EXTRA 통로 생성 방식

현재 EXTRA는 MST 완료 후 별도 단계에서 처리한다.

핵심 구조:

```text
for attempt in 0 .. rooms.Count - 2:
    if rng.NextDouble() >= ExtraConnProb:
        continue

    현재 grid 기준으로 모든 non-MST / non-EXTRA room pair 검사
    각 room pair마다 extraCandidateCount개의 path 후보 생성
    dirty 후보 제거
    pair별 최고 점수 path 1개 선택
    전체 pair-best 중 최고 점수 path 1개 carve
    grid 업데이트
```

중요:

- `extraCandidateCount`는 floor 전체 후보 수가 아니다.
- `extraCandidateCount`는 **room pair 하나당 path 후보 수**다.
- 각 attempt마다 최종적으로 최대 1개의 EXTRA만 carve된다.
- attempt는 `rooms.Count - 1`회 수행될 수 있으므로 한 floor에 EXTRA가 여러 개 생길 수 있다.
- 로딩 중 계산이므로 현재는 과최적화하지 않는다.
- 로딩이 실제로 길어졌다고 체감될 때 최적화한다.

### 3.4 Elite Room과 EXTRA 제외

Elite Room 조건:

```csharp
floor > 0 && floor % 10 == 5
```

예:

- 5층
- 15층
- 25층
- 35층

선정 방식:

- MST 기준 leaf room 중에서 후보 선정.
- 시작방 제외.
- MST depth가 가장 깊은 leaf room 우선.
- tie-break:
  1. 시작방과 중심 거리 제곱이 큰 방
  2. index 낮은 방
- leaf room이 없으면 fallback으로 시작방 제외 가장 먼 방을 선정하고 warning.
- Elite Room은 무조건 존재해야 한다는 설계다.
- Elite Room은 진행을 막으면 안 되므로 side room 성격이어야 한다.
- EXTRA 통로 후보에서 Elite Room이 src/dst인 pair는 제외한다.

---

## 4. Room / Enemy / Door 구조

### 4.1 RoomSpawner

주요 책임:

- Room entry 시 enemy spawn.
- first visit gating.
- SpawnRoom / StairRoom / EliteRoom 등 특수 room 처리.
- EnemyPoolManager를 통한 enemy request.
- deterministic spawn seed 사용.
- Elite Key holder 지정.

중요 사항:

- Elite Room에는 enemy를 spawn하지 않는다.
- Elite Key holder는 Elite Room 밖의 일반 enemy 후보 중 결정론적으로 선정된다.
- key holder 선정은 기존 deterministic seed domain 구조를 사용한다.
- UnityEngine.Random global state를 사용하지 않는다.

### 4.2 EnemyData / Enemy AI

현재 enemy 큰 분류:

- Contact
- Ranged

Ranged movement type:

- Chase
- Kiting
- Random

Contact special attack:

- None
- Rush
- Jump

주의:

- “Charge Enemy”라는 용어는 피한다.
- 돌진형 적은 “돌진형 적”으로 부른다.
- Jump enemy도 charge/windup phase가 있으므로 “charge enemy”라고 부르면 혼동된다.

### 4.3 EnemyInventory

추가된 구조:

- enemy가 실제 장착한 것은 아니지만, drop할 item을 보유한다.
- `EnemyInventory`는 enemy prefab 전체에 추가되어 있다.
- Pool request/release/cleanup 시 clear하여 누수를 막는다.
- Elite Key holder는 `EnemyInventory`에 `elite_key` item을 가진다.
- 사망 시 `DropItemSpawner`가 inventory를 읽고 drop object를 생성한다.

### 4.4 Door 구조

현재 필요한 door:

- 일반 room encounter door
- Elite Room을 막는 Elite Door

설계 결정:

- `DoorType` enum은 추가하지 않았다.
- 불필요한 enum 확장을 하지 않는다.
- Elite Door는 별도 Tilemap을 쓰지 않는다.
- 기존 `doorTilemap`에 배치하되, 일반 door와 다른 `eliteDoorTile` sprite/tile을 사용한다.
- runtime 구분은 `_eliteDoorPositions` 같은 별도 set으로 관리한다.

Elite Door 동작:

- Key가 없으면 열리지 않는다.
- Key가 있으면 접촉 시 열린다.
- 문이 실제로 열린 경우에만 Elite Key를 소모한다.
- Key 소모 후 KeyIcon Off.

---

## 5. Item / Drop 구조

### 5.1 ItemDatabase

단일 ScriptableObject:

```text
Assets/Perfabs/Scriptable/Item/ItemDatabase.asset
```

ItemData 필드:

- `itemCode`
- `displayName`
- `icon`
- `description`
- `itemType`
- `stackable`
- `maxStack`

ItemType:

- Key
- Currency
- Consumable
- Equipment
- Relic
- Material

Validation:

- 빈 itemCode warning
- 중복 itemCode warning
- 앞뒤 공백 warning
- 자동 수정 없음

현재 등록된 핵심 item:

```text
elite_key
```

### 5.2 DroppedItem

구조:

- prefab: `Assets/Perfabs/Item/DroppedItem.prefab`
- SpriteRenderer로 ItemData.icon 표시.
- trigger collider로 player pickup.
- Player가 접촉하면 itemCode에 따라 처리.
- 현재 `elite_key` 획득 시 `PlayerEliteKeyInventory.GrantEliteKey()` 호출.
- 획득 후 drop object는 Destroy.

주의:

- 현재 drop 수량이 적으므로 Destroy 사용.
- 추후 대량 drop이 생기면 pool 전환을 고려한다.

### 5.3 DropItemSpawner

주요 책임:

- Enemy 사망 위치에 drop 생성.
- floor transition / town return cleanup 시 active drop 제거.
- cleanup 때 enemy drop을 새로 뿌리지 않는다.

---

## 6. Player / Input / Interaction 구조

### 6.1 PlayerInputKeySettings

추가된 ScriptableObject:

```text
Assets/Perfabs/Scriptable/Input/PlayerInputKeySettings.asset
```

기본값:

```text
Move Up: ArrowUp
Move Down: ArrowDown
Move Left: ArrowLeft
Move Right: ArrowRight
Interact / Confirm: Z
Inventory: I
SkillSlot1: Q
SkillSlot2: W
SkillSlot3: E
SkillSlot4: R
```

중요:

- WASD가 기본이 아니다.
- 현재 기본 이동은 방향키다.
- Q/W/E/R은 skill slot 1/2/3/4에 대응한다.
- slot API는 기존 흐름 보존을 위해 0-based:
  - 0 = Q
  - 1 = W
  - 2 = E
  - 3 = R

Validation:

- 중복 key warning
- Key.None warning
- 자동 수정 없음

### 6.2 PlayerInputReader

중앙 입력 리더.

제공 API:

- `MoveInput`
- `InteractConfirmPressedThisFrame`
- `InventoryPressedThisFrame`
- `GetSkillSlotPressedThisFrame(int slotIndex)`
- 기존 호환:
  - `WasSkillPressed(int slot)`
  - `WasStairPressed`

주의:

- `Keyboard.current` 직접 접근은 중앙 입력 리더에만 남기는 방향.
- 예외적으로 `TownDungeonTransitionManager`에 debug `T` 귀환 입력이 남아 있다.
- 추후 debug input도 settings로 뺄지 결정 필요.

### 6.3 Interact / Confirm

기존:

- Z키 = 계단 이동

변경 방향:

- Z키는 전체 상호작용 / 확인키다.
- UI 창이 있으면 confirm 역할.
- 계단 위면 기존처럼 층 이동.
- 현재 계단 입력은 `InteractConfirmPressedThisFrame`로 교체됨.

### 6.4 Inventory

- I 키 입력은 `InventoryPressedThisFrame`로 제공된다.
- 실제 inventory UI는 아직 미구현.
- 다음 작업 후보 중 하나다.

---

## 7. Teleport / Location 구조

### 7.1 TeleportDestinationDatabase

단일 ScriptableObject가 여러 destination을 관리한다.

```text
Assets/Perfabs/Scriptable/Teleport/TeleportDestinationDatabase.asset
```

각 `TeleportLocationData` 필드:

- `id`
- `displayName`
- `description`
- `locationType`
- `locationRootId`
- `localSpawnPosition`
- `minimapLocationId`

설계 결정:

- 지역마다 ScriptableObject를 하나씩 만들지 않는다.
- 하나의 database asset에 여러 목적지를 등록한다.
- TeleportService는 destinationId string만 가진다.
- runtime 구조는 string ID 기반 유지.
- Inspector 편의용 dropdown drawer가 있다.

### 7.2 TeleportDestinationIdAttribute / Drawer

구조:

- `[TeleportDestinationId]` attribute.
- Editor drawer는 `Assets/Editor` 아래에 있다.
- DB를 찾아 id 목록 dropdown 표시.
- 옆에 직접 string 입력 필드도 유지.
- DB에 새 id를 추가하면 Inspector redraw 시 목록에 반영된다.
- 현재 값이 DB에 없으면 warning 표시.
- 값을 강제 변경하지 않는다.

### 7.3 LocationRoot / LocationRootRegistry

TeleportDestinationPoint / TeleportDestinationRegistry는 삭제되었다.

현재 구조:

- 각 장소 root에 `LocationRoot`를 붙인다.
- `locationRootId`로 registry에 등록한다.
- TeleportDestinationDatabase의 `locationRootId`를 기준으로 root를 찾는다.
- `localSpawnPosition`을 root 기준 좌표로 저장한다.
- 실제 이동 위치는:

```csharp
root.transform.TransformPoint(localSpawnPosition)
```

장점:

- spawn point GameObject가 필요 없다.
- scene object 직접 참조를 SO에 넣지 않는다.
- root 기준 local position으로 장소 구조 변경에 비교적 안전하다.
- registry lookup은 Dictionary 기반 O(1).

### 7.4 TownDungeonTransitionManager

주요 책임:

- 현재 위치 `GameLocationType` 관리.
- 목적지 locationType에 따라 root 전환.
- Dungeon 입장 시 새 dungeon run 생성.
- Dungeon -> Town 귀환 시 runtime cleanup.
- Minimap source 전환.

처리 흐름:

#### Town -> Dungeon

```text
TeleportPlayer(player, "dungeon_entrance")
  -> DB에서 destination 조회
  -> ApplyLocationRoots(Dungeon)
  -> minimap.SetDungeonSource()
  -> StartNewDungeonRun()
  -> DungeonManager.Generate()
  -> Player.SpawnAtStart()
```

Dungeon은 절차 생성이므로 `localSpawnPosition`을 직접 사용하지 않고 기존 spawn room 배치 흐름을 유지한다.

#### Dungeon -> Town

```text
TeleportPlayer(player, "town_return")
  -> CleanupDungeonRuntime()
  -> ApplyLocationRoots(Town)
  -> minimap.SetTilemapSource("town")
  -> LocationRootRegistry.TryGet("town")
  -> root.TransformPoint(localSpawnPosition)
  -> player teleport
```

Cleanup 내용:

- active projectile release
- active enemy pool 반환
- room runtime encounter state clear
- active dropped item 제거
- elite key clear

---

## 8. Minimap 구조

### 8.1 MinimapController

현재 이중 모드:

- Dungeon mode
- Tilemap mode

추가 Camera / RenderTexture를 사용하지 않는다.  
Texture2D를 직접 생성해 RawImage에 표시한다.

### 8.2 Dungeon mode

source:

- DungeonData grid
- FogOfWar explored/visible state

특징:

- 미탐험 cell은 투명.
- explored/visible 색 구분.
- DungeonData row 0이 상단인 구조라 Texture에는 Y flip 필요.
- player marker는 texture에 굽지 않고 RectTransform으로 별도 이동.
- 계단은 dungeon mode에서만 표시한다.
- explored된 계단만 표시한다.
- 계단 색은 짙은 청색.
- 계단 marker는 기존보다 크게 표시되도록 padding이 추가되었다.
- Player marker 색은 짙은 녹색.

최근 Play Mode 확인:

- Player marker 색상 변경 정상.
- 계단 marker 확대 정상.
- 문제 없음 확인 후 commit 완료.

### 8.3 Tilemap mode

source:

- `TilemapMinimapSource`

구조:

```text
LocationRoot
├─ WalkTileMap
├─ WallTileMap
└─ TilemapMinimapSource
```

TilemapMinimapSource 필드:

- `locationId`
- `groundTilemap`
- `wallTilemap`

특징:

- Town은 항상 전체 공개.
- Tilemap 기반으로 미니맵을 만든다.
- Town에서도 미니맵은 상시 표시된다.
- Tilemap 좌표계는 Y↑이고 Texture2D index 0도 하단 기준이므로 Y flip을 하지 않는다.
- 기존에 Town이 상하 반전되던 문제는 flip 제거로 수정됨.

### 8.4 LocationMinimapRegistry

- TilemapMinimapSource가 OnEnable/OnDisable에서 등록/해제한다.
- `minimapLocationId`로 source를 찾는다.
- `town_start`, `town_return`은 모두 `minimapLocationId = "town"`로 설정되어 동일 Town minimap을 공유한다.

---

## 9. Fog of War 구조

중요 변경:

- FogOfWarController는 minimap이 읽을 수 있는 query/event를 제공한다.
- `IsExploredCell(Vector2Int)` 추가.
- `VisibilityChanged` event 추가.
- MinimapController는 fog event를 받아 dungeon texture를 refresh한다.
- 첫 1층에서 fog 초기 visibility event를 놓치는 문제를 막기 위해 minimap bootstrap coroutine이 있다.
- 최대 60프레임 동안 DungeonData와 Fog 초기화를 기다린다.

기존 fog 핵심:

- room full reveal
- room border wall reveal
- door/padding reveal
- corridor circular reveal
- wall line-of-sight blocking
- closed door blocks vision option
- diff-based tile update

---

## 10. Combat / Projectile / Hit 구조

### 10.1 Projectile

현재 주요 구조:

- ProjectileController는 transform.position 기반 이동.
- Player hit는 Physics2D query 대신 distance-based sqrMagnitude + cached Player reference.
- PlayerCombatController.Active를 사용해 player cache를 해석한다.
- wall mode:
  - Destroy
  - PassThrough
  - Bounce
- OutOfBounds release 처리 있음.
- FogVisibilityRenderer로 fog 밖 projectile 시각 숨김 처리.
- projectile root rotation은 이동 방향 기준으로 Initialize/Bounce 시 갱신.
- per-frame rotation 계산 없음.

최근 최적화:

- enemy hit candidate 해석에서 dead code였던 GetComponentInParent fallback 제거.
- root collider와 EnemyController가 같은 root에 있다는 prefab 검증 후 TryGetComponent 단일 호출로 단순화.
- Dash path damage도 동일하게 root TryGetComponent 기반.

### 10.2 Player damage / invincibility

- PlayerCombatController에서 damage invincibility 처리.
- Dash invincibility는 dash option에 따라 별도 external invincibility로 시작.
- Player invincibility flash는 Shader/MaterialPropertyBlock 기반.
- 일반 hit flash와 invincibility flash는 분리되어 있다.

### 10.3 Status effects

- Slow / Stun UI icon 구조 존재.
- Status Bar > BuffSlot 아래 SlowStatusIcon / StunStatusIcon.
- 생성/삭제/pool 없이 pre-created icon SetActive.
- reapply 시 SetAsLastSibling으로 순서 갱신.
- Stun 중에는 facing/aim이 input으로 갱신되지 않도록 수정되어 있다.

---

## 11. 현재 완료된 최근 작업 목록

### 11.1 SpawnRegion 자동 보정 제거

- `NormalizeCurrentStageRegion` 제거.
- `None` / 복합 flag warning only.
- Inspector 값 유지.
- SpawnRegion은 지형 생성에는 영향 없음.
- enemy spawn filter / seed domain에는 영향 있음.

### 11.2 Projectile / Enemy hit 후보 해석 단순화

- ProjectileController에서 GetComponentInParent fallback 제거.
- PlayerDashController도 GetComponentInParent fallback 제거.
- 모든 enemy prefab root collider + EnemyController 구조 확인.
- Play Mode 검증 필요로 남겼으나 이후 큰 이슈 보고 없음.

### 11.3 Minimap 추가 및 개선

- DungeonData + Fog 기반 minimap 추가.
- 첫 층 미표시 문제 bootstrap으로 수정.
- Town Tilemap 기반 minimap 추가.
- Town 시작/귀환 시 stale dungeon texture 잔존 문제 수정.
- Town 상하 반전 수정.
- Player marker 짙은 녹색.
- Stair marker 짙은 청색 + 크기 확대.
- Play Mode 확인 및 commit 완료.

### 11.4 Town / Teleport 구조

- 시작 위치를 Town으로 변경.
- TownDungeonTransitionManager 추가.
- 처음에는 단일 목적지 SO 구조였으나, 사용자 요구에 따라 단일 TeleportDestinationDatabase 구조로 변경.
- 이후 SpawnPoint GameObject 의존 제거.
- LocationRoot + localSpawnPosition 구조로 변경.
- Inspector typo 방지 drawer 추가.
- Play Mode 확인 및 commit 완료.

### 11.5 Elite Room / Elite Key / Item Drop

- 5/15/25층 등 `floor % 10 == 5`에서 Elite Room 생성.
- MST leaf room 기반 선정.
- Elite Room은 EXTRA 제외.
- Elite Room은 enemy 미스폰.
- Elite Door 생성.
- Elite Door는 기존 doorTilemap에 eliteDoorTile로 표시.
- Elite Key holder deterministic 선정.
- 처음에는 holder 사망 즉시 key 지급이었으나, 이후 ItemDatabase + EnemyInventory + DroppedItem 구조로 변경.
- Elite Key pickup 후 KeyIcon On.
- Elite Door 개방 시 key 소모, KeyIcon Off.
- Play Mode에서 item drop / pickup / Elite Door open 확인 완료.

### 11.6 PlayerInputKeySettings

- 이동/상호작용/인벤토리/스킬 키를 SO로 분리.
- 기본 이동은 방향키.
- Z = Interact / Confirm.
- I = Inventory.
- Q/W/E/R = skill slot 1~4.
- Play Mode 정상 확인 및 commit 완료.

---

## 12. 현재 남은 주요 작업 후보

우선순위는 상황에 따라 달라질 수 있다.

### 12.1 Inventory UI 구현

현재 상태:

- I key 입력은 존재.
- ItemDatabase / DroppedItem / ItemType 구조는 존재.
- 실제 player inventory data / inventory UI는 아직 없음.
- Elite Key는 별도 PlayerEliteKeyInventory로 관리 중.

권장 방향:

1. PlayerInventory 추가.
2. ItemStack 구조 정의.
3. Inventory UI는 우선 read-only list 또는 grid.
4. I 키로 inventory open/close.
5. UI open 중 Z는 confirm 역할.
6. 기존 Elite Key는 임시 별도 구조 유지하거나 PlayerInventory로 통합할지 설계 후 결정.

주의:

- Elite Key는 현재 gameplay gate로 쓰이므로 곧바로 일반 inventory로 옮기면 door/key icon 흐름이 깨질 수 있다.
- 먼저 wrapper/adapter 방식으로 연동하는 것이 안전하다.

### 12.2 Interaction system 일반화

현재:

- Z는 계단 이동에 연결됨.
- Teleport는 collider trigger 기반.
- Elite Door는 접촉 시 key 있으면 열림.

개선 방향:

- `IInteractable` 또는 `InteractionTarget` 구조 검토.
- Player 주변/현재 cell에서 상호작용 후보 검색.
- 우선순위:
  1. UI confirm
  2. interactable object
  3. stair
  4. door
- 단, trigger 자동 발동이 필요한 portal과 confirm interaction이 필요한 object는 구분 필요.

### 12.3 Debug T 귀환 입력 정리

현재:

- TownDungeonTransitionManager에 debug `T` 입력이 남아 있음.
- PlayerInputKeySettings로 아직 이동하지 않음.

선택지:

- 개발용 debug key로 유지.
- DebugInputSettings로 분리.
- 빌드에서는 비활성화.
- TeleportService 테스트용 UI 버튼으로 대체.

### 12.4 Item drop pooling

현재:

- DroppedItem은 Destroy 사용.
- 드롭 수량이 적은 현재는 문제 없음.

향후:

- gold/material 등 대량 드롭이 추가되면 pool 도입 권장.
- DropItemSpawner가 pool manager 역할을 하거나 별도 DroppedItemPool 추가.

### 12.5 ItemDatabase editor 편의

현재:

- itemCode string 기반.
- validation warning만 있음.

향후:

- ItemCode drawer/dropdown.
- duplicate/empty highlight.
- icon preview.
- item type filter.

### 12.6 TeleportDestinationDatabase editor 개선

현재:

- destinationId drawer 있음.
- 직접 string 입력 가능.
- DB 기반 dropdown 가능.

향후:

- locationRootId dropdown drawer 추가 가능.
- minimapLocationId dropdown drawer 추가 가능.
- destination database 내 list reorder/duplicate 검사 UI 개선.

### 12.7 Elite Room Play Mode 심화 검증

이미 key drop / pickup / door open은 확인됨.

추가 확인 추천:

- 5층 같은 seed 2회 생성 시 Elite Room / key holder 동일성.
- leaf room이 없는 fallback 케이스 warning.
- Elite Room이 진행 필수 경로를 막지 않는지.
- Elite Door 개방 후 fog/minimap/door collider 정상.
- Key holder enemy가 cleanup될 때 drop이 생성되지 않는지.
- floor transition 후 key/drop 상태 초기화.

### 12.8 Town 기능 확장

예상 장소:

- 상점
- 강화소
- 제단
- 기타 hub 시설

현재 구조상 권장:

```text
ShopRoot
├─ WalkTileMap
├─ WallTileMap
├─ LocationRoot(locationRootId="shop")
└─ TilemapMinimapSource(locationId="shop")
```

TeleportDestinationDatabase에:

```text
id = shop_start
locationType = Town 또는 별도 Shop 타입
locationRootId = shop
localSpawnPosition = ...
minimapLocationId = shop
```

GameLocationType은 필요 시 확장 가능.

주의:

- Town > Town / Dungeon > Dungeon은 root 전환 부담이 작다.
- Town > Dungeon / Dungeon > Town은 cleanup/generate/minimap/fog 처리가 필요하다.
- 상점/강화소를 별도 LocationType으로 나눌지는 실제 기능이 생긴 뒤 결정해도 된다.

---

## 13. Play Mode 검증 체크리스트

새 작업 후 아래 중 관련 항목을 확인한다.

### 13.1 Town / Teleport

- Play 시작 시 Town 위치로 spawn.
- Town minimap 표시.
- Player marker 위치 정상.
- portal 진입 시 Dungeon으로 전환.
- Dungeon root 활성 / Town root 비활성.
- Fog / Minimap dungeon mode 정상.
- T 또는 귀환 기능으로 Town 복귀.
- Town minimap으로 즉시 전환.
- dungeon 재입장 시 새 run 생성.

### 13.2 Dungeon generation

- floor 1 일반 생성.
- floor 5 Elite Room 생성.
- staircase 1개 정상.
- bad door 0.
- detached room 없음.
- Elite Room이 leaf room.
- Elite Room에 EXTRA 연결 없음.
- Elite Room에 enemy spawn 없음.

### 13.3 Elite / Item

- key holder enemy 존재.
- key holder 사망 직후 KeyIcon이 바로 켜지지 않음.
- 바닥에 DroppedItem 생성.
- Player pickup 시 KeyIcon On.
- Key 없이 Elite Door 열리지 않음.
- Key 보유 후 Elite Door 접촉 시 열림.
- 문 열림 후 KeyIcon Off.
- floor transition / town return 시 key/drop reset.

### 13.4 Input

- 방향키 이동.
- Z 계단 이동 / confirm.
- Q/W/E/R 스킬.
- I inventory input 감지.
- SO에서 key 변경 시 반영.
- 중복 key warning spam 없음.

### 13.5 Minimap

- Town: 전체 공개.
- Town: 상하 반전 없음.
- Town: marker 위치 정상.
- Dungeon: fog explored/visible 표시.
- Dungeon: unexplored stair 미표시.
- Dungeon: explored stair 짙은 청색 + 충분한 크기.
- Player marker 짙은 녹색.
- Town/Dungeon 전환 시 stale texture 없음.

### 13.6 Combat / Enemy

- Contact/Ranged/Rush/Jump enemy 피격.
- Projectile hit 정상.
- Dash path damage 정상.
- Enemy pool 재사용 시 key holder / inventory 누수 없음.
- death animation / drop timing 정상.

---

## 14. 자주 헷갈렸던 설계 결정 정리

### 14.1 SpawnRegion은 지형을 바꾸지 않는다

현재 SpawnRegion 변경 시:

- dungeon layout은 같음.
- enemy spawn filter/seed에는 영향.
- visual theme에는 아직 직접 영향 없음.

즉 “SpawnRegion 바꿨는데 지형이 안 바뀜”은 현재 구조상 정상이다.

### 14.2 EXTRA는 floor당 1개 고정이 아니다

- attempt 1회당 최대 1개.
- attempt는 `rooms.Count - 1`회.
- ExtraConnProb roll이 여러 번 통과하면 한 floor에 여러 EXTRA 가능.

### 14.3 extraCandidateCount는 room pair당 후보 수다

잘못된 해석:

```text
floor 전체에서 extraCandidateCount개 후보만 생성
```

현재 의도:

```text
room pair 하나마다 extraCandidateCount개 path 후보 생성
```

### 14.4 Elite Door는 별도 Tilemap이 아니다

- 같은 Door Tilemap.
- tile sprite만 다르다.
- runtime 구분은 HashSet/position set.

### 14.5 KeyIcon은 BuffSlot이 아니다

구조:

```text
Status Bar
├─ ItemSlot
│  └─ KeyIcon
└─ BuffSlot
```

- KeyIcon은 item slot 아래에 별도 존재.
- key 보유 여부에 따라 SetActive on/off.
- buff status icon 구조를 사용하지 않는다.

### 14.6 Teleport 목적지는 개별 SO가 아니다

잘못된 구조:

```text
TownStart.asset
TownReturn.asset
DungeonEntrance.asset
```

현재 구조:

```text
TeleportDestinationDatabase.asset
  ├─ town_start
  ├─ town_return
  └─ dungeon_entrance
```

### 14.7 SpawnPoint GameObject 의존은 제거했다

현재는:

- LocationRoot id
- localSpawnPosition

으로 이동 위치를 계산한다.

---

## 15. 추천 다음 작업 순서

현재 흐름상 자연스러운 순서:

1. **Inventory UI 1차 구현**
   - I 키 사용처 확보.
   - ItemDatabase 활용 시작.
   - elite_key는 기존 PlayerEliteKeyInventory와 충돌하지 않게 설계.

2. **Interaction/Confirm 구조 정리**
   - Z가 UI confirm / stair / object interaction을 모두 담당할 수 있게 우선순위 설계.

3. **Town 상호작용 시설 1개 추가**
   - 예: 상점 또는 강화소.
   - Teleport/TilemapMinimap/LocationRoot 구조 검증용으로 좋음.

4. **Item drop pooling 또는 DropTable**
   - drop item이 늘어나면 DropTable 구조 추가.
   - 현재는 EnemyInventory가 직접 drop item을 들고 있음.
   - 장기적으로는 EnemyData -> DropTable -> EnemyInventory/runtime drop plan 흐름 고려.

5. **Elite Room 추가 검증/튜닝**
   - Elite Door sprite/tile polish.
   - Elite Room reward 구조.
   - Elite Room 전용 chest/reward.

---

## 16. 보고서 작성 형식 권장

앞으로 구현 결과 보고는 다음 형식이 좋다.

```text
1. 변경 요약
2. 수정/추가/삭제 파일 목록
3. 새 구조 설명
4. 기존 기능 보존 사항
5. 결정론/성능/GC 영향
6. 빌드 / diff / grep 결과
7. Play Mode 확인 필요 항목
8. 남은 리스크
9. 추천 커밋 메시지
```

Play Mode 완료 후 사용자가 확인했다면:

```text
Play Mode 확인 완료:
- 항목 A 정상
- 항목 B 정상
- Console warning/error 없음
Commit 완료:
- 커밋 메시지 ...
```

---

## 17. 최근 완료 커밋/작업 메모

최근 사용자가 완료했다고 말한 작업:

- Minimap player/stair 가시성 개선.
- Play Mode 문제 없음 확인.
- commit 완료.
- ItemDatabase / DroppedItem / EnemyInventory / Elite Key drop 구조 구현.
- Play Mode에서 item drop, pickup, Elite Door open 확인.
- PlayerInputKeySettings 구현.
- Play Mode 정상 확인.
- commit 완료.

---

## 18. 주의할 기존 경고/더티 상태

과거 자주 언급된 사항:

- `Coin.OnCoinsChanged` CS0649 warning은 기존 warning으로 자주 남아 있었다.
- `UserSettings/Layouts/default-6000.dwlt`는 이전부터 dirty가 되는 경우가 많았다.
- 일부 `git diff --check` 실패는 기존 Main.unity trailing whitespace 또는 layout 파일 공백 때문인 적이 있다.
- 보고서 작성 시 “기존 warning인지 / 이번 작업에서 새로 생긴 warning인지”를 구분해야 한다.

---

## 19. 다음 채팅방에서 바로 사용할 요약

다음 채팅방에서 이 문서만 보고 작업을 이어갈 때는 아래 요약을 먼저 읽으면 된다.

```text
현재 Proto_JBRL은 Town 시작 + TeleportDestinationDatabase 기반 이동 + Town/Dungeon Minimap + Elite Room/Elite Door/Elite Key drop + PlayerInputKeySettings까지 구현되어 있다.

Teleport는 개별 목적지 SO가 아니라 단일 TeleportDestinationDatabase의 id 기반이다. SpawnPoint GameObject는 제거되었고 LocationRootRegistry + localSpawnPosition으로 이동한다.

Minimap은 DungeonData/Fog 기반 Dungeon mode와 TilemapMinimapSource 기반 Tilemap mode를 모두 지원한다. Town은 항상 전체 공개, Dungeon은 fog 기반이다. Dungeon stair marker는 explored 상태에서 짙은 청색으로 크게 표시되고 player marker는 짙은 녹색이다.

Elite Room은 floor % 10 == 5에서 생성된다. MST leaf room을 고르고 EXTRA에서 제외한다. Elite Room에는 enemy가 spawn되지 않는다. Elite Door는 별도 Tilemap이 아니라 기존 doorTilemap에 eliteDoorTile로 배치된다. Elite Key holder는 일반 enemy 중 deterministic하게 선정되고, 사망 시 즉시 지급이 아니라 EnemyInventory -> DroppedItem -> Player pickup 흐름으로 지급된다. Elite Door를 열면 key가 소모된다.

입력은 PlayerInputKeySettings SO로 관리된다. 기본 이동은 방향키, Z는 interact/confirm, I는 inventory, Q/W/E/R은 skill slot 1~4다. Inventory UI는 아직 미구현이다.

다음 추천 작업은 Inventory UI 1차 구현 또는 Z interact/confirm 시스템 일반화다.
```

---
