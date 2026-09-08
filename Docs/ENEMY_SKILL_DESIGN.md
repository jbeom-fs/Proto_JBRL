# 적 스킬 데이터 구조 설계

> 작성 기준일: 2026-09-04
> 기준 커밋: master HEAD `39549816`
> 범위: `Assets/Scripts/Enemy/Skill/` — 적 패턴을 데이터 주도로 저작하는 구조와 그 설계 근거
> 상태: Jump · Dash 이관 완료 / **Projectile 미착수(§6)** / InstantArea 미구현

---

## 1. 왜 이 구조인가

개편 전에는 패턴 종류마다 **C# 클래스 2개(Data + Runtime)** 가 필요했다.

```
Patterns/
  EnemyJumpPatternData.cs        + EnemyJumpPatternRuntime.cs
  EnemyDashPatternData.cs        + EnemyDashPatternRuntime.cs
  EnemyProjectilePatternData.cs  + EnemyProjectilePatternRuntime.cs
```

새 패턴을 만들려면 저작자가 코드를 건드려야 했다. 이를 **데이터 1종 + 실행타입 분기**로 바꿔 **새 패턴 = 에셋 1개 생성**이 되게 했다.

착수 근거는 플레이어 쪽이 이미 그렇게 하고 있다는 것이었다 — `SkillExecutionType` 에 `Dash` / `Blink` 가 있어 다중 프레임 이동도 선언적으로 표현되고 있었다.

---

## 2. 구조

```
Assets/Scripts/Enemy/Skill/
  EnemySkillExecutionType.cs   InstantArea / Projectile / Dash / Jump   ← 적 전용 enum
  PatternShapeData.cs          patternType / coneHalfAngle / customCells  ← 거리 없는 "모양" 타입
  EnemySkillData.cs            : EnemyPatternData
  EnemySkillRuntime.cs         : EnemyPatternRuntime   (executionType 분기)
```

### 병존이 성립하는 조건

```
EnemyPatternSet.patterns : List<EnemyPatternData>
EnemyPatternRunner / EnemyBrain : 패턴 타입별 분기 0건
```

새 타입이 `EnemyPatternData` 를 상속하기만 하면 **신구 타입이 한 리스트에 섞여 돈다.** 그래서 개편 과정에서 `EnemyPatternRunner` / `EnemyBrain` / `EnemyPatternSet` / `EnemyPatternContext` **수정이 0**이었고, 옛 패턴과 나란히 돌리며 비교 검증하는 점진 이관이 가능했다.

이 성질은 **의도적으로 유지해야 한다.** 러너에 실행타입 분기를 넣는 순간 이관 안전망이 사라진다.

### 필드 구성

| 그룹 | 필드 |
|---|---|
| 베이스 상속 (`EnemyPatternData`) | `displayName` / `cooldown` / `minRange` / **`maxRange`** / `weight` / `recoveryDuration`(후딜) |
| 실행 | `executionType` |
| 타이밍 | `castDelay`(선딜) |
| 범위 | `searchShape`(PatternShapeData) — **거리는 `maxRange`** |
| 피해 | `damageShape`(PatternShapeData) + `damageRange`(int) + `damage` |
| 이동 | `moveSpeed` / `jumpVisualHeight` / `stayInRoom` / `lockFacingDuringExecute` / `stopOnWall` |
| 애니메이션 | `castAnimation` + `castAnimationTrigger` / `executeAnimation` + `executeAnimationTrigger` |

---

## 3. 확정된 설계 결정

### 3-1. 적 전용 타입 · 적 전용 실행타입 enum

플레이어 `SkillData`(298줄)의 절반이 플레이어 전용 축이다 — `owningForm` / `grade` / `resourceType` / `recastStages` / `buffs`. 공유하면 적 저작 화면의 절반이 무의미한 칸이 된다.

실행타입 enum도 공유하지 않는다. 플레이어엔 `Jump` 가 없고, 적이 안 쓸 `Buff` / `Blink` / `AreaOverTime` 이 있다.

> **공유 경계** — `AttackPattern` 셀 열거 함수와 `CustomShapeMatcher` 까지. 그 위(데이터·실행기)는 분리한다.

### 3-2. 애니메이션 = enum + 문자열 override

`EnemyAnimationKey` 는 8값 고정이라, 보스 고유 모션 하나를 추가하려면 **enum + 상수 해시 + `_has*` 필드 + `CacheAnimatorParameters` if + `PlayPatternAnimation` case** 다섯 군데를 코드로 고쳐야 했다.

`_has*` bool 13개를 `HashSet<int>` 로 일반화하고 문자열 오버로드를 열었다.

```csharp
PlayPatternAnimation(EnemyAnimationKey key, string customTrigger, Vector3 targetPosition)
```

- 강등 안전망이 이미 있었다 — `SetTriggerOrAttack` 은 파라미터가 없으면 **일반 Attack 으로 대체**한다. 문자열 트리거도 같은 경로를 타므로 스프라이트가 빈약한 적에게 스킬을 줘도 깨지지 않는다
- 🔴 커스텀 경로는 `key == None` 검사를 타면 안 된다. 문자열만 채우고 `key` 는 `None` 으로 두는 것이 정상 사용법이다
- 🔴 커스텀 경로에서 `FacePosition` 을 부르지 않는다. 패턴 런타임이 `LockSpecialFacing` 으로 방향을 명시 제어하므로 서로 싸운다
- 두 애니메이션 체계(`SkillAnimationType` / `EnemyAnimationKey`)는 **통합하지 않는다.** Animator Controller 와 스프라이트가 애초에 다르다

### 3-3. facing 양자화를 하지 않는다 (자유 각도)

4방향 / 8방향 스냅을 검토했다가 폐기했다.

```
① facing   = (플레이어 − self).normalized                     ← 연속값, 스냅 없음
② offsets  = AttackPattern.FillTargets(shape, (0,0), Vector2Int.up, N, cone, buf, cells)
                                              ↑ Vector2Int 오버로드 · 로컬 전방 고정 (정수 연산)
③ world_i  = self + rot(angle) × (offset × cellSize)          ← 회전은 여기서 한 번만
④ filter   = IsFootprintWalkable (+ 던전이면 stayInRoom)
⑤ pick     = 플레이어에 가장 가까운 후보 (argmin)
⑥ 실패     = Start() 가 false → 러너가 쿨다운 없이 같은 프레임 재추첨
```

🔴 **`FillTargets` 의 `Vector2 facing` 오버로드를 쓰면 안 된다.** 내부 `RoundToCell` 때문에 45°에서 서로 다른 셀이 같은 칸으로 뭉치고 `AddUnique` 가 버린다(`Line range 3` 이 2칸이 된다). **정수 격자를 보존하는 회전은 90° 배수뿐**이라 어떤 구현으로도 피할 수 없다.

플레이어 스킬의 `Custom` 경로(`CustomShapeMatcher`)가 이미 같은 방식으로 자유 각도를 처리한다.

**스프라이트 방향과는 분리돼 있다.** 적 방향 표현은 `spriteRenderer.flipX` 좌/우뿐이고 `LockFacing` 이 `direction.x` 부호만 쓴다. `Assets/Scripts/Enemy/` 에 transform 회전 코드는 0건이다. 즉 **계산은 대각으로 정밀하게, 그림은 좌/우로** 가 성립한다.

> 🔴 **transform 회전은 어떤 경우에도 쓰지 않는다.** 4방향 모션이 필요해지면 Animator 블렌드 파라미터(`MoveX`/`MoveY`)나 별도 클립으로 푼다.

### 3-4. 거리는 `maxRange` 하나

초기 구현은 `maxRange`(선택 조건)와 `searchRange.patternRange`(이동 거리)를 따로 뒀다가, 동기화 부담과 "안 닿는 접근 점프"를 만들어 통합했다.

```
탐색 반경 = Mathf.RoundToInt(MaxRange / cellSize)
피해 범위 = damageRange (int)
```

`PatternRangeData` 에서 거리를 빼 **`PatternShapeData`(순수 모양)** 로 만들고, 거리는 쓰는 쪽이 제공한다.

facing 이 플레이어를 향하도록 격자를 회전시키므로 플레이어는 항상 로컬 `+Y` 위에 있고, `Circle`/`Line` 의 전방 최대 오프셋 `(0, N)` 이 플레이어 쪽 최대 도달 지점이 된다.

부수 효과로 옛 링 검색의 **사거리 경계 탈락이 소멸**했다. 후보 필터에 거리 검사가 없다.

#### 🔴 `maxRange` 는 상한 보장이 아니라 근사다 (이동 스킬 축 · 미결)

후보 필터에 거리 검사가 없다는 것은 곧 **`maxRange` 를 넘는 착지·돌진이 가능하다**는 뜻이다. 세 경로가 있다.

| # | 경로 | 예 |
|---|---|---|
| ① | `RoundToInt` 반올림 | `maxRange 2.6`, cellSize 1 → 탐색 반경 **3** → 전방 후보 `(0,3)` = 3유닛 |
| ② | `Circle` 대각 후보 | 전방 후보가 장애물로 탈락하면 argmin 이 대각을 고른다 → 최대 **`N√2`** |
| ③ | `Custom` | `AddCustomTargets` 는 `range` 인자를 **아예 쓰지 않는다.** 칠한 셀이 곧 거리다 |

②·③ 모두 **self 로부터의 이동 거리가 `maxRange` 를 넘을 수 있다.**

🔴 argmin 이 "플레이어에 가장 가까운 후보"를 고른다는 것이 **플레이어를 지나치지 않는다는 보장은 아니다.** 앞쪽 후보가 장애물로 전부 탈락하면 **플레이어 뒤쪽 후보가 최근접이 되어 선택된다.** 예: 플레이어가 5칸 거리인데 4칸 후보가 막히고 6칸 후보가 살아 있으면 6칸이 뽑힌다.

**결정 필요** — 둘 중 하나를 택해야 한다.

- **(가) 근사로 둔다** — 현재 동작. 저작 시 `maxRange` 를 정수로 잡고 대각 초과를 감수한다. 이 문서와 저작 가이드의 표현을 "최대 이동 거리의 **근사값**"으로 통일한다
- **(나) 상한을 보장한다** — 후보 필터에 `(world_i − self).sqrMagnitude <= MaxRange²` 를 추가한다.
  🔴 이 검사는 옛 링 검색에서 **실패 사유 ②(사거리 경계 탈락)** 를 만들던 바로 그 필터다. 경계에 정확히 놓인 후보가 부동소수 오차로 탈락하는 문제가 함께 돌아온다.
  🔴 **옛 대시의 `+ max(1, radius)` 방식을 그대로 쓰면 안 된다.** 그건 오차 보정이 아니라 **사거리를 1칸 이상 늘리는 것**이라 "상한 보장"이라는 목적과 모순된다. 상한이 목적이면 `MaxRange² + ε`(ε = 부동소수 허용오차 수준) 만 허용해야 한다.
  ⚠️ **거리 필터만으로 상한은 보장된다.** `RoundToInt` 가 `maxRange 2.6` 에서 후보를 `(0,3)` 까지 만들어도 필터가 그 후보를 제거하므로 실제 도달은 2칸에 그친다.
  ⚠️ 다만 그 결과 **경계값까지 정확히 도달하지는 못한다**(2.6 을 저작해도 실제 최대는 2). 이건 상한 보장과 별개의 "경계 도달 정밀도" 문제이며, 후보가 셀 격자 위에만 생기는 한 남는다.
  🔴 **`FloorToInt` 는 이 정밀도를 개선하지 않는다.** `FloorToInt(2.6) = 2` 라 도달 최대는 여전히 2다. `RoundToInt` ↔ `FloorToInt` 는 **후보를 어디까지 생성할지 정하는 선택지**일 뿐이며, 거리 필터를 함께 쓰면 최종 결과가 같아진다(필터 없이 상한을 근사하고 싶을 때만 의미가 있다). 정밀도가 실제로 필요해지면 **`maxRange` 를 셀 단위로 저작하도록 강제**하는 편이 단순하다

현재 저작값(`maxRange 12` / `15`)은 정수라 ①은 발생하지 않으며, ②·③이 실측에서 문제를 일으킨 적은 아직 없다. 다만 **문서가 "정확히 `maxRange`"라고 단정하던 것은 오류였다.**

### 3-5. 피해는 셀 + `OverlapBox`

`damageShape` 셀을 `CustomShapeMatcher` 로 월드 배치하고 셀마다 판정한다.

```csharp
Physics2D.OverlapBox(cellCenter, cellSize, matcher.AngleDeg, CombatLayers.PlayerFilter, s_HitBuffer)
```

대상당 1회는 `HashSet<IDamageable>` 가 보장한다.

🔴 **원점 포함 규칙** — `AttackPattern` 내장 타입은 원점을 구조적으로 제외한다(`Circle` 은 `dx != 0 || dy != 0`, `Line`/`Cone` 은 `i = 1` 부터). 착지 후보 열거에는 옳지만(제자리 점프 차단) 피해 범위에서는 **착지한 그 칸의 플레이어가 안 맞는다.**

| 타입 | 처리 |
|---|---|
| 내장 (`Circle`/`Line`/`Cone`/…) | 런타임에서 `(0,0)` **추가** |
| `Custom` | 저작한 그대로 (중앙이 빈 링 저작을 막지 않기 위해) |

🔴 `AttackPattern.cs` 를 고쳐 해결하면 **플레이어 스킬이 함께 바뀐다.** 보정은 `EnemySkillRuntime` 안에서만 한다.

### 3-6. 이동 중 물리 처리

| 문제 | 처리 |
|---|---|
| 물리 되밀림 | `EnemyController.SetFlightModeEnabled(true)` → 이동 동안 `Rigidbody2D` 를 **Kinematic** 으로 |
| 발판 가드 | `SetWalkGuardSuppressed(true)` |
| 무한 이동 | 타임아웃 `(거리/속도) × 2 + 0.25` 초과 시 강제 착지 |
| 벽 정지(Dash) | `Physics2D.OverlapCircle(next, CollisionFootprintRadius, CombatLayers.WallMask)` — **현재 위치에서** 정지 |

🔴 **`isTrigger` 토글은 채택하지 않았다.** `CombatLayers` 의 ContactFilter2D 가 `useTriggers = false` 라 트리거로 만들면 **이동 내내 피격 불가**가 된다. Kinematic 은 콜라이더가 논트리거로 남아 피격이 유지된다.

Kinematic 은 Static(벽 타일맵)과 접촉하지 않아 비행 중 벽 통과가 함께 해결되고, Dynamic 인 플레이어는 밀어낸다(의도).

🔴 **복원 지점이 넷이다** — `CompleteMove` / `Cleanup` / `EnemyController` 초기화 / **풀 재사용**. `ApplyStationaryPhysicsSettings()` 가 `bodyType` 을 건드리지 않아 자동 복구되지 않는다. 빠지면 재사용된 적이 영구 Kinematic(유령)이 된다.

🔴 **벽 정지는 `CompleteMove(false)`** 로 현재 위치에서 멈춘다. `true` 로 두면 `_targetPosition` 으로 텔레포트해 벽을 뚫는다.

> **원칙** — 전역 footprint 판정(`IsFootprintWalkable`)은 코너 샘플이 `cellSize × 0.5 − 0.01 = 0.49` 로 clamp 돼 있어 실제 반경을 반영하지 않는다. 이는 대형 적이 벽 근처에 못 서는 회귀를 막으려고 의도적으로 넣은 것이다. **정확한 덩치가 필요한 곳에만 물리 쿼리를 국소 추가한다.**

#### 🔴 이동 구간을 검사하지 않는다 — 터널링 (이동 스킬 축 · 미결)

현재 `TickMove` 는 **점 단위**로만 검사한다.

```
next = current + dir × (moveSpeed × deltaTime)
  ├ 벽 검사   : next 한 점만            (Dash)
  ├ 위치 대입 : transform.position = next
  └ 피해 검사 : 대입 후 위치 한 점만    (Dash)
```

`current → next` **사이 구간은 아무도 보지 않는다.** `deltaTime` 은 `EnemyBrain.Update` 가 넘기는 `Time.deltaTime` 이라 프레임 히칭에 그대로 노출된다.

| 상황 | 스텝 (moveSpeed 15 기준) |
|---|---|
| 60fps | 0.25 유닛 |
| 30fps | 0.5 유닛 |
| 히칭 (Unity `maximumDeltaTime` 기본 0.333) | **약 5 유닛 = 5셀** |

즉 프레임이 크게 튀면 **중간의 벽이나 플레이어를 통째로 건너뛸 수 있다.** `HashSet<IDamageable>` 은 중복 타격만 막을 뿐 누락은 막지 못한다.

**대응 후보**

- **(가) 스텝 분할** — 한 프레임 이동량이 `cellSize`(또는 `damageRange` 반폭)를 넘으면 여러 번으로 쪼개 같은 검사를 반복한다. 국소적이고, 히칭이 없는 평상시엔 분할이 일어나지 않아 비용이 0이다.
  🔴 **완전한 보장은 아니다.** 샘플 간격이 `cellSize` 이므로 그보다 얇은 벽이나 작은 콜라이더는 여전히 두 샘플 사이로 빠져나갈 수 있다. 누락 **확률을 크게 낮추는 완화책**으로 이해해야 한다
- **(나) 구간 검사** — `current → next` **구간 전체를 판정**한다. 샘플 간격에 의존하지 않으므로 벽 두께와 무관하게 누락을 막을 수 있다. 적 footprint 가 원형이므로 `Physics2D.CircleCast(current → next, CollisionFootprintRadius, WallMask)` 가 **권장 구현**이다. 다만 피해 쪽은 구간을 덮는 셀 집합을 만들어야 해서 셀 기반 피해 모양과 잘 맞지 않는다
- **(다) 이동량 상한** — `step` 을 `cellSize` 로 clamp. 가장 싸지만 히칭 시 적이 느려진다(이동 거리 손실)

**벽 누락을 확실히 막아야 하면 (나), 완화로 충분하면 (가).** 실측에서 통과 사례가 관측되지 않았으므로 우선순위는 낮지만, 벽이 많은 좁은 보스방을 저작하기 전에는 판단해야 한다. 절충안으로 **벽은 (나)의 구간 검사, 피해는 (가)의 스텝 분할**을 각각 쓰는 것도 가능하다.


---

## 4. 실행타입별 사용 필드

| 필드 | Jump | Dash | Projectile | InstantArea |
|---|:---:|:---:|:---:|:---:|
| `castDelay` / `recoveryDuration` | ✅ | ✅ | ✅ | ✅ |
| `maxRange` (선택 + 거리 **근사**, §3-4) | ✅ | ✅ | 선택만 | 선택만 |
| `searchShape` | ✅ `Circle` | ✅ `Line` | — | — |
| `damageShape` / `damageRange` (피해 **모양**) | ✅ | ✅ | — (투사체가 판정) | ✅ |
| `damage` (피해 **량**) | ✅ | ✅ | ✅ 발사 요청에 전달 | ✅ |
| `moveSpeed` | ✅ | ✅ | — | — |
| `jumpVisualHeight` | ✅ | `0` | — | — |
| `stopOnWall` | — | ✅ | — | — |
| `stayInRoom` | ✅ | ✅ | — | — |
| `lockFacingDuringExecute` | ✅ | ✅ | 읽지 않음 — **항상 잠금**(확정, §6-4) | — |
| 애니메이션 2단 | ✅ | ✅ | ✅ | ✅ |
| Kinematic / 워크 가드 / 타임아웃 | ✅ | ✅ | — | — |

**"실행타입에 따라 무시되는 필드"는 허용된 패턴이다.** Dash 가 `jumpVisualHeight: 0` 으로 시각 호를 끄는 것이 그 예이며, 코드 분기 없이 저작값으로 처리된다.

### Jump / Dash 대비

| | Jump | Dash |
|---|---|---|
| 목표 결정 | `searchShape` 셀 열거 + argmin | **동일 로직** (`searchShape = Line`) |
| 페이즈 | `Windup → Move → Impact → Recovery` | `Windup → Move → Recovery` |
| 피해 | 착지 후 1회 | 이동 중 매 프레임, 돌진당 대상 1회 |
| 시각 호 | `jumpVisualHeight` | `0` 저작 → 코드 분기 없이 직선 |

`searchShape` 를 `Line` 으로 두면 로컬 오프셋이 `(0,1)…(0,N)` 이 되고 플레이어 방향으로 회전되므로 **돌진 종점이 자동으로 나온다.** Dash 전용 목표 코드는 없다.

---

## 5. 저작 예시

| 에셋 | 값 |
|---|---|
| `Elite_Magma_01_JumpSkill` | `maxRange 12` / `searchShape Circle` / `damageShape Circle` + `damageRange 3` / `moveSpeed 9` / `castDelay 0.45` |
| `Elite_Magma_01_DashSkill` | `maxRange 15` / `searchShape Line` / `damageShape Circle` + `damageRange 1` / `moveSpeed 15` / `stopOnWall 1` / `jumpVisualHeight 0` |

⚠️ **셀 피해는 적 덩치를 반영하지 않는다.** Magma(콜라이더 반경 2.7)는 벽 정지 시 중심이 벽에서 2.7 떨어지는데 `damageRange 1`(반폭 1.5)은 벽에 붙은 플레이어에 닿지 않을 수 있다. 큰 적의 스킬을 저작할 때는 **"몸통 반경 + 원하는 사거리"를 셀로 환산**해야 한다.

---

## 6. Projectile 이관 설계 (S5, 미착수)

> **착수 판단** — 이 절의 설계는 확정이며 **바로 착수 가능하다.**
> §3-4( 상한 정책)와 §3-6(터널링 대책)은 **이동 스킬(Jump·Dash) 축의 별도 결정**이고,
> Projectile 은 이동이 없어 둘 다 해당하지 않는다. **선행 조건이 아니다.**

### 6-1. 핵심 — Projectile 은 **명중 판정**을 하지 않는다 (피해 **량**은 쓴다)

옛 런타임이 하는 일은 `ProjectileFireRequest` 를 만들어 `ProjectileFireService.Fire()` 에 넘기는 것이 전부다. **명중 판정은 투사체 프리팹(`ProjectileController`)이 자체 콜라이더로 한다.**

따라서 다음이 성립한다.

- `searchShape` / `damageShape` / `damageRange` — **미사용** (모양·범위는 투사체가 가진다)
- 🔴 **`damage` 는 쓴다** — `CreateRequest` 가 `Damage > 0 ? Damage : Data.attack` 으로 해석해 발사 요청에 실어 보낸다. 이 폴백은 Jump·Dash 와 동일하다
- 목표 **위치**가 없다. 필요한 것은 **조준 방향** 하나뿐이다
- `moveSpeed` / `stopOnWall` / `jumpVisualHeight` / `stayInRoom` — 이동이 없으므로 미사용
- Kinematic 전환 / 워크 가드 억제 / 이동 타임아웃 — **호출하지 않는다**

### 6-2. 투사체 전용 필드는 직렬화 클래스로 묶는다

옛 데이터의 투사체 필드 10개가 `EnemySkillData` 에 없다.

```
projectilePrefab / projectileSpeed / projectileLifetime
firePattern / projectileCount / spreadAngle / burstInterval
wallHitMode / maxBounceCount / impact(EnemyAttackImpactData)
```

flat 으로 추가하면 Jump 저작 시 무관한 칸이 10개 늘어난다. **`[Serializable] class ProjectileSettings` 하나로 묶어 필드 1개로 추가**한다 — `PatternShapeData` 와 같은 방식이며, 나중에 `executionType` 에 따라 접었다 펴는 드로어도 가능해진다.

### 6-3. 페이즈 — `Burst` 추가

```
Windup → (Burst) → Recovery
```

`Burst` 는 `firePattern == Burst` 일 때만 거치고, 그 외는 1회 발사 후 곧장 Recovery 로 간다. `Phase` enum 에 `Burst` 를 추가하며 `Move` / `Impact` 는 타지 않는다.

### 6-4. 🔴 조준 추적 · 방향 잠금 · 연사 규칙을 보존한다

옛 런타임의 방향 처리는 4단계이며 **Jump·Dash 와 다르다.**

```
Start()          조준 해석 → LockSpecialFacing → _unlockFacing = true   ← 옵션 검사 없이 항상
TickWindup()     매 프레임 조준 재해석 + LockSpecialFacing              ← 선딜 동안 추적
FireOrStartBurst 조준 재해석 + Lock → 발사                              ← 여기서 방향 확정
TickBurst()      재해석하지 않음 — 확정된 _aimDirection 으로 연사        ← Burst 동안 고정
Cleanup()        UnlockSpecialFacing                                    ← 종료·취소 공통
```

즉 **선딜 동안 추적 → 발사 직전 확정 → Burst 동안 고정 → 종료·취소 시 해제**다. Jump·Dash 는 `Start()` 에서 목표를 고정하므로 선딜 추적이 없다.

**결정 — 옵션을 볼 것인가 (확정: 보지 않는다)**

옛 Projectile 은 `lockFacingDuringDash` 같은 플래그를 **보지 않고 무조건 잠근다.** `EnemySkillData` 에는 `lockFacingDuringExecute` 가 있다.

- **(가) 무조건 잠근다** — 동작 보존. 저작 화면의 `lockFacingDuringExecute` 는 Projectile 에서 **읽히지 않는 필드**가 된다.
  ⚠️ 이는 `jumpVisualHeight` 와 다른 사례다 — 그 필드는 Dash 에서도 `ApplyVisualOffset` 이 **실제로 읽고**, 저작값 `0` 이 `height <= 0f` 분기를 타 효과가 꺼지는 것이다. 코드가 필드를 건너뛰는 것과 저작값으로 끄는 것은 구분해야 한다
- **(나) 옵션을 따른다** — 필드 의미가 실행타입 전반에서 일관되지만, **꺼두면 발사 방향과 스프라이트가 어긋난다.** 옛 저작에는 이 조합이 없으므로 미검증 경로가 생긴다

**(가) 확정.** 이관은 동작 보존이 우선이고, 옵션화는 실수요가 생길 때 별도로 판단한다.

> ✅ **확정(2026-09-04)** — (가) 항상 잠금으로 간다. §4 매트릭스도 같은 표기다.


### 6-5. `recoveryAnimation` 은 버린다

옛 Projectile 은 애니메이션이 3단(windup / fire / recovery)이고 `EnemySkillData` 는 2단(cast / execute)이다. 저작된 `recoveryAnimation` 값은 `None`(0)이라 실사용이 없고, Jump · Dash 도 후딜 모션이 없다. 필요해지면 그때 공통 3번째 슬롯으로 승격한다.

### 6-6. 🔴 `ProjectileFireService != null` 검사를 이식하지 않는다

옛 런타임의 `CanRun()` 은 이 조건을 추가로 요구했다. 그대로 공통 `CanRun()` 에 넣으면 **Jump / Dash 까지 이 조건에 묶인다.**

그런데 확인 결과 이 검사는 트립될 수 없다.

```csharp
// EnemyPatternRunner.cs:7
private readonly ProjectileFireService _projectileFireService = new ProjectileFireService();
```

러너가 `readonly` + 인라인 `new` 로 생성하고 컨텍스트가 그대로 받으므로 **항상 non-null** 이다. 발사 지점도 `_context.ProjectileFireService?.Fire(request)` 로 이미 null-safe 다.

→ **`CanRun()` 을 수정하지 않는다.** 실행타입별 분기를 만들 필요도 없다.

### 6-7. 값 이관 초안

`Elite_Magma_01_ProjectilePattern` → `Elite_Magma_01_ProjectileSkill`

```
cooldown / minRange / maxRange / weight / recoveryDuration 0.5   그대로
windupDuration 0.4     → castDelay 0.4
damage 5               → damage 5
windupAnimation 2      → castAnimation 2 (Projectile)
fireAnimation 2        → executeAnimation 2
recoveryAnimation 0    → (버림)
투사체 10필드          → ProjectileSettings { prefab c66242ce… / speed 8 / lifetime 3 /
                          firePattern Spread(2) / count 5 / spread 30 / burst 0.1 /
                          wallHitMode Destroy(0) / bounce 1 /
                          impact { knockbackForce 0 / knockbackDuration 0 /
                                   slowMultiplier 1  <- 0 아님 / slowDuration 0 / stunDuration 0 } }
```

🔴 `slowMultiplier` 의 무효값은 **0이 아니라 1**이다(`EnemyAttackImpactData.Default` 가 1로 초기화한다). 0으로 옮기면 값의 의미가 바뀐다.

`ProjectileFireRequest` 생성은 옛 `CreateRequest` 를 **그대로 복사**한다 — 필드 매핑이 1:1이라 손댈 이유가 없다.

### 6-8. 참조 정리

| 대상 | guid | 참조하는 곳 |
|---|---|---|
| `Elite_Magma_01_ProjectilePattern.asset` | `96584ada0ae440c4b67c2eb84a4cd614` | `Elite_Magma_01_PatternSet`, **`Elite_Magma_01_PatternSet_Phase2`** |
| `EnemyProjectilePatternData.cs` | `abeddd609749f6444ba075622291b221` | 위 에셋 하나뿐 |

⚠️ Jump · Dash 때와 달리 **Phase2 가 영향권**이다. 패턴셋 3개 중 2개를 고쳐야 한다.

**S5 가 끝나면 `Patterns/` 폴더가 비고, "새 패턴 = 에셋 1개 생성"이 완성된다.**

---

## 7. 알려진 제약

상세는 `HandOff/Known_Issue.md`.

| # | 내용 |
|---|---|
| Q24 | 큰 적(반경 2.7)은 벽에 붙은 플레이어에게 도달하지 못한다 |
| Q25 | footprint 판정이 실제 콜라이더 반경을 반영하지 않는다 (Q24 · 벽 끼임의 공통 뿌리) |
| Q26 | 이동 중 스턴 · 넉백이면 Kinematic 과 억제 상태가 유지되고 넉백이 삼켜진다 (자가 회복됨) |
| Q27 | 타임아웃 강제 착지는 목표 지점으로 텔레포트한다 |
| — | **`maxRange` 는 상한 보장이 아니라 근사다** — 반올림 · `Circle` 대각 · `Custom` 세 경로로 초과 가능 (§3-4) |
| — | **이동 구간을 검사하지 않는다(터널링)** — 프레임 히칭 시 중간 벽 · 플레이어를 건너뛸 수 있다 (§3-6) |

**미구현** — `InstantArea` 실행타입. `Start()` 에서 개발 빌드 경고 후 `false` 를 반환한다.

**저작 도구 부재** — `Custom` 모양은 현재 인스펙터 기본 리스트에 좌표를 타이핑해야 한다. 셀 페인팅 드로어(`PatternShapeData` 의 `PropertyDrawer`)가 들어오면 해소되며, 내장 타입에도 미리보기가 생겨 `damageRange` 값 산정 실수를 막는다.
