#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Tables;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Item
{
    /// <summary>비교 행이 속한 툴팁 영역. 값 순서가 표시 순서다.</summary>
    public enum EquipmentComparisonSection
    {
        Base,         // 무기 기본 능력(WeaponStats) 또는 방어구 Base 옵션
        Enhancement,  // 무기 고유 강화 상세 (차이는 무기 기본 능력에 이미 반영)
        Options,      // Implicit·Prefix·Postfix 장비 옵션
    }

    /// <summary>`교체 시` 차이의 판단 색.</summary>
    public enum EquipmentComparisonTone
    {
        None,     // 차이 표시 없음
        Better,   // 높을수록 유리한 능력이 늘어남
        Worse,    // 높을수록 유리한 능력이 줄어듦
        Neutral,  // 이점이 섞였거나 판단 기준이 없음
    }

    /// <summary>선택 아이템과 장착 아이템 하나의 같은 능력 행. 한쪽에만 있으면 다른 쪽 문구는 null이다.</summary>
    public struct EquipmentComparisonRow
    {
        public long Key;                      // 여러 장착 카드의 행을 맞추는 키. 오름차순이 표시 순서다
        public EquipmentComparisonSection Section;
        public bool IsImplicit;
        public string? SelectedText;
        public string? EquippedText;
        public string? DiffText;              // `교체 시 …` (선택 − 장착). 차이가 없으면 null
        public EquipmentComparisonTone Tone;
    }

    /// <summary>
    /// 인벤토리 장비 비교 계산 (INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §능력 차이 표시).
    /// 두 아이템 데이터만 읽는 순수 로직이며 장착 카드마다 한 번 호출한다. 아이템·장비·저장 상태를 바꾸지 않는다.
    /// 무기 기본 능력은 WeaponStats(고유 강화 반영 캐시)를 그대로 쓴다.
    /// 옵션은 같은 ModTableId + 같은 ModSlot끼리만 비교하고, 그 밖의 옵션은 합치지 않고 추가·제거로 표시한다.
    /// </summary>
    public static class EquipmentComparisonCalculator
    {
        private enum ValueKind
        {
            Range,            // 최소~최대 (피해)
            Decimal2,         // 소수 둘째 자리 (초당 공격 횟수)
            PercentPoint,     // 확률·비율 % → 차이는 %p
            IncreasePercent,  // % 증가 → 차이는 증가율 %p
            Flat,             // 고정 수치
            PerSecond,        // Base 공격 속도 (Value1 × 0.01 /초)
            Status,           // 발동형·지원하지 않는 효과: 추가·제거·변경만
            InfoOnly,         // 무기 고유 강화 상세: 차이 없음
            Unknown,          // 테이블 참조 없음: 상세 확인 불가, 차이 없음
        }

        private struct Entry
        {
            public long Key;
            public EquipmentComparisonSection Section;
            public bool IsImplicit;
            public string Text;
            public ValueKind Kind;
            public int Raw1;
            public int Raw2;
            public float Decimal;
            public ModInstance? Mod;   // 가산 옵션 합산 시 문구를 다시 만들 원본
            public int Count;          // 같은 키로 합산된 옵션 수
            public int Tier;           // 합산된 옵션의 티어가 다르면 0
        }

        // 행 키 그룹 (값 순서 = 표시 순서)
        private const int KeyGroupWeaponBase = 0;
        private const int KeyGroupBaseMod = 1;
        private const int KeyGroupEnhancement = 2;
        private const int KeyGroupImplicit = 3;
        private const int KeyGroupPrefix = 4;
        private const int KeyGroupPostfix = 5;

        // 무기 기본 능력 행 (단일 툴팁과 같은 순서)
        private const int WeaponPhysics = 0;
        private const int WeaponFire = 1;
        private const int WeaponIce = 2;
        private const int WeaponLightning = 3;
        private const int WeaponPoison = 4;
        private const int WeaponCrit = 5;
        private const int WeaponAttackSpeed = 6;

        private const long OccurrenceMask = 0xFF;

        // 호출마다 비우고 다시 채우는 scratch 버퍼. 호출 사이에 상태를 남기지 않는다.
        private static readonly List<Entry> _selectedScratch = new();
        private static readonly List<Entry> _equippedScratch = new();

        /// <summary>
        /// 선택 아이템과 장착 아이템 하나를 비교해 키 순서의 행을 output에 채운다.
        /// equipped가 null(빈 칸)이면 선택 아이템 행만 채우고 차이를 붙이지 않는다.
        /// </summary>
        public static void Compare(ItemData selected, ItemData? equipped, List<EquipmentComparisonRow> output)
        {
            output.Clear();
            CollectEntries(selected, _selectedScratch);
            if (equipped != null)
                CollectEntries(equipped, _equippedScratch);
            else
                _equippedScratch.Clear();

            bool hasEquipped = equipped != null;
            int si = 0;
            int ei = 0;
            while (si < _selectedScratch.Count || ei < _equippedScratch.Count)
            {
                bool takeSelected = si < _selectedScratch.Count
                    && (ei >= _equippedScratch.Count || _selectedScratch[si].Key <= _equippedScratch[ei].Key);
                bool takeEquipped = ei < _equippedScratch.Count
                    && (si >= _selectedScratch.Count || _equippedScratch[ei].Key <= _selectedScratch[si].Key);

                EquipmentComparisonRow row = new EquipmentComparisonRow();
                if (takeSelected == true && takeEquipped == true)
                {
                    Entry s = _selectedScratch[si++];
                    Entry e = _equippedScratch[ei++];
                    FillRow(ref row, s);
                    row.SelectedText = s.Text;
                    row.EquippedText = e.Text;
                    SetChangedDiff(ref row, s, e);
                }
                else if (takeSelected == true)
                {
                    Entry s = _selectedScratch[si++];
                    FillRow(ref row, s);
                    row.SelectedText = s.Text;
                    if (hasEquipped == true)
                        SetOneSidedDiff(ref row, s.Kind, isAdded: true);
                }
                else
                {
                    Entry e = _equippedScratch[ei++];
                    FillRow(ref row, e);
                    row.EquippedText = e.Text;
                    SetOneSidedDiff(ref row, e.Kind, isAdded: false);
                }
                output.Add(row);
            }

            _selectedScratch.Clear();
            _equippedScratch.Clear();
        }

        private static void FillRow(ref EquipmentComparisonRow row, in Entry entry)
        {
            row.Key = entry.Key;
            row.Section = entry.Section;
            row.IsImplicit = entry.IsImplicit;
        }

        // ========== 차이 ==========

        private static void SetOneSidedDiff(ref EquipmentComparisonRow row, ValueKind kind, bool isAdded)
        {
            if (kind == ValueKind.InfoOnly || kind == ValueKind.Unknown)
                return;

            row.DiffText = isAdded == true ? "교체 시 추가" : "교체 시 제거";
            if (kind == ValueKind.Status)
                row.Tone = EquipmentComparisonTone.Neutral;
            else
                row.Tone = isAdded == true ? EquipmentComparisonTone.Better : EquipmentComparisonTone.Worse;
        }

        private static void SetChangedDiff(ref EquipmentComparisonRow row, in Entry s, in Entry e)
        {
            switch (s.Kind)
            {
                case ValueKind.Range:
                    {
                        int dMin = s.Raw1 - e.Raw1;
                        int dMax = s.Raw2 - e.Raw2;
                        if (dMin == 0 && dMax == 0)
                            return;
                        row.DiffText = $"교체 시 {FormatSigned(dMin)}~{FormatSigned(dMax)}";
                        if (dMin >= 0 && dMax >= 0)
                            row.Tone = EquipmentComparisonTone.Better;
                        else if (dMin <= 0 && dMax <= 0)
                            row.Tone = EquipmentComparisonTone.Worse;
                        else
                            row.Tone = EquipmentComparisonTone.Neutral;
                        return;
                    }
                case ValueKind.Decimal2:
                    {
                        // 표시값(소수 둘째 자리) 기준으로 비교해 표시상 같은 값에 차이를 붙이지 않는다
                        float d = Mathf.Round(s.Decimal * 100f) - Mathf.Round(e.Decimal * 100f);
                        if (d == 0f)
                            return;
                        row.DiffText = $"교체 시 {(d * 0.01f).ToString("+0.00;-0.00")}";
                        row.Tone = d > 0f ? EquipmentComparisonTone.Better : EquipmentComparisonTone.Worse;
                        return;
                    }
                case ValueKind.PerSecond:
                    {
                        int d = s.Raw1 - e.Raw1;
                        if (d == 0)
                            return;
                        row.DiffText = $"교체 시 {(d * 0.01f).ToString("+0.##;-0.##")}/초";
                        row.Tone = d > 0 ? EquipmentComparisonTone.Better : EquipmentComparisonTone.Worse;
                        return;
                    }
                case ValueKind.PercentPoint:
                case ValueKind.IncreasePercent:
                case ValueKind.Flat:
                    {
                        int d = s.Raw1 - e.Raw1;
                        if (d == 0)
                            return;
                        if (s.Kind == ValueKind.PercentPoint)
                            row.DiffText = $"교체 시 {FormatSigned(d)}%p";
                        else if (s.Kind == ValueKind.IncreasePercent)
                            row.DiffText = $"교체 시 증가율 {FormatSigned(d)}%p";
                        else
                            row.DiffText = $"교체 시 {FormatSigned(d)}";
                        row.Tone = d > 0 ? EquipmentComparisonTone.Better : EquipmentComparisonTone.Worse;
                        return;
                    }
                case ValueKind.Status:
                    if (s.Raw1 != e.Raw1 || s.Raw2 != e.Raw2)
                    {
                        row.DiffText = "교체 시 변경";
                        row.Tone = EquipmentComparisonTone.Neutral;
                    }
                    return;
                default:
                    // InfoOnly·Unknown: 차이 없음
                    return;
            }
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }

        // ========== 행 수집 ==========

        private static void CollectEntries(ItemData item, List<Entry> output)
        {
            output.Clear();
            EquipmentData? equip = item.Equipment;
            if (equip == null)
                return;

            bool isWeapon = equip.EquipType == GE.EquipmentTypeMask.Weapon;
            if (isWeapon == true)
            {
                WeaponStatCache stats = equip.WeaponStats;
                AddWeaponDamage(output, WeaponPhysics, "물리 피해", stats.Physics);
                AddWeaponDamage(output, WeaponFire, "화염 피해", stats.Fire);
                AddWeaponDamage(output, WeaponIce, "냉기 피해", stats.Ice);
                AddWeaponDamage(output, WeaponLightning, "번개 피해", stats.Lightning);
                AddWeaponDamage(output, WeaponPoison, "독 피해", stats.Poison);

                string? critText = EquipmentOptionText.FormatWeaponCritLine(stats.CriRate);
                if (critText != null)
                    output.Add(CreateEntry(MakeKey(KeyGroupWeaponBase, WeaponCrit, 0), EquipmentComparisonSection.Base,
                        false, critText, ValueKind.PercentPoint, stats.CriRate, 0));

                string? asText = EquipmentOptionText.FormatWeaponAttackSpeedLine(stats.AttackSpeed);
                if (asText != null)
                {
                    Entry asEntry = CreateEntry(MakeKey(KeyGroupWeaponBase, WeaponAttackSpeed, 0), EquipmentComparisonSection.Base,
                        false, asText, ValueKind.Decimal2, 0, 0);
                    asEntry.Decimal = stats.AttackSpeed;
                    output.Add(asEntry);
                }

                for (int i = 0; i < equip.WeaponLocalEnhancements.Count; i++)
                {
                    WeaponLocalEnhancementInstance enhancement = equip.WeaponLocalEnhancements[i];
                    if (enhancement == null || enhancement.Value == 0)
                        continue;
                    long baseKey = MakeKey(KeyGroupEnhancement, (int)enhancement.StatType, 0);
                    output.Add(CreateEntry(baseKey + CountOccurrences(output, baseKey), EquipmentComparisonSection.Enhancement,
                        false, EquipmentOptionText.FormatWeaponEnhancementLine(enhancement), ValueKind.InfoOnly, enhancement.Value, 0));
                }
            }

            for (int i = 0; i < equip.Mods.Count; i++)
            {
                ModInstance mod = equip.Mods[i];
                if (mod == null)
                    continue;
                // 무기 Base는 WeaponStats가 이미 표시한다 (단일 툴팁·장착 처리와 같은 규칙)
                if (isWeapon == true && mod.Slot == GE.ModSlot.Base)
                    continue;
                AddModEntry(output, mod);
            }

            for (int i = 0; i < output.Count; i++)
            {
                Entry entry = output[i];
                if (entry.Count <= 1 || entry.Mod == null)
                    continue;
                ModInstance merged = new ModInstance
                {
                    ModTableId = entry.Mod.ModTableId,
                    Slot = entry.Mod.Slot,
                    Tier = entry.Tier,
                    Value1 = ClampToUShort(entry.Raw1),
                    Value2 = ClampToUShort(entry.Raw2),
                    Table = entry.Mod.Table,
                };
                entry.Text = FormatModText(merged);
                output[i] = entry;
            }

            SortByKey(output);
        }

        private static void AddWeaponDamage(List<Entry> output, int id, string label, DamageRange range)
        {
            string? text = EquipmentOptionText.FormatWeaponDamageLine(label, range);
            if (text == null)
                return;
            output.Add(CreateEntry(MakeKey(KeyGroupWeaponBase, id, 0), EquipmentComparisonSection.Base,
                false, text, ValueKind.Range, range.Min, range.Max));
        }

        private static void AddModEntry(List<Entry> output, ModInstance mod)
        {
            int group = mod.Slot switch
            {
                GE.ModSlot.Base => KeyGroupBaseMod,
                GE.ModSlot.Implicit => KeyGroupImplicit,
                GE.ModSlot.Prefix => KeyGroupPrefix,
                _ => KeyGroupPostfix,
            };
            EquipmentComparisonSection section = mod.Slot == GE.ModSlot.Base
                ? EquipmentComparisonSection.Base
                : EquipmentComparisonSection.Options;
            bool isImplicit = mod.Slot == GE.ModSlot.Implicit;
            long baseKey = MakeKey(group, mod.ModTableId, 0);

            if (mod.Table == null)
            {
                string unknownText = $"[{EquipmentOptionText.GetSlotName(mod.Slot)}] 알 수 없는 옵션 #{mod.ModTableId} · 상세 확인 불가";
                output.Add(CreateEntry(baseKey + CountOccurrences(output, baseKey), section, isImplicit,
                    unknownText, ValueKind.Unknown, mod.Value1, mod.Value2));
                return;
            }

            ValueKind kind = GetValueKind(mod.Table, mod.Slot);
            if (kind != ValueKind.Status)
            {
                // 같은 키의 가산 옵션은 한 행으로 합산한다
                for (int i = 0; i < output.Count; i++)
                {
                    if (output[i].Key != baseKey)
                        continue;
                    Entry merged = output[i];
                    merged.Raw1 += mod.Value1;
                    merged.Raw2 += mod.Value2;
                    merged.Count++;
                    if (merged.Tier != mod.Tier)
                        merged.Tier = 0;
                    output[i] = merged;
                    return;
                }
            }

            long key = kind == ValueKind.Status ? baseKey + CountOccurrences(output, baseKey) : baseKey;
            Entry entry = CreateEntry(key, section, isImplicit, FormatModText(mod), kind, mod.Value1, mod.Value2);
            entry.Mod = mod;
            entry.Tier = mod.Tier;
            output.Add(entry);
        }

        /// <summary>
        /// 의미가 확인된 가산형 수치 옵션만 수치 종류를 정하고, 나머지(발동형·계산 시 조회·미지원 효과)는 상태만 표시한다.
        /// 가산 판정은 장착 처리(EquipHelper.ApplyPassiveMod)가 StatModifier로 더하는 효과와 같다.
        /// </summary>
        private static ValueKind GetValueKind(ModTable table, GE.ModSlot slot)
        {
            if (table.ApplyType != GE.ModApplyType.Passive)
                return ValueKind.Status;

            switch (table.EffectType)
            {
                case GE.ModEffectType.AddedPhysDamage:
                case GE.ModEffectType.AddedFireDamage:
                case GE.ModEffectType.AddedIceDamage:
                case GE.ModEffectType.AddedLightningDamage:
                case GE.ModEffectType.AddedPoisonDamage:
                    return ValueKind.Range;
                case GE.ModEffectType.IncreasedStat:
                    return ValueKind.IncreasePercent;
                case GE.ModEffectType.FlatStat:
                    // 비-Base AttackSpeed는 증가율 의미다 (EquipHelper.ApplyPassiveMod·EquipmentOptionText와 같은 규칙)
                    if (table.TargetStat == GE.Stat.AttackSpeed)
                        return slot == GE.ModSlot.Base ? ValueKind.PerSecond : ValueKind.IncreasePercent;
                    return EquipmentOptionText.IsPercentFlatStat(table.TargetStat) == true
                        ? ValueKind.PercentPoint
                        : ValueKind.Flat;
                default:
                    return ValueKind.Status;
            }
        }

        private static string FormatModText(ModInstance mod)
        {
            // 방어구 Base는 단일 툴팁처럼 문구만, 장비 옵션은 슬롯·티어를 문맥으로 붙인다
            string line = mod.Slot == GE.ModSlot.Base
                ? EquipmentOptionText.FormatModLine(mod)
                : EquipmentOptionText.FormatOptionLine(mod);
            string condition = mod.Table != null
                ? EquipmentOptionText.FormatSkillTagCondition(mod.Table.Tags)
                : string.Empty;
            return condition.Length > 0 ? $"{line} ({condition})" : line;
        }

        private static Entry CreateEntry(long key, EquipmentComparisonSection section, bool isImplicit,
            string text, ValueKind kind, int raw1, int raw2)
        {
            return new Entry
            {
                Key = key,
                Section = section,
                IsImplicit = isImplicit,
                Text = text,
                Kind = kind,
                Raw1 = raw1,
                Raw2 = raw2,
                Count = 1,
            };
        }

        private static long MakeKey(int group, int id, int occurrence)
        {
            // [group 8비트][id 32비트][occurrence 8비트]
            return ((long)group << 48) | ((long)(uint)id << 8) | ((long)occurrence & OccurrenceMask);
        }

        private static int CountOccurrences(List<Entry> entries, long baseKey)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if ((entries[i].Key & ~OccurrenceMask) == baseKey)
                    count++;
            }
            return count;
        }

        private static void SortByKey(List<Entry> entries)
        {
            // 행 수가 적어 삽입 정렬로 충분하다 (비교 델리게이트를 만들지 않는다)
            for (int i = 1; i < entries.Count; i++)
            {
                Entry current = entries[i];
                int j = i - 1;
                while (j >= 0 && entries[j].Key > current.Key)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = current;
            }
        }

        private static ushort ClampToUShort(int value)
        {
            if (value <= 0) return 0;
            return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
        }
    }
}
