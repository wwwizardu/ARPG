#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: 제단 UI. 1회용 단기 버프(30분 게임시간) + 12시간 게임시간 쿨다운.
    /// 쿨다운은 PlacedObjectComponent.LastUseGameTime (엔티티 단위).
    /// 마을당 Shrine 1개 강제 (BuildableItemTable.MaxPerVillage = 1) — 어뷰징 차단.
    ///
    /// 버프 3종 (BuffTable Id 매핑은 Step 10b에서 결정):
    ///   - 가호의 빛: 받는 데미지 -10%
    ///   - 사냥꾼의 정확: 치명타 +10%
    ///   - 헤르메스의 발: 이속 +20%
    ///
    /// Step 10 MVP: 인터페이스/스켈레톤만.
    /// </summary>
    public class UIShrine : UIBaseForm
    {
        // [SerializeField] private Button _buff1Button = null!;
        // [SerializeField] private Button _buff2Button = null!;
        // [SerializeField] private Button _buff3Button = null!;

        private const float COOLDOWN_HOURS = 12f;
        private const float BUFF_DURATION_HOURS = 0.5f; // 30분 게임시간

        public enum ShrineBuff { ProtectionLight, HuntersAim, HermesFeet }

        private int _shrineEntityId = -1;
        private int _villageId = -1;
        private float _lastUseGameTime;
        private bool _isOnCooldown;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
        }

        public void Bind(int shrineEntityId)
        {
            _shrineEntityId = shrineEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shrineEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIShrine] Bind 실패 — entityId={shrineEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;
            _lastUseGameTime = po.LastUseGameTime;

            CheckCooldown();
            RefreshAll();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            CheckCooldown();
            RefreshAll();
        }

        // ========== 액션 stub ==========

        public void OnClickBuff(ShrineBuff buff)
        {
            CheckCooldown();
            if (_isOnCooldown)
            {
                float remain = COOLDOWN_HOURS - (AR.s.Time.CurrentGameTime - _lastUseGameTime);
                AR.s.UI.SetNotify($"제단 쿨다운 ({remain:F1}h 남음)");
                return;
            }

            // TODO(Step 10b+): Gold 차감 (Stage 기반)
            // TODO: BuffTable Id 매핑 후 AR.s.Buff.ApplyBuff(player, buffId, BUFF_DURATION_HOURS)
            // TODO: 쿨다운 갱신 — PlacedObjectComponent.LastUseGameTime = now + Component.SetComponent
            float now = AR.s.Time.CurrentGameTime;
            UpdateLastUseGameTime(now);

            Debug.Log($"[Shrine] v{_villageId} 버프 {buff} 적용 + 12h 쿨다운 시작");
            CheckCooldown();
            RefreshAll();
        }

        // ========== 내부 상태 ==========

        private void CheckCooldown()
        {
            float now = AR.s.Time.CurrentGameTime;
            _isOnCooldown = (now - _lastUseGameTime) < COOLDOWN_HOURS;
        }

        private void UpdateLastUseGameTime(float now)
        {
            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_shrineEntityId, out var po) == false)
                return;
            po.LastUseGameTime = now;
            AR.s.Component.SetComponent(_shrineEntityId, po);
            _lastUseGameTime = now;
        }

        // ========== 갱신 hook ==========

        private void RefreshAll()
        {
            // TODO(Step U1+): 쿨다운 중이면 버튼 회색, 가격 표시 등
        }
    }
}
