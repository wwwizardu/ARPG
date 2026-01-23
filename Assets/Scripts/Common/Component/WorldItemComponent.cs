#nullable enable

using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 월드에 떨어진 아이템 컴포넌트
    /// ECS 엔티티로 관리되며 GameObject(ItemObject)와 연동
    /// </summary>
    public struct WorldItemComponent
    {
        /// <summary>
        /// 아이템 테이블 ID
        /// </summary>
        public int ItemTableId;

        /// <summary>
        /// 아이템 엔티티 ID (고유 식별자)
        /// </summary>
        public int EntityId;

        /// <summary>
        /// 아이템 수량
        /// </summary>
        public int Quantity;

        /// <summary>
        /// 드랍된 시간 (Time.time 기준)
        /// </summary>
        public float DropTime;

        /// <summary>
        /// 자동 소멸 시간 (초, 0이면 소멸하지 않음)
        /// </summary>
        public float ExpireTime;

        /// <summary>
        /// 자동 줍기 가능 여부
        /// </summary>
        public bool AutoPickupEnabled;

        /// <summary>
        /// 자동 줍기 범위 (AutoPickupEnabled가 true일 때만 사용)
        /// </summary>
        public float AutoPickupRange;

        /// <summary>
        /// 만료되었는지 확인
        /// </summary>
        public readonly bool IsExpired(float currentTime)
        {
            if (ExpireTime <= 0f)
                return false;

            return (currentTime - DropTime) >= ExpireTime;
        }

        /// <summary>
        /// 남은 수명 (초)
        /// </summary>
        public readonly float GetRemainingTime(float currentTime)
        {
            if (ExpireTime <= 0f)
                return float.MaxValue;

            float elapsed = currentTime - DropTime;
            return Mathf.Max(0f, ExpireTime - elapsed);
        }
    }
}
