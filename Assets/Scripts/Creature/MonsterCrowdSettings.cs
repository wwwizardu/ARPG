using System;
using UnityEngine;

namespace ARPG.Monster
{
    [Serializable]
    public sealed class MonsterCrowdSettings
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private bool _avoidanceEnabled = true;
        [SerializeField] private bool _overlapRecoveryEnabled = true;
        [SerializeField] private bool _playerBlockedByMonsters = true;
        [SerializeField] private bool _npcBlockedByMonsters = true;
        [SerializeField, Min(8)] private int _gridWidth = 64;
        [SerializeField, Min(0.25f)] private float _cellSize = 1f;
        [SerializeField, Min(0f)] private float _clearance = 0.025f;
        [SerializeField, Range(0f, 0.5f)] private float _lookAheadTime = 0.18f;
        [SerializeField, Min(0f)] private float _sideHoldTime = 0.5f;
        [SerializeField, Min(0f)] private float _retryInterval = 0.12f;
        [SerializeField, Min(0f)] private float _passWaitTime = 1f;
        [SerializeField, Min(0f)] private float _passHoldTime = 0.3f;
        [SerializeField, Range(1, 8)] private int _pathLookAheadTicks = 3;
        [SerializeField, Range(1, 3)] private int _recoveryIterations = 2;
        [SerializeField, Min(0f)] private float _recoverySpeed = 2f;
        [SerializeField] private int[] _excludedMonsterTableIds = Array.Empty<int>();

        public bool Enabled { get => _enabled; set => _enabled = value; }
        public bool AvoidanceEnabled { get => _avoidanceEnabled; set => _avoidanceEnabled = value; }
        public bool OverlapRecoveryEnabled { get => _overlapRecoveryEnabled; set => _overlapRecoveryEnabled = value; }
        /// <summary>플레이어가 몬스터 몸에 막혀 미끄러진다. 몬스터 이동은 플레이어와 무관하다. 군집 이동이 꺼지면 함께 꺼진다.</summary>
        public bool PlayerBlockedByMonsters { get => _playerBlockedByMonsters; set => _playerBlockedByMonsters = value; }
        /// <summary>실체화된 NPC도 플레이어처럼 몬스터 몸에 막혀 미끄러진다. 몬스터 이동은 NPC와 무관하다. 군집 이동이 꺼지면 함께 꺼진다.</summary>
        public bool NpcBlockedByMonsters { get => _npcBlockedByMonsters; set => _npcBlockedByMonsters = value; }
        public int GridWidth { get => Mathf.Clamp(_gridWidth, 8, 256); set => _gridWidth = value; }
        public float CellSize { get => Mathf.Max(0.25f, _cellSize); set => _cellSize = value; }
        public float Clearance => Mathf.Max(0f, _clearance);
        public float LookAheadTime => Mathf.Clamp(_lookAheadTime, 0f, 0.5f);
        public float SideHoldTime => Mathf.Max(0f, _sideHoldTime);
        public float RetryInterval => Mathf.Max(0f, _retryInterval);
        /// <summary>서로 막은 두 개체가 이만큼 기다리면 그 둘만 서로 통과한다 (한쪽만 기다리는 줄 서기는 유지).</summary>
        public float PassWaitTime => Mathf.Max(0f, _passWaitTime);
        /// <summary>통과 상대와 떨어진 뒤에도 통과 상태를 유지하는 시간.</summary>
        public float PassHoldTime => Mathf.Max(0f, _passHoldTime);
        /// <summary>앞 경유점 직선 확인 간격(고정 틱, 50Hz 기준 3 = 0.06초). 개체마다 실행 틱을 엇갈리게 한다.</summary>
        public int PathLookAheadTicks => Mathf.Clamp(_pathLookAheadTicks, 1, 8);
        public int RecoveryIterations => Mathf.Clamp(_recoveryIterations, 1, 3);
        public float RecoverySpeed => Mathf.Max(0f, _recoverySpeed);
        public int[] ExcludedMonsterTableIds
        {
            get => _excludedMonsterTableIds;
            set => _excludedMonsterTableIds = value ?? Array.Empty<int>();
        }

        public bool Includes(int monsterTableId)
        {
            if (_excludedMonsterTableIds == null)
                return true;
            for (int i = 0; i < _excludedMonsterTableIds.Length; i++)
            {
                if (_excludedMonsterTableIds[i] == monsterTableId)
                    return false;
            }
            return true;
        }
    }
}
