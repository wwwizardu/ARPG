#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Monster
{
    public class MonsterManager : MonoBehaviour
    {
        private List<Creature.CharacterBase> _monsters = new();
        private Dictionary<int, GameObject> _monsterPrefabs = new();
        
        [SerializeField] private Transform _monsterParent;
        
        private bool _initialized = false;

        public void Initialize()
        {
            if (_monsterParent == null)
            {
                GameObject monsterParentObj = new GameObject("Monsters");
                _monsterParent = monsterParentObj.transform;
                _monsterParent.SetParent(transform);
            }

            _initialized = true;
        }

        public void AddMonster(Creature.CharacterBase monster)
        {
            if (monster == null)
                return;

            if (!_monsters.Contains(monster))
            {
                _monsters.Add(monster);
                monster.transform.SetParent(_monsterParent);
            }
        }

        public void RemoveMonster(Creature.CharacterBase monster)
        {
            if (monster == null)
                return;

            _monsters.Remove(monster);
        }

        public List<Creature.CharacterBase> GetAllMonsters()
        {
            return new List<Creature.CharacterBase>(_monsters);
        }

        public Creature.CharacterBase? GetNearestMonster(Vector3 position, float maxDistance = float.MaxValue)
        {
            Creature.CharacterBase? nearest = null;
            float nearestDistance = maxDistance;

            foreach (var monster in _monsters)
            {
                if (monster == null)
                    continue;

                float distance = Vector3.Distance(position, monster.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = monster;
                }
            }

            return nearest;
        }

        public void CleanupDestroyedMonsters()
        {
            for (int i = _monsters.Count - 1; i >= 0; i--)
            {
                if (_monsters[i] == null)
                {
                    _monsters.RemoveAt(i);
                }
            }
        }

        private void Update()
        {
            if (!_initialized)
                return;

            // 주기적으로 파괴된 몬스터 정리
            if (Time.frameCount % 60 == 0) // 매 60프레임마다
            {
                CleanupDestroyedMonsters();
            }
        }
    }
}