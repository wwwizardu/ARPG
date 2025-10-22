---
name: update-stat
description: GlobalEnum.Stat 변경 시 관련된 모든 파일을 자동으로 업데이트합니다
---

# Update Stat Skill

GlobalEnum.Stat에 스탯이 추가, 변경, 삭제되었을 때 필요한 모든 파일을 자동으로 업데이트하는 스킬입니다.

## ⚠️ 중요: 스탯 개수 계산 방법

**GlobalEnum.Stat의 enum 개수 ≠ StatTable의 프로퍼티 개수**

- **GlobalEnum.Stat**: 모든 게임 내 스탯을 정의 (26개)
  - 포함: `Str`, `Dex`, `Int`, `Hp`, `Mp`, `HpGeneration`, `MpGeneration`, `AttackMin`, `AttackMax`, `CriRate`, `CriDamageMul`, `MoveSpeed`, `MoveSpeedMul`, `AttackSpeed`, `AttackSpeedMul`, `CastSpeed`, `CastSpeedMul`, `Defense`, `FireResist`, `IceResist`, `LightningResist`, `PoisonResist`, `Luck`, `BloodingRate`, `IgniteRate`

- **StatTable**: 구글 시트에서 다운로드하는 기본 스탯만 포함 (22개)
  - 제외: `MoveSpeedMul`, `AttackSpeedMul`, `CastSpeedMul` (런타임에서만 사용되는 배율 스탯)
  - 포함: `Str`, `Dex`, `Int`, `MaxHp`, `MaxMp`, `HpGeneration`, `MpGeneration`, `AttackMin`, `AttackMax`, `CriRate`, `CriDamage`, `MoveSpeed`, `AttackSpeed`, `CastSpeed`, `Defense`, `FireResist`, `IceResist`, `LightningResist`, `PoisonResist`, `Luck`, `BloodingRate`, `IgniteRate`

**따라서 DownloadTables.cs 업데이트 시에는 반드시 `Tables.cs`의 `StatTable` 클래스에 있는 프로퍼티 개수를 세어야 합니다!**

## 작업 순서

### 1. StatTable 프로퍼티 개수 확인 (필수!)
**중요**: GlobalEnum.Stat이 아닌 `Assets/Scripts/Common/Tables.cs`의 `StatTable` 클래스를 확인합니다.

1. `Tables.cs` 파일의 `StatTable` 클래스를 읽습니다
2. `[JsonProperty]` 어트리뷰트가 붙은 프로퍼티 개수를 정확히 셉니다
3. 이 개수가 실제 구글 시트의 스탯 컬럼 개수입니다

예시:
```csharp
public class StatTable : TableBase
{
    [JsonProperty("Str")] public int Str;           // 1
    [JsonProperty("Dex")] public int Dex;           // 2
    ...
    [JsonProperty("BloodingRate")] public int BloodingRate;  // 21
    [JsonProperty("IgniteRate")] public int IgniteRate;      // 22
}
```
→ StatTable의 스탯 개수 = **22개**

### 2. DownloadTables.cs 업데이트
`Assets/Scripts/Editor/DownloadTables.cs` 파일을 수정합니다:

#### 2.1. StatTable 다운로드 범위 수정
- `DownloadTable<StatTable>()` 호출 부분을 찾습니다 (현재 40번째 줄 근처)
- **1단계에서 확인한 StatTable의 프로퍼티 개수**를 사용하여 range를 계산합니다
  - 기본 컬럼: Id(A), 웹용1(B), 웹용2(C) = 3개
  - 스탯 컬럼: **StatTable 클래스의 프로퍼티 개수** (현재 22개)
  - 총 컬럼 = 3 + StatTable 프로퍼티 개수
  - 엑셀 컬럼 문자로 변환 (예: 3+20=23 -> W, 3+22=25 -> Y)
- 형식: `await DownloadTable<StatTable>("318209064&range=A:{마지막컬럼}", 1, SaveType.String);`

**예시 계산**:
- StatTable 프로퍼티 개수 = 22개
- 총 컬럼 = 3 + 22 = 25
- 25번째 컬럼 = Y
- 결과: `"318209064&range=A:Y"`

#### 2.2. ParseStatTable() 함수 수정
- `ParseStatTable()` 함수 전체를 다시 작성합니다
- 검증 로직:
  ```csharp
  if (values.Length < {3 + 스탯개수})
  {
      Debug.LogError($"[ParseStatTable] Invalid data length. Expected at least {3 + 스탯개수}, got {values.Length}. Id: {table.Id}");
      return;
  }
  ```
- 파싱 로직:
  - values[0]: Id (이미 파싱됨)
  - values[1], values[2]: 웹용 (스킵)
  - values[3]부터: GlobalEnum.Stat의 순서대로 각 스탯 파싱
  - 각 스탯을 `table.{스탯이름} = int.Parse(values[인덱스]);` 형식으로 파싱

### 3. Tables.cs의 StatTable 클래스 업데이트
`Assets/Scripts/Common/Tables.cs` 파일의 `StatTable` 클래스를 수정합니다:

- GlobalEnum.Stat의 모든 enum 값에 대해 프로퍼티를 생성합니다
- 각 프로퍼티 형식:
  ```csharp
  [JsonProperty("{스탯이름}")] public int {스탯이름};
  ```
- 주석이 있으면 유지합니다

### 4. Stats.cs의 Stats 클래스 업데이트
`Assets/Scripts/Creature/Stats.cs` 파일의 `Stats` 클래스를 수정합니다:

- GlobalEnum.Stat의 모든 enum 값에 대해 프로퍼티를 생성합니다
- 각 프로퍼티 형식:
  ```csharp
  public int {스탯이름}
  {
      get { return this[GlobalEnum.Stat.{스탯이름}]; }
      set { this[GlobalEnum.Stat.{스탯이름}] = value; }
  }
  ```
- 특수 케이스 처리:
  - `Hp` -> `MaxHp` 프로퍼티명 사용
  - `Mp` -> `MaxMp` 프로퍼티명 사용
  - `CriDamageMul` -> `CriDamage` 프로퍼티명 사용

### 5. StatController.Reset() 메서드 업데이트 (**필수**)
`Assets/Scripts/Creature/Stats.cs` 파일의 `StatController` 클래스 내 `Reset()` 메서드를 업데이트합니다.

**중요**: 이 단계는 필수입니다! StatTable에 새로운 스탯이 추가되면, `Reset()` 함수에서도 해당 스탯을 `_statsBase`에 대입해야 합니다.

#### 5.1. Reset() 메서드 찾기
`StatController` 클래스의 `Reset()` 메서드를 찾습니다 (약 26번째 줄 근처).

#### 5.2. 새로운 스탯 대입 코드 추가
`UpdateStat()` 호출 직전에 새로운 스탯을 추가합니다.

**추가 위치**: `_statsBase.Luck = _owner.Table.Stat.Luck;` 다음 줄

**추가 형식**:
```csharp
_statsBase.{스탯이름} = _owner.Table.Stat.{스탯이름};
```

**예시** (BloodingRate, IgniteRate 추가):
```csharp
_statsBase.Luck = _owner.Table.Stat.Luck;
_statsBase.BloodingRate = _owner.Table.Stat.BloodingRate;
_statsBase.IgniteRate = _owner.Table.Stat.IgniteRate;

UpdateStat();
```

#### 5.3. 주의사항
- **배율 스탯 제외**: `MoveSpeedMul`, `AttackSpeedMul`, `CastSpeedMul`은 StatTable에 없으므로 추가하지 않습니다
  - 이 배율 스탯들은 고정값(100)으로 직접 설정됩니다:
    ```csharp
    _statsBase.MoveSpeedMul = 100;
    _statsBase.AttackSpeedMul = 100;
    _statsBase.CastSpeedMul = 100;
    ```
- **StatTable에 있는 스탯만**: Tables.cs의 StatTable 클래스에 프로퍼티가 있는 스탯만 추가합니다
- **순서 유지**: 가능하면 StatTable의 프로퍼티 순서대로 추가합니다

## 엑셀 컬럼 문자 변환 로직

컬럼 번호를 엑셀 문자로 변환하는 방법:
- 1-26: A-Z
- 27: AA (26 + 1)
- 예시: 23 -> W, 25 -> Y, 27 -> AA

## 주의사항

1. **파일 백업**: 수정 전에 현재 파일 상태를 확인하고 백업합니다
2. **Enum 순서 유지**: GlobalEnum.Stat의 enum 순서가 구글 시트의 컬럼 순서와 일치해야 합니다
3. **특수 케이스 확인**: Hp->MaxHp, Mp->MaxMp, CriDamageMul->CriDamage 같은 특수 매핑을 유지합니다
4. **주석 보존**: 기존 주석이 있다면 최대한 유지합니다
5. **테스트**: 수정 후 Unity에서 에러가 없는지 확인합니다

## 실행 예시

사용자가 "BloodingRate와 IgniteRate를 GlobalEnum.Stat에 추가했어"라고 하면:

1. ❌ **잘못된 방법**: GlobalEnum.cs를 읽어서 enum 개수 세기
   - GlobalEnum.Stat에는 26개의 enum이 있지만, StatTable에는 22개만 사용됨

2. ✅ **올바른 방법**: Tables.cs의 StatTable 클래스 확인
   - StatTable의 프로퍼티를 세어보니 22개 (BloodingRate, IgniteRate 포함)
   - 총 컬럼 = 3 + 22 = 25 (Y 컬럼)

3. DownloadTables.cs의 40번째 줄을 `"318209064&range=A:Y"`로 수정
4. ParseStatTable()의 검증을 `values.Length < 25`로 수정
5. ParseStatTable()에 다음 코드 추가:
   ```csharp
   table.BloodingRate = int.Parse(values[23]);
   table.IgniteRate = int.Parse(values[24]);
   ```
6. Tables.cs의 StatTable에 프로퍼티가 이미 추가되어 있는지 확인
7. Stats.cs의 Stats 클래스에 프로퍼티 추가
8. Stats.cs의 StatController.Reset() 메서드에 스탯 대입 코드 추가:
   ```csharp
   _statsBase.BloodingRate = _owner.Table.Stat.BloodingRate;
   _statsBase.IgniteRate = _owner.Table.Stat.IgniteRate;
   ```

## 일반적인 실수 및 해결 방법

### 실수 1: GlobalEnum.Stat의 개수를 세어 계산
**문제**: GlobalEnum.Stat에는 런타임 전용 스탯(`MoveSpeedMul`, `AttackSpeedMul`, `CastSpeedMul`)이 포함되어 있어, 실제 구글 시트 컬럼 개수와 다릅니다.

**해결**: 항상 `Tables.cs`의 `StatTable` 클래스에 있는 `[JsonProperty]` 프로퍼티 개수를 세어야 합니다.

### 실수 2: 컬럼 범위를 잘못 계산
**문제**: 3+24=27=AA로 계산했지만, 실제로는 3+22=25=Y여야 함

**해결**:
- StatTable 프로퍼티 개수를 정확히 세기
- 엑셀 컬럼 변환 공식 확인 (A=1, Z=26, AA=27)

### 실수 3: values.Length 검증 값을 잘못 설정
**문제**: `values.Length < 27`로 설정했지만, 실제로는 25개만 필요함

**해결**: `3 + StatTable 프로퍼티 개수`로 계산

### 실수 4: StatController.Reset() 메서드 업데이트 누락
**문제**: 새로운 스탯을 추가했지만, `StatController.Reset()` 메서드에 스탯 대입 코드를 추가하지 않아서 런타임에 스탯 값이 0으로 초기화됨

**해결**:
- `Reset()` 메서드에서 `_statsBase.{스탯이름} = _owner.Table.Stat.{스탯이름};` 코드 추가
- `UpdateStat()` 호출 직전에 추가해야 함
- StatTable에 있는 모든 스탯에 대해 대입 코드 작성

## 완료 확인

모든 작업이 완료되면 다음 파일들이 수정되어야 합니다:

### 수정된 파일 체크리스트

1. ✅ **DownloadTables.cs**
   - [ ] `DownloadTable<StatTable>()` 호출의 range 업데이트 (`A:Y` 등)
   - [ ] `ParseStatTable()` 함수의 `values.Length` 검증 업데이트
   - [ ] `ParseStatTable()` 함수에 새로운 스탯 파싱 코드 추가

2. ✅ **Tables.cs**
   - [ ] `StatTable` 클래스에 새로운 스탯 프로퍼티 추가 (`[JsonProperty]` 포함)

3. ✅ **Stats.cs** (2곳 수정)
   - [ ] `Stats` 클래스에 새로운 스탯 프로퍼티 추가 (getter/setter)
   - [ ] `StatController.Reset()` 메서드에 스탯 대입 코드 추가

### 최종 확인 사항

1. **컴파일 확인**: Unity 에디터에서 컴파일 에러가 없는지 확인
2. **구글 시트 확인**: 구글 시트의 컬럼 개수가 코드와 일치하는지 확인
3. **테스트**:
   - Unity 에디터에서 `ARPG > Download Table` 실행
   - 에러 없이 테이블이 다운로드되는지 확인
   - 캐릭터 생성 시 새로운 스탯 값이 정상적으로 로드되는지 확인
