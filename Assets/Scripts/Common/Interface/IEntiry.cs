using UnityEngine;

namespace ARPG
{
    public interface IEntity
    {
        GlobalEnum.EntityType EntityType  { get; }

        public void Initialize()
        {
            Debug.Log("IActor Initialized");
        }

        public void Reset()
        {
            Debug.Log("IActor Reset");
        }
    }
}


