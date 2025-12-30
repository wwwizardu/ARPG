using UnityEngine;

namespace ARPG
{
    public interface IMovable : IEntity
    {
        Vector3 Vector3 { get; }
        void UpdateVelocity(float inDeltaTime);
        void UpdatePosition(float inDeltaTime);        
    }    
}

