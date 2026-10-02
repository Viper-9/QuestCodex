# 퀘스트 존 덤프 원본

`QuestCodex.ZoneDump` 플러그인으로 남긴 덤프를 그대로 보관한 것이다. 2026-09-25 원본은 당시 버전(0.0.1)이 레이드 진입 15초 뒤 자동으로 남긴 것이고, 0.0.2부터는 자동 덤프 없이 키를 눌러야 덤프한다(0.0.3부터 기본 키 `F10`, 수식 키 무시 — `F9`는 FieldKit 과 겹치고 BepInEx 단축키 판정이 Shift 등을 누른 상태에서 실패했다).
정식 기능에 쓸 스냅샷은 이 원본을 가공해서 따로 만든다.

- 수집일: **2026-10-01** 전 맵 재덤프(플러그인 0.0.2, 콜라이더 기록 포함). 2026-09-25 원본은 git 기록에 남아 있다
- 클라이언트: EFT `0.16.9.40743` (SPT 4.1.5 서버), 퀘스트를 하나도 수락하지 않은 상태
- 좌표: Unity 월드 좌표 그대로 (`X`, `Y` = 높이, `Z`). 서버 `looseLoot.json`과 같은 좌표계
- `Zones`: `TriggerWithId`(비활성 포함) + `FlareShootDetectorZone`(신호탄 존)
- `Doors`: `KeyId`가 있는 `WorldInteractiveObject` 전부. 문뿐 아니라 잠긴 컨테이너와 트렁크도 들어 있다
- `Colliders`(0.0.2부터): 존 오브젝트의 콜라이더마다 종류와 월드 `Min`/`Max`. `BoxCollider`면 실제 상자 `Center`(월드), `Size`(로컬 크기 × lossyScale), `Rotation`(사원수), `Yaw`(도)도 기록한다. `Bounds`는 이 콜라이더들의 축 정렬 외곽이라 회전된 상자를 크게 보이게 한다(08 스펙 §6). 10/1 재덤프에서는 **모든 존이 `BoxCollider`** 였다
- **모드 존도 함께 잡힌다**(9/25에는 0개, 10/1에는 WTT 존이 씬에 생성된 상태라 맵마다 수십 개). `build-snapshot.js`가 WTT `CustomQuestZones` 파일과 **맵·ID·위치(1m 이내)가 같은 존**을 걸러 낸다(10/1 기준 545개). 모드 존은 서버가 실행 시 WTT 파일에서 따로 읽는다

| 파일 | 맵 | 덤프 존(모드 포함) | 스냅샷 존 ID(바닐라) | 신호탄 | 회전된 상자(모드 포함) | 잠긴 오브젝트 |
|---|---|---|---|---|---|---|
| `bigmap.json` | Customs | 213 | 69 | 1 | 177 | 35 |
| `factory4_day.json` | Factory 주간 | 98 | 72 | 0 | 33 | 4 |
| `factory4_night.json` | Factory 야간 | 97 | 71 | 0 | 33 | 4 |
| `sandbox.json` | Ground Zero 저레벨 | 85 | 24 | 0 | 18 | 6 |
| `rezervbase.json` | Reserve | 150 | 72 | 2 | 116 | 33 |
| `tarkovstreets.json` | Streets | 104 | 89 | 2 | 54 | 63 |
| `woods.json` | Woods | 158 | 64 | 1 | 96 | 4 |
| `shoreline.json` | Shoreline | 142 | 71 | 0 | 61 | 38 |
| `interchange.json` | Interchange | 63 | 33 | 0 | 22 | 101 |
| `lighthouse.json` | Lighthouse | 180 | 72 | 2 | 80 | 26 |
| `laboratory.json` | Labs | 58 | 13 | 0 | 1 | 16 |
| `labyrinth.json` | Labyrinth | 8 | 8 | 0 | 1 | 18 |

- 신호탄 수는 ID가 있는 것만 셌다(ID가 빈 감지 존은 퀘스트와 무관). 세관의 `quest_terminal_flare_zone`은 9/25 덤프가 신호탄 수집 기능 이전이라 이번에 처음 잡혔다.
- **Ground Zero 고레벨(`sandbox_high`)은 덤프하지 않는다.** 저레벨과 존이 ID·위치까지 같고(9/25 비교), 서버의 짝 맵 규칙이 고레벨 퀘스트에서 저레벨 좌표를 같은 맵으로 본다.
- 공장은 주간·야간의 존 배치가 달라서 둘 다 덤프한다(야간에만 `huntsman_013`, `kill1`, `ter_017_area_1` 등).

대조: `node tools/zone-dump/check-coverage.js tools/zone-dump/dumps`

## 지도 변형 덤프 `variants/` (09 스펙)

맵을 통째로 바꾸는 모드를 깐 상태로 뜬 덤프다. `build-snapshot.js`가 스냅샷의 `variants.<변형>`에 넣고, 서버는 **그 모드가 로드됐을 때만** 해당 맵의 존·문을 통째로 이것으로 바꾼다. 바닐라 덤프(위 표)와 섞지 않는다.

| 파일 | 모드 | 수집일 | 덤프 존(모드 포함) | 스냅샷 존 ID | 잠긴 오브젝트 |
|---|---|---|---|---|---|
| `variants/manimal/interchange.json` | ManimalInterchange 1.0.8(`Mode: test`, 씬 `SourceBuild 1.1.5.47242`) | 2026-10-03 | 93 | 61 | 100 |

- 바닐라 인터체인지(33개)에 없는 존 28개가 늘었고, 바닐라에만 있는 존은 없다. 새 존은 라이브 1.x 존(`ny25_*`, `taah_*`, `batya_*`, `shop_*_hide`, `shorl_exit_*` 등)이다.
- 바닐라 퀘스트가 쓰는 `place_WARBLOOD_04_2`가 확장 씬에서 190m 옮겨졌다((274, 15) → (429, 125)).
- `q14_10_kill_ice`(ManimalIcebreaker 퀘스트)는 확장 씬에만 있다.
- 모드가 정식판으로 바뀌어 씬이 달라지면 다시 덤프한다. 절차는 바닐라와 같고, 결과 파일만 이 폴더에 둔다.

## 남은 존 (2026-10-01 기준, 461개 중 457개 확보)

10/1 재덤프로 미궁 존 7개(Hypotheses Testing, Confidential Info, This Tape Sucks, Keeper's Word 3개, Offensive Reconnaissance)를 확보했다. 아래 4개 퀘스트가 남아 있다.

| 퀘스트 | 퀘스트 맵 | 빠진 존 ID | 추정 / 다음 할 일 |
|---|---|---|---|
| 아이스크림 콘 | woods | `bunker2` (VisitPlace) | 스폰 근처 덤프 두 번(9/25, 10/1) 모두 없음. ZB-014 벙커 근처까지 가서 덤프해 볼 것(하위 씬 지연 로드 의심). 그래도 없으면 BSG가 삭제한 존 — sptQuestLive 의 이 퀘스트도 여전히 `bunker2`를 쓴다 |
| New Day, New Paths | marathon | `Check_cinema` | 같은 퀘스트의 다른 존(`Transits_to_streets`, `Prospect_mira`)은 Ground Zero에 있음. 트랜짓 관련이면 `TransitPoint` 등 다른 컴포넌트 의심 |
| Beneath The Streets | marathon | `Labs_transits` | 나머지 4개 존은 Labs에 있음. 10/1 Labs 재덤프에도 없음. 이름상 트랜짓 지점 → 위와 같은 의심 |
| Friend from Norvinsk - Part 5 | any | `1` (2건) | 존 ID가 `"1"` — BSG 데이터의 자리표시자로 추정, 찾을 대상이 아닐 가능성이 큼 |

진단 아이디어: 덤프 키를 누르면 씬의 모든 `MonoBehaviour`에서 위 ID 문자열을 값으로 가진 필드를 찾아 컴포넌트 종류와 위치를 로그로 남기게 한다.

## SPT 업데이트 시 갱신 방법

> 자세한 절차(맵 목록, 덤프 직후 `node tools/zone-dump/check-dump.js <맵>` 확인 기준, 겪은 함정)는 `dev-docs/ops/zone-dump.runbook.md`에 있다. 아래는 요약이다.

존 좌표는 서버 데이터에 없고 클라이언트의 맵 씬 안에만 있어서, 스냅샷(`server/Data/quest-zones.json`)은 게임에 들어가서 뽑아야 한다. 매번 모든 맵을 돌 필요는 없고, **바뀐 맵만** 다시 덤프하면 된다.

모드 퀘스트 존(WTT `CustomQuestZones`)과 퀘스트 아이템 위치(looseLoot)는 서버가 실행 중에 직접 읽으므로 갱신할 필요가 없다.

1. **갱신이 필요한지 확인**: 새 SPT 데이터로 루트에서 `dotnet test`를 돌린다.
   - `VanillaSmokeTests.Vanilla_catalog_has_quest_locations`가 실패하면 못 찾는 존 목록이 위 "남은 존"과 달라졌다는 뜻이다.
   - 서버 로그의 `questZoneNotFound` 경고 수가 늘어난 것으로도 알 수 있다.
2. **어느 맵인지 확인**: `node tools/zone-dump/check-coverage.js tools/zone-dump/dumps`
   - 빠진 존마다 `퀘스트 이름 [퀘스트 맵]`이 나온다. 맵이 `아무 지역`·`Transition`이면 존 ID와 같은 퀘스트의 다른 목표로 맵을 짐작한다.
   - SPT 데이터 경로가 다르면 두 번째 인자로 `…/SPT_Data/database`를 넘긴다.
3. **그 맵만 덤프**
   - 게임 폴더의 `BepInEx/plugins/QuestCodex.ZoneDump/`에 플러그인이 있어야 한다. 없으면 `tools/zone-dump/`를 빌드해서 넣는다.
   - **퀘스트를 수락하지 않은 상태로** 해당 맵에 진입해 `F10`을 누른다(0.0.2부터 자동 덤프 없음). 누르면 `LogOutput.log`에 `Dump key F10 pressed`와 `Dumped …`가 남는다. 하위 씬이 늦게 로드되는 곳은 그 근처까지 가서 누른다.
   - 결과는 플러그인 폴더의 `dumps/<맵>.json`에 생긴다.
4. **스냅샷 재생성**
   - 새 덤프 파일로 `tools/zone-dump/dumps/`의 같은 이름 파일을 교체한다.
   - `node tools/zone-dump/build-snapshot.js tools/zone-dump/dumps server/Data/quest-zones.json "EFT <클라이언트 버전>"`을 실행한다(덤프 폴더 전체를 다시 읽는다). 네 번째 인자로 모드 폴더(`…/SPT_Runtime/user/mods`)를 넘기면 WTT 모드 존 복사본을 그 기준으로 거른다.
   - `check-coverage.js`를 다시 돌려 빠진 존이 줄었는지 확인한다.
5. **기대값과 문서 갱신**: 위 "남은 존" 표, `VanillaSmokeTests`의 `remaining` 목록, 이 README 상단의 수집일·클라이언트 버전·맵 표를 함께 고친다. 그다음 `dotnet test` → 배포.

주의: 맵이 개편되어 **존 위치만 옮겨지고 ID는 그대로**인 경우는 테스트로 잡히지 않는다. 패치 노트에 맵 개편이 있으면 그 맵은 테스트 결과와 상관없이 다시 덤프한다.

새 맵(예: 아직 정의가 없는 `terminal`)의 **지도 그림**은 이 절차와 별개다. DynamicMaps가 그 맵을 지원한 뒤 `node tools/maps/build-maps.js <DynamicMaps 클론>`을 다시 돌린다. 그 전까지 그 맵의 좌표는 카탈로그에는 들어가지만 팝업 탭은 만들어지지 않는다.
