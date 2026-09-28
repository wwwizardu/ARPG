#nullable enable
using System;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 개인 활동의 현재 표현 단계. ECS 엔티티 유무와 무관하게 NpcSaveData에 영속한다.
    /// </summary>
    public enum NpcActivityPhase
    {
        // 다음 활동을 선택할 수 있는 기본 상태.
        Idle = 0,
        // Hunt 같은 외부 활동의 종료 시각을 기다리는 상태.
        Venturing = 1,
        // 회복 종료 시각까지 활동 선택에서 제외되는 상태.
        Wounded = 2,
        // 영구 종료. NS2에서 관계/연대기 정리 훅을 연결한다.
        Dead = 3,
    }

    /// <summary>현재 활동의 종류. Idle 상태의 휴식 진행 여부도 구분한다.</summary>
    public enum NpcActivityKind
    {
        None = 0,
        Rest = 1,
        Hunt = 2,
        // 마을 상점 거래 (NPC_AMBITION_DESIGN D39). 1시간 뒤 종료 시점에 TradeAtShop 1회. 제자리 활동이라 Phase=Idle.
        Trade = 3,
    }

    /// <summary>오프스크린 Hunt 종료 시 단 한 번 결정되는 결과.</summary>
    public enum NpcHuntOutcome
    {
        None = 0,
        Success = 1,
        Failed = 2,
        Wounded = 3,
        Dead = 4,
    }

    /// <summary>
    /// NPC별 경량 활동 상태. 활동 중간에는 시간만 흐르고, 종료 시점에만 결과를 판정한다.
    /// </summary>
    [Serializable]
    public class NpcActivityState
    {
        // -1이면 개인 활동. 0 이상의 값이면 NpcPartyState가 활동 시간축과 결과를 소유한다.
        public int PartyId = -1;
        // Phase는 표현 단계, Kind는 그 단계에서 진행 중인 실제 활동을 분리해 표현한다.
        // 예: 휴식은 제자리 활동이므로 Phase=Idle, Kind=Rest로 저장한다.
        public NpcActivityPhase Phase = NpcActivityPhase.Idle;
        public NpcActivityKind Kind = NpcActivityKind.None;
        // v1은 "rest"/"hunt"만 사용한다. Travel·Dungeon 단계에서 Zone/목적지 키로 확장한다.
        public string TargetKey = string.Empty;
        // 활동 중 이동 경로의 양 끝점. Hunt v1은 Origin→Target→Origin의 단일 왕복 경로를 사용한다.
        public float OriginPositionX;
        public float OriginPositionY;
        public float TargetPositionX;
        public float TargetPositionY;
        public float ReturnPositionX;
        public float ReturnPositionY;
        // 전체 구간에서 TargetPosition에 도달하는 비율. 왕복 길이가 달라져도 일정 속도를 유지한다.
        public float OutboundFraction;
        public bool HasMovementPath;
        // 경로 재계산과 무관하게 Hunt 결과 난수의 정체성을 유지하는 최초 출발 시각.
        public float OutcomeSeedGameTime;
        // 게임시간 단위(hour). 실제 중간 시뮬레이션 없이 이 종료 시각에서만 상태를 전이한다.
        public float PhaseStartedGameTime;
        public float PhaseEndGameTime;
        // 보상·전기·연대기 반영(NS2)이 소비할 마지막 Hunt 결과 스냅샷.
        public NpcHuntOutcome LastHuntOutcome = NpcHuntOutcome.None;
        public int LastHuntFoodYield;
        public float LastOutcomeGameTime;
        // 마지막 부상 회복 종료 시각. "부상 회복 직후" 필요(NPC_AMBITION_DESIGN §6.2) 판정용. -1 = 기록 없음 (구 세이브 포함).
        public float RecoveredGameTime = -1f;

        [System.Xml.Serialization.XmlIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public UnityEngine.Vector2 OriginPosition
        {
            get => new UnityEngine.Vector2(OriginPositionX, OriginPositionY);
            set { OriginPositionX = value.x; OriginPositionY = value.y; }
        }

        [System.Xml.Serialization.XmlIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public UnityEngine.Vector2 TargetPosition
        {
            get => new UnityEngine.Vector2(TargetPositionX, TargetPositionY);
            set { TargetPositionX = value.x; TargetPositionY = value.y; }
        }

        [System.Xml.Serialization.XmlIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public UnityEngine.Vector2 ReturnPosition
        {
            get => new UnityEngine.Vector2(ReturnPositionX, ReturnPositionY);
            set { ReturnPositionX = value.x; ReturnPositionY = value.y; }
        }

        public void EnsureInitialized()
        {
            // 구 세이브 JSON에는 새 필드가 없을 수 있으므로 문자열만 최소 보정한다.
            if (TargetKey == null)
                TargetKey = string.Empty;
            if (HasMovementPath && OutboundFraction <= 0f)
            {
                // v7 이전 왕복 경로는 Origin→Target→Origin만 저장했으므로 귀환점과 비율을 복원한다.
                ReturnPosition = OriginPosition;
                OutboundFraction = 0.5f;
            }
            if (Kind == NpcActivityKind.Hunt && OutcomeSeedGameTime <= 0f)
                OutcomeSeedGameTime = PhaseStartedGameTime;
            if (PartyId < -1)
                PartyId = -1;
        }
    }
}
