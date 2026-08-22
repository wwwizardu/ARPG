#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Data
{
    [Serializable]
    public class ItemData
    {
        public int Id;

        public int ItemInstanceId;

        public EquipmentData? Equipment;

        // 스킬북 시스템 (SKILLBOOK_DESIGN.md §3.2) — Table.ItemType==SkillBook 일 때만 셋
        public SkillBookData? SkillBook;

        // 스킬 페이지 시스템 (SKILL_RUNE_DESIGN.md) — Table.ItemType==SkillPage 일 때만 셋
        public SkillPageData? SkillPage;

        public int Quantity;

        [NonSerialized] public ItemTable? Table;

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.GetItem(Id);
            }

#if UNITY_EDITOR
            if (Equipment == null && Table != null && Table.ItemType == GlobalEnum.ItemType.Equipment)
            {
                // 테이블 업데이트 등으로 Equipment가 누락된 경우 복구 (에디터 전용)
                Equipment = AR.s.Item.RepairEquipmentData(Table);
            }
#endif

            if (Equipment != null)
            {
                Equipment.OnLoadCompleted(Table);
            }

            if (SkillBook != null)
            {
                SkillBook.OnLoadCompleted();
            }

            if (SkillPage != null)
            {
                SkillPage.OnLoadCompleted();
            }
        }
    }

    /// <summary>
    /// 스킬북 인스턴스 데이터. 같은 ItemId(등급 책)라도 SkillId가 다르면 다른 책으로 취급.
    /// EquipmentData가 ItemData의 인스턴스 변동분이듯, SkillBookData도 같은 패턴.
    /// </summary>
    [Serializable]
    public class SkillBookData
    {
        public int SkillId;

        [JsonProperty("SocketedPageItems")]
        [SerializeReference]
        public List<ItemData> SocketedPages = new();

        public int PageCapacityBonus;

        public int PageSlotsBonus;

        [NonSerialized] public SkillTable? Table;

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.GetSkill(SkillId);
            }

            SocketedPages ??= new List<ItemData>();

            for (int i = 0; i < SocketedPages.Count; i++)
            {
                SocketedPages[i].OnLoadCompleted();
            }
        }
    }

    /// <summary>
    /// 스킬 페이지 인스턴스 데이터. 실제 효과는 SkillEffectTable 행을 참조한다.
    /// v1에서는 페이지 아이템 자체의 roll 없이 SkillEffectId만 저장한다.
    /// </summary>
    [Serializable]
    public class SkillPageData
    {
        public int SkillEffectId;

        [NonSerialized] public SkillEffectTable? Table;

        public void OnLoadCompleted()
        {
            if (Table == null)
            {
                Table = AR.s.Data.GetSkillEffect(SkillEffectId);
            }
        }
    }

    /// <summary>
    /// 데미지 범위 (Min, Max)
    /// </summary>
    [Serializable]
    public struct DamageRange
    {
        public int Min;
        public int Max;

        public void Add(int min, int max)
        {
            Min += min;
            Max += max;
        }

        public void ApplyIncrease(int percent)
        {
            if (percent <= 0) return;
            float mul = 1f + percent / 100f;
            Min = Mathf.RoundToInt(Min * mul);
            Max = Mathf.RoundToInt(Max * mul);
        }
    }

    /// <summary>
    /// 무기 기본 능력 계산 결과 캐시.
    /// ModSlot.Base와 WeaponLocalEnhancements만 반영하며 일반 장비 옵션은 포함하지 않는다.
    /// </summary>
    public struct WeaponStatCache
    {
        public DamageRange Physics;
        public DamageRange Fire;
        public DamageRange Ice;
        public DamageRange Lightning;
        public DamageRange Poison;
        public int CriRate;            // % (예: 5 = 5%)
        public float AttackSpeed;      // 초당 공격 횟수 (예: 1.2)
    }

    /// <summary>
    /// 개별 무기의 기본 능력에만 적용되는 고유 강화 원본 데이터.
    /// Value는 증가율(%)이며 일반 Mod 슬롯과 글로벌 StatModifier 경로를 사용하지 않는다.
    /// </summary>
    [Serializable]
    public class WeaponLocalEnhancementInstance
    {
        public GE.WeaponLocalStatType StatType;
        public int Value;
    }

    [Serializable]
    public class EquipmentData
    {
        public int Id;

        public int Quality;

        /// <summary>
        /// 모든 Mod (Base + Implicit + Prefix + Postfix) - 저장 대상
        /// </summary>
        public List<ModInstance> Mods = new();

        /// <summary>
        /// 개별 무기의 기본 능력에만 적용되는 고유 강화 목록 - 저장 대상.
        /// 일반 Mod와 달리 슬롯/테이블 참조가 없고 글로벌 스탯으로 등록되지 않는다.
        /// </summary>
        public List<WeaponLocalEnhancementInstance> WeaponLocalEnhancements = new();

        public GE.EquipmentTypeMask EquipType { get; set; }

        // ========== 무기 스탯 캐시 (NonSerialized) ==========
        [NonSerialized] private WeaponStatCache _weaponStats;
        [NonSerialized] private bool _weaponStatsDirty = true;

        /// <summary>
        /// Base와 무기 고유 강화가 반영된 최종 무기 기본 능력.
        /// 관련 데이터 변경 시 InvalidateWeaponStats() 호출 필요.
        /// </summary>
        public WeaponStatCache WeaponStats
        {
            get
            {
                if (_weaponStatsDirty)
                {
                    RecomputeWeaponStats();
                    _weaponStatsDirty = false;
                }
                return _weaponStats;
            }
        }

        /// <summary>
        /// 캐시 무효화. Mod 추가/제거/수정 시 호출.
        /// </summary>
        public void InvalidateWeaponStats()
        {
            _weaponStatsDirty = true;
        }

        /// <summary>
        /// 재료 서비스가 준비되기 전에도 안전하게 고유 강화 원본을 기록할 수 있는 데이터 진입점.
        /// 상한/가중치/재료 소비는 향후 전용 서비스가 검증하며 여기서는 무기 여부와 양수 값만 보장한다.
        /// </summary>
        public bool AddWeaponLocalEnhancement(GE.WeaponLocalStatType statType, int value)
        {
            if (EquipType != GE.EquipmentTypeMask.Weapon || value <= 0)
                return false;

            WeaponLocalEnhancements ??= new List<WeaponLocalEnhancementInstance>();
            WeaponLocalEnhancements.Add(new WeaponLocalEnhancementInstance
            {
                StatType = statType,
                Value = value,
            });
            InvalidateWeaponStats();
            return true;
        }

        public int GetWeaponLocalEnhancementTotal(GE.WeaponLocalStatType statType)
        {
            if (WeaponLocalEnhancements == null)
                return 0;

            int total = 0;
            for (int i = 0; i < WeaponLocalEnhancements.Count; i++)
            {
                WeaponLocalEnhancementInstance enhancement = WeaponLocalEnhancements[i];
                if (enhancement != null && enhancement.StatType == statType)
                    total += enhancement.Value;
            }
            return total;
        }

        public void OnLoadCompleted(ItemTable? inItemTable)
        {
            Mods ??= new List<ModInstance>();
            WeaponLocalEnhancements ??= new List<WeaponLocalEnhancementInstance>();

            if (inItemTable == null)
            {
                Debug.LogError("[EquipmentData] OnLoadCompleted() - ItemTable is null");
                return;
            }

            EquipType = Utils.CategoryToEquipmentTypeMask(inItemTable.Category);

            // ModInstance의 테이블 참조 연결
            for (int i = 0; i < Mods.Count; i++)
            {
                Mods[i].OnLoadCompleted();
            }

            PurgeIncompatibleMods();

            InvalidateWeaponStats();
        }

        /// <summary>
        /// 자기 EquipType에 허용되지 않은 Prefix/Postfix mod 제거.
        /// Base/Implicit slot은 기획상 슬롯 제약 우회가 가능하므로 정리 대상에서 제외.
        /// </summary>
        private void PurgeIncompatibleMods()
        {
            if (Utils.IsSingleEquipmentTypeMask(EquipType) == false)
            {
                Debug.LogWarning($"[EquipmentData] Invalid EquipType mask: {EquipType}");
                return;
            }

            for (int i = Mods.Count - 1; i >= 0; i--)
            {
                ModInstance mod = Mods[i];
                if (mod.Table == null)
                    continue;
                if (mod.Slot == GE.ModSlot.Implicit || mod.Slot == GE.ModSlot.Base)
                    continue;
                if (Utils.ContainsEquipmentType(mod.Table.AllowedEquipTypes, EquipType) == false)
                {
                    Debug.LogWarning($"[EquipmentData] Purged incompatible mod from {EquipType}: {mod.Table.Name} (Allowed={mod.Table.AllowedEquipTypes})");
                    Mods.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 무기 기본 능력 계산:
        /// 1. ModSlot.Base만 읽어 고정 기본 능력 구성
        /// 2. WeaponLocalEnhancements의 증가율을 종류별로 합산
        /// 3. 각 기본 능력에 증가율 합계를 한 번 적용
        /// Implicit/Prefix/Postfix는 장착자의 글로벌 옵션이므로 이 캐시에서 제외한다.
        /// </summary>
        private void RecomputeWeaponStats()
        {
            WeaponStatCache cache = new WeaponStatCache();

            for (int i = 0; i < Mods.Count; i++)
            {
                ModInstance mod = Mods[i];
                if (mod.Slot != GE.ModSlot.Base || mod.Table == null)
                    continue;

                switch (mod.Table.EffectType)
                {
                    // Added 데미지 (Value1=min, Value2=max)
                    case GE.ModEffectType.AddedPhysDamage:
                        cache.Physics.Add(mod.Value1, mod.Value2);
                        break;
                    case GE.ModEffectType.AddedFireDamage:
                        cache.Fire.Add(mod.Value1, mod.Value2);
                        break;
                    case GE.ModEffectType.AddedIceDamage:
                        cache.Ice.Add(mod.Value1, mod.Value2);
                        break;
                    case GE.ModEffectType.AddedLightningDamage:
                        cache.Lightning.Add(mod.Value1, mod.Value2);
                        break;
                    case GE.ModEffectType.AddedPoisonDamage:
                        cache.Poison.Add(mod.Value1, mod.Value2);
                        break;

                    // Flat Stat: 단일 스탯 Add
                    case GE.ModEffectType.FlatStat:
                        AccumulateFlatStat(ref cache, mod);
                        break;
                }
            }

            int physIncPct = 0;
            int fireIncPct = 0;
            int iceIncPct = 0;
            int lightIncPct = 0;
            int poisonIncPct = 0;
            int critIncPct = 0;
            int asIncPct = 0;

            for (int i = 0; i < WeaponLocalEnhancements.Count; i++)
            {
                WeaponLocalEnhancementInstance enhancement = WeaponLocalEnhancements[i];
                if (enhancement == null || enhancement.Value == 0)
                    continue;

                switch (enhancement.StatType)
                {
                    case GE.WeaponLocalStatType.PhysicsDamage:
                        physIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.FireDamage:
                        fireIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.IceDamage:
                        iceIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.LightningDamage:
                        lightIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.PoisonDamage:
                        poisonIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.CriticalRate:
                        critIncPct += enhancement.Value;
                        break;
                    case GE.WeaponLocalStatType.AttackSpeed:
                        asIncPct += enhancement.Value;
                        break;
                }
            }

            cache.Physics.ApplyIncrease(physIncPct);
            cache.Fire.ApplyIncrease(fireIncPct);
            cache.Ice.ApplyIncrease(iceIncPct);
            cache.Lightning.ApplyIncrease(lightIncPct);
            cache.Poison.ApplyIncrease(poisonIncPct);

            if (critIncPct > 0)
            {
                cache.CriRate = Mathf.RoundToInt(cache.CriRate * (1f + critIncPct / 100f));
            }

            if (asIncPct > 0)
            {
                cache.AttackSpeed *= (1f + asIncPct / 100f);
            }

            _weaponStats = cache;
        }

        private static void AccumulateFlatStat(ref WeaponStatCache cache, ModInstance mod)
        {
            if (mod.Table == null) return;

            switch (mod.Table.TargetStat)
            {
                case GE.Stat.AttackMin:
                    cache.Physics.Min += mod.Value1;
                    break;
                case GE.Stat.AttackMax:
                    cache.Physics.Max += mod.Value1;
                    break;
                case GE.Stat.FireAttackMin:
                    cache.Fire.Min += mod.Value1;
                    break;
                case GE.Stat.FireAttackMax:
                    cache.Fire.Max += mod.Value1;
                    break;
                case GE.Stat.IceAttackMin:
                    cache.Ice.Min += mod.Value1;
                    break;
                case GE.Stat.IceAttackMax:
                    cache.Ice.Max += mod.Value1;
                    break;
                case GE.Stat.LightningAttackMin:
                    cache.Lightning.Min += mod.Value1;
                    break;
                case GE.Stat.LightningAttackMax:
                    cache.Lightning.Max += mod.Value1;
                    break;
                case GE.Stat.PoisonAttackMin:
                    cache.Poison.Min += mod.Value1;
                    break;
                case GE.Stat.PoisonAttackMax:
                    cache.Poison.Max += mod.Value1;
                    break;
                case GE.Stat.CriRate:
                    cache.CriRate += mod.Value1;
                    break;
                case GE.Stat.AttackSpeed:
                    // 100배 정수 저장 → 초당 횟수로 변환 (1.2 공속 = 120)
                    cache.AttackSpeed += mod.Value1 * 0.01f;
                    break;
            }
        }

        /// <summary>
        /// EquipmentBaseStatTable 기반으로 베이스 스탯 Mod 초기화 (모든 일반 장비의 표준 옵션).
        /// **고정값** — 베이스 스탯엔 RNG 없음. 같은 아이템은 항상 같은 베이스 스탯.
        /// Prefix/Postfix 모드가 랜덤 옵션을 담당.
        /// </summary>
        public void InitBaseMods(int itemId)
        {
            var baseStat = AR.s.Data.GetEquipmentBaseStat(itemId);
            if (baseStat == null)
                return;

            for (int slot = 0; slot < baseStat.Mods.Length; slot++)
            {
                var mod = baseStat.Mods[slot];
                if (mod == null)
                    continue;

                int modId = baseStat.GetModId(slot);
                (int value1, int value2) = baseStat.GetValues(slot);

                Mods.Add(new ModInstance
                {
                    ModTableId = modId,
                    Slot = GE.ModSlot.Base,
                    Tier = 0,   // Base는 Tier 시스템 사용 안 함 (아이템별 커스텀 수치)
                    Value1 = (ushort)value1,
                    Value2 = (ushort)value2,
                });
            }
        }

        /// <summary>
        /// ItemImplicitTable 기반으로 Implicit Mod 초기화 (유니크/특수 아이템의 추가 옵션).
        /// </summary>
        public void InitImplicitMods(int itemId)
        {
            var implicits = AR.s.Data.GetItemImplicits(itemId);

            for (int i = 0; i < implicits.Count; i++)
            {
                var imp = implicits[i];
                if (imp.TierData == null)
                    continue;

                ushort value1 = (ushort)UnityEngine.Random.Range(imp.TierData.Min1, imp.TierData.Max1 + 1);
                ushort value2 = (ushort)UnityEngine.Random.Range(imp.TierData.Min2, imp.TierData.Max2 + 1);

                Mods.Add(new ModInstance
                {
                    ModTableId = imp.ModId,
                    Slot = GE.ModSlot.Implicit,
                    Tier = imp.Tier,
                    Value1 = value1,
                    Value2 = value2,
                });
            }
        }

    }
}
