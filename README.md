# JBRogLike — 아키텍처 보고서

> 작성 기준일: 2026-06-17
> 기준 커밋: `2ad95348` (master, HEAD)
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
11a. [시스템 9 — 마을·던전 전환 및 미니맵](#11a-시스템-9--마을던전-전환-및-미니맵)
11b. [시스템 10 — 아이템 / 드랍 / Elite Key / Soul](#11b-시스템-10--아이템--드랍--elite-key--soul)
11c. [시스템 11 — 개발자 콘솔](#11c-시스템-11--개발자-콘솔)
11d. [시스템 12 — Elite Arena](#11d-시스템-12--elite-arena)
11e. [시스템 13 — Boss Area](#11e-시스템-13--boss-area)
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
| 거점 구조 | 마을(Town) ↔ 던전(Dungeon) ↔ Elite Arena ↔ Boss Area 전환 (`LocationTransitionManager`, 구 `TownDungeonTransitionManager`) — 마을·Arena·Boss Area는 Tilemap 고정 맵, 던전은 절차적 생성. Boss Area = N층마다 진입(§11e) |
| 이동 방식 | 실시간 8방향 이동(Classic=방향키 / ActionMouseAim=WASD) + 그리드 충돌 + 대시 스킬 |
| 조준 방식 | **2가지 프리셋**(`PlayerControlScheme`): Classic = 8방향 입력 기반(`AimDirectionUtility`) / ActionMouseAim = 마우스 커서 기반 **360° 자유조준** — 기본공격 / 스킬 / 투사체 / 대시 공통 |
| 전투 방식 | 실시간, 패턴 기반 범위 공격 + 스킬 4슬롯 (InstantArea / Projectile / Dash) + 스킬 castDelay·recoveryDelay 중 이동 잠금 |
| 플레이어 상태이상 | 적 공격에서 받는 넉백·슬로우·스턴 (`ApplyEnemyCombatImpact` 단일 진입점, `EnemyAttackImpactData`) |
| 방 타입 | Normal · MonsterDen · Spawn · Stair · Elite (5의 배수+5 층 자동) |
| 적 AI | FSM (Idle → Chase → Attack), A* 경로탐색, Contact/Ranged 행동 분기, Contact Special Attack(Rush/Jump), Elite Pattern Set(Projectile/Dash/Jump), `isStationary`/`immuneToKnockback` 플래그 |
| 적 전투 | 근접 접촉 피해 + Contact Special(Rush 돌진 / Jump 도약 + 착지 임팩트) + 원거리 투사체 (Single/Burst/Spread/Circle) + 벽 반사 + Elite 패턴 사이클 — Rush/Jump/Projectile/Elite 임팩트는 `EnemyAttackImpactData`(knockback·slow·stun) 적용 |
| 아이템 / 인벤토리 | `ItemDatabase` ScriptableObject + `ItemData` (Key·Currency·Consumable·Equipment·Relic·Material·Soul) + `useEffects`/`passiveEffects` + `soulFormId` + `removeOnFloorTransition`/`removeOnDungeonExit` 플래그. `PlayerInventory`(InventoryItemStack 리스트, 스택/논-스택, 층/던전 이탈 시 자동 정리) — 적이 `EnemyInventory.AddDropItem` 으로 사망 시 `DropItemSpawner.SpawnDrops` → `DroppedItem.OnTriggerEnter2D` 에서 `PlayerInventory.AddItem` 으로 픽업. Consumable 은 슬롯 클릭으로 `HealHp` 사용, Relic 은 소지 중 평면 스탯 패시브를 `PlayerItemStats` 로 합산, Soul 은 `PlayerInventory.OwnsSoulForm` 을 통해 Form 보유권으로 판정 |
| Elite Floor / Elite Key | 층이 `% 10 == 5` 이면 MST leaf 가장 깊은 방을 Elite Room 으로 자동 지정, Elite Door 로 봉인. 같은 층의 일반 방 적 중 결정론적으로 1마리가 `elite_key` 를 드랍하며 플레이어가 습득하면 PlayerInventory 에 들어가고 Elite Door 접촉 시 자동 개방·열쇠 1개 소모 |
| Elite 적 | `EnemyData.isElite=true` + `elitePatternSet` 부착 시 `ElitePatternRunner`(MonoBehaviour)가 매 Tick `ElitePatternSet.Patterns` 를 순회해 쿨다운·사거리 조건 만족 패턴 1개를 실행. 패턴 종류: Projectile / Dash / Jump (ScriptableObject 변형) |
| 시야 | Fog of War (Bresenham 시야 차단, 미탐사/탐사/현재시야 3단계) |
| 진행 방식 | 계단을 통한 층 이동 (무한 층 구조) |
| 입력 키 | `PlayerInputKeySettings` ScriptableObject — `controlScheme`(Classic/ActionMouseAim) + 이동/액션/스킬 키를 에셋 1개로 일괄 설정. ActionMouseAim 은 마우스 버튼 바인딩(`InputBinding`/`PointerButton`) 지원. 중복 키·`Key.None` 자동 경고 |

---

## 2. 레이어 아키텍처

전체 시스템은 **Clean Architecture** 원칙에 따라 4개 레이어로 분리되어 있습니다.

```
┌──────────────────────────────────────────────────────────────┐
│  Application Layer (MonoBehaviour)                           │
│  PlayerController · PlayerInputReader · PlayerInventory      │
│  PlayerCombatController · PlayerDashController               │
│  PlayerAnimationController · PlayerFormController            │
│  DungeonManager · FloorTransitionService                     │
│  LocationTransitionManager (구 TownDungeonTransitionManager) · LocationRoot │
│  TeleportService                                             │
│  EnemyBrain · NormalEnemyBrain · RoomSpawner                 │
│  ElitePatternRunner                                          │
│  EnemyInventory · DropItemSpawner · DroppedItem              │
│  ProjectilePool · ProjectileController                       │
│  FogOfWarController                                          │
│  GamePauseController                                         │
│  DeveloperConsoleUI · DeveloperConsoleCommandExecutor        │
│  EliteArenaEncounterController                               │
│  EliteArenaPortal · EliteArenaReturnPortal                   │
│  GameOverFlowController · GameOverSceneReloadRestartHandler  │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure Layer (ScriptableObject Event Bus / Data)    │
│  DungeonEventChannel · CombatEventChannel                    │
│  PlayerInputKeySettings · ItemDatabase                       │
│  TeleportDestinationDatabase                                 │
├──────────────────────────────────────────────────────────────┤
│  Domain / Pure Service Layer (순수 C# — Unity 의존 없음)      │
│  DungeonData · DungeonGenerator · RoomRegistry               │
│  DungeonQueryService · SpawnPositionService                  │
│  WeaponData · SkillData · EnemyData · ItemData               │
│  PlayerFormData · PlayerFormId                               │
│  InventoryItemStack                                          │
│  ElitePatternSet · ElitePatternData · ElitePatternRuntime    │
│  ElitePatternContext (+ EliteProjectile/Dash/Jump 변형)      │
│  PlayerResource · AttackPattern · AStarPathfinder            │
│  SkillExecutor · SkillTargetResolver · SkillExecutionContext │
│  SkillSlotRuntime · SkillCooldownController                  │
│  ProjectileFireService · ProjectileFireRequest               │
│  AimDirectionUtility · CombatLayers · CharacterPhysicsSetup  │
│  MovementBlockerQuery · DeterministicSeedUtility · PerfStage │
│  GamePauseSource · GameLocationType                          │
│  LocationRootRegistry · LocationMinimapRegistry              │
│  DeveloperConsoleService · DeveloperConsoleCommandResult     │
│  WalkabilityQuery · WorldEnvironmentQuery                    │
│  WalkabilityArea                                             │
├──────────────────────────────────────────────────────────────┤
│  Presentation Layer                                          │
│  DungeonTilemapRenderer                                      │
│  EnemyHealthBar · PlayerStatusBarUI                          │
│  SkillSlotUI · SkillUIManager · SkillRangePreviewer          │
│  PlayerStatusEffectUI · StatusEffectIconView                 │
│  HitFlashFeedback · PlayerInvincibilityFlashFeedback         │
│  EnemyAnimationController · FogVisibilityRenderer            │
│  MinimapController · TilemapMinimapSource                    │
│  InventoryUIController · InventorySlotUI · UIDraggableWindow │
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
- **공유 8방향 조준**: AimDirectionUtility가 입력 → 8방향 raw / 정규화 / 그리드 카디널 변환을 단일 책임으로 처리 (스킬·투사체·대시·미리보기 공용). ActionMouseAim 프리셋에서는 커서 방향을 양자화 없이 연속 벡터로 사용해 **360° 자유조준** — grid 패턴은 `AttackPattern.FillTargets` 의 `Vector2 facing` 연속 오버로드(Cone=연속 각도 / Line·Single=셀 반올림)로 처리
- **적 공격 임팩트 통합**: `EnemyAttackImpactData`(knockback·slow·stun) 구조로 Rush·Jump·Projectile 의 부가 효과를 동일하게 관리, `PlayerCombatController.ApplyEnemyCombatImpact()` 단일 진입점으로 데미지·넉백·슬로우·스턴 적용
- **런타임 탐색 캐싱**: `DungeonManager` 가 `RoomSpawner` 참조를 SerializeField + 1회 경고로 캐싱, 매 `FindAnyObjectByType` 호출 회피 (다른 컨트롤러도 동일 패턴 사용)
- **위치 기반 미니맵 전환**: `LocationMinimapRegistry`(static Dict) + `TilemapMinimapSource.OnEnable/OnDisable` 자동 등록으로, `MinimapController`가 씬 계층을 직접 탐색하지 않고 locationId 조회만으로 소스 전환
- **텔레포트 데이터 드리븐**: `TeleportDestinationDatabase` ScriptableObject 가 목적지(id · displayName · description · locationType · locationRootId · localSpawnPosition · minimapLocationId)를 보유하고, `LocationRootRegistry`(static Dict, `LocationRoot.OnEnable/OnDisable` 자동 등록)가 씬 내 LocationRoot 트랜스폼을 노출 — 텔레포트 시 `root.TransformPoint(localSpawnPosition)` 으로 월드 좌표가 계산됨 (씬 마커 MonoBehaviour는 더 이상 없음)
- **입력 키 데이터 드리븐**: `PlayerInputKeySettings` ScriptableObject 1개가 `controlScheme`(Classic/ActionMouseAim) + 이동/액션/스킬 키(키보드 + 마우스 버튼)를 보유, `PlayerInputReader` 가 매 프레임 참조 — 에셋 교체·프리셋 전환만으로 조작 변경, OnValidate 단계에서 `Key.None` / 중복 키·마우스 버튼 자동 경고
- **아이템 데이터 드리븐**: `ItemDatabase` ScriptableObject 가 `itemCode` 키로 `ItemData` (DisplayName/Icon/ItemType/Stackable/MaxStack/useEffects/passiveEffects/soulFormId)를 보관, `EnemyInventory.AddDropItem(itemCode)` → `DropItemSpawner.SpawnDrops` → `DroppedItem.Initialize` 파이프라인이 코드 수정 없이 새 아이템을 지원. Consumable 즉시 효과는 `ItemEffectApplier`, Relic 평면 스탯은 `PlayerItemStats`, Soul 기반 Form 보유권은 `PlayerInventory.OwnsSoulForm` 이 담당
- **Elite Floor 자동화**: `floor % 10 == 5` 인 층에서 `DungeonGenerator.AssignEliteRoom` 이 시작 방에서 MST 깊이 최대 leaf(동률 시 거리 최대)를 Elite Room 으로 선정, `DungeonTilemapRenderer.PlaceEliteDoors` 가 perimeter 의 corridor-인접 셀에 `eliteDoorTile` 배치 — `RoomSpawner.PrepareEliteKeyPlan` 이 결정론적 RNG (`EliteKeyDomain`) 로 같은 층의 일반 방 적 1마리에 `elite_key` 드랍 부여, 플레이어가 키 보유 상태로 Elite Door 접촉 시 `TryOpenEliteDoorWithKey(PlayerInventory, ItemData)` 가 한 셀 카빙 + 인벤토리에서 키 1개 제거
- **Elite 적 패턴 시스템**: `EnemyData.isElite=true` + `elitePatternSet` 부착 시 `ElitePatternRunner` (MonoBehaviour) 가 매 Tick `ElitePatternSet.Patterns` 를 순회해 쿨다운·`MinRange`/`MaxRange`·weight 조건을 만족하는 첫 패턴을 실행. 패턴 종류는 `EliteProjectilePatternData` / `EliteDashPatternData` / `EliteJumpPatternData` ScriptableObject 변형이며 각각 windup·animation key·EnemyAttackImpactData 를 보유. 기존 Contact Special(Rush/Jump)와는 독립된 사이클 — Special 은 모든 Contact 적의 1개 고정 공격, Elite Pattern 은 Elite 전용 다중 패턴 풀
- **인벤토리 데이터 드리븐**: `PlayerInventory`(MonoBehaviour) 가 `InventoryItemStack` 리스트를 보유, stackable/maxStack 정책을 자동 적용. `ItemData.removeOnFloorTransition`/`removeOnDungeonExit` 플래그로 층/던전 이탈 시 자동 정리. `OnInventoryChanged` 는 UI 갱신과 Relic 패시브 재계산의 단일 트리거이며, Elite Key 도 일반 ItemData 한 항목으로 통합 (과거의 `PlayerEliteKeyInventory` 는 제거됨). `OwnsSoulForm(formId)` 는 ItemType.Soul + soulFormId + count>0 조합으로 Form 소유 여부를 판정
- **게임 일시정지 통합**: `GamePauseController` 가 `GamePauseSource`(DeveloperConsole / Inventory / PauseMenu / Cutscene) 별 요청 카운트로 `Time.timeScale=0` 토글. 여러 출처가 동시에 정지를 요청해도 1회만 적용, 마지막 출처 해제 시 이전 timeScale 복원
- **GC 최소화**: 이벤트 인자에 `struct` 사용, 코루틴 캐싱, NonAlloc 물리, A* 버퍼 재사용, 스킬 슬롯 / 투사체 / 시야 셀 버퍼 재사용
- **공간 독립 walkability**: `WalkabilityQuery`(static) + `WalkabilityArea`(OnEnable/OnDisable 자동 등록) 로 Dungeon·Elite Arena·Boss Arena 등 모든 공간에서 단일 query API 사용. 전투 코드는 `WorldEnvironmentQuery` 파사드만 호출하며 공간 종류를 알지 못해도 됨 — 새 공간은 `WalkabilityArea` 컴포넌트 부착만으로 자동 등록
- **Elite Arena 포탈 lifecycle 관리**: `EliteArenaEncounterController.Active` 정적 참조 + `RoomSpawner.PrepareEliteRoomPortal` → `EliteArenaEncounterController.PrepareEntrancePortal` 에서 생성주기 시작, `MarkCompletedAndDisable` + `ClearRuntimeState` 로 층 이동·던전 이탈 시 일괄 정리
- **개발자 콘솔 실행 분리**: `DeveloperConsoleService`(파싱·등록) + `DeveloperConsoleCommandExecutor`(MonoBehaviour, 게임 상태 변경) 로 책임 분리 — 서비스 레이어가 Unity 의존성 없이 테스트 가능, 새 명령은 Executor에 메서드 추가만으로 등록. 아이템 지급은 `/give <category> <code> [count]` 로 재편되어 category(ItemType) 검증과 category별 itemCode 자동완성을 수행

---

## 3. 파일 구조

```
Assets/Scripts/
│
├── PlayerController.cs             # 입력·이동·방 감지·대시 중 이동 위임 + Elite Door 접촉 시 자동 개방(TryOpenEliteDoorOnContact, PlayerInventory + elite_key ItemData 조회)
├── PlayerInputReader.cs            # 키보드/마우스 입력 단일 집계 (실행 순서 제어) — PlayerInputKeySettings 참조, ActionMouseAim 시 커서 월드 조준(HasMouseAim/AimWorldPoint) + UI 위 클릭 차단
├── PlayerInputKeySettings.cs       # 키 바인딩 ScriptableObject — controlScheme(Classic/ActionMouseAim) + 키보드/마우스 바인딩(InputBinding/PointerButton)
├── PlayerAnimationController.cs    # 4방향 이동 애니메이션 (MoveX/Y, LastMoveX/Y) — PlayerFormController 와 협력
├── PlayerFormController.cs         # 플레이어 시각 폼 (PlayerFormData) 적용 — AnimatorController 스왑, default sprite, facing flipX, dash visual rotation/token, SkillData.AnimationType (Attack/Spin/Dash/CustomTrigger) 별 trigger 발동. TrySwitchForm 시 PlayerInventory 기반 Soul 보유 게이팅(NotOwned, Normal 화이트리스트, inventory 미결선 폴백) 후 ApplyForm/EquipWeapon(form.DefaultWeapon) 로 loadout(무기·스킬) 동시 적용
├── GameLocationType.cs             # 위치 분류 enum (Town/Dungeon)
│
├── DungeonManager.cs               # 던전 생애주기 조율 (Facade) — extraCandidateCount/extraOverlapScoreWeight 등 EXTRA 점수 weight 노출, PrepareEliteKeyPlan
│                                    #   FloorTransition 시 CleanupPlayerInventoryForFloorTransition(RemoveOnFloorTransition) + Dungeon 런타임 오브젝트 일괄 정리
│
├── LocationTransitionManager.cs # 마을↔던전 전환 조율 (TeleportDestinationDatabase 기반)
│                                    #   SetDungeonSource/SetTilemapSource → MinimapController 라우팅
│                                    #   CleanupDungeonRuntime (투사체·적·드랍 아이템·Elite Key 일괄 회수) / StartNewDungeonRun
│                                    #   (구 EnterDungeon/EnterTown 디버그 메서드는 제거됨 — 디버그 진입은 콘솔 /tp 명령만 사용)
│
├── TeleportService.cs              # 텔레포트 트리거 (OnTriggerEnter2D + 쿨다운 + transitionManager 위임)
├── TeleportDestinationDatabase.cs  # 텔레포트 목적지 ScriptableObject DB
│                                    #   (TeleportLocationData: id · displayName · description · locationType ·
│                                    #    locationRootId · localSpawnPosition · minimapLocationId)
├── TeleportDestinationIdAttribute.cs # 인스펙터 문자열 필드를 destination id 드롭다운으로 렌더링하는 PropertyAttribute
├── LocationRoot.cs                 # 씬에 배치하는 위치 루트 (OnEnable/OnDisable → LocationRootRegistry 자동 등록)
└── LocationRootRegistry.cs         # 위치 루트 static Dict 레지스트리 — LocationTransitionManager 가 root.TransformPoint(localSpawnPosition) 으로 월드 좌표 계산
│
├── Data/
│   ├── DungeonData.cs              # 타일 그리드 + 방 목록 (Domain)
│   ├── WeaponData.cs               # 무기 ScriptableObject
│   ├── SkillData.cs                # 스킬 ScriptableObject (executionType + Projectile/Dash 필드 + Animation 필드 + 자원: resourceType(None/Bullet/ParryStack)/requiredAmount/consumeAmount/bulletShortageMode/reloadAmount). MP(mpCost) 폐지
│   ├── SkillExecutionType.cs       # 스킬 실행 라우팅 enum (InstantArea/Projectile/Dash/AreaOverTime/Buff)
│   ├── SkillResourceType / BulletShortageMode  # 스킬 자원 타입 enum (None/Bullet/ParryStack), 탄 부족 처리(RequireFullCost/AllowPartialUse) — SkillData.cs 내 정의
│   ├── PlayerFormData.cs           # 플레이어 폼 ScriptableObject (formId/displayName/animatorController/defaultSprite/facing·dash 옵션 + basicAttackMode(Damage/Parry/Bullet) + defaultWeapon=loadout). skills[] 필드는 제거(loadout 단일 소스=WeaponData)
│   ├── PlayerFormId.cs             # 폼 식별 enum (Normal/Sword/Dagger/Freischutz/Parry)
│   ├── ProjectileTargetHitMode.cs  # 타깃 적중 정책 enum (DestroyOnHit/Pierce/HitOncePerTarget)
│   └── EnemyData.cs                # 적 ScriptableObject — Contact(+Special Rush/Jump) / Ranged + 투사체 패턴
│                                    #   (EnemySpecialAttackType: None/Rush/Jump + 전용 파라미터 그룹)
│                                    #   (EnemyAttackImpactData struct: knockback/slow/stun — rushImpact/jumpImpact/projectileImpact 공용)
│                                    #   (isStationary: AI 이동/분리/넉백 위치 변화 정지 + Rigidbody FreezeAll, immuneToKnockback: 데미지·상태이상은 적용되나 임펄스만 무시)
│                                    #   (minFloor/maxFloor: 등장 가능 층 범위 — IsAvailableOnFloor(floor) 필터, OnValidate 가 잘못된 범위 자동 경고)
│                                    #   (isElite + elitePatternSet: Elite 적 활성 — ElitePatternRunner 가 elitePatternSet.Patterns 순회 실행. OnValidate 가 둘 중 하나만 설정된 경우 경고)
│
├── Generate/
│   ├── DungeonGenerator.cs         # BSP + Prim MST 생성 알고리즘 (순수 C#) — IsEliteFloor(floor%10==5) → AssignEliteRoom (MST leaf 가장 깊은 방), EXTRA 통로는 elite room 제외
│   ├── DungeonTypes.cs             # 공유 타입 (RoomType, RoomInfo+StableRoomKey+IsElite, DeterministicSeedUtility { EnemySpawnDomain, EliteKeyDomain }, 이벤트 인자)
│   ├── DungeonEventChannel.cs      # 던전 이벤트 버스 (ScriptableObject)
│   ├── DungeonQueryService.cs      # 그리드 유틸리티 (IsWalkable, 좌표 변환)
│   ├── SpawnPositionService.cs     # 플레이어 스폰 좌표 계산 서비스
│   ├── FloorTransitionService.cs   # 층 이동 코루틴·로딩 화면·GC 관리
│   ├── RoomRegistry.cs             # 방 상태 관리 (타입·문 닫힘) — Elite Room 은 항상 IsExempt 처리
│   ├── DungeonTilemapRenderer.cs   # Tilemap 3레이어 배치 (바닥·벽·문) + eliteDoorTile + PlaceEliteDoors / TryOpenEliteDoorWithKey
│   ├── FogOfWarController.cs       # 안개 시야 — Bresenham LoS, 미탐사/탐사/현재시야 3상태
│   ├── RoomFootprintSampler.cs     # 방 overlap 검증용 공통 9-sample 유틸 (PlayerController·DungeonTilemapRenderer 공용)
│   ├── SpawnRegion.cs              # 스폰 지역 플래그 (Dungeon/Forest/Castle)
│   └── RoomSpawner.cs              # 방 진입 시 적 스폰, 방 클리어 감지, PrepareEliteKeyPlan(결정론적 elite_key 드랍 슬롯 선정)
│
├── Items/
│   ├── ItemType.cs                 # 아이템 분류 enum (Key/Currency/Consumable/Equipment/Relic/Material/Soul)
│   ├── ItemEffect.cs               # ItemEffectType + ItemEffect(value) — 사용 효과 / 패시브 평면 스탯 데이터
│   ├── ItemEffectApplier.cs        # Consumable useEffects 적용 정적 서비스 (HealHp → PlayerCombatController.RestoreHp)
│   ├── ItemData.cs                 # 직렬화 가능한 단일 아이템 정의 (itemCode·displayName·icon·description·itemType·stackable·maxStack·useEffects·passiveEffects·soulFormId·정리 플래그)
│   ├── ItemDatabase.cs             # ScriptableObject — itemCode→ItemData Dictionary 캐시 + OnValidate 중복/공백 검사 + itemCode 자동완성 목록 제공
│   ├── DroppedItem.cs              # 월드에 떨어진 아이템 MonoBehaviour — OnTriggerEnter2D 시 PlayerInventory.AddItem 호출 (성공 시 Destroy + DropItemSpawner.Unregister)
│   └── DropItemSpawner.cs          # 사망 위치 기준 EnemyInventory 의 드랍 목록을 Instantiate (Singleton, dropSpacing 으로 다중 아이템 정렬, ClearAllActiveDrops)
│
├── Inventory/
│   ├── PlayerInventory.cs          # MonoBehaviour — InventoryItemStack 리스트 보유, AddItem/RemoveItem/HasItem/GetItemCount,
│   │                                #   OwnsSoulForm(formId) (ItemType.Soul + soulFormId 기반 Form 보유 판정),
│   │                                #   RemoveItemsOnFloorTransition / RemoveItemsOnDungeonExit (ItemData 플래그 기반),
│   │                                #   OnInventoryChanged 이벤트 (InventoryUIController 가 구독)
│   ├── PlayerItemStats.cs          # 순수 C# Relic 패시브 집계기 — MaxHp/Attack/Defense/MoveSpeedBonus 합산
│   └── InventoryItemStack.cs       # [Serializable] (ItemData, count) 스택 1개 — Add/Remove
│
├── System/
│   ├── GamePauseController.cs      # 일시정지 컨트롤러 (Singleton-ish s_Active) — GamePauseSource 별 요청 카운터 4개,
│   │                                #   Pause/Resume API, ApplyPauseState 가 Time.timeScale 토글, OnDisable 시 timeScale 복원
│   └── GamePauseSource.cs          # 일시정지 출처 enum (DeveloperConsole/Inventory/PauseMenu/Cutscene)
│
├── Combat/
│   ├── IDamageable.cs              # 피해 수신 인터페이스
│   ├── AttackPattern.cs            # 공격 패턴 enum + 좌표 계산기 (FillTargets API)
│   ├── AttackExecutor.cs           # 공격 판정·히트 감지·데미지 적용
│   ├── AimDirectionUtility.cs      # 8방향 입력 양자화 + raw/정규화/카디널 변환 (Domain)
│   ├── CombatLayers.cs             # Enemy/Player Layer 캐싱 + ContactFilter2D 공유
│   ├── CharacterPhysicsSetup.cs    # Rigidbody2D + CircleCollider2D 공통 셋업 (Player·Enemy 공유, NoFriction 머터리얼 캐시, 기존 CircleCollider 보존)
│   ├── MovementBlockerQuery.cs     # Player 이동/대시가 `EnemyData.blocksMovement=true` 적과 겹치는지 판정 (Collider2D→EnemyController 캐시)
│   ├── PlayerCombatController.cs   # 플레이어 전투 진입점 (HP·공격·스킬·무적시간·8방향 조준·castDelay/recoveryDelay 잠금) + ISkillResourceLedger(Bullet 탄창·재장전 / ParryStack 패리) (MP 폐지)
│   │                               #   + ApplyEnemyCombatImpact(damage, hitDir, knockback, slow, stun) 단일 진입점
│   │                               #   + 슬로우(_enemySlows 강도 최대값) / 스턴(_stunTimer) / 넉백(EnemyKnockbackRoutine → playerMovement.TryApplyExternalDisplacement)
│   │                               #   + IsSlowed/IsStunned/MoveSpeedMultiplier · OnStatusEffectApplied/Ended(PlayerStatusEffectType)
│   ├── PlayerStatusEffectType.cs   # 플레이어 상태이상 enum (Slow, Stun)
│   ├── PlayerResource.cs           # HP 상태 컨테이너 (Domain) — MP 폐지. 스킬 자원은 PlayerCombatController 의 ISkillResourceLedger(Bullet/ParryStack)
│   ├── PlayerDashController.cs     # 대시 코루틴 — 발자국 검사·외부 무적·path/contact 데미지 분리
│   ├── SkillExecutor.cs            # 스킬 실행 라우팅 (InstantArea/Projectile/Dash 분기)
│   ├── SkillTargetResolver.cs      # 스킬 셀·미리보기 반경·투사체 거리 공통 계산
│   ├── SkillExecutionContext.cs    # 스킬 1회 사용에 필요한 런타임 정보 컨테이너
│   ├── SkillSlotRuntime.cs         # 스킬 슬롯 1칸의 SkillData·쿨다운 상태 (MonoBehaviour 미의존). CanUse(ISkillResourceLedger) 로 쿨다운+자원 확인. ISkillResourceLedger 인터페이스 정의
│   ├── SkillProjectileUtility.cs   # 유효 발사 수 계산 + Bullet AllowPartialUse 판정 헬퍼
│   ├── SkillExecutionResult        # Execute 결과(Success + 실제 ResourceConsumed) — 동적 소모용, SkillExecutor.cs 내 정의
│   ├── SkillCooldownController.cs  # 기본 공격 쿨다운만 담당 (스킬 쿨다운은 슬롯 런타임이 보유)
│   ├── ProjectileFireService.cs    # 투사체 발사 패턴 처리 (Single/Burst/Spread/Circle)
│   ├── ProjectileFireRequest.cs    # 투사체 1회 발사 파라미터 (적·플레이어 공용)
│   ├── ProjectileController.cs     # 풀링 발사체 — 벽 반사·관통·파괴, 맵 범위 밖 자동 release, Fog 가시성, 회전 모드 (KeepPrefab/FaceMoveDirection)
│   ├── ProjectilePool.cs           # 투사체 사전 풀링 (SetActive/DisableComponents 모드) — ReleaseAllActiveProjectiles로 층 이동 시 일괄 회수
│   ├── HitFlashFeedback.cs         # 피격 시 SpriteRenderer 색상 점멸 (적·플레이어 공용)
│   ├── PlayerInvincibilityFlashFeedback.cs # 무적 시 셰이더 _FlashAmount 보간 (PropertyBlock)
│   ├── CombatEventChannel.cs       # 전투 이벤트 버스 (ScriptableObject)
│   └── WorldEnvironmentQuery.cs    # 전투 코드용 환경 query 파사드 — WalkabilityQuery에 위임 (IsWalkablePoint/IsFootprintWalkable/HasGeometryLineOfSight/IsWallAt/IsInsideKnownCombatSpace)
│
├── Visual/
│   └── FogVisibilityRenderer.cs    # FogOfWar visible 상태에 따라 Renderer.enabled 토글 (적·적 투사체 공용)
│
├── Enemy/
│   ├── EnemyController.cs          # 적 HP·피해·사망·상태이상·넉백 벽 클램핑 (Die 시 EnemyBrain.HandleDeathStarted 호출)
│   │                               #   + RequireComponent(EnemyInventory), MarkAsEliteKeyHolder/ClearEliteKeyHolder
│   │                               #   + Die 시 DropItemSpawner.SpawnDrops(_inventory, position) 호출
│   ├── EnemyInventory.cs           # 적 드랍 목록 (EnemyDropItem readonly struct: ItemCode·Amount)
│   ├── EnemyBrain.cs               # FSM 조율 추상 + MovementHandler/TargetHandler/ActionHandler
│   │                               #   + EnemySpecialAnimationType(Charge/Rush/Jump/Land) 트리거 라우팅
│   │                               #   + LockSpecialFacing/UnlockSpecialFacing/HandleDeathStarted
│   │                               #   (상태 인스턴스는 EnemyStates.cs에 정의, BossEnemyBrain은 CreateState 오버라이드)
│   ├── NormalEnemyBrain.cs         # 기본 몬스터용 경량 Brain (커스텀 상태 없음)
│   ├── EnemyStates.cs              # IdleState · ChaseState · AttackState (internal sealed, A* 추격 포함)
│   ├── EnemyMovementHandler.cs     # A* 이동 + 군중 분리 + Ranged 이동 분기 (Chase/Kiting/Random)
│   ├── EnemyTargetHandler.cs       # 플레이어 감지·시야 갱신
│   ├── EnemyActionHandler.cs       # Contact/Ranged 행동 사이클·쿨다운
│   │                               #   + Contact Special Attack 상태머신 (Windup→Rush/Jump→Recovery)
│   │                               #   + Rush 경로 데미지(1회 제한 HashSet) / Jump 착지 임팩트
│   ├── AStarPathfinder.cs          # GC 최소화 A* 탐색기
│   ├── EnemyHealthBar.cs           # 머리 위 체력바 렌더러
│   ├── EnemyAnimationController.cs # 적 이동/공격/사망 애니메이션 + 사격 방향 페이싱
│   │                               #   + Charge/Rush/Jump/Land/Dash/Projectile 트리거 + LockFacing/UnlockFacing (Special 중 페이싱 고정)
│   │                               #   + PlayEliteAnimation(EnemyAnimationKey) — Elite Pattern 런타임이 호출
│   ├── EnemyPoolManager.cs         # 적 오브젝트 풀
│   └── Elite/
│       ├── ElitePatternSet.cs                 # ScriptableObject — `List<ElitePatternData>` 컨테이너 (`EnemyData.elitePatternSet` 에 연결)
│       ├── ElitePatternData.cs                # 추상 ScriptableObject — DisplayName/Cooldown/MinRange/MaxRange/Weight/RecoveryDuration + CreateRuntime() 추상
│       ├── ElitePatternRuntime.cs             # 추상 패턴 런타임 — Start/Tick/Cancel + IsFinished 플래그
│       ├── ElitePatternContext.cs             # Brain·Enemy·Data·Movement·Action·Animation·Collider·DungeonManager·ProjectileFireService·CoroutineRunner 일괄 노출
│       ├── ElitePatternRunner.cs              # MonoBehaviour — `Initialize(brain)` 후 매 Tick `EnemyData.IsElite` 확인 → 쿨다운/사거리 충족 패턴 1개 실행, Finish 시 cooldown 적용
│       └── Patterns/
│           ├── EliteProjectilePatternData.cs  # 발사 패턴 (windup, prefab, speed, lifetime, firePattern, count, spread, burstInterval, wallHitMode, maxBounceCount, impact)
│           ├── EliteProjectilePatternRuntime.cs # windup → Fire(ProjectileFireService) → recovery 순으로 진행, EnemyAnimationKey 분기로 Animator 트리거
│           ├── EliteDashPatternData.cs        # 돌진 (windup, dashSpeed, damage, hitRadius, stopOnWall, lockFacingDuringDash, windupAnimation, dashAnimation)
│           │                                  #   dashDuration 제거 → dashSpeed 기반 목표 위치 이동으로 변경
│           ├── EliteDashPatternRuntime.cs     # windup → 목표 위치(플레이어 위치 기반) 결정 → dashSpeed×dt 이동 → 타겟 1회 데미지 → recovery
│           │                                  #   WalkabilityQuery.TryFindNearestWalkable 로 목표 위치 보정 (Arena/Dungeon 공용)
│           ├── EliteJumpPatternData.cs        # 도약 (windup, jumpDuration, maxDistance, impactDamage, impactRadius, jumpVisualHeight, stayInRoom, lockFacingDuringJump)
│           └── EliteJumpPatternRuntime.cs     # windup → WalkabilityQuery 기반 착지점 결정 → 비행 → 착지 임팩트 → recovery
│
├── UI/
│   ├── MinimapController.cs        # 이중 모드 미니맵 — Dungeon(DungeonData 기반) / Tilemap(TilemapMinimapSource 기반)
│   │                                #   SetDungeonSource() / SetTilemapSource(locationId) 공개 API
│   │                                #   Texture2D → RawImage 렌더링, 플레이어 마커 오버레이
│   │                                #   Dungeon: Y축 뒤집기(row0=top) / Tilemap: Y축 그대로(Y↑=Y↑)
│   ├── TilemapMinimapSource.cs     # 위치별 Tilemap 미니맵 소스 MonoBehaviour
│   │                                #   ① 명시 모드: groundTilemap/wallTilemap/doorTilemap 인스펙터 직접 연결 (backward compat)
│   │                                #   ② 자동 모드: autoDiscoverChildren=true 시 자식 Tilemap 을 GameObject Layer(Walkable/Wall/Door)로 분류
│   │                                #   OnEnable/OnDisable → LocationMinimapRegistry 자동 등록, 색상 3종(ground/wall/door) 분리
│   ├── LocationMinimapRegistry.cs  # 씬 내 TilemapMinimapSource를 locationId로 조회하는 정적 레지스트리
│   ├── PlayerStatusBarUI.cs        # 플레이어 HP 상태바 (슬라이더 + 텍스트) + Elite Key 아이콘 — PlayerInventory.OnInventoryChanged 로 elite_key 보유 수에 따라 아이콘 토글. MP 바 제거
│   ├── ParryStackBarUI.cs          # 패리 폼 자원 UI — 현재 ParryStack 을 Slider 로 표시 (임시). 현재 폼이 Parry 일 때만 노출
│   ├── FreischutzMagazineUI.cs     # 마탄 폼 탄창 UI — Bullet/Bullet_empty 이미지 칸 + x/max·Reloading 텍스트. 현재 폼이 Bullet 일 때만 노출
│   ├── PlayerStatusEffectUI.cs     # 슬로우/스턴 아이콘 컨테이너 — PlayerCombatController.OnStatusEffectApplied/Ended 구독, RefreshActiveIcons 매 프레임
│   ├── StatusEffectIconView.cs     # 슬롯 1칸 아이콘 뷰 (icon · fill · 남은시간 텍스트)
│   ├── SkillSlotUI.cs              # 스킬 슬롯 1개 렌더링 (아이콘·쿨타임)
│   ├── SkillUIManager.cs           # 4슬롯 초기화·층 변경 갱신
│   ├── SkillRangePreviewer.cs      # Q/W/E/R 미리보기 — InstantArea/Projectile/Dash + 기본공격 홀드
│   ├── GameOverFlowController.cs   # 사망 이벤트 구독 → 지연 후 게임오버 UI 표시
│   ├── InventoryUIController.cs    # 인벤토리 패널 — PlayerInventory 구독, 5개 카테고리 탭 필터/전체 그룹 정렬, 슬롯 클릭 Consumable 사용, 인벤토리 키·ESC 토글, 콘솔 열림 시 자동 닫힘
│   ├── InventorySlotUI.cs          # 인벤토리 슬롯 단일 뷰 (아이콘·수량 텍스트 Bind) + IPointerClickHandler 로 컨트롤러에 클릭 위임
│   ├── UIDraggableWindow.cs        # 드래그 가능한 UI 패널 기반 MonoBehaviour
│   ├── GameOverUIController.cs     # 게임오버 UI 페이드 인/아웃·확인 버튼 (UI 참조 누락 시 1회 경고 후 표시 skip)
│   ├── GameOverRestartHandler.cs   # IGameOverRestartHandler 인터페이스
│   └── GameOverSceneReloadRestartHandler.cs # 활성 씬 재로드로 재시작
│
├── DebugConsole/
│   ├── DeveloperConsoleUI.cs       # 개발자 콘솔 UI MonoBehaviour — ` 키 토글, TMP_InputField 입력, ScrollRect 로그, Tab 자동완성 순환, GamePauseController 연동
│   ├── DeveloperConsoleService.cs  # 순수 C# 명령 레지스트리 — 명령 Dictionary + 인수 제안 프로바이더 Dictionary, Execute/GetArgumentSuggestions/GetCommandNames API, /give category resolver
│   ├── DeveloperConsoleCommandExecutor.cs # 명령 실행 MonoBehaviour (구 CommandContext 대체) — 게임 상태 변경 호출을 담당 (RoomSpawner·DungeonManager·LocationTransitionManager·EliteArenaEncounterController·PlayerController·PlayerInventory·PlayerFormController 참조 보유)
│   └── DeveloperConsoleCommandResult.cs  # 명령 실행 결과 (readonly struct) — Success/Error/Clear/Ignored 팩토리 메서드
│
├── EliteArena/
│   ├── EliteArenaEncounterController.cs  # Elite Arena 인카운터 총괄 — static Active, 입장/복귀/취소, Elite spawn, portal lifecycle, WalkabilityArea passthrough API
│   ├── EliteArenaPortal.cs              # Elite Room 내 진입 포탈 MonoBehaviour — 플레이어 접촉 시 TryEnterArenaFromPortal 호출, Bind/MarkCompletedAndDisable/ResetRuntimeState
│   └── EliteArenaReturnPortal.cs        # Arena 내 복귀 포탈 — Elite 사망 후 ShowReturnPortal로 활성화, 접촉 시 TryReturnFromArena 호출
│
├── World/
│   ├── WalkabilityArea.cs    # 전투 공간 단위 컴포넌트 (Elite Arena 등) — walk/wall Tilemap 쌍, OnEnable/OnDisable → WalkabilityQuery 자동 등록
│   │                         #   IsInsideWorld/IsWalkableWorld/IsFootprintWalkableWorld/HasLineOfSightWorld/TryGetNearestWalkableWorldPosition API
│   │                         #   Inspector 튜닝: footprintInsetMultiplier(0.1~1.0, 기본 0.85) — 4-corner sample 거리를 radius 대비 인셋으로 완화,
│   │                         #                   debugLogFootprintFailures(1초/회 throttle) — 어떤 cell 이 왜 막혔는지 로그,
│   │                         #                   drawCellBoundsGizmo — Selected 시 walkTilemap.cellBounds 시각화
│   │                         #   walk/wall Tilemap 이 서로 다른 transform/cellSize 여도 각 Tilemap 의 WorldToCell 로 안전 처리
│   └── WalkabilityQuery.cs   # 정적 라우팅 서비스 — 등록된 WalkabilityArea 우선, 없으면 DungeonData fallback
│                             #   IsWalkable/IsFootprintWalkable/HasLineOfSight/IsInsideKnownArea/TryFindNearestWalkable
│                             #   FindAreaContaining(world) 으로 호출자가 "Area 내부인지" 자체 분기 가능 (FogVisibilityRenderer 등이 사용)
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
├── SkillDataEditor.cs              # SkillData CustomEditor — Basic/Resource(자원·Bullet 설정)/InstantArea/Projectile/Dash 섹션 + Reserved foldout + 음수·non-positive·partial 설정 경고
├── EnemyDataEditor.cs              # EnemyData CustomEditor — Basic / Contact + Contact-Special(Rush/Jump 전용 그룹) 또는 (Ranged-Timing + Ranged-Movement + Ranged-Projectile) / Separation-Collision / Reward-Misc / Unhandled 섹션 분기 + 미사용 필드 자동 분리
└── TeleportDestinationIdDrawer.cs  # `[TeleportDestinationId]` 문자열 필드를 TeleportDestinationDatabase 의 id 드롭다운으로 렌더링
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

> 방별 적 스폰은 별도의 결정론 경로(`DeterministicSeedUtility.CreateSeed(globalSeed, currentStageRegion, floor, RoomInfo.StableRoomKey, "enemy_spawn")`)를 사용합니다. `DungeonManager.currentStageRegion`(`SpawnRegion` 비트 플래그) 으로 같은 시드라도 지역별 스폰 RNG 를 분리할 수 있습니다. 자세한 내용은 [9-1-2. 결정론적 방 스폰 시드](#9-1-2-결정론적-방-스폰-시드-deterministicseedutility) 참조.

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
| 5 | DOOR_CLOSED | 닫힌 문 (Elite Door 포함 — 그리드상 동일 값, 시각만 `eliteDoorTile` 로 분리) |

### 4-6. Elite Floor / Elite Room / Elite Door

```
IsEliteFloor(floor) = floor > 0 && floor % 10 == 5     // 5, 15, 25, 35, ...

ConnectAll() 종료 후:
  AssignEliteRoom(rooms, mstEdges, layoutInfo):
    BuildMstDepths(rooms[0] 시작 BFS) → 각 방의 MST 깊이 계산
    degree[i] == 1 (leaf) 중 (depth 최대, 동률 시 시작 방과의 거리 제곱 최대) 선택
    leaf 가 없으면 단순 distance 최대 fallback (warning 발행)
    선택된 방의 RoomInfo.IsElite = true
  ConnectExtraCorridors(elite=eliteRoomIndex):
    EXTRA 통로 후보에서 elite room 을 src/dst 로 사용하지 않음
      → 단일 mandatory 통로만으로 elite room 접근 보장

DungeonTilemapRenderer.PlaceEliteDoors(data):
  data.TryGetEliteRoom(out room) → room.perimeter 의 corridor-인접 cell 마다
  data.SetTileValue(DOOR_CLOSED) + eliteDoorTile 배치 + _eliteDoorPositions 등록
  (일반 문과 다르게 RoomSpawner 의 close/open 사이클에서 제외)
```

플레이어가 Elite Key 를 보유한 채 Elite Door 셀과 콜라이더가 겹치면 `PlayerController.TryOpenEliteDoorOnContact` 가 PlayerInventory 에서 `elite_key` ItemData 를 조회해 `DungeonTilemapRenderer.TryOpenEliteDoorWithKey(PlayerInventory, ItemData)` 를 호출 — 한 셀만 corridor 로 카빙하고 인벤토리에서 키 1개를 제거합니다. 키가 없으면 일반 EMPTY 벽처럼 막힙니다.

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

> **참고**: 문 개폐는 `RoomSpawner` → `DungeonManager.CloseCurrentRoomDoors / OpenCurrentRoomDoors`로 직접 호출됩니다 (`DoorController` 위임 클래스는 제거되었습니다). DungeonManager는 실제 문 상태 전환이 발생한 직후 `OnRoomDoorsClosed` / `OnRoomDoorsOpened`를 발행해, `closedDoorsBlockVision`을 사용하는 FogOfWarController가 즉시 시야를 재계산할 수 있도록 합니다.

### CombatEventChannel

| 이벤트 | 발행자 | 구독자 |
|--------|--------|--------|
| `OnEnemyKilled(EnemyController)` | EnemyController | RoomSpawner (방 클리어 판정) |
| `OnPlayerHpChanged(cur, max)` | PlayerCombatController | PlayerStatusBarUI |
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

`EnemyData.blocksMovement = true` 로 설정된 적은 플레이어의 **일반 이동**을 물리적으로 막습니다. 적 AI 자체의 이동·넉백에는 영향이 없으며, **대시는 적을 통과**하므로 이 쿼리를 쓰지 않습니다(대시는 벽/지형 walkable 만 검사).

```
MovementBlockerQuery.IsPlayerMovementBlocked(worldPos, radius):
  Physics2D.OverlapCircle(worldPos, radius, CombatLayers.EnemyFilter, s_BlockerBuffer)
  히트된 collider → Collider2D→EnemyController 정적 캐시(s_EnemyCache)로 해석
  IsAlive && data.blocksMovement → true 반환

사용처:
  PlayerController.CanMoveTo       — 일반 이동 ⊃ Diagonal slide 후보
  (대시 PlayerDashController 는 적 통과를 허용 → 이 쿼리 미사용, 벽/지형만 검사)
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

### 6-4. 입력 키 맵 (PlayerInputKeySettings ScriptableObject)

`controlScheme` 필드로 2가지 프리셋 중 하나를 선택합니다 (0=ClassicKeyboard, 1=ActionMouseAim). 코드 기본값은 Classic 이라 기존 동작은 회귀 없이 보존되며, 에셋 필드 하나로 조작계가 통째로 전환됩니다.

**ClassicKeyboard**

| 키 | 동작 | 설정 필드 |
|----|------|----------|
| ↑↓←→ | 이동 + Facing 방향 갱신 + 8방향 조준 raw 입력 | `up/down/left/right` |
| Z | 계단·상호작용 (`InteractConfirmPressedThisFrame`, `WasStairPressed` alias) | `interactConfirm` |
| I | 인벤토리 열기/닫기 — `InventoryUIController` 가 `InventoryPressedThisFrame` 을 읽어 토글 | `inventory` |
| Space | 기본 공격 (홀드 시 범위 미리보기) — Facing 4방향 기준 | `basicAttack` |
| A | 재장전 (Bullet 폼) | `reload` |
| Q / W / E / R | 스킬 슬롯 1~4 (홀드 시 범위 미리보기) | `skillSlot1~4` |

**ActionMouseAim (현재 활성)**

이동/조준을 분리한 액션 조작계. 왼손 WASD + Q/E, 오른손 마우스. 조준은 커서 방향 **360° 연속**.

| 입력 | 동작 | 설정 필드 |
|----|------|----------|
| WASD | 이동 | `actionMouseUp/Down/Left/Right` |
| 마우스 커서 | 360° 조준 (`HasMouseAim`/`AimWorldPoint`) | — |
| 좌클릭 | 기본 공격 | `actionMouseBasicAttack` |
| 우클릭 | 스킬 1 | `actionMouseSkillSlot1` |
| Q / E | 스킬 2 / 3 | `actionMouseSkillSlot2/3` |
| 4 | 스킬 4 | `actionMouseSkillSlot4` |
| Space | 재장전 (Bullet 폼) | `actionMouseReload` |

- 스킬 슬롯 인덱스(0~3)는 `WeaponData.skills` 순서 고정 — 프리셋은 입력 키만 다르게 매핑합니다.
- 마우스 버튼 바인딩은 `InputBinding`(`Key` + `PointerButton`) 구조. UI 위(`EventSystem.IsPointerOverGameObject`)에서는 좌/우클릭 전투 입력이 차단됩니다.
- 커서 월드 좌표는 `PlayerInputReader.aimCamera`(미지정 시 `Camera.main`) 기준으로 계산합니다.

`PlayerInputReader` 는 `keySettings` 가 비어 있으면 Classic 기본 키로 폴백하고 1회 경고를 출력합니다. `PlayerInputKeySettings.OnValidate` 가 `Key.None`·동일 키 중복·마우스 버튼 중복을 에디터에서 자동 감지합니다.

`PlayerInventory` 가 `elite_key` ItemData 를 보유하면 `PlayerController` 가 매 프레임 `TryOpenEliteDoorOnContact` 를 호출 — 별도 키 입력 없이 접촉만으로 Elite Door 가 열립니다 (`TryGetDatabaseItem("elite_key", out keyItem)` → `dungeonRenderer.TryOpenEliteDoorWithKey(_inventory, keyItem)`). 구 `PlayerEliteKeyInventory` 는 제거되었고 Elite Key 는 일반 ItemData 한 항목으로 통합됨.

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
  ├── IsSkillBusy => _isSkillCasting || _skillRecoveryTimer > 0 || _isParrySequenceActive || _isReloading
  └── BlocksPlayerMovement => 캐스팅·후딜·(옵션)패리 중 이동 잠금 (재장전은 이동 허용) ← PlayerController/PlayerAnimationController 가 구독

흐름:
  TryUseSkill(slot):
    castDelay > 0 이면 BeginSkillCast → SkillCastRoutine 로 castDelay 후 ExecuteSkillIfReady
    castDelay == 0 이면 즉시 ExecuteSkillIfReady
  ExecuteSkillIfReady:
    성공 시 Spend(자원, 실제 소모량) / slot.StartCooldown / StartSkillRecovery(recoveryDelay)
    실패 가드: IsDead / IsDashing / DungeonManager.IsTransitioning / 슬롯 데이터 불일치
  TickSkillRecovery(dt) — Update에서 매 프레임 감소

게이트(IsSkillBusy 검사):
  Update 기본공격 입력 / TryBasicAttack / TryUseSkill / CanUseSkillSlot
  PlayerController.Update — BlocksPlayerMovement 시 입력 처리 skip
  PlayerAnimationController — BlocksPlayerMovement 시 MoveX/Y 0으로 강제
```

> 사망 / 대시 시작 / 풀링 비활성화 등에서는 `ClearSkillTimingState()`가 진행 중 코루틴을 중단하고 `_skillRecoveryTimer` 를 0 으로 리셋합니다.

> **조준 방향 결정**: ClassicKeyboard 에서는 기본 공격이 `PlayerController.FacingDirection`(이동 키 우선 → 카디널 4방향)을, 스킬·투사체·대시는 `AimDirectionUtility.TryGetEightWayRaw(MoveInput)` 으로 얻은 8방향 raw 입력을 사용합니다(입력이 비면 `_lastAimDirection`, 기본값 down 폴백). **ActionMouseAim 에서는 커서 방향을 양자화 없이 연속 벡터(`_aimDirectionContinuous`)로 사용해 기본공격 포함 모든 스킬이 360° 자유조준** — grid 패턴은 월드→그리드 Y 반전(`(x,-y)`) 후 `AttackPattern` 의 연속 `Vector2 facing` 오버로드로 해석합니다. 미리보기(`SkillRangePreviewer`)도 실행과 동일 소스(`CurrentAimDirection`)·동일 Y 반전·동일 패턴 함수를 사용해 발사 결과와 시각이 일치합니다.
>
> 참고: 연속 조준 도입으로 Classic 에서도 **대각 이동 중 grid 패턴(Cone/Line/Single·근접 기본공격) 방향이 X우선 카디널(→) → 실제 대각(↗)** 으로 따라가도록 통일되었습니다(투사체는 원래 대각). 상하좌우 직선 이동은 종전과 동일하며, 이는 의도된 동작입니다.

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
  ├── 탄창(마탄 폼): usesMagazine, magazineSize, reloadTime, reloadAmount  ← EquipWeapon 시 주입+풀충전
  └── skills[4] (SkillData[])   ← 폼 loadout 단일 소스 (PlayerFormData.skills 폐지)

SkillData (ScriptableObject)
  ├── executionType (SkillExecutionType)  ← InstantArea/Projectile/Dash/AreaOverTime/Buff
  ├── 공통: damage, cooldown, castDelay, recoveryDelay
  ├── 자원: resourceType(None/Bullet/ParryStack), requiredAmount, consumeAmount,
  │        bulletShortageMode(RequireFullCost/AllowPartialUse), reloadAmount  ← MP 폐지, 자원 기반
  ├── 공통: isMultiTarget, canPenetrateWalls
  ├── 공통: attackPattern, patternRange, coneHalfAngle
  ├── 공통: knockback/slow 파라미터
  ├── Animation: animationType(None/Attack/Spin/Dash/CustomTrigger),
  │              customAnimationTrigger, rotateAnimationByDirection,
  │              animationBaseAngle  ← PlayerFormController.PlaySkillAnimation 가 사용
  ├── Projectile: prefab, speed, lifetime, count, spreadAngle,
  │              firePattern, wallHitMode, targetHitMode,
  │              maxBounceCount, spawnOffset, burstInterval, burstSpacing
  └── Dash: distance, duration, stopOnWall,
           damageOnPath, damageOnContact, invincibleDuringDash

(Inspector는 SkillDataEditor가 executionType 별로 InstantArea/Projectile/Dash 섹션만 노출,
 AreaOverTime/Buff는 Reserved 안내, 미사용 필드는 Reserved foldout으로 접어둠)

PlayerResource (Domain)
  └── currentHp, maxHp   (MP 폐지 — 스킬 자원은 PlayerCombatController 의 ISkillResourceLedger 가 관리)

PlayerCombatController
  ├── PlayerResource (HP 상태) · ISkillResourceLedger 구현 (Bullet 탄창 / ParryStack 자원 원장)
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
  └── CanUse(ISkillResourceLedger) — 쿨다운 + 자원(resourceType별 required) 확인

SkillCooldownController
  └── 기본 공격 쿨다운만 담당 (스킬 쿨다운은 SkillSlotRuntime이 보유)
```

### 7-2. 기본 공격 흐름

```
TryBasicAttack():
  ① 가드(IsDead/Dashing/Stunned/SkillBusy) + IsAttackReady & currentWeapon 확인
  ② 현재 폼의 BasicAttackMode 로 분기:
     • Parry  → 데미지 없는 패리 시퀀스(선딜→무적→후딜). 무적 중 피해 1회 가로채기 → +ParryStack,
                흰색 점멸. 선딜 중 피격 시 패리 취소. (BeginParryBasicAttack)
     • Bullet → 탄 1발 확인 → 없으면 자동 재장전. 있으면 투사체 1발 발사 + 탄 1 소모
                (basicAttackSkillData, executionType=Projectile). 발사로 탄 0 시 자동 재장전.
     • Damage → 기존 근접 패턴 공격:
                SetAttackCooldown → SkillTargetResolver → AttackExecutor.ExecuteAttackWorld(...)
                + basicAttackSkillData 로 PlaySkillAnimation
```

> `basicAttackSkillData`(SkillData)는 폼에 따라 쓰임이 다릅니다: Damage/Parry 폼은 애니메이션 라우팅용, Bullet(Freischutz) 폼은 실제 발사 투사체 정의(executionType=Projectile)로 사용. 런타임 폼 전환 시 `WeaponData.basicAttackSkillData` + `PlayerCombatController.ActiveBasicAttack`(무기 우선, 비면 SerializeField fallback)로 폼별 자동 교체됨(§15 런타임 폼 전환).

### 7-3. 스킬 실행 흐름 — SkillExecutor 라우팅

```
TryUseSkill(slotIndex):
  ① IsDead / IsDashing / IsSkillBusy 가드
  ② 슬롯·쿨다운·자원 확인 (SkillSlotRuntime.CanUse(ledger))
  ③ castDelay > 0 → BeginSkillCast → SkillCastRoutine(_isSkillCasting=true)
                    castDelay 만료 후 ExecuteSkillIfReady 호출
     castDelay == 0 → 즉시 ExecuteSkillIfReady

ExecuteSkillIfReady(slotIndex, expectedSkill):
  ① IsDead / IsDashing / DungeonManager.IsTransitioning 가드
  ② slot.Data == expectedSkill / CanUse 재검증 (코루틴 중 슬롯 변경 대응)
  ③ SkillExecutionContext 생성
       (caster, transform, skill, slotIndex, aim, gridFacing,
        TotalAttack, hitRadius)
  ④ SkillExecutor.Execute(context) → SkillExecutionResult{Success, ResourceConsumed}
       switch (skill.executionType):
         InstantArea  → ExecuteInstantArea()  (소모 = consumeAmount)
         Projectile   → ExecuteProjectile()   (Bullet AllowPartialUse 면 실제 발사 수 = 소모)
         Dash         → ExecuteDash()
         Blink        → ExecuteBlink()  (가장 가까운 적 뒤로 순간이동, Dagger Q)
         Buff         → ExecuteBuff()   (현재 Dagger R 마커 버프 전용 — 범용 버프 미구현)
         AreaOverTime → 미구현 (경고 로그 1회)
  ⑤ 성공 시 Spend(resourceType, result.ResourceConsumed) / ApplySkillReload /
            TryStartAutoReloadIfEmpty / slot.StartCooldown / StartSkillRecovery / RaiseSkillUsed
     실패 시 자원 소모·쿨다운 둘 다 없음

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

플레이어 스킬과 적 원거리 공격 모두 `ProjectileFireService` → `ProjectilePool` → `ProjectileController` 동일 경로를 사용합니다.

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
       0.05~타일×0.25 간격으로 IsFootprintWalkable(4코너, 벽/지형만·적 블로커 무시) 검사
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
  OnEnemyHit      — 히트된 적마다 콜백 (데미지 적용 직전 호출). Dagger E 가 마커 폭발 + E 쿨 1회 리셋에 사용
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

### 8-4-4. Elite Pattern Set (Elite 전용 패턴 사이클)

`EnemyData.isElite = true` 이고 `elitePatternSet` 이 부착된 적은 일반 행동(Contact 접촉 피해 / Ranged 사이클 / Contact Special) 와 별도로 `ElitePatternRunner` (MonoBehaviour) 가 추가 사이클을 실행합니다.

```
ElitePatternRunner.Tick(dt):
  ① _brain.Data.IsElite && elitePatternSet != null && Enemy.IsAlive 확인
     (false 면 진행 중 패턴 Cancel 후 종료)
  ② 활성화 시점에 RebuildPatterns — Weight > 0 인 패턴만 _patterns 리스트에 등록
  ③ TickCooldowns(dt) — 각 패턴 _cooldowns Dictionary 감소
  ④ 현재 _currentRuntime 진행 중이면 Tick 위임,
     IsFinished 시 FinishCurrent → 그 패턴의 Cooldown 적용
  ⑤ 진행 중인 패턴이 없을 때 TryStartNextPattern:
       distance = √(Target.SqrDistanceToTarget)
       _patterns 순회: cooldown == 0 && IsInRange(distance) 인 첫 패턴 선정
       pattern.CreateRuntime() → context.Initialize → runtime.Start(context)
```

`ElitePatternContext` 가 Brain/Enemy/Data/Movement/Action/Animation/Collider/DungeonManager/ProjectileFireService/CoroutineRunner 를 모두 노출해 런타임이 필요한 서비스에 접근할 수 있습니다.

| 패턴 (ScriptableObject) | 핵심 파라미터 | 동작 |
|---|---|---|
| `EliteProjectilePatternData` | windupDuration / firePattern (Single/Burst/Spread/Circle) / projectileCount / spreadAngle / burstInterval / wallHitMode / maxBounceCount / impact (EnemyAttackImpactData) | windup (windupAnimation) → ProjectileFireService.Fire → recovery |
| `EliteDashPatternData` | windup / dashSpeed / damage / hitRadius / stopOnWall / lockFacingDuringDash / windupAnimation / dashAnimation | windup → **목표 위치(플레이어 위치) 기반** WalkabilityQuery 로 보정 → dashSpeed×dt 이동(목표 도달 시 종료) → 타겟 1회 데미지 → recovery. ~~dashDuration 제거~~ |
| `EliteJumpPatternData` | windup / jumpDuration / maxDistance / impactDamage / impactRadius / jumpVisualHeight / stayInRoom / lockFacingDuringJump | windup → `WalkabilityQuery.TryFindNearestWalkable` 로 착지점 결정 → 비행 보간 → 착지 임팩트(impactRadius OverlapCircle) → recovery |

공통 ElitePatternData 필드: `displayName` / `cooldown` / `minRange` / `maxRange` / `weight` / `recoveryDuration` + `OnValidate` 가 음수·역전 범위·weight 0 을 자동 경고.

> Contact Special (Rush/Jump) 과 차이점: Special 은 모든 Contact 적이 0~1개 고정 공격(`specialAttackType`)을 갖고 ActionHandler 내부 상태머신으로 처리. Elite Pattern 은 Elite 적만 다수 패턴을 ScriptableObject 풀로 갖고 ElitePatternRunner 가 외부 컴포넌트로 처리.

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
       roomSeed = FNV-1a(globalSeed, currentStageRegion, floor, room.StableRoomKey, "enemy_spawn")
       roomRng  = new System.Random(roomSeed)
  ⑤ EnemyPoolManager에서 예산 기반 적 선택 (roomRng.Next 사용)
       (방 면적 × densityFactor × 방 타입 배율)
       (SpawnRegion 비트 필터링 + EnemyData.IsAvailableOnFloor(currentFloor) 필터,
        _poolEnemyTable 은 enemyName 기준 정렬 후 선택)
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

DeterministicSeedUtility.CreateSeed(globalSeed, currentStageRegion, floor, stableRoomKey, domain):
  FNV-1a 해시(long globalSeed, int region, int floor, int stableRoomKey, string domain)
  → 양수 int 시드 반환

도메인 상수:
  EnemySpawnDomain = "enemy_spawn"
    → RoomSpawner.SpawnEnemiesInRoom 에서 사용
  EliteKeyDomain   = "elite_key"
    → RoomSpawner.PrepareEliteKeyPlan 에서 키 드랍 슬롯 선정 시 사용
    → 다른 결정론 시스템을 추가할 땐 새 도메인 문자열을 정의해 시드 충돌 방지
```

```
SpawnRegion (Flags enum, Generate/SpawnRegion.cs):
  None / Dungeon / Forest / Castle 등 비트 플래그
  DungeonManager.currentStageRegion 이 시드 입력에 포함 — 지역별 RNG 분기 + EnemyData.allowedRegions 필터 공용

RoomInfo.StableRoomKey:
  DungeonManager.BuildRoomInfos 가 생성 시 CreateStableRoomKey 로 채움
  Spawn/Stair 자동 분류 후에도 보존
  RoomSpawner._startedRoomKeys / _pendingRoomStart 도 이 키로 식별
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

`CombatEventChannel` 이벤트를 구독해 HP를 실시간으로 표시합니다 (MP 바는 폐지, 폼 고유 자원은 별도 UI).

```
PlayerStatusBarUI:
  ├── HP 슬라이더 (Slider) — 수치 비율에 따라 갱신
  └── HP 텍스트 (cur / max 형식)

구독 이벤트:
  OnPlayerHpChanged(cur, max) → HP 슬라이더 + 텍스트 갱신
  (MP 바·OnPlayerMpChanged 는 폐지됨)

폼 고유 자원 UI (구 MP 영역 재사용, 현재 폼 BasicAttackMode 기준 표시):
  Parry  → ParryStackBarUI (ParryStack 슬라이더)
  Bullet → FreischutzMagazineUI (탄창 칸 Bullet/Bullet_empty + x/max·Reloading)
  Damage → 자원 UI 숨김
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

스킬 키 홀드 시 스킬 범위, 기본 공격 키 홀드 시 기본 공격 범위를 LineRenderer로 시각화합니다(슬롯별 홀드 감지는 바인딩과 무관한 `IsSkillHeld(slot)`/`IsBasicAttackHeld` 사용 — 두 프리셋 공용).

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
  슬롯 변경 시 즉시 / 조준 방향 변경 시 (Classic=FacingDirection / ActionMouseAim=커서 연속 방향) — Line/Cone/Single/Projectile/Dash
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

## 11a. 시스템 9 — 마을·던전 전환 및 미니맵

### 11a-1. 마을·던전 전환 (LocationTransitionManager)

마을(Town)과 던전(Dungeon) 사이의 전환을 조율합니다. TeleportDestinationDatabase(ScriptableObject)에 등록된 목적지 ID로 이동 대상을 결정합니다.

```
LocationTransitionManager.TeleportPlayer(player, destinationId):
  ① TeleportDestinationDatabase에서 TeleportLocationData 조회
  ② CurrentLocation → destination.LocationType 방향 판별
       enteringDungeon: 현재 위치가 Dungeon이 아닌데 목적지가 Dungeon
       leavingDungeon : 현재 위치가 Dungeon인데 목적지가 Dungeon이 아님
  ③ leavingDungeon → CleanupDungeonRuntime():
       player.Inventory.RemoveItemsOnDungeonExit()  ← ItemData.RemoveOnDungeonExit=true 항목 제거 (elite_key 포함)
       ProjectilePool.ReleaseAllActiveProjectiles(Manual)
       EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange()
       DropItemSpawner.Instance?.ClearAllActiveDrops()
       roomSpawner.ClearRuntimeEncounterState()
  ④ ApplyLocationRoots(to):
       townRoot  active = !isDungeon
       dungeonRoot active = isDungeon
       minimapRoot active = true  ← 항상 표시, 소스만 전환
  ⑤ CurrentLocation = to
  ⑥ enteringDungeon:
       minimap.SetDungeonSource()
       StartNewDungeonRun() — dungeonManager.Generate() + fogOfWar + roomSpawner.Reset + player.SpawnAtStart
                              (Elite Key 등의 잔존 항목은 ③ 의 RemoveOnDungeonExit 단계에서 이미 정리됨)
     else:
       minimap.SetTilemapSource(destination.MinimapLocationId)
       TryMovePlayerToDestination():
         LocationRootRegistry.TryGet(destination.LocationRootId) → root
         worldPos = root.transform.TransformPoint(destination.LocalSpawnPosition)
         player.TeleportTo(worldPos)
```

`debugDungeonEntranceDestinationId` / `debugReturnDestinationId` 는 개발자 콘솔 `/tp` 명령의 기본 목적지 ID 후보입니다 (구 `EnterDungeon()` / `EnterTown()` 디버그 메서드는 제거되어 콘솔 `/tp` 로만 호출).

**목적지 데이터 (TeleportLocationData)**

| 필드 | 설명 |
|------|------|
| `id` | 고유 문자열 키 (예: `town_start`, `dungeon_entrance`, `town_return`) |
| `displayName` / `description` | UI 표시·툴팁용 메타데이터 |
| `locationType` | `GameLocationType.Town` / `Dungeon` |
| `locationRootId` | `LocationRootRegistry` 에 등록된 LocationRoot 의 id (필수) |
| `localSpawnPosition` | LocationRoot 기준 로컬 좌표 — `root.TransformPoint(localSpawnPosition)` 으로 월드화 |
| `minimapLocationId` | 미니맵 소스 ID — 비어 있으면 `id` 폴백 |

`[TeleportDestinationId]` PropertyAttribute + `TeleportDestinationIdDrawer` 가 인스펙터의 destination id 문자열 필드를 DB 의 id 드롭다운으로 렌더링합니다 (LocationTransitionManager / TeleportService 가 사용).

**LocationRoot (씬 배치 컴포넌트)**

씬에 배치하는 위치 루트 MonoBehaviour. `locationRootId` 가 SerializeField 로 노출되고 `OnEnable`/`OnDisable` 에서 `LocationRootRegistry`(static Dict) 에 자동 등록/해제됩니다. 텔레포트 시 destination 이 가리키는 root 트랜스폼의 `TransformPoint(localSpawnPosition)` 으로 최종 월드 좌표가 결정됩니다 — 기존 `TeleportDestinationPoint` / `TeleportDestinationRegistry` 는 제거되었습니다. 같은 root 안에 여러 destination 을 두려면 localSpawnPosition 만 바꿔 재사용 가능합니다.

### 11a-2. 이중 모드 미니맵 (MinimapController)

```
MinimapMode:
  Dungeon  — DungeonData(int[,] 그리드) 기반 픽셀 텍스처
  Tilemap  — TilemapMinimapSource(ground/wall Tilemap) 기반 픽셀 텍스처

SetDungeonSource():
  _mode = Dungeon, StartInitialInitializeRoutine (최대 60프레임 폴링)
  DungeonData + FogOfWar 갱신 구독 복원

SetTilemapSource(locationId):
  minimapImage.texture = null  ← 이전 텍스처 즉시 클리어 (스테일 방지)
  _mode = Tilemap
  LocationMinimapRegistry.TryGet(locationId) → 즉시 초기화
  실패 시 pendingTilemapLocationId 저장 + 폴링 루틴 시작 (레지스트리 등록 대기)
```

**좌표계 차이**

| 모드 | Y축 | 처리 |
|------|-----|------|
| Dungeon | DungeonData row 0 = 맵 상단 (Y↓) | 픽셀 배열 Y 뒤집기 적용 |
| Tilemap | Unity Tilemap Y↑ = Texture2D Y↑ | 뒤집기 없음 |

플레이어 마커도 같은 규칙으로 좌표 계산이 분기됩니다 (`UpdateDungeonPlayerMarker` / `UpdateTilemapPlayerMarker`).

**TilemapMinimapSource 레지스트리 패턴**

`TilemapMinimapSource` MonoBehaviour를 TownRoot(또는 위치 루트)에 부착하면, `OnEnable` 시 `LocationMinimapRegistry`(static Dictionary)에 `locationId`로 자동 등록됩니다. `MinimapController.SetTilemapSource`는 이 레지스트리를 조회해 즉시 또는 폴링으로 소스를 획득합니다.

**자동 분류 모드 (autoDiscoverChildren)**

`autoDiscoverChildren = true` 인 경우 `OnEnable` 시 1회 자식 Tilemap 을 `GetComponentsInChildren<Tilemap>` 로 수집한 뒤 각 GameObject 의 Layer(`Walkable` / `Wall` / `Door` — 인스펙터에서 이름 커스터마이즈 가능)에 따라 `_walkableTilemaps` / `_wallTilemaps` / `_doorTilemaps` 세 List 로 분류합니다. 명시 모드(groundTilemap/wallTilemap/doorTilemap 직접 연결)와 병행 가능하며, 한 location 에 walk/wall 각각 여러 개의 Tilemap 이 있어도 모두 처리됩니다. 분류 실패 시(0개 매칭) 인스펙터에 1회 경고를 출력합니다.

### 11a-3. 미니맵 마커·계단 가시성

`MinimapController` 는 던전 모드에서 다음 시각 보강을 적용합니다.

- **계단 마커 확장**: `stairMarkerPixelPadding` 픽셀만큼 `STAIR_UP` 셀의 박스를 키워 `stairColor` 로 채움 → 작은 미니맵에서도 계단을 찾기 쉬움 (탐사된 셀만)
- **문 색상 분리**: `visibleDoorColor` / `exploredDoorColor` 가 `DOOR_CLOSED` 에 적용되어 방·통로와 문이 시각적으로 구분
- **플레이어 마커 가시성 게이팅**: `UpdateDungeonPlayerMarker` 가 `fogOfWar.IsExploredCell(grid)` 가 false 면 marker 를 SetActive(false) → 텔레포트 직후/맵 밖 위치에서 마커가 노출되지 않음
- **플레이어 마커 색상**: `playerMarkerGraphic` (선택) 에 `playerColor` 를 1회 주입
- **마커 크기 스냅**: `SnapPlayerMarkerSize` 가 Canvas scaleFactor 에 맞춰 sizeDelta 를 정수 픽셀로 라운드 → 안티앨리어싱 흐림 방지

---

## 11b. 시스템 10 — 아이템 / 드랍 / Elite Key / Soul

### 11b-1. 아이템 데이터 (ItemData / ItemDatabase)

`ItemData` 는 `[Serializable]` 단일 아이템 정의, `ItemDatabase` ScriptableObject 가 `List<ItemData>` 를 보유하고 `OnEnable` / `OnValidate` 에서 `itemCode → ItemData` Dictionary 캐시를 재구축합니다.

| 필드 | 설명 |
|------|------|
| `itemCode` | 고유 키 (예: `elite_key`). DropItemSpawner / DroppedItem 모두 이 문자열로 조회 |
| `displayName` / `description` / `icon` | UI 메타데이터 |
| `itemType` | `ItemType` enum — Key / Currency / Consumable / Equipment / Relic / Material / Soul |
| `stackable` / `maxStack` | `PlayerInventory.AddItem` 이 자동 적용 (스택 불가 항목은 amount 만큼 슬롯 분리) |
| `useEffects` | Consumable 사용 시 1회 적용되는 `ItemEffect[]` — 현재 `HealHp` 지원 |
| `passiveEffects` | Relic 소지 중 상시 적용되는 `ItemEffect[]` — MaxHp / Attack / Defense / MoveSpeed 평면 스탯 지원 |
| `soulFormId` | `ItemType.Soul` 일 때 해금되는 `PlayerFormId`. `PlayerInventory.OwnsSoulForm(formId)` 가 이 값을 사용 |
| `removeOnFloorTransition` | true 면 `PlayerInventory.RemoveItemsOnFloorTransition()` 가 층 이동 시 자동 제거 (Elite Key 가 이 플래그를 사용) |
| `removeOnDungeonExit` | true 면 `RemoveItemsOnDungeonExit()` 가 던전 → 마을 전환 시 자동 제거 |

`OnValidate` 가 `itemCode` 공백/중복을 자동 경고. 런타임 조회는 `ItemDatabase.TryGetItem(code, out item)` 으로 0-할당 캐시 lookup. 개발자 콘솔 자동완성은 `/give <category>` 에서 `DeveloperConsoleItemCategoryResolver` 가 ItemType category 를 해석한 뒤 `ItemDatabase.GetItemCodes(output)` → `PlayerInventory.GetDatabaseItemCodes(output)` → category 필터링 경로로 같은 캐시를 사용합니다.

| `ItemEffectType` | 적용 위치 | 설명 |
|------------------|-----------|------|
| `HealHp` | `useEffects` | `ItemEffectApplier.ApplyUseEffects` 가 `PlayerCombatController.RestoreHp(value)` 호출 |
| `MaxHpBonus` | `passiveEffects` | Relic 소지 수만큼 최대 HP 가산 |
| `AttackBonus` | `passiveEffects` | Relic 소지 수만큼 `TotalAttack` 가산 |
| `DefenseBonus` | `passiveEffects` | Relic 소지 수만큼 `TotalDefense` 가산 |
| `MoveSpeedBonus` | `passiveEffects` | Relic 소지 수만큼 이동속도 % 가산 |

현재 `ItemDatabase.asset` 에는 `elite_key`, 검증용 `Test_Potion` / `Test_Relic`, Form 해금용 Soul 4종(`Soul_Sword`, `Soul_Dagger`, `Soul_Freichutz`, `Soul_Parry`)이 등록되어 있습니다.

### 11b-2. 드랍 파이프라인

```
EnemyController.Initialize(data):
  _inventory.Clear()                ← 풀 재사용마다 비움

(이벤트로 또는 RoomSpawner 가)
EnemyController.MarkAsEliteKeyHolder():
  _holdsEliteKey = true
  _inventory.AddDropItem("elite_key")  ← 사망 시 자동 드랍

EnemyController.Die() → FinishDeath 흐름:
  DropItemSpawner.Instance.SpawnDrops(_inventory, transform.position)
  → 드랍 목록을 itemDatabase 로 ItemData 해석
  → DroppedItem prefab 을 Instantiate, Initialize(item, amount) 호출
    (sprite, sortingLayer/Order, pickupCollider.isTrigger=true)
  → 다중 아이템은 dropSpacing 간격으로 X 축 정렬
  → _activeDrops 에 등록 (층 이동/마을 이동 시 ClearAllActiveDrops 로 일괄 회수)

DroppedItem.OnTriggerEnter2D(player):
  player.TryGetComponent<PlayerInventory>(out inventory) →
    inventory.AddItem(_itemData, _amount) 성공 시:
      DropItemSpawner.Instance?.Unregister(self) + Destroy(self)
  (Currency/Consumable/Equipment/Soul 등 모든 ItemType 이 인벤토리에 들어감.
   Consumable 사용과 Relic 평면 패시브, Soul 기반 Form 보유 판정은 구현됨,
   Equipment 장착 로직은 별도 시스템으로 보류)
```

### 11b-3. Elite Key 결정론적 드랍 슬롯 (RoomSpawner.PrepareEliteKeyPlan)

`DungeonManager.RunGenerationPipeline` 끝에서 호출됩니다. Elite floor (`HasEliteRoom`) 일 때만 활성.

```
candidates = []                                  ← (roomKey, spawnIndexInRoom) 리스트
for each room in dungeonManager.Data:
  if room.IsElite                  → skip
  if room.Type not in (Normal, MonsterDen) → skip
  spawnCount = CountDeterministicSpawns(room, dungeonManager)
    ← SpawnRoom 과 동일한 결정론적 RNG·예산 시뮬레이션을 dry-run 하여
       해당 방에서 실제 스폰될 적의 수를 미리 계산
  for spawnIndex in [0..spawnCount): candidates.add((roomKey, spawnIndex))

seed = DeterministicSeedUtility.CreateSeed(
         globalSeed, currentStageRegion, floor,
         eliteRoom.StableRoomKey,    ← elite room 의 stable key 를 소금으로
         EliteKeyDomain)             ← "elite_key" 도메인
selected = rng.Next(candidates.Count)
_eliteKeyPlan = { Active=true, RoomKey=selected.roomKey, SpawnIndexInRoom=selected.spawnIndexInRoom }

방 진입 시 SpawnRoom:
  spawnedIndex 가 ShouldAssignEliteKey(room, spawnedIndex) 와 일치하면
  enemy.MarkAsEliteKeyHolder() 호출 → 그 적이 죽으면 elite_key 드랍
```

같은 seed/floor 에서는 항상 같은 방의 같은 인덱스 적이 키를 보유합니다. 후보가 0이면 1회 경고 후 키 드랍을 생략 (elite 층에서 일반 방 적이 전혀 없는 비정상 경우).

### 11b-4. Soul 아이템 기반 Form 보유 판정

`ItemType.Soul` 은 사용 효과/패시브가 아니라 **Form 해금 토큰**으로 동작합니다. `ItemData.soulFormId` 에 해금 대상 `PlayerFormId` 를 지정하고, `PlayerInventory.OwnsSoulForm(formId)` 가 현재 보유 스택을 순회해 `ItemType.Soul && SoulFormId == formId && Count > 0` 인 항목이 있는지 검사합니다.

```
/give soul Soul_Dagger
  → DeveloperConsoleService.ExecuteGive
       category "soul" → ItemType.Soul 검증
       itemCode 실제 ItemType 불일치 시 에러
  → DeveloperConsoleCommandExecutor.ExecuteItemGive
  → PlayerInventory.AddItem(Soul_Dagger, 1)

/form set Dagger
  → PlayerFormController.TrySwitchForm(Dagger)
       formDatabase.TryGet(Dagger) 실패 → UnknownForm
       IsFormOwned(Dagger) false     → FormSwitchResult.NotOwned
       CanSwitchNow() false          → Busy
       이미 현재 폼                    → AlreadyActive
       통과                           → ApplyForm(DaggerForm) + EquipWeapon(defaultWeapon)
```

`PlayerFormId.Normal` 은 항상 보유로 처리합니다. `PlayerFormController.inventory` 가 미결선된 경우에는 기존 전환 테스트 흐름을 깨지 않기 위해 소유 게이팅을 통과시키는 안전 폴백을 둡니다. 콘솔 `/form set <id>` 도 동일한 `TrySwitchForm` 을 사용하므로 Soul 미보유 폼은 `Form locked: soul not owned for <id>` 로 거부됩니다.

### 11b-5. 플레이어 인벤토리 (PlayerInventory)

`PlayerInventory` MonoBehaviour 가 모든 보유 아이템을 `InventoryItemStack`(ItemData + count) 리스트로 관리합니다 — 과거의 `PlayerEliteKeyInventory`(bool + EliteKeyChanged 이벤트) 는 제거되었고, Elite Key 도 `itemCode = "elite_key"` 인 일반 ItemData 한 항목으로 통합되었습니다.

| API | 설명 |
|-----|------|
| `AddItem(item, amount)` | stackable 면 기존 스택에 합치고 초과분은 새 스택 생성, 아니면 amount 만큼 슬롯 분리. 성공 시 `OnInventoryChanged` 발행 |
| `RemoveItem(item, amount)` / `RemoveAll(item)` / `RemoveAllByCode(code)` | 보유량 검사 후 제거 |
| `HasItem(item, amount)` / `GetItemCount(item)` | 보유량 조회 |
| `TryGetDatabaseItem(itemCode, out item)` | 부착된 `ItemDatabase` 에서 ItemData 조회 (PlayerController 의 Elite Door 처리가 사용) |
| `GetDatabaseItemCodes(output)` | 부착된 `ItemDatabase` 의 itemCode 목록을 output List 에 추가 (개발자 콘솔 `/give` category별 자동완성의 원천) |
| `OwnsSoulForm(formId)` | 보유 스택 중 `ItemType.Soul` + `SoulFormId == formId` + `Count > 0` 조합이 있으면 true |
| `RemoveItemsOnFloorTransition()` | `ItemData.RemoveOnFloorTransition=true` 항목만 제거 — `DungeonManager.CleanupPlayerInventoryForFloorTransition` 에서 호출 |
| `RemoveItemsOnDungeonExit()` | `ItemData.RemoveOnDungeonExit=true` 항목만 제거 — `LocationTransitionManager` 던전 이탈 시 호출 |
| `Clear()` | 전체 비움 |
| `OnInventoryChanged` (event) | `InventoryUIController` 가 구독해 슬롯 갱신, `PlayerCombatController` 가 Relic 패시브 재계산, `PlayerStatusBarUI` 가 elite_key 보유 수로 키 아이콘 토글 |

`PlayerController` 가 `RequireComponent<PlayerInventory>` 로 부착을 보장하며, Elite Door 접촉 시 `_inventory.TryGetDatabaseItem("elite_key", out keyItem)` → `dungeonRenderer.TryOpenEliteDoorWithKey(_inventory, keyItem)` 가 한 셀 카빙 + 인벤토리에서 키 1개 제거합니다.

### 11b-6. 아이템 효과 적용 (Consumable / Relic)

아이템 효과는 `ItemEffectType` 기반의 평면 스탯·즉시 효과만 담당합니다. 행동형 유물 효과(처치 시 회복, 대시 불길 등)는 이 enum 에 넣지 않고 별도 런타임 축으로 확장하는 전제입니다.

```text
Consumable 클릭:
  InventorySlotUI.OnPointerClick
  → InventoryUIController.HandleSlotClicked(item)
  → item.ItemType == Consumable && useEffects 존재 && PlayerCombatController.Active 생존 확인
  → ItemEffectApplier.ApplyUseEffects(item, combat)
      HealHp → combat.RestoreHp(value)
  → 적용 성공 시 PlayerInventory.RemoveItem(item, 1)
  → OnInventoryChanged 로 UI 자동 갱신
```

Relic 패시브는 `PlayerCombatController` 가 `PlayerInventory.OnInventoryChanged` 를 구독해 `PlayerItemStats.Recalculate(inventory.Items)` 를 호출합니다.

| 집계 대상 | 규칙 |
|----------|------|
| 대상 ItemType | `ItemType.Relic` 만 집계. Equipment 는 아직 보류 |
| 스택 처리 | `passiveEffects` 의 value × stack.Count |
| 적용 스탯 | `MaxHpBonus`, `AttackBonus`, `DefenseBonus`, `MoveSpeedBonusPercent` |
| MaxHp 변화 | `delta = 새 MaxHpBonus - 이전 MaxHpBonus`; 생존 중이면 현재 HP 도 delta 만큼 증감 후 `[1, 새 MaxHp]` 클램프 |
| 사망 상태 | 스탯 재계산은 수행, 현재 HP 조정은 생략 |

`TotalAttack` / `TotalDefense` 는 무기 보너스 뒤에 Relic 보너스를 더하고, `MoveSpeedMultiplier` 는 상태이상 배율에 `(1 + MoveSpeedBonusPercent / 100)` 을 곱합니다. `MaxHp` 프로퍼티는 `maxHp + MaxHpBonus` 를 반환하며 `RestoreHp` / 데미지 로그 / HP 이벤트도 유효 최대 HP 를 사용합니다.

### 11b-7. 인벤토리 UI 탭 / 슬롯 클릭

`InventoryUIController` 는 5개 고정 탭을 사용합니다. `tabButtons` 가 5개 유효 결선되지 않았으면 경고 1회 후 기존 전체 표시로 폴백합니다.

| 탭 | 포함 ItemType |
|----|---------------|
| 전체 | 모두. 표시 순서는 소모품 → 유물 → 재료 → 기타 그룹 순서, 그룹 내부는 획득 순서 유지 |
| 소모품 | Consumable |
| 유물 | Relic |
| 재료 | Material |
| 기타 | Key / Currency / Equipment |

필터링은 `_filteredBuffer` 재사용 List 로 처리하며 LINQ / `List.Sort` 를 쓰지 않습니다. 전체 탭은 그룹 순서대로 `playerInventory.Items` 를 반복 스캔해 append 하므로 안정 정렬이 보장됩니다. `InventorySlotUI` 는 `IPointerClickHandler` 로 클릭을 컨트롤러에 위임하며, 필터링은 표시만 바꾸고 실제 `ItemData` 참조와 `PlayerInventory` 저장 구조는 변경하지 않습니다.

### 11b-8. Soul 강화 (영구 폼 메커니즘 강화)

`ItemType.Soul` 이 Form 해금 토큰이라면, **Soul 강화는 보유한 Form 의 메커니즘 스탯을 영구적으로 키우는 축**입니다. 공통 raw 스탯(공격/방어/HP/이동)은 Relic·런 영역이 담당하고 Soul 강화는 폼 고유 메커니즘 전용입니다(도메인 분리 — "영구 vs 런"이 아니라 "무엇을 강화하나"로 구분).

| 구성 | 역할 |
|------|------|
| `SoulStatType` (enum, 10종) | AttackSpeed / CooldownReduction / Crit / Lifesteal / MagazineSize / ReloadSpeed / ParryStackMax / ParryGrace / ComboDamage / AilmentDamage |
| `PlayerSoulEnhancements` (MonoBehaviour) | `(PlayerFormId, SoulStatType)` 별 레벨 side-store. GetLevel/AddLevel/SetLevel/OnChanged. **스탯별 개별 투자**(폼 단일 레벨 아님), 직렬화 대비 List + Dictionary 캐시 |
| `SoulEnhancementTable` (SO) | 폼별 `SoulStatGrowth{stat, perLevel, maxLevel}` 정의 (레벨당 증가치·최대 레벨) |
| `SoulStatBonus` | 활성 폼 기준 보너스 집계 = Σ(레벨 × perLevel). float[] 인덱스, alloc 없음 |

`PlayerCombatController` 가 폼 전환(`EquipWeapon`)·강화 변경(`OnChanged`) 시 `RecalculateSoulBonus()` 로 **활성 폼 보너스만** 재계산합니다(폼 전환 시 출렁임 의도, Normal 폴백). 적용 훅:

| 스탯 | 적용 지점 |
|------|-----------|
| MagazineSize | `ApplyWeaponMagazine` — `maxBullet = magazineSize + bonus` |
| AttackSpeed | `EffectiveAttackCooldown` — `attackCooldown × (1 − %/100)` (SetAttackCooldown 3곳) |
| ParryStackMax | `ParryStackResource.SetMax` |
| ParryGrace | `ParryStackResource.SetGraceDuration` (flat 초) |
| CooldownReduction | `SkillSlotRuntime.StartCooldown(multiplier)` |
| ReloadSpeed | `EffectiveReloadTime` — `reloadTime × (1 − %/100)` |

`Crit` / `Lifesteal` / `ComboDamage` / `AilmentDamage` 는 enum·집계만 있고 적용 훅 미구현(각 신규 전투 메커니즘 필요 — Sword 콤보, Dagger 상태이상 DoT 등).

콘솔 `/enhance <form> <stat> [count]` 로 레벨을 부여합니다(2층 자동완성 form→stat, `DeveloperConsoleSoulStatResolver` 로 stat 토큰 격리, count 기본 1 가산). 강화 재료(폼별 조각) 소비·Town Soul Altar 입력은 미구현 — 현재 콘솔/Inspector 로만 레벨 조정.

---

## 11c. 시스템 11 — 개발자 콘솔

인게임 개발자 콘솔 (`` ` `` 키 토글)로 명령어 입력·자동완성·결과 출력을 제공합니다.

### 11c-1. 구성 파일

| 파일 | 역할 |
|------|------|
| `DeveloperConsoleUI` | MonoBehaviour UI 컨트롤러 — `` ` `` 키 토글, `TMP_InputField` 입력, ScrollRect 로그 출력, Tab 자동완성, `GamePauseController` 연동 |
| `DeveloperConsoleService` | 순수 C# 명령 레지스트리 — 명령 Dictionary + 인수 제안 프로바이더 Dictionary, `Execute` / `GetArgumentSuggestions` / `GetCommandNames` API, `/give` category resolver |
| `DeveloperConsoleCommandExecutor` | MonoBehaviour 실행 컨트롤러 — `DeveloperConsoleService`가 파싱·등록을 담당하고 게임 상태 변경(적 처치·문 개방·텔레포트·층 이동·아이템 지급·폼 전환)은 이 컴포넌트로 위임. `RoomSpawner` · `DungeonManager` · `LocationTransitionManager` · `EliteArenaEncounterController` · `PlayerController` · `PlayerInventory` · `PlayerFormController` · `TeleportDestinationDatabase` 보유 (구 `DeveloperConsoleCommandContext` readonly struct 대체) |
| `DeveloperConsoleCommandResult` | 명령 실행 결과 (readonly struct) — `Success(msg)` / `Error(msg)` / `Clear()` / `Ignored()` 팩토리 메서드 |

### 11c-2. 등록된 명령

| 명령 | 설명 | 인수 자동완성 |
|------|------|--------------|
| `/help` | 등록된 명령 목록 + 사용법 출력 | 없음 |
| `/clear` | 콘솔 로그 초기화 | 없음 |
| `/echo [text]` | 입력 텍스트 그대로 출력 | 없음 |
| `/tp [destinationId]` | 플레이어를 목적지로 순간이동 | `TeleportDestinationDatabase` ID 목록 |
| `/dooropen [doorType]` | 현재 층의 문 일괄 개방 | `normal` `elite` |
| `/kill` | 현재 방 또는 Elite Arena 내 모든 적 즉시 처치 (디버그 전용) | 없음 |
| `/floor add [count]` | 현재 층 + count 이동 | `add` `sub` `set` |
| `/floor sub [count]` | 현재 층 - count 이동 | |
| `/floor set [floor]` | 지정 층으로 이동 | |
| `/form set [id]` | 플레이어 폼 즉시 전환 | `set` → `PlayerFormId` 목록 |
| `/give <category> <code> [count]` | PlayerInventory 에 ItemDatabase 아이템 지급. category 는 `ItemType` 토큰(`soul`/`relic`/`consumable`/`currency`/`material`/`key`/`equipment`) | category → 해당 ItemType itemCode 목록 |

### 11c-3. 자동완성 구조

```
입력 토큰 분석 (ParseAutocompleteTokens):
  토큰 1개 & 후행 공백 없음 → 명령 이름 자동완성 (prefix 필터링)
  토큰 1개 & 후행 공백 있음 → 첫 번째 인수 자동완성
  토큰 2개 & 후행 공백 없음 → 두 번째 자리 입력 중, 첫 번째 인수 자동완성

인수 제안 프로바이더 (_argumentProviders Dictionary):
  "floor"    → { "add", "sub", "set" }
  "form"     → { "set" }
  "give"     → { "soul", "relic", "consumable", "currency", "material", "key", "equipment" }
  "dooropen" → { "normal", "elite" }
  "tp"       → TeleportDestinationDatabase.GetDestinationIds()

하위 인수 제안 (_subArgumentProviders Dictionary):
  ("form", "set") → PlayerFormId enum names
  ("give", "<category>") → DeveloperConsoleItemCategoryResolver.TryResolveCategory(category) → PlayerInventory.GetDatabaseItemCodes(type)
```

Tab 키로 제안 순환·적용, Esc 로 제안 패널만 닫음 (이후 Esc 는 콘솔 전체 닫기).  
콘솔 열림 시 `GamePauseController.AddSource(GamePauseSource.DeveloperConsole)` 로 게임 일시정지, 닫힘 시 해제.

> `/kill` 명령은 `DeveloperConsoleCommandExecutor.ExecuteKill()` → `RoomSpawner.ForceKillCurrentEncounterEnemiesForDebug()` 로 라우팅됩니다. 일반 방이면 현재 방의 생존 적을, Elite Arena 인카운터 중이면 `EliteArenaEncounterController.ForceKillActiveEliteForDebug()` 로 Elite 적을 처치합니다.

> `/give` 명령은 category와 실제 `ItemData.ItemType` 이 다르면 실패합니다. 예: `/give soul Soul_Dagger` 는 성공하지만 `/give relic Soul_Dagger` 는 `Item category mismatch` 를 반환합니다. `/form set <id>` 는 `PlayerFormController.TrySwitchForm` 을 그대로 사용하므로 Soul 미보유 폼은 `FormSwitchResult.NotOwned` 로 차단됩니다.

---

## 11d. 시스템 12 — Elite Arena

Elite Floor(`floor % 10 == 5`)의 Elite Room에 포탈이 배치되고, 플레이어가 포탈에 진입하면 별도 고정 Arena 씬으로 텔레포트해 Elite 적과 1:1 전투를 치르는 시스템입니다.

### 11d-1. 전체 흐름

```
던전 생성 시:
  RoomSpawner.PrepareEliteKeyPlan() — 일반 방의 elite_key 드랍 슬롯 1개 결정론적 선정

Elite Room 진입 시 RoomSpawner.SpawnRoom(room):
  room.IsElite → PrepareEliteRoomPortal(room, dungeonManager)
    → EliteArenaEncounterController.PrepareEntrancePortal(room, dungeonManager)
      Elite Room 중앙 walkable 타일에 EliteArenaPortal Instantiate·배치

플레이어가 포탈 콜라이더에 접촉:
  EliteArenaPortal.OnTriggerEnter2D → TryEnterArenaFromPortal(portal, room, player)
    RoomSpawner.TrySelectEliteForArena(room, out eliteData)
    LocationTransitionManager.TryTeleportPlayer(player, arenaDestinationId)
    EliteArenaEncounterController.TrySpawnElite(eliteData) → 씬 내 eliteSpawnPoint에 적 배치
    DungeonManager.CloseCurrentRoomDoors() (Elite Room 문 봉인)
    portal.SetLocked(true)

Elite 적 사망 시:
  OnEliteDied → ShowReturnPortal (ArenaReturnPortal 활성화)

플레이어가 복귀 포탈 접촉:
  EliteArenaReturnPortal.OnTriggerEnter2D → TryReturnFromArena(player)
    player.TeleportTo(_originReturnPosition)
    DungeonManager.OpenCurrentRoomDoors()
    LocationTransitionManager.RestoreDungeonMinimapSource()
    portal.MarkCompletedAndDisable(originRoom) → 이후 같은 방에서 포탈 비활성
    CancelEncounter() → HideReturnPortal
```

### 11d-2. 컴포넌트 책임 분리

| 컴포넌트 | 역할 |
|---------|------|
| `EliteArenaEncounterController` | 인카운터 전체 조율 (static `Active`), Elite spawn/defeat, portal lifecycle, `WalkabilityArea` passthrough |
| `EliteArenaPortal` | Elite Room 내 진입 포탈 — `Bind(controller, room)` 후 접촉 감지, `IsCompletedForRoom` 으로 중복 진입 차단 |
| `EliteArenaReturnPortal` | Arena 내 복귀 포탈 — Elite 사망 후 `ShowReturnPortal`로 활성화 |
| `WalkabilityArea` | Arena walk/wall Tilemap 쌍 — OnEnable/OnDisable 자동 등록, walkability·LOS API 제공 |
| `WalkabilityQuery` | 정적 라우팅 — `WalkabilityArea` 우선, 없으면 `DungeonData` fallback |
| `WorldEnvironmentQuery` | 전투 코드용 퍼사드 — 어떤 공간인지 몰라도 `WorldEnvironmentQuery.IsFootprintWalkable(pos, r)` 1회 호출 |

### 11d-3. WalkabilityArea 등록 패턴

`WalkabilityArea` MonoBehaviour를 Arena 루트 오브젝트에 부착하면, `OnEnable` 시 `WalkabilityQuery`(static `List<WalkabilityArea>`)에 자동 등록됩니다. 던전 절차 공간은 `DungeonManager`/`DungeonData`로 처리되고, Arena 등 특수 공간은 `WalkabilityArea` 단위로 격리됩니다.

```
EliteDashPatternRuntime / EliteJumpPatternRuntime / PlayerDashController / EnemyMovementHandler:
  CanOccupy(pos) → WorldEnvironmentQuery.IsFootprintWalkable(pos, footprintRadius)
    → WalkabilityQuery.IsFootprintWalkable
      Area 안 → WalkabilityArea.IsFootprintWalkableWorld (walk tile + wall tile 판정)
      Area 밖 → DungeonData footprint 4코너 IsWalkable (기존 던전 판정)
```

서로 다른 Area에 걸친 LOS는 항상 차단(`HasLineOfSight`에서 fromArea ≠ toArea → false)하여 공간 간 투사체·시야 누출을 방지합니다.

> **Area-우선 분기가 명시적으로 남아 있는 호출처** (현재 4곳):
> - [`EnemyMovementHandler.HasLineOfSight`](Assets/Scripts/Enemy/EnemyMovementHandler.cs#L107) — Area 안에서는 walkable LOS, Dungeon 에서는 `!= EMPTY` 만 차단(닫힌 문 너머 추적 유지 의도)
> - [`EnemyTargetHandler.IsTargetOnTrackableTile`](Assets/Scripts/Enemy/EnemyTargetHandler.cs#L93) — 같은 의도. Area=walkable, Dungeon=`!= EMPTY`
> - [`EliteJumpPatternRuntime.TryResolveJumpTarget`](Assets/Scripts/Enemy/Elite/Patterns/EliteJumpPatternRuntime.cs#L221) — Area 안에서는 `TryFindNearestWalkable`, Dungeon 에서는 `dungeon.GetRoomAt + StayInRoom`
> - [`FogVisibilityRenderer.ResolveVisibility`](Assets/Scripts/Visual/FogVisibilityRenderer.cs#L89) — Area 안에서는 항상 visible 로 처리(Fog bypass)
>
> 그 외 `PlayerController.CanMoveTo` 는 `LocationTransitionManager.IsInTown` 분기로 Town 일 때만 `Physics2D.OverlapCircle(CombatLayers.WallMask)` 를 사용합니다(Town walls 가 Collider 기반이라서). 이 5개 분기를 단일 라우팅으로 통합하는 정리 안건은 별도 문서 참고.

### 11d-4. 복귀·정리 흐름

- 층 이동 또는 마을 이동 시 `LocationTransitionManager.CleanupDungeonRuntime()` 이 `EliteArenaEncounterController.Active?.ClearRuntimeState()` 호출 → 진행 중 인카운터 취소, 포탈 비활성화
- Elite 적은 `EnemyPoolManager` 풀에서 꺼내므로 층 이동 시 `EnemyPoolManager.ReleaseAllActiveEnemiesForLocationChange()` 로 일괄 회수됨
- `LocationTransitionManager.RestoreDungeonMinimapSource()` 가 복귀 시 미니맵을 Dungeon 모드로 복원 (Elite Arena 진입 시 minimap이 Arena Tilemap으로 전환된 경우 대비)

---

## 11e. 시스템 13 — Boss Area

특정 층(예: 20·40·60)에 도달하면 일반 던전 생성 대신 전용 Boss Area(같은 씬 내 고정 fixed area)로 이동해 보스와 전투하고, 처치 후 출구로 다음 층으로 진행하는 시스템입니다. Elite Arena 의 입장/스폰/퇴장 lifecycle 을 `ArenaEncounterBase` 로 공통화해 재사용합니다. **1차 구현 완료(2026-06-08, placeholder boss/shared tilemap) — 정식 보스맵·수치·엔딩 연출은 후속.** 상세 기획: `HandOff/BOSS_AREA_DESIGN.md`.

### 11e-1. 핵심 결정

| 항목 | 결정 |
|---|---|
| 보스층 진입 | **N층 자체가 Boss Area** (일반 던전 N층 없음). 19층 출구 → Boss Area → 출구 → 21층 |
| 맵 구성 | 같은 씬(Main.unity) 내 fixed area. Elite Arena 패턴 확장. 1차는 elite arena tilemap 공유 |
| 사망 처리 | 기존 GameOver 흐름 재사용 (`CombatEventChannel.OnPlayerDied` → `GameOverFlowController`) |
| 보스층 매핑 | 데이터 기반 `BossEncounterTable`(SO) — 코드 하드코딩 없음 |

### 11e-2. 전체 흐름

```
층 전환 요청 시 DungeonManager.TryTransitionToFloor(targetFloor):
  bossTable.TryGetBoss(targetFloor, out entry) 성공 →
    TryEnterBossFloor(targetFloor, entry):
      floor = targetFloor
      BossEncounterController.Active.Begin(entry, player)
        TryTeleportPlayerToArena(player, entry.BossAreaDestinationId)
          → teleport 가 destination.minimapLocationId 로 미니맵 fixed source 자동 전환
        SpawnArenaEnemyAtPosition(entry.Boss, spawnPos, OnBossDied)
        출구(BossExitPortal) 잠금
    (일반 FloorTransition 코루틴은 호출하지 않음)
  실패(보스층 아님) → 기존 StartCoroutine(FloorTransition(targetFloor))

보스 사망 시:
  OnBossDied → _bossDefeated=true → ShowExitPortal (BossExitPortal 활성)

플레이어가 출구 포탈 접촉:
  BossExitPortal.OnTriggerEnter2D → controller.RequestProceed(player)
    entry.IsFinal → HandleFinalBossDefeated() (엔딩 stub 로그, 층 전환 없음)
    else → ProceedRequested 이벤트 발행

DungeonManager.HandleBossProceedRequested(entry, player):  (ProceedRequested 구독)
  TryTransitionToFloor(floor + 1)  → 일반 던전 생성 경로
  pending(controller, targetFloor) 기록
  FloorTransition 코루틴 끝에서 CompletePendingBossProceedIfNeeded(completedFloor):
    targetFloor 매칭 → controller.CompleteProceedToNextFloor()
      RestoreDungeonMinimapSource() + CancelEncounter()
```

### 11e-3. 컴포넌트 책임 분리

| 컴포넌트 | 역할 |
|---------|------|
| `BossEncounterTable` (SO) | `floor → BossEncounterEntry`(boss EnemyData / bossAreaDestinationId / areaId / isFinal). `TryGetBoss(floor)` 선형 조회, OnValidate 중복 floor 경고 |
| `ArenaEncounterBase` | Elite·Boss 공통 lifecycle 헬퍼 — teleport, enemy spawn, return/exit portal show·hide, minimap restore, spawn position resolve. `EliteArenaEncounterController` 도 이를 상속 |
| `BossEncounterController` | `:ArenaEncounterBase`, static `Active`(Elite 와 별도 타입). `Begin`/`OnBossDied`/`RequestProceed`/`CompleteProceedToNextFloor`/`CancelEncounter`. `ProceedRequested` 이벤트 발행 |
| `BossExitPortal` | 보스 처치 후 활성화되는 출구 포탈 — 접촉 시 `RequestProceed`, 잠금·중복 진입 가드 |
| `DungeonManager` | `TryTransitionToFloor` 보스층 분기 + `ProceedRequested` 구독 + pending 완료 매칭 |

### 11e-4. 미니맵·위치 처리 (Elite 와의 차이)

- **진입 미니맵 전환은 별도 코드 훅 없음.** Boss Area teleport destination 에 `minimapLocationId` + `useTilemapMinimap` 를 설정하면, `LocationTransitionManager.TryTeleportPlayer` 내부의 `ApplyMinimapSourceForLocation` 이 진입 시 자동 전환. (Elite 는 컨트롤러가 직접 처리)
- ⚠️ **Boss Area destination 은 `locationType = Dungeon(1)` 로 설정해야 한다.** 퇴장 시 `DungeonManager.TryTransitionToFloor(floor+1)` 첫 가드가 `LocationTransitionManager.IsInDungeon` 를 검사하므로, Dungeon 이 아니면 다음 층 진행이 막히고 dungeonRoot 도 비활성화됨.
- 퇴장 시 미니맵 복원은 `CompleteProceedToNextFloor()` → `RestoreDungeonMinimapSource()`.
- Elite 와 달리 Boss 의 다음 층 이동은 `player.TeleportTo` 직접 호출이 아니라 일반 `FloorTransition`(던전 재생성 + 플레이어 스폰 이벤트)을 경유한다.

### 11e-5. 구현 상태 / 후속

- 1차 구성: 20/40/60층 모두 `boss_arena` destination·area 공유, placeholder 보스 `Elite_Magma_01`, 60층 `isFinal=true`.
- **통합 흐름 Play 검증 전부 통과(2026-06-09)**: 20/40/60층 진입 → 보스 스폰 → 처치 → 출구 포탈 → 다음 층 정상 진입, 60층 `isFinal` 엔딩 정지(다음 층 안 넘어감), 보스전 사망 = 기존 GameOver, Elite Arena 회귀 무손상, LocationRoot 갇힘/스폰 리스크 통과. **코드·흐름 완성.**
- 남은 건 검증이 아니라 컨텐츠: 보스별 전용 맵(현재 elite tilemap 공유) / 정식 보스 EnemyData·수치 / 60층 엔딩 연출(현재 Debug.Log stub) / 처치 보상 연계 / 마을 메타루프.

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
| 결정론적 방 스폰 시드 | `DeterministicSeedUtility.CreateSeed` + `RoomInfo.StableRoomKey` | 방마다 globalSeed/currentStageRegion/floor/StableRoomKey/domain FNV-1a 해시로 `System.Random` 생성 — UnityEngine.Random 오염에 흔들리지 않음 |
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
| WalkabilityArea OnEnable/OnDisable 자동 등록 | `WalkabilityQuery.s_Areas` static List | Elite Arena 등 특수 공간이 활성화될 때만 리스트에 추가 — 런타임 Find 없음, Area 0개 시 DungeonData fallback |
| WorldEnvironmentQuery 파사드 | `WorldEnvironmentQuery → WalkabilityQuery` | 전투 코드가 공간 종류를 몰라도 `IsFootprintWalkable` 1회 호출로 Dungeon/Arena 자동 라우팅 |
| Elite Dash 목표 위치 기반 이동 | `EliteDashPatternRuntime.TryResolveDashTarget` + `dashSpeed×dt` | dashDuration 대신 목표 도달 시 종료 — `WalkabilityQuery.TryFindNearestWalkable` 로 Arena 내 유효 위치 선택, 벽에 막히면 `stopOnWall` 즉시 종료 |
| Elite Arena 포탈 정적 캐시 | `EliteArenaEncounterController.Active` | `EliteArenaPortal` / `RoomSpawner` 가 매 프레임 FindAnyObjectByType 없이 controller 참조 |
| 개발자 콘솔 kill 명령 단일 진입 | `DeveloperConsoleCommandExecutor.ExecuteKill` → `RoomSpawner.ForceKillCurrentEncounterEnemiesForDebug` | 일반 방/Elite Arena 여부에 무관하게 1 메서드로 처리 — Arena 인카운터 중이면 `EliteArenaEncounterController.ForceKillActiveEliteForDebug` 로 위임 |

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
                           ├── PlayerResource (HP 갱신)
                           └── CombatEventChannel.RaisePlayerHpChanged()
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
   - 등장 가능 층 범위: `minFloor` / `maxFloor` (둘 다 ≥ 1, max ≥ min — 어긋나면 인스펙터·OnValidate 경고)
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

### 새 아이템 / 드랍 추가

1. `ItemDatabase` 에셋에 새 `ItemData` 추가 (itemCode·displayName·icon·itemType·stackable·maxStack 입력, 필요 시 useEffects/passiveEffects/soulFormId/removeOnFloorTransition/removeOnDungeonExit 설정)
2. 적이 드랍하려면 `EnemyController.MarkAsEliteKeyHolder` 와 동일한 패턴으로 `EnemyInventory.AddDropItem("새_코드")` 호출하는 분기를 추가 (예: 보스 사망 시, 특정 RoomType 진입 시 등)
3. 픽업 자체는 자동 — `DroppedItem.OnTriggerEnter2D` 가 모든 ItemType 을 `PlayerInventory.AddItem` 으로 추가
4. Consumable 즉시 효과는 `useEffects` + `ItemEffectApplier` 경로를 사용. 현재 `HealHp` 지원
5. Relic 평면 패시브는 `passiveEffects` + `PlayerItemStats` 경로를 사용. 현재 MaxHp/Attack/Defense/MoveSpeed 지원
6. Soul 아이템은 `itemType=Soul` + `soulFormId` 지정만으로 `PlayerInventory.OwnsSoulForm` / `PlayerFormController.TrySwitchForm` 게이팅에 반영
7. Equipment 장착, Currency 소비처, 행동형 Relic 특수 효과는 별도 시스템으로 확장

### 새 Elite 패턴 추가

1. `ElitePatternData` 상속 ScriptableObject 작성:
   - `CreateRuntime()` 오버라이드해 전용 `ElitePatternRuntime` 인스턴스 반환
   - `[CreateAssetMenu(...)]` 로 인스펙터에서 생성 가능하게
2. `ElitePatternRuntime` 상속 클래스 작성 — `Start(context)` / `Tick(dt)` / `Cancel()` 구현, `IsFinished=true` 로 종료
3. `ElitePatternSet` 에셋에 패턴 추가 → `EnemyData.elitePatternSet` 에 연결, `isElite=true` 활성화

> 패턴 런타임이 필요한 서비스(투사체 발사, 코루틴, Animator 트리거)는 모두 `ElitePatternContext` 가 노출하므로 별도 의존 주입 불필요.

### 새 텔레포트 목적지 추가

1. `TeleportDestinationDatabase` 에셋에 새 `TeleportLocationData` 추가
   - `id` (유니크), `locationType` (Town/Dungeon)
   - `locationRootId` — 씬에 배치된 `LocationRoot` 의 id
   - `localSpawnPosition` — 그 root 기준 로컬 오프셋
   - 필요 시 `minimapLocationId` (TilemapMinimapSource 와 매칭)
2. 새 LocationRoot 가 필요하면 빈 GameObject 에 `LocationRoot` 부착 + `locationRootId` 입력 (씬 활성 시 자동 등록)
3. 트리거에서 텔레포트하려면 `TeleportService` 를 콜라이더에 부착 + `targetDestinationId` 드롭다운 선택

### 새 입력 키 추가

1. `PlayerInputKeySettings.cs` 에 새 `Key` 필드 추가 (`OnValidate` 의 keys/names 배열에도 추가)
2. `PlayerInputReader` 에 `WasXxxPressedThisFrame` 프로퍼티/플래그 추가, `Update` 에서 `WasPressedThisFrame(keyboard, settings.xxx)` 로 갱신
3. 호출자 코드(`PlayerController` 등)에서 해당 프로퍼티 참조

### 새 플레이어 폼 추가

1. `PlayerFormId` enum 에 새 식별자 추가
2. `Create > JBRogLike > Player > Form` 으로 `PlayerFormData` 에셋 생성 — `animatorController` (Idle/Walk/Attack/Spin/Dash/Death 등 트리거를 가진 controller), `defaultSprite`, `defaultSpriteFacesRight`, `useHorizontalFlipForFacing`, dash 회전 옵션 입력
3. `basicAttackMode`(Damage/Parry/Bullet) 선택 + `defaultWeapon` 에 그 폼의 loadout WeaponData(stats + skills[4], 마탄이면 탄창 필드) 연결
4. `SetCurrentForm(formData)` 또는 `ApplyForm` 시 Animator/sprite/trigger 갱신 + 실제 폼 변경이면 `EquipWeapon(defaultWeapon)` 로 무기·스킬 자동 장착 (loadout 단일 소스 = WeaponData, `PlayerFormData.skills` 는 폐지)
   ※ 런타임 폼 전환은 `PlayerFormController.TrySwitchForm(PlayerFormId)`(+`PlayerFormDatabase` formId→asset 매핑)로 구현됨 — 콘솔 `/form set <id>` 진입점. `Normal` 은 항상 허용, 나머지 폼은 `ItemType.Soul` + `soulFormId` 보유 여부로 게이팅. 새 Form 을 게임플레이에서 해금하려면 대응 Soul ItemData 를 추가하고 드랍/보상 파이프라인에 연결

### 새 스킬 애니메이션 분기 추가

1. `SkillAnimationType` enum 에 새 값 추가 (예: `Sweep`)
2. `PlayerFormController.PlaySkillAnimation` switch 에 케이스 추가 — 신규 trigger hash 캐싱(`CacheAnimatorParameters`) + `PlayTriggerAnimation` 또는 전용 헬퍼 호출
3. SkillData 에셋에서 `animationType` 을 새 값으로 지정. `customAnimationTrigger` 가 필요한 비표준 trigger 라면 `CustomTrigger` 를 사용

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
| **플레이어 전투** | 기본 공격, 스킬 4슬롯, HP 관리 (PlayerResource) + 폼 고유 자원(Bullet/ParryStack) 원장 (MP 폐지) |
| **공격 판정 분리** | AttackExecutor — 히트 감지·데미지 적용 독립 처리 |
| **스킬 실행 라우팅** | SkillExecutor — InstantArea/Projectile/Dash/Blink/Buff 분기 (AreaOverTime 미구현) |
| **스킬 슬롯 런타임 분리** | SkillSlotRuntime — MonoBehaviour 미의존 슬롯 상태(데이터·쿨다운) |
| **스킬 타겟 공통화** | SkillTargetResolver — 미리보기·기본공격·스킬이 동일한 셀 계산 사용 |
| **스킬 실행 컨텍스트** | SkillExecutionContext — caster/aim/grid/totalAttack/hitRadius 일체 전달 |
| **쿨다운 관리** | 기본 공격은 SkillCooldownController, 스킬은 SkillSlotRuntime이 보유 |
| **발사체 시스템** | 직선 이동, 벽/유닛 충돌, 관통 옵션 |
| **투사체 발사 공통화** | ProjectileFireService — 적 원거리·플레이어 스킬이 Single/Burst/Spread/Circle 동일 처리 |
| **투사체 타겟 정책** | ProjectileTargetHitMode — DestroyOnHit / Pierce / HitOncePerTarget |
| **플레이어 투사체 스킬** | SkillData.executionType=Projectile — prefab/속도/수명/패턴/벽반사 인스펙터 설정 |
| **플레이어 대시 스킬** | PlayerDashController — 발자국 검사 이동(적 통과·벽만 검사), 경로/접촉 데미지·무적 옵션, 외부 무적 카운터, `OnEnemyHit` 콜백(Dagger E 마커 폭발 훅) |
| **외부 무적 시스템** | BeginExternalInvincibility/EndExternalInvincibility — 다중 출처(대시 등) 무적 중첩 처리 |
| **무적 셰이더 플래시** | PlayerInvincibilityFlashFeedback — MaterialPropertyBlock 기반 _FlashAmount 보간 |
| **일반 피격/외부 무적 분리** | HitFlashFeedback(피격 색상) ↔ PlayerInvincibilityFlashFeedback(셰이더) 독립 |
| **투사체/대시 미리보기** | SkillRangePreviewer — Projectile은 발사 패턴별, Dash는 거리 + 벽 클리핑 |
| **기본 공격 미리보기** | Space 홀드 시 무기 attackPattern 시각화 (스킬 미리보기와 우선순위 분리) |
| **Fog of War** | FogOfWarController — 미탐사/탐사/현재시야 3상태, Bresenham LoS, 닫힌 문 시야 차단 |
| **적 전투** | IDamageable, 방어력 계산, 사망 처리 |
| **적 체력바** | 실시간 갱신, 색상 그라디언트, 자동 숨김, collider 윗변 기준 위치 + root lossyScale 역수로 월드 크기 정규화(적 스케일 무관 일정) |
| **적 AI (FSM)** | Idle/Chase/Attack 상태, A* 경로탐색, 군중 분리 |
| **적 상태이상** | 넉백, 슬로우 (지속시간 기반) |
| **적 스폰 시스템** | 방 진입 트리거, 예산 기반 스폰, 방 클리어 감지 |
| **오브젝트 풀링** | EnemyPoolManager (적 재사용) |
| **HP 상태바 UI** | PlayerStatusBarUI — HP 슬라이더 + 텍스트, 이벤트 구독 갱신 (MP 바 폐지) |
| **폼 고유 자원 UI** | ParryStackBarUI(패리 스택 슬라이더) / FreischutzMagazineUI(탄창 칸 Bullet/Bullet_empty) — 현재 폼 BasicAttackMode 로 표시 분기, 구 MP 영역 재사용 |
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
| **결정론적 방 적 스폰** | `DeterministicSeedUtility` + `RoomInfo.StableRoomKey` + `DungeonManager.currentStageRegion`(`SpawnRegion`) — 방별 `System.Random` 으로 적 종류·위치 재현성 보장 |
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
| **플레이어 상태이상 시스템** | `PlayerStatusEffectType`(Slow/Stun) + `PlayerCombatController.ApplyEnemyCombatImpact` — 적 공격에서 받는 데미지·넉백·슬로우·스턴을 단일 진입점으로 처리, 슬로우는 활성 효과 중 가장 강한 강도만 적용, 스턴 중 이동·방향 전환·스킬 입력 차단. **(2026-06-09 리팩터: 넉백/슬로우/스턴 로직을 `PlayerStatusEffects` 순수 C# 클래스로 분리, 넉백은 코루틴→Tick 타이머 환원·변위 콜백 주입, 컨트롤러는 facade 위임 — 동작 보존)** |
| **플레이어 상태이상 아이콘 UI** | `PlayerStatusEffectUI` + `StatusEffectIconView` — 슬로우/스턴 아이콘과 잔여 시간 게이지·텍스트 표시, `OnStatusEffectApplied/Ended` 이벤트 구독 |
| **마을·던전 전환 시스템** | `LocationTransitionManager` — `TeleportDestinationDatabase` ScriptableObject 기반 목적지 관리, 진입/이탈 시 CleanupDungeonRuntime + StartNewDungeonRun, minimapRoot 항상 표시 |
| **텔레포트 시스템** | `TeleportService` + `TeleportDestinationDatabase` + `LocationRoot` + `LocationRootRegistry` — DB 조회·씬 루트 자동 등록·`root.TransformPoint(localSpawnPosition)` 으로 월드 좌표 계산 |
| **이중 모드 미니맵** | `MinimapController` — Dungeon(DungeonData 그리드) / Tilemap(TilemapMinimapSource) 두 모드 전환, `SetDungeonSource()` / `SetTilemapSource(id)` 공개 API, 전환 시 스테일 텍스처 즉시 클리어 |
| **Town Tilemap 미니맵** | `TilemapMinimapSource` + `LocationMinimapRegistry` — 위치별 Tilemap 소스 자동 등록 레지스트리, Texture2D 픽셀 렌더링 (Y↑ 뒤집기 없음, Dungeon과 좌표계 분리) |
| **적 공격 임팩트 데이터화** | `EnemyAttackImpactData`(knockback·slow·stun) struct — `rushImpact`/`jumpImpact`/`projectileImpact` 가 공유, `EnemyActionHandler.ApplyEnemyImpactToTarget` 단일 라우팅 |
| **`isStationary` / `immuneToKnockback` 플래그** | `EnemyData` — 위치 고정 적과 넉백 면역 적 구현 (데미지·상태이상은 그대로 적용) |
| **CombatLayers Wall 마스크 추가** | `WallMask`/`WallFilter`/`HasWallLayer` — `Wall`/`Obstacle` 이름 자동 폴백으로 knockback clamp 등의 LayerMask 호출 정적 캐시화 |
| **RoomSpawner 참조 SerializeField 캐싱** | `DungeonManager.roomSpawner` + `TryGetRoomSpawner` — `FindAnyObjectByType` 제거, 누락 시 1회 경고 |
| **GameOver UI 자동 빌드 제거** | `BuildDefaultUi` 경로 삭제, 인스펙터 미설정 시 1회 경고만 출력하고 표시 skip |
| **EXTRA 통로 외곽 우회 방지** | `DrawLCorridor`가 EXTRA(optional)에서는 primary/alternate 모두 충돌 시 skip + bool 반환 — `connectedPairs` 보존으로 잘못된 logical 상태 누적 차단 |
| **Elite Floor 자동 분기** | `DungeonGenerator.IsEliteFloor(floor%10==5)` + `AssignEliteRoom` — MST leaf 가장 깊은 방을 Elite 로 지정, EXTRA 통로에서 elite 방 제외해 단일 진입 경로 보장 |
| **Elite Door 타일·자동 개방** | `DungeonTilemapRenderer.eliteDoorTile` + `PlaceEliteDoors` + `TryOpenEliteDoorWithKey(PlayerInventory, ItemData)` — Elite Room perimeter 의 corridor-인접 cell 만 elite door 로 봉인, 플레이어 접촉 시 인벤토리에서 `elite_key` 1개 소모 + 한 셀 카빙 |
| **Elite Key 결정론적 드랍** | `RoomSpawner.PrepareEliteKeyPlan` + `DeterministicSeedUtility.EliteKeyDomain` — `CountDeterministicSpawns` dry-run 으로 모든 일반 방의 스폰 슬롯 수 집계 후, elite room StableRoomKey 기반 RNG 로 1개 슬롯 선택, `MarkAsEliteKeyHolder` 가 그 적의 EnemyInventory 에 `elite_key` 추가 |
| ~~**PlayerEliteKeyInventory**~~ | (제거됨) Elite Key 가 일반 ItemData 로 통합되어 `PlayerInventory` 가 모든 보유 항목을 관리. 구 bool/EliteKeyChanged 이벤트 경로는 삭제 |
| **PlayerController Elite Door 접촉 처리** | `TryOpenEliteDoorOnContact` — 매 프레임 dungeonRenderer.TryOpenEliteDoorWithKey 호출, 키 보유 시 접촉만으로 자동 개방 |
| **아이템 데이터베이스** | `ItemDatabase` ScriptableObject + `ItemData`(itemCode/displayName/icon/itemType/stackable/maxStack/useEffects/passiveEffects/soulFormId/정리 플래그) + `ItemType` enum(Key/Currency/Consumable/Equipment/Relic/Material/Soul) — `TryGetItem` Dictionary 캐시, OnValidate 중복/공백 itemCode 자동 경고, `GetItemCodes` 로 콘솔 자동완성 지원 |
| **EnemyInventory + DropItemSpawner** | `EnemyInventory.AddDropItem(itemCode)` → Die 시 `DropItemSpawner.Instance.SpawnDrops` 가 ItemDatabase 로 ItemData 해석 후 `DroppedItem` Instantiate, dropSpacing 으로 다중 드랍 정렬 |
| **DroppedItem 픽업** | `OnTriggerEnter2D` 에서 `player.TryGetComponent<PlayerInventory>` 후 `inventory.AddItem(_itemData, _amount)` 호출 — 성공 시 `DropItemSpawner.Instance?.Unregister(self)` + `Destroy(self)`. ItemType 분기 없이 모든 아이템(Currency/Consumable/Key/Soul 등)을 인벤토리로 통합 |
| **마을·던전 전환 시 드랍 정리** | `LocationTransitionManager.CleanupDungeonRuntime` 에 `DropItemSpawner.ClearAllActiveDrops` 추가 + 층 이동시에도 `DungeonManager.FloorTransition` 시작점에서 호출 — 이전 층 드랍이 신생 던전에 잔존하지 않음 |
| **PlayerInputKeySettings ScriptableObject** | 이동/액션/스킬 12개 키를 단일 에셋으로 일괄 설정, `PlayerInputReader` 가 keySettings 미설정 시 기본 키 폴백 + 1회 경고, OnValidate 가 `Key.None`/중복 키 자동 검출 |
| **PlayerInputReader 인벤토리 입력 hook** | `InventoryPressedThisFrame` flag 노출, `InventoryUIController.Update` 가 이를 읽어 I 키 토글 처리 |
| **LocationRoot 기반 텔레포트** | 기존 `TeleportDestinationPoint`/`Registry` 제거, `LocationRoot`(SerializeField id + OnEnable/OnDisable 자동 등록) + `LocationRootRegistry` static Dict 로 대체. `TeleportLocationData` 가 `locationRootId` + `localSpawnPosition` 을 보유, `root.TransformPoint(localSpawnPosition)` 으로 월드 좌표 계산 |
| **TeleportDestinationId 드롭다운** | `[TeleportDestinationId]` PropertyAttribute + Editor drawer 가 string 필드를 DB id 드롭다운으로 변환 — LocationTransitionManager / TeleportService 인스펙터에 적용 |
| **TeleportDestinationData 메타데이터 확장** | `displayName` / `description` 필드 추가, `minimapLocationId` 가 빈 문자열일 때 `id` 폴백 |
| **미니맵 계단 마커 확장** | `stairMarkerPixelPadding` — STAIR_UP 셀을 padding 픽셀만큼 키워 그려 작은 미니맵에서도 가시성 확보 (탐사된 셀만) |
| **미니맵 문 색상** | `visibleDoorColor` / `exploredDoorColor` — DOOR_CLOSED 셀이 방·통로와 구분되어 표시 |
| **미니맵 플레이어 마커 가시성 게이팅** | `UpdateDungeonPlayerMarker` 가 `fogOfWar.IsExploredCell(grid)` false 시 SetActive(false) — 텔레포트 직후/맵 밖에서 마커 노출 차단 |
| **미니맵 마커 픽셀 스냅** | `SnapPlayerMarkerSize` + `SnapMarkerPosition` 이 Canvas scaleFactor 기준 정수 픽셀로 라운드 — 안티앨리어싱 흐림 방지 |
| **방 perimeter / 모서리 doorway 검증** | `PathCarvesRoomPerimeter`, `PathUsesRoomCornerDoorway` — 통로가 다른 방 테두리 ROOM 셀이나 모서리 doorway 를 횡단하지 못하도록 사전 차단 |
| **DungeonGenDebug `--scene-settings`** | Unity 외부 콘솔에서도 실제 씬 설정(120×80, room 10–50)으로 시뮬레이션 가능, `RoomPerimeterCorridorScan`/`CornerDoorwayScan` 출력 추가 |
| **Generator 디버그 connect-state 로그** | `DebugConnectState` + `DebugReachableRoomsFromR0` — 단계마다 connected/remaining/reachable(BFS) 비교로 logical-only / grid-only 불일치 추적 |
| **DungeonSettings.ExtraCandidateCount** | 인스펙터 `DungeonManager.extraCandidateCount` (기본 12) — pair 당 EXTRA 후보 생성·점수화 개수 노출 |
| **성능 최적화** | NonAlloc 물리, A* 버퍼 재사용, 오브젝트 풀, 청크 로딩, 문 배치 N→1 |
| **인벤토리 UI** | `InventoryUIController`(PlayerInventory 구독·슬롯 동적 풀·5개 고정 탭 필터·전체 탭 그룹 정렬·인벤토리 키·ESC 토글) + `InventorySlotUI`(아이콘·수량 Bind + 클릭 위임) + `UIDraggableWindow`(드래그 패널) — 개발자 콘솔 열림 시 자동 닫힘 |
| **플레이어 인벤토리** | `PlayerInventory` + `InventoryItemStack` — AddItem/RemoveItem/HasItem/GetItemCount + stackable/maxStack 정책 자동 적용, `OwnsSoulForm(formId)` 로 Soul 보유 기반 Form 소유 판정, `RemoveItemsOnFloorTransition`/`RemoveItemsOnDungeonExit` 가 ItemData 플래그 기반으로 층/던전 이탈 시 자동 정리. Elite Key 가 일반 ItemData 한 항목으로 통합되어 과거 `PlayerEliteKeyInventory` 는 제거됨 |
| **Soul 강화** | `SoulStatType`(10종) + `PlayerSoulEnhancements`((form,stat)별 레벨, 스탯별 개별 투자) + `SoulEnhancementTable`(SO, perLevel/maxLevel) + `SoulStatBonus`(활성 폼 집계). PlayerCombatController 가 폼 전환·강화 변경 시 재계산 — 탄창/공속/패리스택max/쿨감/재장전속/ParryGrace 6종 훅 적용. 콘솔 `/enhance <form> <stat> [count]` (§11b-8) |
| **드롭 설정(데이터 기반)** | `EnemyDropEntry{itemCode,min/maxAmount,chance}` + `EnemyData.drops` + `EnemyDropRoller`(결정적 롤, `EnemyDropDomain`). 스폰 통합·소울 분해는 미구현(1/3 단계) |
| **ItemData 정리 플래그** | `removeOnFloorTransition` / `removeOnDungeonExit` — 층 이동·던전 이탈 파이프라인에서 자동 청소 (elite_key 가 이 플래그 사용) |
| **DroppedItem 일반화** | `OnTriggerEnter2D` 가 ItemType 분기 없이 `PlayerInventory.AddItem` 호출로 일원화 — Currency/Consumable/Soul 등 모든 아이템이 인벤토리에 픽업 가능 |
| **ItemEffect 1차** | `ItemEffectType` + `ItemEffect` — `useEffects`(Consumable 1회) / `passiveEffects`(Relic 소지 중 상시) 데이터 추가. 현재 HealHp, MaxHpBonus, AttackBonus, DefenseBonus, MoveSpeedBonus 지원 |
| **Consumable 사용** | `InventorySlotUI.OnPointerClick` → `InventoryUIController.HandleSlotClicked` → `ItemEffectApplier.ApplyUseEffects` → 성공 시 `PlayerInventory.RemoveItem(item, 1)` — UI는 OnInventoryChanged 로 자동 갱신 |
| **Relic 평면 패시브** | `PlayerItemStats.Recalculate` 가 ItemType.Relic 의 passiveEffects 를 스택 수 비례 합산, `PlayerCombatController` 가 OnInventoryChanged 에서 재계산 후 TotalAttack/TotalDefense/MaxHp/MoveSpeedMultiplier 에 반영 |
| **Elite Pattern Set** | `EnemyData.isElite + elitePatternSet` + `ElitePatternRunner` — Projectile/Dash/Jump ScriptableObject 패턴이 cooldown/minRange/maxRange/weight 조건을 만족하면 순서대로 실행. Contact Special 과 독립된 사이클 |
| **Elite Pattern 런타임** | `ElitePatternData`(abstract SO) + `ElitePatternRuntime`(abstract) + `ElitePatternContext`(Brain/Enemy/Movement/Action/Animation/Collider/DungeonManager/ProjectileFireService/CoroutineRunner 일괄 노출) — 새 패턴 추가는 두 클래스(Data+Runtime) 작성 + CreateAssetMenu 만으로 가능 |
| **게임 일시정지 컨트롤러** | `GamePauseController` + `GamePauseSource`(DeveloperConsole/Inventory/PauseMenu/Cutscene) — 출처별 카운터 + Time.timeScale 토글, OnDisable 시 이전 timeScale 복원 |
| ~~**DungeonPortal 진입 트리거**~~ | (제거됨) `DungeonPortal.cs` 는 미사용 코드로 정리되어 삭제. 마을→던전 진입은 `TeleportService` + `TeleportDestinationDatabase` 의 trigger 콜라이더로 일원화됨 |
| **개발자 콘솔** | `DeveloperConsoleUI`(`` ` `` 키 토글·TMP_InputField·Tab 자동완성·GamePause 연동) + `DeveloperConsoleService`(명령·인수 제안 Dictionary 기반) + `DeveloperConsoleCommandExecutor`(MonoBehaviour, 게임 상태 변경 담당) + 9개 내장 명령(/help /clear /echo /tp /dooropen /kill /floor /form /give) |
| **개발자 콘솔 실행 분리** | `DeveloperConsoleCommandExecutor` (MonoBehaviour) — 구 `DeveloperConsoleCommandContext` (readonly struct) 대체. 파싱·등록은 Service, 게임 상태 변경은 Executor 로 책임 분리 |
| **개발자 콘솔 /kill 명령** | `DeveloperConsoleCommandExecutor.ExecuteKill` → `RoomSpawner.ForceKillCurrentEncounterEnemiesForDebug()` — 일반 방이면 현재 방 생존 적, Elite Arena 인카운터 중이면 `EliteArenaEncounterController.ForceKillActiveEliteForDebug()` 로 분기 |
| **개발자 콘솔 /give 명령** | `/give <category> <code> [count]` — `DeveloperConsoleItemCategoryResolver` 가 category(ItemType 토큰)를 해석하고 실제 `ItemData.ItemType` 불일치를 차단한 뒤 `DeveloperConsoleCommandExecutor.ExecuteItemGive` 가 PlayerInventory 를 찾아 AddItem. 자동완성은 category 토큰 → 해당 타입 itemCode 목록 |
| **적 등장 층 범위 필터** | `EnemyData.minFloor`/`maxFloor` + `IsAvailableOnFloor(floor)` — `RoomSpawner.BuildCandidates(region, budget, currentFloor)` 가 SpawnRegion·예산 필터 직후 층 범위로 후보 차단. `EnemyDataEditor` 가 Min/Max Floor 필드 + 잘못된 범위(`HasInvalidFloorRange`) 인스펙터 경고 표시, `EnemyData.OnValidate` 가 동일 검사 후 콘솔 경고 |
| **World Environment Query** | `WalkabilityArea`(walk/wall Tilemap 쌍 + OnEnable/OnDisable 자동 등록) + `WalkabilityQuery`(정적 라우팅 — Area 우선, DungeonData fallback) + `WorldEnvironmentQuery`(전투 코드용 파사드) — Dungeon·Elite Arena 등 공간 종류와 무관하게 단일 API로 walkability/LOS/footprint 판정 |
| **Elite Arena 시스템** | `EliteArenaEncounterController`(static Active, 입장/복귀/취소/Elite spawn) + `EliteArenaPortal`(Elite Room 내 진입 포탈) + `EliteArenaReturnPortal`(Elite 사망 후 복귀 포탈) — `RoomSpawner.PrepareEliteRoomPortal` → `EliteArenaEncounterController.PrepareEntrancePortal` 로 Elite Room 중앙에 포탈 배치, 접촉 시 `LocationTransitionManager.TryTeleportPlayer` 로 Arena 진입, 복귀 시 `RestoreDungeonMinimapSource` 로 미니맵 복원 |
| **Elite Dash 목표 위치 기반** | `EliteDashPatternRuntime` — dashDuration 제거, dashSpeed×dt 이동으로 변경. `WalkabilityQuery.TryFindNearestWalkable` 로 플레이어 위치 기반 목표 결정 (Arena/Dungeon 공용) |
| **Elite Jump WalkabilityQuery 통합** | `EliteJumpPatternRuntime` — `WalkabilityQuery.TryFindNearestWalkable` 로 착지점 결정 (Arena Tilemap / Dungeon grid 자동 라우팅) |
| **`LocationTransitionManager` 이름 정리** | 구 `TownDungeonTransitionManager` → `LocationTransitionManager` 리네이밍 (Town·Dungeon 외에 Elite Arena·향후 Boss Arena 등 다중 지역 전환을 표현). 인스펙터 필드/`Active` 정적 참조 동일, 동작 변경 없음 (commit 01897373) |
| **Elite Arena 복귀 시 미니맵 즉시 복원** | `EliteArenaEncounterController.TryReturnFromArena` 가 `LocationTransitionManager.RestoreDungeonMinimapSource` 호출 — Arena 진입 중 Tilemap 미니맵으로 전환됐던 source 를 Dungeon source 로 복귀 (commit 07be7b2e) |
| **TilemapMinimapSource Layer 자동 분류** | `autoDiscoverChildren=true` 시 자식 Tilemap 을 GameObject Layer(Walkable/Wall/Door, 인스펙터에서 이름 재정의 가능)로 한 번에 분류해 `_walkableTilemaps`/`_wallTilemaps`/`_doorTilemaps` 세 List 구성. 명시 모드와 병행 가능, 매칭 0개 시 1회 경고 |
| **전투 판정 World Environment Query 일원화** | 모든 전투 코드(`PlayerController.CanMoveTo`(Dungeon/Arena 한정), `PlayerDashController`, `EnemyController.IsFootprintWalkable`, `EnemyMovementHandler.CanMoveTo`, Kiting/Random 후퇴 검사, Elite Dash/Jump `CanOccupy`, `AttackExecutor.HasGeometryLineOfSight`, `ProjectileController.IsWallPosition`/`IsOutOfDungeonBounds`)가 `WorldEnvironmentQuery` 1개 facade 만 호출. Dungeon/Arena 라우팅은 `WalkabilityQuery` static 이 처리 (commit 7335129d, 85dce89e) |
| **미사용 코드 정리** | `Projectile.cs`(구 트리거 발사체) / `DoorController.cs`(문 위임 thin wrapper) / `DungeonPortal.cs`(마을 측 진입 트리거) / `NormalEnemyAI.cs`(NormalEnemyBrain Obsolete 래퍼) 삭제 — 모든 호출처가 새 경로(ProjectileController·DungeonManager 직접 호출·TeleportService·NormalEnemyBrain) 로 이관 완료. 함께 `DungeonManager.FindStairPos`/`DungeonQueryService.FindStairPos`, `LocationTransitionManager.EnterDungeon/EnterTown`, `EliteArenaEncounterController` 의 5개 walkability passthrough, `WalkabilityArea` 의 Vector2 오버로드 등 미사용 API 도 제거 (commit 50aa15fb) |
| **ExtendPack 데모 정리** | 미사용 에셋팩 데모 씬 8개 + 데모 스크립트 21개(2D Dungeon Tilemap CoinSystem/SceneScripts, Minifantasy DUN_*/FP_* prop variants 등)·meta·빈 폴더 삭제. **룰타일·스프라이트·prefab 등 실사용 아트는 보존**(Main.unity 미참조 GUID 확인 후 제거). 데모 prefab missing-script 경고는 무해 (commit e50c86da, 2026-06-09) |
| **전투 컨트롤러 책임 분리 리팩터링** | `PlayerCombatController` 비대화(1427줄) 완화 — 적 상태이상(넉백/슬로우/스턴)을 `PlayerStatusEffects`(순수 C#, 넉백 코루틴→`Tick(dt)` 타이머 환원+`Action<Vector2>` 변위 주입), 패리 스택 자원을 `ParryStackResource`(순수 C#, `[SerializeField]` 튜닝값은 컨트롤러 유지 후 Awake 생성자 주입=직렬화 경로 보존)로 추출. public API·UI 이벤트·동작 전부 보존(behavior-preserving), 컨트롤러는 facade 위임. 남은 추출 후보(MagazineReloader/Dagger/Aim)는 결합·코루틴 비용 대비 보류 (commit 5f95e23e, 2026-06-09) |
| **플레이어 폼 시스템** | `PlayerFormController` (MonoBehaviour) + `PlayerFormData` ScriptableObject + `PlayerFormId` enum(Normal/Sword/Dagger/Freischutz/Parry) — `ApplyForm(formData)` 가 Animator runtime controller·default Sprite 스왑, `ApplyFacing(direction)` 가 `useHorizontalFlipForFacing` 옵션으로 SpriteRenderer.flipX 갱신, dash 시 `rotateDashAnimationByDirection` 활성 폼은 visualTransform 을 `Atan2(dir)+dashBaseAngle` 로 회전하고 dash visual token 으로 movement/state 종료 시점에 안전 복귀. 첫 Sword form 프리팹/애니메이션(Idle/Walk/Attack/Spin/Dash/Death) 추가 (commit 3d82b687) |
| **스킬 애니메이션 SkillData 이관** | `SkillData` 에 Animation 섹션(`animationType`(None/Attack/Spin/Dash/CustomTrigger) · `customAnimationTrigger` · `rotateAnimationByDirection` · `animationBaseAngle`) 추가. `SkillExecutionContext.CasterForm` 으로 `PlayerFormController` 가 컨텍스트에 노출되고, `SkillExecutor.Execute` 가 실행 직전 `CasterForm.PlaySkillAnimation(skill, direction)` 호출 — 애니메이션 트리거가 SkillExecutor / WeaponData 가 아닌 SkillData 한 곳에서만 관리됨. Dash 는 `PlayerDashController` 가 SkillData 의 AnimationType=Dash 분기로 토큰 발급, 기본 공격은 `PlayerCombatController.basicAttackSkillData` (전용 `BasicAttackAnimation.asset`) 로 동일 경로 재사용. `SkillDataEditor` 에 Animation 섹션 인스펙터 추가 (commit c192604b) |
| **MP 폐지 + 스킬 자원 시스템** | MP/mpCost/MaxMp/RaisePlayerMpChanged 전면 제거. `SkillData` 에 `SkillResourceType`(None/Bullet/ParryStack) + `requiredAmount`/`consumeAmount` 추가. `PlayerCombatController` 가 `ISkillResourceLedger`(Has/Spend/GetAmount) 구현, `SkillSlotRuntime.CanUse(ledger)` 로 게이팅. `SkillExecutor.Execute` 가 `SkillExecutionResult`(Success+실제 ResourceConsumed) 반환 → 실제 소모만 Spend, 실패 시 소모·쿨다운 둘 다 없음. 밸런스는 쿨타임 + 폼 고유 자원 |
| **패리 폼** | `PlayerBasicAttackMode.Parry`. 기본공격 = 데미지 없는 패리(선딜→무적→후딜 각 설정값). 무적구간 동안 흰색 점멸, 방향 무관 모든 피해 1회 가로채기 → +ParryStack(`CompleteParryIntercept`). 첫 가로채기 즉시 무적 종료. 선딜 중 피격 시 패리 취소. ParryStack 은 자원(스킬 소모) + 유예시간 후 점감, 방/문/층 이동 시 초기화. 임시 `ParryStackBarUI` 슬라이더. **(2026-06-09 리팩터: 스택+grace/decay 로직을 `ParryStackResource` 순수 C# 클래스로 분리, 튜닝값은 컨트롤러 `[SerializeField]` 유지+생성자 주입 — 직렬화 경로·동작 보존)** |
| **마탄(Freischutz) 폼** | `PlayerBasicAttackMode.Bullet`. 탄창(Bullet) 자원 = `WeaponData`(usesMagazine/magazineSize/reloadTime/reloadAmount)에서 EquipWeapon 시 주입+풀충전, **방/층 이동 시 유지**. 기본공격 = 투사체 1발+탄 1소모(탄 0이면 자동 재장전). 스킬: `projectileCount`(발사)+`consumeAmount`(탄) 분리, `BulletShortageMode`(RequireFullCost/AllowPartialUse). 재장전: 자동(탄0)/수동(A키, 마탄폼만)/스킬(`reloadAmount`), 공격만 차단·이동 허용. `FreischutzMagazineUI`(Bullet/Bullet_empty 칸). 식별자 `PlayerFormId.Freischutz`(구 Bow) |
| **Form→loadout 단일 소스(안 B)** | `PlayerFormData.skills[]` 제거. `ApplyForm` 이 실제 폼 변경 시 `EquipWeapon(form.DefaultWeapon)` 호출 → 무기·스킬·탄창이 폼에 맞게 일괄 교체. loadout = WeaponData 단일 소스. ParryForm→ParryWeapon, FreischutzForm→FreischutzWeapon, Normal·Sword→TestSword |
| **대거(Dagger) 폼** | `PlayerFormId.Dagger`, basicAttackMode=Damage. 마커 기반 암살 루프 — Q=Blink(가장 가까운 적 뒤 순간이동+마커), W=Projectile(단검 투척+마커), E=Dash(마커 적 폭발+추가뎀+E 쿨 1회 리셋, 적 통과), R=Buff(N초간 기본공격 hit 적에 마커). DaggerForm/DaggerWeapon/DaggerQWER 에셋 + `SkillExecutionType.Blink` 신설 |
| **Dagger 마커 시스템** | `DaggerMarkerRegistry`(중앙 단일·비중첩 마커, 재부착 시 지속시간 갱신, 적 `OnDied` + `EnemyController.Initialize` 풀 재사용 양쪽 clear) + `SkillData` 마커 플래그(appliesDaggerMarker/detonatesDaggerMarker/markerDetonationDamage/resetCooldownOnMarkerDetonate/markerDuration). 부착 훅 = projectile·blink hit·Buff 중 평타 hit, 폭발 훅 = dash `OnEnemyHit`(데미지 적용 **직전** 호출 → 마커 적이 그 대시로 죽어도 폭발·쿨리셋 보장) |
| **Dagger 마커 비주얼** | `DaggerMarkerVisualPool`(Main.unity 루트 배치 + `RuntimeInitializeOnLoadMethod` 폴백, markerSprite 직접 연결/Resources fallback, 풀링) — 부착 적 위 `Test_Marker` 표시·`MarkerAnchorWorld` 추적, 폭발 시 burst |
| **EnemyController.MarkerAnchorWorld** | collider 중심(`TransformPoint(offset)`) 월드 앵커 — 마커/표식이 pivot(발밑) 대신 몸 중앙 기준에 표시 |
| **Freischutz·Dagger 폼 애니메이션** | 두 폼 전용 스프라이트시트(FreischutzForm.png 6×5 30프레임 / DaggerForm.png 6·5·6·5·6 28프레임)를 Idle/Walk/Attack/Dash/Death 5클립(@12fps)으로 슬라이스·구성하고 `Player_FreischutzForm`/`Player_DaggerForm` AnimatorController(SwordForm 구조 = MoveX/Y·LastMoveX/Y·IsMoving·IsDead·AttackTrigger·SpinTrigger·DashTrigger·DeathTrigger) 신설. `FreischutzForm.asset`(구 Player_Movement 공용 컨트롤러 placeholder 교체)·`DaggerForm.asset`(빈 필드 신규 연결)의 `animatorController`/`defaultSprite` 결선. 스킬은 기존 `animationType=Attack` → `AttackTrigger` 경로로 자동 연결(스킬 에셋 무수정). Dagger 는 `useHorizontalFlipForFacing`+`rotateDashAnimationByDirection` 유지 |
| **런타임 폼 전환 + Soul 보유 게이팅** | `PlayerFormDatabase`(formId→PlayerFormData SO 매핑) + `PlayerFormController.TrySwitchForm(PlayerFormId)` — DB 조회 → `IsFormOwned`(Normal 화이트리스트, inventory 미결선 시 안전 폴백, 그 외 `PlayerInventory.OwnsSoulForm`) → `CanSwitchNow()` 가드(dash/skill/dead/stun 중 거부) → `ApplyForm` 재사용. 반환 `FormSwitchResult`(Switched/AlreadyActive/NoDatabase/UnknownForm/NotOwned/Busy). `WeaponData.basicAttackSkillData` + `PlayerCombatController.ActiveBasicAttack`(무기 우선 fallback)로 폼별 평타 교체. `CombatEventChannel.OnLoadoutChanged`(EquipWeapon 발행)→`SkillUIManager.RefreshAllSlots` 구독으로 전환 시 스킬 UI 자동 갱신. 콘솔 `/form set <id>` 진입점(자동완성 2층, UI 3토큰 확장) |
| **Parry 폼 애니메이션** | `Parryform.png`(auto-slice 5행×6프레임: Idle/Walk/Parry정면/Parry측면/Death)을 5클립 + 전용 controller + `ParryForm.asset` 결선(defaultSprite=Parryform_0, useHorizontalFlipForFacing). **정면/측면 분기** = `PlayerFormController.ApplyParryFacing` — 조준 정지=정면(Int param `ParryFacing`=0, flipX=false) / 조준 방향 있음=측면(=1)+`ResolveFlipX`(순수 상하는 직전 flip 유지). 컨트롤러는 `AttackTrigger` 후 `ParryFacing` 으로 Parry_Front/Side 분기. SkillData 진입점(`PlaySkillAnimation`) 유지 |
| **Boss Area (1차)** | `BossEncounterTable`(SO, floor→boss/destination/isFinal) + `ArenaEncounterBase`(Elite·Boss 공통 lifecycle 추출) + `BossEncounterController`(:Base, Active 싱글톤, Begin→spawn→OnBossDied→`BossExitPortal`→`ProceedRequested`) + `DungeonManager.TryTransitionToFloor` 보스층 분기·`ProceedRequested`→floor+1→`CompleteProceedToNextFloor`(미니맵 복원). N층=Boss Area(일반 던전 N층 없음), 미니맵 진입전환=destination `minimapLocationId` 자동(코드훅 없음), **destination `locationType=Dungeon` 필수**(퇴장 `IsInDungeon` 가드). 사망=기존 GameOver. placeholder boss=Elite_Magma_01·shared tilemap. **통합 흐름 Play 검증 전부 통과(20/40/60·60층 엔딩 정지·보스전 사망·Elite 회귀, 2026-06-09) — 코드·흐름 완성, 남은 건 컨텐츠.** 상세 §11e |

### 미구현 (다음 단계)

| 항목 | 우선순위 | 비고 |
|------|----------|------|
| AreaOverTime 스킬 핸들러 | 중간 | SkillExecutionType enum 자리 마련, SkillExecutor에 분기만 추가하면 됨 (Blink 는 구현 완료) |
| 범용 Buff 스킬 핸들러 | 낮음 | 현재 `ExecuteBuff` 는 Dagger R 마커 버프(`appliesDaggerMarker` 분기)만 처리 — 능력치 강화·실드 등 범용 버프는 미구현 |
| 폼 전환 게임플레이 진입점 | 중간 | `TrySwitchForm` + `/form set` + Soul 보유 게이팅 + **Soul 강화(SoulStatType/PlayerSoulEnhancements/SoulEnhancementTable/SoulStatBonus, `/enhance`)** 구현 완료. 남은 범위는 인게임 해금 UX(보상/드랍으로 Soul 지급)·Form 선택 UI |
| 드롭/경제 루프 | 중간 | 데이터 기반 드롭 1차(`EnemyDropEntry`/`EnemyData.drops`/`EnemyDropRoller`, 결정적 롤) 구현. 남은: 스폰 통합(SpawnRoom→엘리트/보스), 소울 분해(중복→폼별 재료), Material 영구보존, Town Soul Altar(누진비용) |
| 신규 시스템 스탯 | 낮음 | Soul 강화의 Crit/Lifesteal/ComboDamage/AilmentDamage 는 enum·집계만, 적용 훅 미구현(각 신규 전투 메커니즘 필요) |
| 아이템 장착·고유 효과 확장 | 중간 | Consumable `HealHp` 사용과 Relic 평면 스탯 패시브는 구현 완료. 남은 범위는 Equipment 장착/해제, Currency 소비처, 행동형 Relic 특수 효과(처치 시 회복·대시 불길 등) |
| Boss Area 정식화 | 중간 | 1차 구현 + **통합 흐름 검증 전부 통과(2026-06-09, §11e)** — 코드·흐름 완성. 남은 건 **컨텐츠뿐**: 보스별 전용맵(현재 elite tilemap 공유) / 정식 보스 EnemyData·수치(현재 placeholder Elite_Magma_01) / 60층 엔딩 연출(현재 Debug.Log stub) / 처치 보상 연계 / 마을 메타루프 |
| 보스 / 에픽 적 패턴 | 중간 | EnemyBrain 상속 + Phase2/Berserk 상태 enum 자리 마련됨. Boss Area(§11e) 는 인프라 완성 — 보스별 고유 패턴 SO 작성만 남음 |
| Elite Arena 보상 컨텐츠 | 중간 | Arena 내 Elite 처치 후 보상(아이템 드랍·특수 패시브 등) 미구현 |
| 적 스킬 발사기 통합 | 낮음 | ProjectileFireService를 적 EnemyBrain 액션 핸들러에서도 직접 호출하도록 통합 |
| 상태이상 시스템 확장 | 낮음 | 독, 빙결 등 StatusEffectData 추가 |
| 세이브 / 로드 | 낮음 | Seed 기반 재현으로 부분 대체 가능 |
| 보스 룸 | 낮음 | RoomType.Boss 추가 후 RoomRegistry 확장. Boss Arena는 WalkabilityArea 컴포넌트 부착만으로 지원 가능 |
| AreaOverTime / Buff Elite 패턴 | 낮음 | ElitePatternData 추가 변형 자리 — 예: 광역 장판, 자기 강화 |
| MonsterDen 방 타입 등록 | 낮음 | RoomRegistry에서 자동 분류 조건 추가 필요 |
| SkillData dash 툴팁 정정 | 낮음 | `dashDamageOnPath`/`OnContact` 인스펙터 툴팁이 아직 "first-pass implementation shares the same detection path"로 남아 있음 — 실제 구현은 분리됨 |

---

*본 문서는 현재 master 브랜치 기준이며, 개발 진행에 따라 갱신됩니다.*
