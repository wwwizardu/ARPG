#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: 강화 UI (Furnace anchor 기준).
    /// HasObjectSet으로 단계 결정:
    ///   - 1단계 (Furnace만): 분해
    ///   - 2단계 (+ Anvil): Mod 재롤
    ///   - 3단계 (+ QuenchVat): Mod 추가 + 비용 -20%
    ///
    /// Step 10 MVP: 인터페이스/스켈레톤만. Mod 재롤 API 연동은 Step U1 후 + Phase F의 Mod 시스템 정책 결정 시 본격.
    /// </summary>
    public class UIForge : UIBaseForm
    {
        // [SerializeField] private Button _disassembleButton = null!;
        // [SerializeField] private Button _rerollButton = null!;
        // [SerializeField] private Button _addModButton = null!;

        public enum ForgeTier
        {
            None,
            Basic,      // Furnace
            Standard,   // + Anvil
            Premium,    // + Anvil + QuenchVat
        }

        private int _forgeEntityId = -1;
        private int _villageId = -1;
        private ForgeTier _tier = ForgeTier.None;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
        }

        /// <summary>
        /// ServiceUIRouter가 Show 직후 호출 — Furnace anchor 좌표로 세트 단계 결정.
        /// </summary>
        public void Bind(int forgeEntityId)
        {
            _forgeEntityId = forgeEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_forgeEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIForge] Bind 실패 — entityId={forgeEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;
            Vector2Int anchor = new Vector2Int(po.TileX, po.TileY);

            // 세트 단계 평가 (가장 높은 단계 기준)
            if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgePremium, anchor))
                _tier = ForgeTier.Premium;
            else if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgeStandard, anchor))
                _tier = ForgeTier.Standard;
            else if (AR.s.Village.HasObjectSet(_villageId, ObjectSetType.ForgeBasic, anchor))
                _tier = ForgeTier.Basic;
            else
                _tier = ForgeTier.None;

            Debug.Log($"[Forge] v{_villageId} 강화 단계 {_tier}");
            RefreshAll();
        }

        // ========== 액션 stub ==========

        /// <summary>장비 분해 (1단계+) — Step 10b에서 본격 구현.</summary>
        public void OnClickDisassemble(int slotIndex)
        {
            if (_tier < ForgeTier.Basic) return;
            // TODO: 인벤토리 슬롯 → 자원 환원 (Mod 시스템 연동)
        }

        /// <summary>Mod 재롤 (2단계+) — Phase F Mod 정책 결정 시 본격.</summary>
        public void OnClickReroll(int slotIndex)
        {
            if (_tier < ForgeTier.Standard) return;
            // TODO: Currency 차감 + Mod 풀 재롤
        }

        /// <summary>Mod 추가 (3단계만) — 빈 슬롯 채우기 + 비용 -20% 자동 적용.</summary>
        public void OnClickAddMod(int slotIndex)
        {
            if (_tier < ForgeTier.Premium) return;
            // TODO: Currency 차감 + Mod 빈 슬롯 채우기
        }

        // ========== 갱신 hook ==========

        private void RefreshAll()
        {
            // TODO(Step U1+): _tier에 따라 버튼 활성/비활성, 비용 표시 등
        }
    }
}
