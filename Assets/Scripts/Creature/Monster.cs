using UnityEngine;

namespace ARPG.Creature
{
    public class Monster : CharacterBase
    {
        private bool _activated = false;

        public override void Initialize()
        {

        }

        public override void Reset()
        {

        }

        public void Activate()
        {
            _activated = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            _activated = false;
            gameObject.SetActive(false);
        }

        public bool IsActivated()
        {
            return _activated;
        }

        protected override void OnUpdate()
        {
            if (_activated == false)
                return;

            base.OnUpdate();

        }
        
        protected override void OnFixedUpdateCharacter(float inDeltaTime)
        {
            if (_activated == false)
                return;

            base.OnFixedUpdateCharacter(inDeltaTime);
        }
    }
}
