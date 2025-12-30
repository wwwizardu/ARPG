using ARPG.Creature;
using UnityEngine;

namespace ARPG
{
    public interface IHittable
    {
        GlobalEnum.TeamType Team { get; }

        public virtual void OnHit(CharacterBase inAttacker, bool isOnHit, GlobalEnum.DamageType inDamageType, int inDamage)
        {
            Debug.Log($"Hit with {inDamage} damage");
        }
    }
}