using ARPG.Creature;
using UnityEngine;

namespace ARPG
{
    public interface IHittable
    {
        public virtual void OnHit(CharacterBase inAttacker, int inDamage)
        {
            Debug.Log($"Hit with {inDamage} damage");
        }
    }
}