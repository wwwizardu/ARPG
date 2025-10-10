using ARPG.Creature;
using UnityEngine;

namespace ARPG
{
    public interface IHittable
    {
        GlobalEnum.TeamType Team { get; }

        public virtual void OnHit(CharacterBase inAttacker, int inDamage)
        {
            Debug.Log($"Hit with {inDamage} damage");
        }
    }
}