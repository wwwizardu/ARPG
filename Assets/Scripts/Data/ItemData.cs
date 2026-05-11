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
    /// 무기 Local 파이프라인 계산 결과 캐시
    /// Flat Mod + Increased% Mod 모두 반영된 최종 무기 스탯
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

    [Serializable]
    public class EquipmentData
    {
        public int Id;

        public int Quality;

        /// <summary>
        /// 모든 Mod (Implicit + Prefix + Postfix) - 저장 대상
        /// </summary>
        public List<ModInstance> Mods = new();

        public GE.EquipmentType EquipType { get; set; }

        // ========== 무기 스탯 캐시 (NonSerialized) ==========
        [NonSerialized] private WeaponStatCache _weaponStats;
        [NonSerialized] private bool _weaponStatsDirty = true;

        /// <summary>
        /// 무기 Local 파이프라인 완료 스탯 (최초 접근 시 lazy 계산, 이후 캐시 사용)
        /// Mod 변경 시 InvalidateWeaponStats() 호출 필요
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

        public void OnLoadCompleted(ItemTable? inItemTable)
        {
            if (inItemTable == null)
            {
                Debug.LogError("[EquipmentData] OnLoadCompleted() - ItemTable is null");
                return;
            }

            EquipType = Utils.CategoryToEquipmentType(inItemTable.Category);

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
        /// Implicit slot은 기획상 슬롯 제약 우회가 가능하므로 정리 대상에서 제외.
        /// </summary>
        private void PurgeIncompatibleMods()
        {
            GE.EquipmentTypeMask selfBit = Utils.EquipTypeToMaskBit(EquipType);
            for (int i = Mods.Count - 1; i >= 0; i--)
            {
                ModInstance mod = Mods[i];
                if (mod.Table == null)
                    continue;
                if (mod.Slot == GE.ModSlot.Implicit)
                    continue;
                if ((mod.Table.AllowedEquipTypes & selfBit) == 0)
                {
                    Debug.LogWarning($"[EquipmentData] Purged incompatible mod from {EquipType}: {mod.Table.Name} (Allowed={mod.Table.AllowedEquipTypes})");
                    Mods.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 무기 Local 파이프라인 계산:
        /// 1. Flat Mod 누적 (Added*, FlatStat TargetStat=무기전용)
        /// 2. Increased% Mod 누적
        /// 3. Flat × (1 + Increased%/100)
        /// </summary>
        private void RecomputeWeaponStats()
        {
            WeaponStatCache cache = new WeaponStatCache();

            // 속성별 Increased% 누적용 (FlatStat Target=AttackMin/Max 와 IncreasedStat Target=AttackMin/Max 모두 합산)
            int physIncPct = 0, fireIncPct = 0, iceIncPct = 0, lightIncPct = 0, poisonIncPct = 0;
            int critIncPct = 0;
            int asIncPct = 0;

            for (int i = 0; i < Mods.Count; i++)
            {
                ModInstance mod = Mods[i];
                if (mod.Table == null)
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

                    // Increased% Stat: 단일 스탯 비율 증가
                    case GE.ModEffectType.IncreasedStat:
                        AccumulateIncreasedStat(mod,
                            ref physIncPct, ref fireIncPct, ref iceIncPct, ref lightIncPct, ref poisonIncPct,
                            ref critIncPct, ref asIncPct);
                        break;
                }
            }

            // Increased% 적용 (Local 파이프라인의 마지막 단계)
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

        private static void AccumulateIncreasedStat(ModInstance mod,
            ref int physIncPct, ref int fireIncPct, ref int iceIncPct, ref int lightIncPct, ref int poisonIncPct,
            ref int critIncPct, ref int asIncPct)
        {
            if (mod.Table == null) return;

            switch (mod.Table.TargetStat)
            {
                case GE.Stat.AttackMin:
                case GE.Stat.AttackMax:
                    physIncPct += mod.Value1;
                    break;
                case GE.Stat.FireAttackMin:
                case GE.Stat.FireAttackMax:
                    fireIncPct += mod.Value1;
                    break;
                case GE.Stat.IceAttackMin:
                case GE.Stat.IceAttackMax:
                    iceIncPct += mod.Value1;
                    break;
                case GE.Stat.LightningAttackMin:
                case GE.Stat.LightningAttackMax:
                    lightIncPct += mod.Value1;
                    break;
                case GE.Stat.PoisonAttackMin:
                case GE.Stat.PoisonAttackMax:
                    poisonIncPct += mod.Value1;
                    break;
                case GE.Stat.CriRate:
                    critIncPct += mod.Value1;
                    break;
                case GE.Stat.AttackSpeed:
                case GE.Stat.AttackSpeedMul:
                    asIncPct += mod.Value1;
                    break;
            }
        }

        /// <summary>
        /// ItemImplicitTable 기반으로 Implicit Mod 초기화
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

        /// <summary>
        /// 특정 EffectType의 Mod 검색 (Value1 합산)
        /// </summary>
        public ushort GetModValue(GE.ModEffectType effectType)
        {
            ushort total = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null && Mods[i].Table.EffectType == effectType)
                    total += Mods[i].Value1;
            }
            return total;
        }

        /// <summary>
        /// 특정 EffectType의 Mod 데미지 범위 합산
        /// </summary>
        public (int min, int max) GetDamageRange(GE.ModEffectType effectType)
        {
            int min = 0, max = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null && Mods[i].Table.EffectType == effectType)
                {
                    min += Mods[i].Value1;
                    max += Mods[i].Value2;
                }
            }
            return (min, max);
        }

        public (int, int) GetPhysicsDamage()
        {
            return GetDamageRange(GE.ModEffectType.AddedPhysDamage);
        }

        public bool IsPhysicsDamage()
        {
            var (min, max) = GetPhysicsDamage();
            return min > 0 || max > 0;
        }

        public bool IsFireDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedFireDamage);
            return min > 0 || max > 0;
        }

        public bool IsIceDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedIceDamage);
            return min > 0 || max > 0;
        }

        public bool IsLightningDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedLightningDamage);
            return min > 0 || max > 0;
        }

        public bool IsPoisonDamage()
        {
            var (min, max) = GetDamageRange(GE.ModEffectType.AddedPoisonDamage);
            return min > 0 || max > 0;
        }

        public int GetCriticalRate()
        {
            int val = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null
                    && Mods[i].Table.EffectType == GE.ModEffectType.FlatStat
                    && Mods[i].Table.TargetStat == GE.Stat.CriRate)
                {
                    val += Mods[i].Value1;
                }
            }
            return val;
        }

        public float GetAttackSpeed()
        {
            ushort val = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Table != null
                    && Mods[i].Table.EffectType == GE.ModEffectType.FlatStat
                    && Mods[i].Table.TargetStat == GE.Stat.AttackSpeed)
                {
                    val += Mods[i].Value1;
                }
            }
            return val * 0.01f;
        }
    }
}
