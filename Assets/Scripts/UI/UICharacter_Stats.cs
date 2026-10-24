#nullable enable
using System;
using ARPG.Manager;
using ARPG.Message;
using ARPG.Skill.Combat;
using ARPG.Utility;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 캐릭터 창 능력치 칸 — 레벨·경험치, 능력치 분배·되돌리기 (PLAYER_LEVEL_CHARM_DESIGN §8.2·§8.3),
    /// 전투 수치, 아이템 수량·희귀도 (ITEM_DROP_BALANCE_DESIGN §8.8). 줄은 Character.uxml에 있고 여기서는 글자만 채운다.
    /// </summary>
    public partial class UICharacter
    {
        private const int SHIFT_CLICK_POINTS = 5;
        private const string PENDING_COLOR = "#78DC78";
        private static readonly GlobalEnum.Stat[] ATTRIBUTE_STATS = { GlobalEnum.Stat.Str, GlobalEnum.Stat.Dex, GlobalEnum.Stat.Int };
        private static readonly string[] ATTRIBUTE_NAMES = { "힘", "민첩", "지능" };
        private static readonly string[] ATTRIBUTE_ELEMENT_KEYS = { "str", "dex", "int" }; // {key}-text, {key}-minus-btn, {key}-plus-btn
        private static readonly string[] DAMAGE_ELEMENT_NAMES =
            { "damage-physical-text", "damage-fire-text", "damage-ice-text", "damage-lightning-text", "damage-poison-text" };
        private static readonly string[] RESIST_ELEMENT_NAMES =
            { "fire-resist-text", "ice-resist-text", "lightning-resist-text", "poison-resist-text" };

        private Text? _levelText;
        private Text? _pointsText;
        private readonly Text?[] _attributeTexts = new Text?[3];
        private Text? _hpText;
        private Text? _mpText;
        private Text? _accuracyText;
        private Text? _evasionText;
        private Text? _hpRegenText;
        private Text? _mpRegenText;
        private readonly Text?[] _damageTexts = new Text?[5]; // 물리·화염·냉기·번개·독
        private Text? _critRateText;
        private Text? _critDamageText;
        private Text? _moveSpeedText;
        private Text? _attackSpeedText;
        private Text? _castSpeedText;
        private Text? _defenseText;
        private readonly Text?[] _resistTexts = new Text?[4]; // 화염·냉기·번개·독
        private Text? _luckText;
        private Text? _itemQuantityText;
        private Text? _itemRarityText;

        private readonly Button?[] _plusButtons = new Button?[3];
        private readonly Button?[] _minusButtons = new Button?[3];
        private Button? _applyButton;
        private Button? _cancelButton;
        private Button? _refundAllButton;

        // [+]로 넣은 아직 확정하지 않은 포인트 (힘·민첩·지능). [적용]에서 확정, [취소]에서 버린다
        private readonly int[] _pendingPoints = new int[3];

        // 버튼·호버 콜백. 처음 Initialize 때 한 번 만든다 (CreateStatHandlers)
        private bool _isStatSubscribed;
        private Action<StatRecalculatedMessage> _statRecalculatedHandler = null!;
        private Action<PlayerProgressChangedMessage> _progressChangedHandler = null!;
        private Action _charmBoardClickedHandler = null!;
        private Action _applyClickedHandler = null!;
        private Action _cancelClickedHandler = null!;
        private Action _refundAllClickedHandler = null!;
        private EventCallback<PointerEnterEvent> _refundAllEnterHandler = null!;
        private EventCallback<PointerLeaveEvent> _tooltipLeaveHandler = null!;
        private readonly Action[] _plusClickedHandlers = new Action[3];
        private readonly Action[] _minusClickedHandlers = new Action[3];
        private readonly EventCallback<PointerEnterEvent>[] _attributeEnterHandlers = new EventCallback<PointerEnterEvent>[3];
        private readonly EventCallback<PointerEnterEvent>[] _minusEnterHandlers = new EventCallback<PointerEnterEvent>[3];

        private void CreateStatHandlers()
        {
            _statRecalculatedHandler = OnStatRecalculated;
            _progressChangedHandler = OnProgressChanged;
            _charmBoardClickedHandler = OnClickCharmBoard;
            _applyClickedHandler = OnClickApplyPending;
            _cancelClickedHandler = OnClickCancelPending;
            _refundAllClickedHandler = OnClickRefundAll;
            _refundAllEnterHandler = OnRefundAllEnter;
            _tooltipLeaveHandler = OnTooltipTargetLeave;
            for (int i = 0; i < ATTRIBUTE_STATS.Length; i++)
            {
                _plusClickedHandlers[i] = CreatePlusClickedHandler(i);
                _minusClickedHandlers[i] = CreateMinusClickedHandler(i);
                _attributeEnterHandlers[i] = CreateAttributeEnterHandler(i);
                _minusEnterHandlers[i] = CreateMinusEnterHandler(i);
            }
        }

        // 인덱스를 잡는 람다는 따로 만들어 CreateStatHandlers가 한 번만 부른다
        private Action CreatePlusClickedHandler(int index) => () => OnClickPlus(index);
        private Action CreateMinusClickedHandler(int index) => () => OnClickMinus(index);
        private EventCallback<PointerEnterEvent> CreateAttributeEnterHandler(int index) => evt => ShowAttributeTooltip(index, evt.currentTarget as VisualElement);
        private EventCallback<PointerEnterEvent> CreateMinusEnterHandler(int index) => evt => ShowRefundOneTooltip(index, evt.currentTarget as VisualElement);

        private void BindStats(VisualElement root)
        {
            _levelText = root.Q<Text>("level-text");
            _pointsText = root.Q<Text>("points-text");
            _hpText = root.Q<Text>("hp-text");
            _mpText = root.Q<Text>("mp-text");
            _accuracyText = root.Q<Text>("accuracy-text");
            _evasionText = root.Q<Text>("evasion-text");
            _hpRegenText = root.Q<Text>("hp-regen-text");
            _mpRegenText = root.Q<Text>("mp-regen-text");
            for (int i = 0; i < _damageTexts.Length; i++)
                _damageTexts[i] = root.Q<Text>(DAMAGE_ELEMENT_NAMES[i]);
            _critRateText = root.Q<Text>("crit-rate-text");
            _critDamageText = root.Q<Text>("crit-damage-text");
            _moveSpeedText = root.Q<Text>("move-speed-text");
            _attackSpeedText = root.Q<Text>("attack-speed-text");
            _castSpeedText = root.Q<Text>("cast-speed-text");
            _defenseText = root.Q<Text>("defense-text");
            for (int i = 0; i < _resistTexts.Length; i++)
                _resistTexts[i] = root.Q<Text>(RESIST_ELEMENT_NAMES[i]);
            _luckText = root.Q<Text>("luck-text");
            _itemQuantityText = root.Q<Text>("item-quantity-text");
            _itemRarityText = root.Q<Text>("item-rarity-text");

            Button? charmBoardButton = root.Q<Button>("charm-board-btn");
            if (charmBoardButton != null) charmBoardButton.clicked += _charmBoardClickedHandler;
            _applyButton = root.Q<Button>("apply-btn");
            if (_applyButton != null) _applyButton.clicked += _applyClickedHandler;
            _cancelButton = root.Q<Button>("cancel-btn");
            if (_cancelButton != null) _cancelButton.clicked += _cancelClickedHandler;
            _refundAllButton = root.Q<Button>("refund-all-btn");
            if (_refundAllButton != null)
            {
                _refundAllButton.clicked += _refundAllClickedHandler;
                _refundAllButton.RegisterCallback(_refundAllEnterHandler);
                _refundAllButton.RegisterCallback(_tooltipLeaveHandler);
            }

            // 힘·민첩·지능 줄 글자에 마우스를 올리면 1포인트 효과, [−]에 올리면 되돌리기 비용
            for (int i = 0; i < ATTRIBUTE_STATS.Length; i++)
            {
                string key = ATTRIBUTE_ELEMENT_KEYS[i];
                _attributeTexts[i] = root.Q<Text>($"{key}-text");
                if (_attributeTexts[i] != null)
                {
                    _attributeTexts[i]!.RegisterCallback(_attributeEnterHandlers[i]);
                    _attributeTexts[i]!.RegisterCallback(_tooltipLeaveHandler);
                }

                _minusButtons[i] = root.Q<Button>($"{key}-minus-btn");
                if (_minusButtons[i] != null)
                {
                    _minusButtons[i]!.clicked += _minusClickedHandlers[i];
                    _minusButtons[i]!.RegisterCallback(_minusEnterHandlers[i]);
                    _minusButtons[i]!.RegisterCallback(_tooltipLeaveHandler);
                }

                _plusButtons[i] = root.Q<Button>($"{key}-plus-btn");
                if (_plusButtons[i] != null)
                    _plusButtons[i]!.clicked += _plusClickedHandlers[i];
            }
        }

        // 스탯 재계산 완료·레벨·포인트 변경 메시지는 창이 열려 있는 동안만 받는다. 열 때 한 번 다시 그린다.
        private void SubscribeStatMessages()
        {
            if (_isStatSubscribed == true || AR.s?.Message == null)
                return;
            AR.s.Message.Subscribe(_statRecalculatedHandler);
            AR.s.Message.Subscribe(_progressChangedHandler);
            _isStatSubscribed = true;
        }

        private void UnsubscribeStatMessages()
        {
            if (_isStatSubscribed == false || AR.s?.Message == null)
                return;
            AR.s.Message.Unsubscribe(_statRecalculatedHandler);
            AR.s.Message.Unsubscribe(_progressChangedHandler);
            _isStatSubscribed = false;
        }

        private void OnProgressChanged(PlayerProgressChangedMessage msg)
        {
            UpdateCharacterStat();
        }

        private void OnStatRecalculated(StatRecalculatedMessage msg)
        {
            UpdateCharacterStat();
        }

        private void UpdateCharacterStat()
        {
            int playerId = AR.s.Data.Player.PlayerId;

            if (AR.s.Component.TryGetComponent<Component.StatComponent>(playerId, out var _statComponent) == false)
            {
                Debug.LogError("[UICharacter] UpdateCharacterStat - StatComponent not found");
                return;
            }

            Data.PlayerData? playerData = AR.s.Data.Player;
            ClampPendingPoints(playerData);
            SetText(_attributeTexts[0], FormatAttributeLine(0, _statComponent.FinalStr));
            SetText(_attributeTexts[1], FormatAttributeLine(1, _statComponent.FinalDex));
            SetText(_attributeTexts[2], FormatAttributeLine(2, _statComponent.FinalInt));
            SetText(_hpText, $"체력 {_statComponent.CurrentHp}/{_statComponent.FinalMaxHp}"
                + FormatPreview(_statComponent.FinalMaxHp, AttributeBonusRules.GetBonusHp(_pendingPoints[0])));
            SetText(_mpText, $"마나 {_statComponent.CurrentMp}/{_statComponent.FinalMaxMp}"
                + FormatPreview(_statComponent.FinalMaxMp, AttributeBonusRules.GetBonusMp(_pendingPoints[2])));
            UpdateProgressionLines(playerData, _statComponent);
            SetText(_hpRegenText, $"체력 재생 {_statComponent.FinalHpGeneration}");
            SetText(_mpRegenText, $"마나 재생 {_statComponent.FinalMpGeneration}");

            // 기본 스킬(슬롯 0) 기준 예상 데미지/치명타 계산 — DamageCalculator가 profile lazy build 처리
            int basicSkillEntityId = EntityManager.GetDeterministicId(playerId, EntityIdCategory.Skill, 0);
            bool hasBasicSkill = AR.s.Component.HasComponent<Component.SkillComponent>(basicSkillEntityId);

            EstimatedDamage est;
            float estimatedCritRate;
            float estAttackSpeed;
            if (hasBasicSkill)
            {
                est = DamageCalculator.CalculateForSkill(basicSkillEntityId);
                estimatedCritRate = DamageCalculator.EstimateCritRate(basicSkillEntityId);
                estAttackSpeed = DamageCalculator.EstimateAttackSpeed(basicSkillEntityId);
            }
            else
            {
                // 스킬 없을 때 폴백: 캐릭터 스탯만으로 추정 (무기 AS, 스킬 배율 없음)
                est = DamageCalculator.Calculate(_statComponent);
                estimatedCritRate = _statComponent.FinalCriRate;
                float weaponAS = WeaponHelper.GetWeaponAttackSpeed(playerId);
                if (weaponAS <= 0f) weaponAS = 1f;
                estAttackSpeed = weaponAS * (1f + _statComponent.FinalAttackSpeed / 100f);
            }

            SetText(_damageTexts[0], $"예상 물리 데미지 {est.PhysMin} - {est.PhysMax}");
            SetText(_damageTexts[1], $"예상 화염 데미지 {est.FireMin} - {est.FireMax}");
            SetText(_damageTexts[2], $"예상 냉기 데미지 {est.IceMin} - {est.IceMax}");
            SetText(_damageTexts[3], $"예상 번개 데미지 {est.LightningMin} - {est.LightningMax}");
            SetText(_damageTexts[4], $"예상 독 데미지 {est.PoisonMin} - {est.PoisonMax}");

            SetText(_critRateText, $"치명타 확률 {estimatedCritRate:F2}%");
            SetText(_critDamageText, $"치명타 피해 {_statComponent.FinalCriDamage}%");
            SetText(_moveSpeedText, $"이동 속도 {_statComponent.FinalMoveSpeedValue:F2}");
            SetText(_attackSpeedText, $"공격 속도 {estAttackSpeed:F2}");
            SetText(_castSpeedText, $"시전 속도 {_statComponent.FinalCastSpeed}");
            SetText(_defenseText, $"방어력 {_statComponent.FinalDefense}");
            SetText(_resistTexts[0], $"화염 저항 {_statComponent.FinalFireResist}");
            SetText(_resistTexts[1], $"냉기 저항 {_statComponent.FinalIceResist}");
            SetText(_resistTexts[2], $"번개 저항 {_statComponent.FinalLightningResist}");
            SetText(_resistTexts[3], $"독 저항 {_statComponent.FinalPoisonResist}");
            SetText(_luckText, $"행운 {_statComponent.FinalLuck}");

            // ① 플레이어 묶음 합계. 희귀도는 곡선을 거친 실제 적용값이 다르면 함께 보여 준다 (§8.2·§8.8)
            SetText(_itemQuantityText, $"아이템 수량 {FormatSignedPercent(_statComponent.FinalItemQuantity)}");
            int rarity = _statComponent.FinalItemRarity;
            int applied = Mathf.RoundToInt(DropBonusResolver.ApplyPlayerRarityCurve(rarity));
            SetText(_itemRarityText, applied != rarity
                ? $"아이템 희귀도 {FormatSignedPercent(rarity)} (적용 {applied}%)"
                : $"아이템 희귀도 {FormatSignedPercent(rarity)}");
        }

        private static void SetText(Text? text, string value)
        {
            if (text != null)
                text.text = value;
        }

        // ===== 레벨·능력치 분배 (PLAYER_LEVEL_CHARM_DESIGN §8.2·§8.3) =====

        private void UpdateProgressionLines(Data.PlayerData? data, Component.StatComponent stat)
        {
            if (data == null)
                return;

            int required = PlayerLevelRules.GetRequiredExp(data.Level);
            SetText(_levelText, required > 0
                ? $"레벨 {data.Level}   경험치 {data.Exp:N0} / {required:N0}"
                : $"레벨 {data.Level}   최고 레벨");

            int pendingTotal = GetPendingTotal();
            int remaining = data.UnspentAttributePoints - pendingTotal;
            SetText(_pointsText, $"남은 포인트 {remaining}");

            int allocatedTotal = data.AllocatedStr + data.AllocatedDex + data.AllocatedInt;
            for (int i = 0; i < ATTRIBUTE_STATS.Length; i++)
            {
                SetButtonVisible(_plusButtons[i], remaining > 0);
                SetButtonVisible(_minusButtons[i], AR.s.Player.GetAllocatedPoints(ATTRIBUTE_STATS[i]) > 0);
            }
            SetButtonVisible(_applyButton, pendingTotal > 0);
            SetButtonVisible(_cancelButton, pendingTotal > 0);
            SetButtonVisible(_refundAllButton, allocatedTotal > 0);

            // 같은 레벨 균형형 몬스터 기준 참고값 (§8.3)
            int monsterEvasion = HitChanceRules.GetMonsterEvasion(data.Level, 1f);
            int monsterAccuracy = HitChanceRules.GetMonsterAccuracy(data.Level, 1f);
            float hitChance = HitChanceRules.GetHitChance(stat.FinalAccuracy, monsterEvasion);
            int previewEvasion = stat.FinalEvasion + AttributeBonusRules.GetBonusEvasion(_pendingPoints[1]);
            float evadeChance = 1f - HitChanceRules.GetHitChance(monsterAccuracy, previewEvasion);
            SetText(_accuracyText, $"정확도 {stat.FinalAccuracy} (같은 레벨 몬스터 적중률 약 {hitChance * 100f:0}%)");
            SetText(_evasionText, $"회피 {stat.FinalEvasion}{FormatPreview(stat.FinalEvasion, AttributeBonusRules.GetBonusEvasion(_pendingPoints[1]))}"
                + $" (피할 확률 약 {evadeChance * 100f:0.#}%)");
        }

        private string FormatAttributeLine(int index, int finalValue)
        {
            int pending = _pendingPoints[index];
            return pending > 0
                ? $"{ATTRIBUTE_NAMES[index]} {finalValue} <color={PENDING_COLOR}>(+{pending})</color>"
                : $"{ATTRIBUTE_NAMES[index]} {finalValue}";
        }

        private static string FormatPreview(int current, int delta)
        {
            return delta > 0 ? $" <color={PENDING_COLOR}>→ {current + delta}</color>" : string.Empty;
        }

        /// <summary>숨긴 버튼도 자리는 남겨 줄 안의 다른 버튼이 옆으로 밀리지 않게 한다.</summary>
        private static void SetButtonVisible(Button? button, bool visible)
        {
            if (button != null)
                button.style.visibility = visible == true ? Visibility.Visible : Visibility.Hidden;
        }

        private int GetPendingTotal()
        {
            return _pendingPoints[0] + _pendingPoints[1] + _pendingPoints[2];
        }

        /// <summary>되돌리기 등으로 남은 포인트가 줄면 예정 포인트를 남은 포인트 안으로 줄인다.</summary>
        private void ClampPendingPoints(Data.PlayerData? data)
        {
            int available = data != null ? data.UnspentAttributePoints : 0;
            for (int i = _pendingPoints.Length - 1; i >= 0; i--)
            {
                int over = GetPendingTotal() - available;
                if (over <= 0)
                    break;
                _pendingPoints[i] = Mathf.Max(0, _pendingPoints[i] - over);
            }
        }

        private void OnClickPlus(int index)
        {
            Data.PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            int remaining = data.UnspentAttributePoints - GetPendingTotal();
            int add = Mathf.Min(shift ? SHIFT_CLICK_POINTS : 1, remaining);
            if (add <= 0)
                return;

            _pendingPoints[index] += add;
            UpdateCharacterStat();
        }

        private void OnClickApplyPending()
        {
            if (GetPendingTotal() <= 0)
                return;

            if (AR.s.Player.CommitAttributeAllocation(_pendingPoints[0], _pendingPoints[1], _pendingPoints[2]) == true)
                Array.Clear(_pendingPoints, 0, _pendingPoints.Length);
            UpdateCharacterStat();
        }

        private void OnClickCancelPending()
        {
            Array.Clear(_pendingPoints, 0, _pendingPoints.Length);
            UpdateCharacterStat();
        }

        private void OnClickMinus(int index)
        {
            GlobalEnum.Stat attribute = ATTRIBUTE_STATS[index];
            if (AR.s.Player.GetAllocatedPoints(attribute) <= 0)
                return;

            int cost = AR.s.Player.GetRefundCost(1);
            ShowConfirm($"{ATTRIBUTE_NAMES[index]} 1포인트를 되돌립니다. {FormatGold(cost)}", () =>
            {
                if (AR.s.Player.TryRefundAttributePoints(attribute, 1) == false)
                    AR.s.UI.SetNotify("골드가 부족합니다");
            });
        }

        private void OnClickRefundAll()
        {
            Data.PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            int points = data.AllocatedStr + data.AllocatedDex + data.AllocatedInt;
            if (points <= 0)
                return;

            int cost = AR.s.Player.GetRefundCost(points);
            ShowConfirm($"포인트 {points}개를 모두 되돌립니다. {FormatGold(cost)}", () =>
            {
                if (AR.s.Player.TryRefundAllAttributePoints() == false)
                    AR.s.UI.SetNotify("골드가 부족합니다");
            });
        }

        private void OnClickCharmBoard()
        {
            AR.s.UI.Show<UICharmBoard>(AddressablePath.CharmBoard, UIManager.Layer.Main);
        }

        private static void ShowConfirm(string message, Action onConfirm)
        {
            UIConfirmDialog? dialog = AR.s.UI.Show<UIConfirmDialog>(AddressablePath.ConfirmDialog, UIManager.Layer.Popup);
            dialog?.Setup(message, onConfirm);
        }

        private static string FormatGold(int cost)
        {
            return cost > 0 ? $"{cost:N0}골드" : "무료";
        }

        private static string FormatSignedPercent(int value)
        {
            return value >= 0 ? $"+{value}%" : $"{value}%";
        }

        // ===== 호버 설명 =====

        private void OnTooltipTargetLeave(PointerLeaveEvent evt)
        {
            AR.s.Tooltip.Hide();
        }

        private void ShowAttributeTooltip(int index, VisualElement? target)
        {
            if (target == null)
                return;
            string body = index switch
            {
                0 => $"힘 1 = 최대 HP +{AttributeBonusRules.HP_PER_STR}",
                1 => $"민첩 1 = 회피 +{AttributeBonusRules.EVASION_PER_DEX}",
                _ => $"지능 1 = 최대 마나 +{AttributeBonusRules.MP_PER_INT}",
            };
            body += "\n클릭: 1포인트, Shift+클릭: 5포인트. [적용]을 눌러야 확정됩니다.";
            AR.s.Tooltip.ShowText(ATTRIBUTE_NAMES[index], body, GetSlotScreenRect(target));
        }

        private void ShowRefundOneTooltip(int index, VisualElement? target)
        {
            if (target == null)
                return;
            int cost = AR.s.Player.GetRefundCost(1);
            AR.s.Tooltip.ShowText($"{ATTRIBUTE_NAMES[index]} 되돌리기", $"1포인트: {FormatGold(cost)}", GetSlotScreenRect(target));
        }

        private void OnRefundAllEnter(PointerEnterEvent evt)
        {
            if (evt.currentTarget is not VisualElement target)
                return;
            Data.PlayerData? data = AR.s.Data.Player;
            int points = data != null ? data.AllocatedStr + data.AllocatedDex + data.AllocatedInt : 0;
            AR.s.Tooltip.ShowText("전체 되돌리기", $"포인트 {points}개: {FormatGold(AR.s.Player.GetRefundCost(points))}",
                GetSlotScreenRect(target));
        }
    }
}
