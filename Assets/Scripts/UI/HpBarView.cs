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
        }
    }
}
