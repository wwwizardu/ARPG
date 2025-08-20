using UnityEngine;

namespace ARPG.Skill
{
    public class Skill_Strike : SkillBase
    {
        private float _attackTime = 1f;
        private float _attackSpeed = 1f; // 스윙 속도, 기본값은 1초에 한번
        public override void Initialize(Creature.CharacterBase inCharacter, SkillController inController, int inSkillId)
        {
            base.Initialize(inCharacter, inController, inSkillId);

            _attackTime = 1f;

        }

        public override bool StartSkill(byte inTargetType, long inTargetId)
        {
            base.StartSkill(inTargetType, inTargetId);

            Creature.Animation attackAnimation = Creature.Animation.Attack;

            float attackTime = _attackTime;

#if UNITY_EDITOR
            if (attackTime < 0 || 1f < attackTime)
            {
                Debug.LogError("[SkillSwing] StartSkill - attackAnimation.Duration is wrong");
            }
#endif
            // 스킬 속도는 1초에 한번을 기준으로 맞춘다. SwingSpeedStat에 따라 증가함
            // _swingSpeed = _character.Status.GetFloat(StatusPropertyType.SwingSpeedStat);
            _processTime = attackTime / _attackSpeed;
            _endTime = (1f - attackTime) / _attackSpeed;

            _character.PlayAnimation(attackAnimation, false, 1, _attackSpeed);

            //Debug.Log($"[SkillSwing] StartSkill - Time({Time.time})");
            return true;
        }

    }
}
