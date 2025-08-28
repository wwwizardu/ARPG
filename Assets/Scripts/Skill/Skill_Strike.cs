using UnityEngine;
using ARPG;

namespace ARPG.Skill
{
    public class Skill_Strike : SkillBase
    {
        private float _attackTime = 1f;
        private float _attackSpeed = 1f; // 스윙 속도, 기본값은 1초에 한번
        private Vector3 _mouseWorldPosition; // 스킬 시작 시 마우스 위치
        private float _attackRange = 2f; // 공격 범위
        private float _attackAngle = 45f; // 공격 각도 (좌우로 45도씩, 총 90도)
        public override void Initialize(Creature.CharacterBase inCharacter, SkillController inController, int inSkillId)
        {
            base.Initialize(inCharacter, inController, inSkillId);

            _attackTime = 1f;

        }

        public override bool StartSkill(byte inTargetType, long inTargetId)
        {
            base.StartSkill(inTargetType, inTargetId);

            // 마우스 위치 저장
            Vector3 mouseScreenPosition = UnityEngine.Input.mousePosition;
            _mouseWorldPosition = Camera.main.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, Camera.main.nearClipPlane));
            _mouseWorldPosition.z = 0f; // 2D이므로 z값은 0으로 설정

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

        public override void EndSkill()
        {
            base.EndSkill();
            
            // 히트 체크 수행
            CheckHit();
        }
        
        protected override void OnHitTarget(GameObject target)
        {
            base.OnHitTarget(target);

            // 히트 처리 로직
            Debug.Log($"[Skill_Strike] Hit target: {target.name}");
        }

        private void CheckHit()
        {
            if (_character == null)
                return;

            Vector3 characterPosition = _character.transform.position;
            Vector3 attackDirection = (_mouseWorldPosition - characterPosition).normalized;

            // 공격 범위 내의 모든 콜라이더 검색
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(characterPosition, _attackRange);

            foreach (Collider2D collider in hitColliders)
            {
                if (collider.gameObject == _character.gameObject)
                    continue; // 자기 자신은 제외

                if (IsWithinAttackAngle(characterPosition, collider.transform.position, attackDirection))
                {
                    // IHittable 인터페이스를 가진 대상만 처리
                    IHittable hittable = collider.GetComponent<IHittable>();
                    if (hittable != null)
                    {
                        // 데미지 계산 (AttackMin과 AttackMax 사이의 랜덤값)
                        int minDamage = _character.Stat.GetAttackMin();
                        int maxDamage = _character.Stat.GetAttackMax();
                        int damage = UnityEngine.Random.Range(minDamage, maxDamage + 1);
                        
                        // IHittable의 OnHit 함수 호출
                        hittable.OnHit(_character, damage);
                    }
                }
            }
        }

        private bool IsWithinAttackAngle(Vector3 attackerPos, Vector3 targetPos, Vector3 attackDirection)
        {
            Vector3 toTarget = (targetPos - attackerPos).normalized;
            float angle = Vector3.Angle(attackDirection, toTarget);
            
            return angle <= _attackAngle;
        }



    }
}
