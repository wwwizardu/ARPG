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
        [SerializeField, Min(8)] private int _gridWidth = 64;
        [SerializeField, Min(0.25f)] private float _cellSize = 1f;
        [SerializeField, Min(0f)] private float _clearance = 0.025f;
        [SerializeField, Range(0f, 0.5f)] private float _lookAheadTime = 0.18f;
        [SerializeField, Min(0f)] private float _sideHoldTime = 0.5f;
        [SerializeField, Min(0f)] private float _retryInterval = 0.12f;
        [SerializeField, Range(1, 3)] private int _recoveryIterations = 2;
        [SerializeField, Min(0f)] private float _recoverySpeed = 2f;
        [SerializeField] private int[] _excludedMonsterTableIds = Array.Empty<int>();

        public bool Enabled { get => _enabled; set => _enabled = value; }
        public bool AvoidanceEnabled { get => _avoidanceEnabled; set => _avoidanceEnabled = value; }
        public bool OverlapRecoveryEnabled { get => _overlapRecoveryEnabled; set => _overlapRecoveryEnabled = value; }
        public int GridWidth { get => Mathf.Clamp(_gridWidth, 8, 256); set => _gridWidth = value; }
        public float CellSize { get => Mathf.Max(0.25f, _cellSize); set => _cellSize = value; }
        public float Clearance => Mathf.Max(0f, _clearance);
        public float LookAheadTime => Mathf.Clamp(_lookAheadTime, 0f, 0.5f);
        public float SideHoldTime => Mathf.Max(0f, _sideHoldTime);
        public float RetryInterval => Mathf.Max(0f, _retryInterval);
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
