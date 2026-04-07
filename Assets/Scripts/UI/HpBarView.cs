#nullable enable
using ARPG.Base;
using ARPG.Message;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.UI
{
    /// <summary>
    /// HP바 비주얼 컴포넌트
    /// DamageMessage를 받아 HP바 UI를 업데이트
    /// 프리팹에 붙여서 EntityBase의 자식으로 사용
    /// </summary>
    public class HpBarView : MonoBehaviour, IEntityMessageHandler
    {
        [SerializeField] private Image _hpBar = null!;

        public void RegisterTo(EntityBase entity)
        {
            entity.RegisterMessageHandler<DamageMessage>(OnDamage);
        }

        private void OnDamage(DamageMessage msg)
        {
            if (_hpBar == null)
                return;

            _hpBar.fillAmount = (float)msg.CurrentHp / (float)msg.MaxHp;

            Vector3 damagePos = _hpBar.transform.position;
            damagePos.y -= 0.25f; // HP바 아래에 데미지 텍스트 표시

            AR.s.FloatingText.ShowDamageText(
                damagePos,
                msg.DamageAmount,
                msg.DamageType,
                msg.IsCritical,
                msg.IsEvaded,
                msg.IsBlocked
            );
        }
    }
}
