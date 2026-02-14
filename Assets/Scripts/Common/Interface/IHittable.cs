using ARPG.Base;
using UnityEngine;

namespace ARPG
{
    public interface IHittable
    {
        GlobalEnum.TeamType Team { get; }

        public virtual void OnHit(EntityBase inAttacker, bool isOnHit, GlobalEnum.DamageType inDamageType, int inDamage)
        {
            Debug.Log($"Hit with {inDamage} damage");
        }
    }
}
