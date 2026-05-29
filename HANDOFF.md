# HANDOFF — JBRogLike 인수인계 문서

> 작성 기준일: 2026-05-29
> 기준 커밋: `b6f88bbf` (master) — "normalForm 추가"
> 엔진: Unity 2D (Tilemap) / 언어: C# (.NET)

이 문서 하나로 프로젝트 이해 → 구현 이어가기가 가능하도록 정리했다.
깊은 시스템 설명은 [README.md](README.md)(2300+줄, 최신)에 있으며, 이 문서는 **현재 상태·다음 작업·반드시 지켜야 할 규칙**에 집중한다.

---

## 1. 프로젝트 정체성

- **장르**: Unity 2D 실시간 로그라이트 (탑다운).
- **공간 구조**: 절차 생성 던전 + 고정 지역(Town / Elite Arena / 향후 Boss Arena) 병행.
- **핵심 루프**: 던전 → 전투 → 방 클리어 → Elite/Boss/고정지역 → 아이템·영혼·Form·스킬 성장.
- **성장 축 = Form 시스템**: Form별 prefab 복제 **금지**. 단일 Player GameObject + `PlayerFormData` ScriptableObject 로 sprite / AnimatorController / loadout 을 데이터 기반 전환.
- **아이템 4분류**:
  - 영혼(soul) — 영구 전투 스타일
  - 영혼각인 — Elite drop, skill variant unlock
  - 유물(relic/rune) — tradeoff modifier
  - 강화재료

---

## 2. 아키텍처 핵심 시스템 (요약)

자세한 흐름·다이어그램은 README 해당 절 참고. 여기선 "어디를 봐야 하는가" 위주.

| 시스템 | 핵심 클래스 | 요점 |
|---|---|---|
| **위치/전환** | `LocationTransitionManager`(구 TownDungeonTransitionManager), `TeleportService`, `TeleportDestinationDatabase` | 마을↔던전↔Arena 전환 조율. destination 에 `minimapLocationId` 보유 |
| **환경 질의** | `WorldEnvironmentQuery` (공통 API) → `WalkabilityQuery` → `WalkabilityArea` 우선, 없으면 `DungeonData` fallback | **호출부는 Dungeon/Arena 분기 금지.** IsWalkable / IsFootprintWalkable / HasLineOfSight / TryFindNearestWalkable |
| **플레이어 폼** | `PlayerFormController` ([Assets/Scripts/PlayerFormController.cs](Assets/Scripts/PlayerFormController.cs)) | `currentForm`(serialized) + `defaultForm` fallback. `ApplyForm()` / `SetCurrentForm()`. `PlaySkillAnimation()` 진입점 |
| **스킬 애니메이션** | `SkillData` + `SkillAnimationType`(None/Attack/Spin/Dash/CustomTrigger) | `SkillExecutor` 가 실행 성공 후 `PlayerFormController.PlaySkillAnimation()` 호출. **기본 공격도 `basicAttackSkillData` 경유** |
| **Dash visual** | token lifecycle (`BeginDashVisualToken`/`CompleteDashAnimationVisual`/`ResetDashVisualRotation`) | Player root 회전 금지, sprite child `visualTransform` 만 Z 회전 |
| **전투 판정** | `AttackExecutor`, `SkillTargetResolver` | world target point 기반(grid 의존 제거). LOS 는 `WorldEnvironmentQuery.HasLineOfSight()` |
| **적** | `EnemyData` + Controller/Brain/Movement/Target/Action/Animation Handler 분리 | Contact(Rush/Jump 특수) / Ranged(Single/Burst/Spread/Circle) |
| **Elite Arena** | `EliteArenaEncounterController`, `EliteArenaPortal`, `EliteArenaReturnPortal` | 입장→elite spawn→처치→Return Portal→원래 방 복귀. **Boss Arena 의 템플릿** |
| **던전 생성** | `DungeonGenerator` (MST + pair-based EXTRA corridor scoring, deterministic) | repro seed 예: `283321776792` floor 3/44/65 |
| **개발자 콘솔** | `DeveloperConsoleService` + executor | `` ` `` 토글. `/floor add|sub|set`, `/dooropen [normal|elite]`, `/kill`, `/tp` |

---

## 3. 절대 규칙 (위반 시 즉시 reject — 과거 실제 incident 기반)

1. **Player root transform 회전 금지.** 회전은 sprite child `visualTransform` Z 회전만.
2. **Dash visual reset 은 token lifecycle 로만.** Animator state 관측 기반 reset 금지 (대각선/연타 버그 재발).
3. **스킬 애니메이션은 `SkillData` 에서 drive.** 코드에서 `TriggerAttackAnimation`/`TriggerDashAnimation` 직접 호출 신규 추가 금지.
4. **`SkillData` 필드 추가 시 `SkillDataEditor` 도 같이 수정** (custom inspector 라 안 그러면 인스펙터에 안 보임).
5. **고정 area 대응은 `WorldEnvironmentQuery` 우선.** `DungeonManager`/`DungeonData` 직접 접근 늘리지 말 것.
6. **Elite Arena 복귀는 일반 teleport 흐름 아님** — minimap/fog/location context reset 누락 시 버그.
7. **`.meta` GUID 보존** — scene/prefab serialized reference 깨짐 방지.
8. **Sprite sheet 규격**: canvas `1536x256`, 6 frames × `256x256` cell, solid `#FF00FF` 배경, cell 경계 엄격. 사용자 매우 민감.
9. **런타임 금지 패턴**: `FindAnyObjectByType`/`GameObject.Find` 신규 추가 금지, 런타임 `AddComponent` fallback 남발 금지, per-frame LINQ/allocation 금지.
10. **변경 범위 밖 scene/layout/UserSettings diff 건드리지 말 것.** `UserSettings/Layouts/default-6000.dwlt` 는 trailing whitespace 로 `git diff --check` 실패 이력 있음.
11. **커밋 메시지 한국어 선호. caveman mode 로 작업** (AGENTS.md).

### 커밋 전 검증 표준
```
dotnet build Assembly-CSharp.csproj /p:UseSharedCompilation=false
dotnet build Assembly-CSharp-Editor.csproj /p:UseSharedCompilation=false
git diff --check
```
+ 금지 API grep + Play Mode 확인.

---

## 4. 완료된 작업 (최근)

- **플레이어 폼 시스템 기반** — `PlayerFormController` + `PlayerFormData` + `PlayerFormId`. `ApplyForm` 이 AnimatorController·default Sprite 스왑, facing flipX, dash visual rotation/token 처리.
- **스킬 애니메이션 SkillData 이관** — `SkillData` 에 Animation 섹션 추가, `SkillExecutor`/`PlayerDashController`/`PlayerCombatController(basicAttackSkillData)` 모두 `PlaySkillAnimation` 단일 경로 사용. `SkillDataEditor` 인스펙터 추가.
- **Sword Form / Normal Form 자산** — 컨트롤러: `Player_SwordForm.controller`, `Player_Movement.controller`(Normal). NormalForm 을 scene 의 default/current form 으로 연결.
- **Spin 애니메이션** — `TestSkill4` 에 `animationType=Spin` 연결, 정상 작동 확인.
- **`PlayerFormId` enum 재정리 (2026-05-29)** — 아래 §5 참고.

---

## 5. PlayerFormId 현재 정의 (2026-05-29 재정리)

```csharp
public enum PlayerFormId
{
    Normal = 0,   // 기본 폼 = Slime 형태 (구 Normal=4, Slime=1 통합)
    Sword  = 1,   // 구 Sword=0
    Dagger = 2,   // 가칭
    Bow    = 3,   // 가칭
    Parry  = 4    // 신규 — 패리 컨셉 폼 (자산/구현 예정)
}
```

- **주의**: `formId` 는 `.asset` 에 int 로 직렬화됨. enum 값 변경 시 기존 에셋 int 도 같이 고쳐야 매핑이 안 깨진다. (이번에 `NormalForm.asset 4→0`, `SwordForm.asset 0→1` 동기화 완료.)
- 코드 어디서도 `PlayerFormId` 를 특정 값으로 분기/비교하지 않음 → enum 재정렬은 로직 영향 없음 (직렬화 에셋만 주의).
- `Slime` enum 값은 제거됨. 기본/Slime 형태 = `Normal`.

---

## 6. ⚠️ Form 시스템 — 미구현 핵심 갭 (다음 구현자 필독)

겉보기엔 폼 시스템이 동작하지만, **런타임 전환과 loadout 연결이 비어 있다.** "weapon/skill 배치 완료"는 에셋 필드에 값만 넣은 상태이고, 그 값을 소비하는 로직이 없다.

### 6-1. 런타임 폼 전환이 존재하지 않음
- `ApplyForm()`/`SetCurrentForm()` 은 **`Awake` 에서 단 한 번만** 호출됨.
- 입력·콘솔·아이템 어디서도 전환 트리거가 없다. 폼은 사실상 인스펙터 고정값.
- → 폼을 바꾸는 진입점(아이템 획득 / 콘솔 명령 / 소울 등)을 설계·구현해야 함.

### 6-2. Form ↔ loadout 미연결
- 실제 스킬 슬롯 source = **`WeaponData.skills[4]`** ([PlayerCombatController.cs:813](Assets/Scripts/Combat/PlayerCombatController.cs#L813), `BindSkillSlots(currentWeapon)`).
- 그런데 `PlayerFormData.DefaultWeapon` / `PlayerFormData.skills[]` 는 **코드 어디서도 안 읽힘.** 폼을 바꿔도 무기/스킬이 안 바뀐다.
- `PlayerCombatController.EquipWeapon()` 도 정의만 있고 호출처가 없다.
- `PlayerFormData.skills[]` 는 `WeaponData.skills[4]` 와 **중복 필드** → 정리 대상.

### 6-3. 구현 시 결정해야 할 설계
1. **Form ↔ Weapon source of truth**: 폼 전환 시 `EquipWeapon(form.DefaultWeapon)` 호출하는 구조로 갈지. 그러면 `PlayerFormData.skills[]` 는 제거하고 `WeaponData.skills[4]` 로 일원화.
2. **전환 트리거**: 누가/어떻게 폼을 바꾸나 (소울 아이템 기반이 기획 방향). 전환 시 `EquipWeapon` + `skillUIManager.RefreshAllSlots()` 호출 필요.
3. **컴파일/에셋 정합성**: 폼 추가는 prefab 이 아니라 `Create > JBRogLike > Player > Form` 으로 `PlayerFormData` 에셋 생성 (README "새 플레이어 폼 추가" 절).

> **현재 상태**: 사용자가 Slime(기본)/Sword/Dagger/Bow/Parry 폼의 **아트(sprite+animator) 자산을 제작 중**. 자산 입고 후 폼별 `PlayerFormData`+`WeaponData` 생성 및 위 6-1/6-2 시스템 구현 예정.

---

## 7. 남은 작업 (우선순위)

1. **나머지 Form 구현** (진행 예정, 아트 입고 대기 중)
   - 아트(sprite/animator) 입고 → Slime/Dagger/Bow/Parry 의 `PlayerFormData`+`WeaponData` 생성
   - §6 의 런타임 폼 전환 + loadout 연결 시스템 구현
   - Parry 폼은 "패리 컨셉" — 별도 메커니즘 기획 필요
2. **Boss Arena / 고정 전투지역** (기획 보류)
   - `EliteArenaEncounterController` 가 그대로 템플릿. `WalkabilityArea(id="boss_arena")` + teleport destination + encounter controller 일반화.
   - 결정 필요: 진입 방식(보스방 portal vs floor 계단 대체), 처치 후 흐름(다음 층/run 종료/보상). 컨트롤러 제네릭화 vs 복제.
3. **Ranged enemy movement type** (Random/Chase/Kiting) — 뭉침 문제 해소.
4. **Soul / 영혼각인 / 유물 시스템 상세 설계** — 기획 단계. Form 전환의 트리거가 여기서 나올 가능성 높음.

### 결정된 사항
- **Town 에서 skill 미사용 유지** (Town 스킬 미동작은 의도. 추가 작업 없음).

---

## 8. 주요 파일 위치 빠른 참조

| 영역 | 경로 |
|---|---|
| 폼 데이터/식별자 | [Assets/Scripts/Data/PlayerFormData.cs](Assets/Scripts/Data/PlayerFormData.cs), [Assets/Scripts/Data/PlayerFormId.cs](Assets/Scripts/Data/PlayerFormId.cs) |
| 폼 컨트롤러 | [Assets/Scripts/PlayerFormController.cs](Assets/Scripts/PlayerFormController.cs) |
| 폼 에셋 | [Assets/Perfabs/Scriptable/PlayerForms/](Assets/Perfabs/Scriptable/PlayerForms/) (SwordForm, NormalForm) |
| 무기/스킬 데이터 | [Assets/Scripts/Data/WeaponData.cs](Assets/Scripts/Data/WeaponData.cs), [Assets/Scripts/Data/SkillData.cs](Assets/Scripts/Data/SkillData.cs) |
| 전투 컨트롤러 | [Assets/Scripts/Combat/PlayerCombatController.cs](Assets/Scripts/Combat/PlayerCombatController.cs) |
| 환경 질의 | [Assets/Scripts/Combat/WorldEnvironmentQuery.cs](Assets/Scripts/Combat/WorldEnvironmentQuery.cs), [Assets/Scripts/World/WalkabilityArea.cs](Assets/Scripts/World/WalkabilityArea.cs) |
| Elite Arena | [Assets/Scripts/EliteArena/EliteArenaEncounterController.cs](Assets/Scripts/EliteArena/EliteArenaEncounterController.cs) |
| 폼 애니 컨트롤러 | `Assets/Animation/Player/Player_Movement.controller`(Normal), `Assets/Animation/Player/SwordForm/Player_SwordForm.controller` |
| 메인 씬 | [Assets/Scenes/Main.unity](Assets/Scenes/Main.unity) |
| 종합 아키텍처 | [README.md](README.md) |

---

## 9. 다음 작업자 체크리스트

- [ ] 아트 자산(sprite/animator) 입고 확인
- [ ] 폼별 `PlayerFormData` + `WeaponData`(skills[4]) 생성
- [ ] 런타임 폼 전환 진입점 구현 (§6-1)
- [ ] 폼 전환 → `EquipWeapon` + UI refresh 연결 (§6-2)
- [ ] `PlayerFormData.skills[]` 중복 필드 정리 결정 (§6-3)
- [ ] Parry 폼 메커니즘 기획
- [ ] 커밋 전 검증 표준(§3) 통과
