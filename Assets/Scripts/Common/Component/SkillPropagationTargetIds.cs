using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// Per-propagation-root target reservation set. IDs stay inline so a root has no
    /// heap-owned mutable collection and all branches share one deterministic record.
    /// </summary>
    public struct SkillPropagationTargetIds
    {
        public const int Capacity = 16;

        /// <summary>
        /// OnHit 루트가 source hit 대상으로 먼저 쓰는 슬롯 수.
        /// SkillPropagationRuntime.TryStartOnHit이 첫 전달 생성 전에 예약한다.
        /// </summary>
        public const int RootSourceReservation = 1;

        /// <summary>
        /// 전달 1건이 예약하는 최대 슬롯 수.
        /// Instant는 선택한 대상만 적중하므로 1칸.
        /// Projectile은 비행 경로에서 선택 대상이 아닌 대상을 맞을 수 있어
        /// 선택 대상 + 실제 적중 대상으로 최대 2칸을 쓴다.
        /// </summary>
        public const int InstantReservationsPerDelivery = 1;
        public const int ProjectileReservationsPerDelivery = 2;

        /// <summary>
        /// Capacity를 넘기지 않고 생성할 수 있는 루트당 전달 수 상한.
        /// Inherit는 스킬의 ProjectileId에 따라 런타임에 Projectile로 해석될 수 있어
        /// Projectile과 같은 보수적 상한을 적용한다.
        /// </summary>
        public static int GetMaxTotalDeliveries(GE.SkillDeliveryType inDeliveryType)
        {
            int reservationsPerDelivery = inDeliveryType == GE.SkillDeliveryType.Instant
                ? InstantReservationsPerDelivery
                : ProjectileReservationsPerDelivery;
            return (Capacity - RootSourceReservation) / reservationsPerDelivery;
        }

        public int Count;
        private int _0, _1, _2, _3, _4, _5, _6, _7;
        private int _8, _9, _10, _11, _12, _13, _14, _15;

        public bool Contains(int inEntityId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Get(i) == inEntityId)
                    return true;
            }

            return false;
        }

        public bool TryAdd(int inEntityId)
        {
            if (inEntityId <= 0 || Contains(inEntityId) == true)
                return false;

            if (Count >= Capacity)
            {
                Debug.LogError($"[SkillPropagationTargetIds] Capacity({Capacity}) exceeded; entity {inEntityId} was not reserved.");
                return false;
            }

            switch (Count)
            {
                case 0: _0 = inEntityId; break;
                case 1: _1 = inEntityId; break;
                case 2: _2 = inEntityId; break;
                case 3: _3 = inEntityId; break;
                case 4: _4 = inEntityId; break;
                case 5: _5 = inEntityId; break;
                case 6: _6 = inEntityId; break;
                case 7: _7 = inEntityId; break;
                case 8: _8 = inEntityId; break;
                case 9: _9 = inEntityId; break;
                case 10: _10 = inEntityId; break;
                case 11: _11 = inEntityId; break;
                case 12: _12 = inEntityId; break;
                case 13: _13 = inEntityId; break;
                case 14: _14 = inEntityId; break;
                case 15: _15 = inEntityId; break;
            }

            Count++;
            return true;
        }

        private int Get(int inIndex)
        {
            switch (inIndex)
            {
                case 0: return _0;
                case 1: return _1;
                case 2: return _2;
                case 3: return _3;
                case 4: return _4;
                case 5: return _5;
                case 6: return _6;
                case 7: return _7;
                case 8: return _8;
                case 9: return _9;
                case 10: return _10;
                case 11: return _11;
                case 12: return _12;
                case 13: return _13;
                case 14: return _14;
                case 15: return _15;
                default:
                    Debug.LogError($"[SkillPropagationTargetIds] Index out of range: {inIndex}.");
                    return 0;
            }
        }
    }
}
