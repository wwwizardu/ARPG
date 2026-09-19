using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Component
{
    public partial class ComponentManager
    {
        public sealed class SuspendedEntity
        {
            internal int _id;
            internal IComponentPool[] _pools;
            internal object[] _values;
            internal GameObject _view;
            internal bool _wasActive;
        }

        // Pool objects stay alive: systems' cached SparseSet references continue to be valid.
        public List<SuspendedEntity> SuspendExceptPlayer(int playerId)
        {
            var kept = new HashSet<int> { playerId };
            var skills = GetComponentPool<SkillComponent>();
            for (int i = 0; i < skills.Count; i++)
                if (skills.GetByIndex(i).OwnerEntityId == playerId) kept.Add(skills.GetEntityId(i));
            var buffs = GetComponentPool<BuffInstance>();
            for (int i = 0; i < buffs.Count; i++)
                if (buffs.GetByIndex(i).TargetEntityId == playerId) kept.Add(buffs.GetEntityId(i));
            var ids = new List<int>(_entityComponents.Keys);
            var suspended = new List<SuspendedEntity>();
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                if (kept.Contains(id) == true) continue;
                var types = new Type[_entityComponents[id].Count];
                _entityComponents[id].CopyTo(types);
                var entry = new SuspendedEntity { _id = id, _pools = new IComponentPool[types.Length],
                    _values = new object[types.Length] };
                for (int j = 0; j < types.Length; j++)
                {
                    var pool = _componentPools[types[j]];
                    if (pool.Contains(id) == false) continue;
                    entry._pools[j] = pool;
                    entry._values[j] = pool.Capture(id);
                    pool.Remove(id);
                }
                if (AR.s.Message.TryGetEntity(id, out var entity) == true) entry._view = entity.gameObject;
                else entry._view = AR.s.System.GetSystem<Systems.System_Render>()?.GetGameObject(id);
                if (entry._view != null)
                {
                    entry._wasActive = entry._view.activeSelf;
                    entry._view.SetActive(false);
                }
                suspended.Add(entry);
            }
            return suspended;
        }

        public void RestoreSuspended(List<SuspendedEntity> suspended)
        {
            for (int i = 0; i < suspended.Count; i++)
            {
                var entry = suspended[i];
                for (int j = 0; j < entry._pools.Length; j++)
                    entry._pools[j]?.Restore(entry._id, entry._values[j]);
                if (entry._view != null) entry._view.SetActive(entry._wasActive);
            }
            suspended.Clear();
        }
    }
}
