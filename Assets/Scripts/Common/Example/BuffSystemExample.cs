using ARPG.Component;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Example
{
    /// <summary>
    /// 버프 시스템 사용 예시
    /// </summary>
    public class BuffSystemExample : MonoBehaviour
    {
        private int _playerEntityId = -1;

        private void Start()
        {
            // 플레이어 Entity 생성
            _playerEntityId = EntityIdHelper.CreateEntity();

            // 스탯 컴포넌트 추가
            StatComponent stat = new StatComponent
            {
                BaseStr = 10,
                BaseDex = 10,
                BaseInt = 10,
                BaseMaxHp = 100,
                BaseAttackMin = 10,
                BaseAttackMax = 20,
                BaseMoveSpeed = 5
            };
            stat.CopyBaseToFinal();
            AR.s.Component.AddComponent(_playerEntityId, stat);

            Debug.Log("=== Buff System Example Started ===");
            Debug.Log($"Initial Stats - HP: {stat.FinalMaxHp}, Attack: {stat.FinalAttackMin}-{stat.FinalAttackMax}, Speed: {stat.FinalMoveSpeed}");
        }

        private void Update()
        {
            // // 1키: 공격력 버프 추가 (10초)
            // if (Input.GetKeyDown(KeyCode.Alpha1))
            // {
            //     int buffId = BuffHelper.AddBuff(_playerEntityId, 1001, 10f);
            //     Debug.Log($"[Example] Added Attack Buff (ID: {buffId})");
            //     PrintCurrentStats();
            // }

            // // 2키: 방어력 버프 추가 (5초)
            // if (Input.GetKeyDown(KeyCode.Alpha2))
            // {
            //     int buffId = BuffHelper.AddBuff(_playerEntityId, 1002, 5f);
            //     Debug.Log($"[Example] Added Defense Buff (ID: {buffId})");
            //     PrintCurrentStats();
            // }

            // // 3키: 이동속도 버프 추가 (8초)
            // if (Input.GetKeyDown(KeyCode.Alpha3))
            // {
            //     int buffId = BuffHelper.AddBuff(_playerEntityId, 1003, 8f);
            //     Debug.Log($"[Example] Added Speed Buff (ID: {buffId})");
            //     PrintCurrentStats();
            // }

            // // 4키: 모든 버프 제거
            // if (Input.GetKeyDown(KeyCode.Alpha4))
            // {
            //     int removedCount = BuffHelper.RemoveAllBuffs(_playerEntityId);
            //     Debug.Log($"[Example] Removed all buffs (Count: {removedCount})");
            //     PrintCurrentStats();
            // }

            // // 5키: 버프 상태 출력
            // if (Input.GetKeyDown(KeyCode.Alpha5))
            // {
            //     PrintBuffStatus();
            //     PrintCurrentStats();
            // }
        }

        /// <summary>
        /// 현재 스탯 출력
        /// </summary>
        private void PrintCurrentStats()
        {
            if (AR.s.Component.TryGetComponent<StatComponent>(_playerEntityId, out StatComponent stat))
            {
                Debug.Log($"[Stats] HP: {stat.FinalMaxHp}, Attack: {stat.FinalAttackMin}-{stat.FinalAttackMax}, " +
                          $"Defense: {stat.FinalDefense}, Speed: {stat.FinalMoveSpeed}");
            }
        }

        /// <summary>
        /// 버프 상태 출력
        /// </summary>
        private void PrintBuffStatus()
        {
            int buffCount = BuffHelper.GetBuffCount(_playerEntityId);
            Debug.Log($"[Buffs] Total: {buffCount}");

            if (buffCount == 0)
            {
                Debug.Log("[Buffs] No buffs");
                return;
            }

            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return;

            int index = 0;
            for (int i = 0; i < buffPool.Count; i++)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);

                if (buff.TargetEntityId == _playerEntityId)
                {
                    Debug.Log($"  [{index}] TableID: {buff.BuffTableID}, Remain: {buff.RemainTime:F1}s / {buff.Duration:F1}s");
                    index++;
                }
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("=== Buff System Example ===");
            GUILayout.Label("1: Add Attack Buff (+10 Attack, 10s)");
            GUILayout.Label("2: Add Defense Buff (+20 Defense, 5s)");
            GUILayout.Label("3: Add Speed Buff (+30% Speed, 8s)");
            GUILayout.Label("4: Remove All Buffs");
            GUILayout.Label("5: Print Buff Status");
            GUILayout.EndArea();
        }
    }
}
