# 모드 속성 추가 — 워크스루

이 문서는 Task 1-6 구현 과정을 단계별로 기록합니다.

## 1. 데이터 모델 확장 (Task 1)

### 개요
카탈로그에 "이 퀘스트는 어느 모드에서 왔는가" 속성을 추가하기 위한 첫 단계. 이 Task에서는 데이터 모델만 확장하고, 실제 값 채우기는 Task 3-4에서 수행합니다.

### 변경 내용

#### 1.1 WarningCodes에 두 상수 추가
`server/Catalog/Models/CatalogModels.cs`의 `WarningCodes` 정적 클래스에:
- `ModQuestScanFailed = "modQuestScanFailed"` — 모드 퀘스트 스캔 중 장애 발생
- `ModQuestIdCollision = "modQuestIdCollision"` — 모드 퀘스트 ID 충돌 감지

이 경고는 Task 2에서 진단 처리되고, Task 4에서 실제 값으로 채워집니다.

#### 1.2 CatalogQuest.ModName 필드 추가
같은 파일의 `CatalogQuest` 클래스에 `string? ModName { get; init; }` 추가:
- **의미**: 비-바닐라(IsVanilla=false) 퀘스트의 경우, 이 퀘스트가 어디서 왔는지를 나타내는 모드 이름
- **null 경우**: 출처 모드를 찾을 수 없을 때 (예: CustomQuestService로 주입된 퀘스트)
- **바닐라 퀘스트**: 이 필드는 항상 null이어야 함 (의미 없음)

#### 1.3 CatalogInput에 두 옵셔널 필드 추가
`server/Catalog/CatalogInput.cs`의 레코드에:
- `IReadOnlyDictionary<string, string>? ModQuestOrigins = null` — 퀘스트 ID → 모드 이름 맵 (Task 3가 소비, Task 4가 채움)
- `IReadOnlyList<Models.CatalogWarning>? ModQuestScanWarnings = null` — 모드 스캔 중 발생한 경고들 (Task 3이 소비, Task 4가 채움)

**기본값이 null인 이유**:
- 이 레코드는 여러 위치(CatalogService.cs, CatalogBuilderTests.cs)에서 위치 인수로 생성됩니다.
- 기본값이 없으면 이 Task만으로 다른 파일들이 컴파일 에러가 됩니다.
- Task 3에서 테스트가, Task 4에서 CatalogService.cs가 실제 값을 채웁니다.
- 이 설계는 기존 호출부를 건드리지 않으면서 새 필드를 추가하는 방법입니다(backward compatibility).

### 빌드 및 테스트 결과
- `dotnet build server/QuestCodex.csproj`: ✓ 성공 (경고/오류 0개)
- `dotnet test tests/QuestCodex.Tests/QuestCodex.Tests.csproj`: ✓ 110개 테스트 모두 통과
  - 새로 추가한 필드들이 모두 옵셔널이므로 기존 코드에 영향 없음

### 네임스페이스 주의사항
- `CatalogModels.cs`: 네임스페이스는 `QuestCodex.Catalog.Models`
- `CatalogInput.cs`: 네임스페이스는 `QuestCodex.Catalog` (Models를 직접 using하지 않음)
- `CatalogInput.cs`에서 `Models.CatalogWarning`으로 참조 가능한 이유: C# 네임스페이스 계층 구조에서 부모 네임스페이스 내의 자식 네임스페이스 멤버에 접근할 수 있기 때문

## 2. 모드 스캔 로직 (ModQuestIndex) (Task 2)

### 개요
Task 1에서 추가한 `CatalogWarning`과 `CatalogQuest.ModName` 필드를 이용해, 실제로 `user/mods/` 폴더 트리를 스캔하여 각 퀘스트의 출처 모드를 파악하는 순수 로직을 구현합니다.

### 핵심 설계 결정

#### 2.1 Scan() / ScanRoot() 분리
- **Scan()**: 인스턴스 메서드 (내부)
  - 어셈블리 경로에서 `modsRoot`와 `selfDir`을 계산
  - 실제 SPT 설치 환경에서 자동 초기화용
  - Lazy<T>로 감싸서 DI 컨테이너 구성 시점에 평가 (Task 4에서 사용)

- **ScanRoot()**: 정적 메서드 (공개)
  - 순수 로직만 분리
  - `modsRoot`, `selfDir`을 직접 인수로 받음
  - 테스트가 임시 디렉터리를 전달해서 호출
  - 실제 어셈블리나 파일 시스템 접근 없이 로직만 검증 가능

#### 2.2 자기 자신 제외 (Self-exclusion)
```csharp
var selfFull = Path.GetFullPath(selfDir);
foreach (var modDir in Directory.GetDirectories(modsRoot))
{
    if (string.Equals(Path.GetFullPath(modDir), selfFull, StringComparison.OrdinalIgnoreCase)) 
        continue;
    // ...
}
```

**이유**:
- `user/mods/QuestCodex/quests.json`은 QuestCodex 자신의 기본 퀘스트들
- 이들을 모드 기여로 오인하면 안 됨
- `OrdinalIgnoreCase` 비교: Windows 파일 경로는 대소문자 무시, 정규화 경로로 비교하면 심볼링크나 상대 경로 변형 처리

#### 2.3 파싱 실패 (JSON 손상)
```csharp
try {
    parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file));
}
catch (Exception ex) {
    warnings.Add(new CatalogWarning(null, WarningCodes.ModQuestScanFailed, 
        $"{modName}: {ex.GetType().Name}: {ex.Message}"));
    continue;
}
```

**설계**:
- 한 모드의 JSON 오류가 전체 스캔을 중단하지 않음
- 경고를 기록하고 (QuestId=null로 시스템 경고 분류) 다음 모드로 계속
- 사용자가 로그를 보고 해당 모드의 JSON을 수정하도록 유도

#### 2.4 퀘스트 ID 충돌 처리
```csharp
if (!origins.TryGetValue(questId, out var existing)) {
    origins[questId] = modName;
} else if (existing != modName) {
    var winner = string.CompareOrdinal(existing, modName) <= 0 ? existing : modName;
    var loser = winner == existing ? modName : existing;
    origins[questId] = winner;
    warnings.Add(new CatalogWarning(questId, WarningCodes.ModQuestIdCollision, 
        $"claimed by '{winner}' and '{loser}', kept '{winner}'"));
}
```

**결정**:
- 두 모드가 같은 questId를 정의하면 **사전식 순서(alphabetical)로 먼저 오는 것을 승자로 선택**
  - "AlphaMod"와 "ZetaMod"가 동일 ID를 주장 → "AlphaMod" 유지
- 이유:
  - 비결정적 순서(예: 폴더 열거 순서)를 피함
  - 같은 환경 재구성 시 동일한 결과 보장
  - 충돌 경고(`QuestId=questId`)에 두 모드 이름 모두 기록

#### 2.5 CustomQuestService 한계를 받아들이는 이유
```csharp
/// CustomQuestService.CreateQuest() 로 주입된 퀘스트는 파일 흔적이 없어 
/// 여기 잡히지 않는다 — 알려진 한계, 해결 시도하지 않는다.
```

**맥락**:
- SPT 서버는 `user/mods/*/quests.json`뿐만 아니라, 런타임에 C# 코드로 동적 퀘스트를 생성할 수 있음
- 동적 생성 퀘스트는 파일 흔적이 없으므로 이 스캔에서 인식 불가
- **해결하지 않는 이유**:
  - 동적 퀘스트는 그 출처 모드를 메타데이터로 제공해야 함 (C# 코드 레벨)
  - 현 스펙(§0 "서버 모드별 퀘스트 파일 기반 분석")은 파일 기반만 다룸
  - 동적 로직은 Task 3-4 범위 밖

### 구현 정리

#### 파일 구조
```
server/Services/ModQuestIndex.cs
└─ [Injectable(Singleton)]
└─ Scan() : 인스턴스 메서드 (어셈블리 기반 자동 초기화)
└─ ScanRoot(modsRoot, selfDir) : 정적 메서드 (테스트용 순수 로직)
```

#### 속성
- `QuestOrigins : IReadOnlyDictionary<string, string>` — questId → modFolderName
- `Warnings : IReadOnlyList<CatalogWarning>` — 스캔 중 발생한 모든 경고 (Task 4가 CatalogInput으로 CatalogService에 전달)

#### 테스트 (ModQuestIndexTests.cs)
1. **Maps_quest_ids_to_their_mod_folder** — 정상 quests.json 두 개: 올바르게 매핑됨, 경고 없음
2. **Self_mod_folder_is_excluded** — QuestCodex 자신의 quests.json: 제외됨
3. **Broken_json_in_one_mod_does_not_stop_the_scan** — 파싱 오류: 해당 모드는 경고 기록, 다른 모드는 정상 처리
4. **Colliding_quest_id_keeps_the_alphabetically_first_mod_and_warns** — 충돌: AlphaMod가 ZetaMod보다 우선, 경고 기록

모두 통과: ✓ 4개 / 4개

### 빌드 및 테스트 결과
- `dotnet build server/QuestCodex.csproj`: ✓ 성공
- `dotnet test tests/QuestCodex.Tests/QuestCodex.Tests.csproj --filter "FullyQualifiedName~ModQuestIndexTests"`: ✓ 4개 테스트 모두 통과 (68ms)

## 3. `CatalogBuilder` 연결 (Task 3)

### 개요
Task 1이 추가한 `CatalogInput.ModQuestOrigins`/`ModQuestScanWarnings`와 `CatalogQuest.ModName`은 지금까지 아무도 읽거나 쓰지 않는 죽은 필드였습니다. 이 Task는 `CatalogBuilder`(순수 함수, SPT DI 없이 테스트됨)가 이 두 입력 필드를 실제로 소비하도록 연결합니다. `ModQuestIndex`를 실제로 주입해 값을 채우는 작업(Task 4, `CatalogService.cs`)과는 분리되어 있습니다 — 이 Task의 테스트는 `ModQuestIndex`를 거치지 않고 `CatalogInput`에 테스트 데이터를 직접 넣어서 검증합니다.

### 핵심 설계 결정

#### 3.1 `ModName`은 `IsVanilla` 확정 이후에만 채운다
```csharp
IsVanilla = input.VanillaQuestIds?.Contains(questId) ?? false,
ModName = input.VanillaQuestIds?.Contains(questId) == true ? null : input.ModQuestOrigins?.GetValueOrDefault(questId),
```
- `ModQuestOrigins`는 파일 시스템 스캔(`user/mods/*/quests.json`) 결과일 뿐이라, 어떤 questId가 그 안에 있다는 사실만으로는 "이 퀘스트가 모드산(産)"이라고 확정할 수 없습니다.
- 스펙 §0에 나오는 "조건-패치 모드"(바닐라 퀘스트 ID를 그대로 재사용해서 `AvailableForStart` 조건만 바꿔치기하는 모드)가 존재하면, 그 모드의 `quests.json`에도 바닐라 questId가 등장할 수 있습니다.
- 그래서 `ModName`을 채우기 전에 반드시 `IsVanilla`가 이미 `true`로 확정되지 않았는지부터 확인합니다. 바닐라로 확정되면 `ModQuestOrigins`에 값이 있어도 무조건 `null`로 덮어씁니다.
- 이 순서를 뒤집어 `ModQuestOrigins`를 먼저 보고 `IsVanilla`를 나중에 체크하면(또는 체크를 생략하면), 조건-패치 모드가 설치된 환경에서 바닐라 퀘스트에 엉뚱하게 모드 이름이 붙는 안전성 버그가 생깁니다. `ModName_is_filled_for_non_vanilla_quests_only` 테스트가 이 성질을 직접 검증합니다(questId 2번은 `vanilla` 집합에도 있고 `modOrigins`에도 있지만, 결과는 반드시 `null`).

#### 3.2 스캔 경고는 `Build()` 상단, 기존 바닐라 스냅샷 경고와 같은 자리에서 병합
```csharp
if (input.VanillaQuestIds is null) { warnings.Add(...VanillaSnapshotMissing...); }
else if (...) { warnings.Add(...VanillaSnapshotMismatch...); }

warnings.AddRange(input.ModQuestScanWarnings ?? []);
```
- `ModQuestScanWarnings`(Task 2의 `ModQuestIndex.Warnings`, 예: `ModQuestScanFailed`, `ModQuestIdCollision`)는 퀘스트 단위 루프가 시작되기 전에 이미 확정된, 빌드와 무관한 "입력 단계" 경고입니다. `VanillaSnapshotMissing`/`Mismatch`와 성격이 같습니다(둘 다 "카탈로그를 만들기 전에 입력 자체에 문제가 있었다"는 경고).
- 기존 패턴 위치에 그대로 이어붙이면 퀘스트별 경고(`BuildFailed`, `MissingLocale` 등)보다 항상 앞에 오게 되어, 경고 목록을 볼 때 "전역 입력 문제 → 퀘스트별 문제" 순서가 유지됩니다.
- `ModQuestScanWarnings`가 `null`인 경우(Task 4 이전, 또는 스캔이 아직 연결되지 않은 호출부)에도 `?? []`로 안전하게 무시됩니다.

### TDD 과정
1. `Input(...)` 헬퍼에 `modOrigins`/`modWarnings` 파라미터 추가, 새 `[Fact]` 2개(`ModName_is_filled_for_non_vanilla_quests_only`, `Mod_scan_warnings_are_merged_into_catalog_warnings`) 작성.
2. RED: 필터 테스트 실행 → 2개 실패(`ModName`이 채워지지 않아 `null`, 경고 목록에 `ModQuestScanFailed` 없음), 기존 14개는 그대로 통과.
3. `CatalogBuilder.cs`의 `Build()`와 `BuildQuest()`를 위 두 블록으로 수정.
4. GREEN: 필터 테스트 16개 전부 통과, 전체 스위트 116개 전부 통과(회귀 없음).

### 빌드 및 테스트 결과
- `dotnet test tests/QuestCodex.Tests/QuestCodex.Tests.csproj --filter "FullyQualifiedName~CatalogBuilderTests"`: ✓ 16개 모두 통과
- `dotnet test tests/QuestCodex.Tests/QuestCodex.Tests.csproj`: ✓ 116개 모두 통과

## 다음 단계
Task 4: `CatalogService.cs`에 `ModQuestIndex`를 실제로 주입해 `ModQuestOrigins`/`ModQuestScanWarnings`에 실제 값을 채우기
