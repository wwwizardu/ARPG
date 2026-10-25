using ARPG.Base;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ARPG.UI
{
    public class UIMainMenu : UIBaseForm
    {
        private Button _exitDungeon;
        private bool _saving;
        private bool _settingsButtonsCreated;

        public override void OnOpen()
        {
            base.OnOpen();
            if (_settingsButtonsCreated == false)
            {
                var template = GetComponentInChildren<Button>(true);
                if (template != null)
                {
                    if (template.GetComponentInParent<LayoutGroup>() == null)
                    {
                        ((RectTransform)template.transform).anchoredPosition = new Vector2(0, 195f);
                        if (template.transform.parent is RectTransform menu) menu.sizeDelta = new Vector2(menu.sizeDelta.x, 540f);
                    }
                    AddMenuButton(template, "설정", -65f, OnClickSettings);
                    AddMenuButton(template, "아이템 퀵슬롯", -130f, OnClickQuickSlots);
                    AddMenuButton(template, "인벤토리", -195f, OnClickInventory);
                    AddMenuButton(template, "스킬북", -260f, OnClickSkillBook);
                    AddMenuButton(template, "참 판", -325f, OnClickCharmBoard);
                    _settingsButtonsCreated = true;
                }
            }
            if (_exitDungeon == null)
            {
                var save = GetComponentInChildren<Button>(true);
                if (save != null)
                {
                    _exitDungeon = Instantiate(save, save.transform.parent);
                    _exitDungeon.name = "ExitDungeon";
                    _exitDungeon.onClick = new Button.ButtonClickedEvent();
                    _exitDungeon.onClick.AddListener(OnClickExitDungeon);
                    var text = _exitDungeon.GetComponentInChildren<TMP_Text>();
                    if (text != null) text.text = "던전 나오기";
                    var legacyText = _exitDungeon.GetComponentInChildren<Text>();
                    if (legacyText != null) legacyText.text = "던전 나오기";
                    if (save.GetComponentInParent<LayoutGroup>() == null)
                        ((RectTransform)_exitDungeon.transform).anchoredPosition += new Vector2(0, -390);
                }
            }
            if (_exitDungeon != null)
            {
                _exitDungeon.gameObject.SetActive(AR.s.Dungeon.IsInside);
                _exitDungeon.interactable = AR.s.Npc.IsPlayerInDuel == false;
            }
            AR.s.Dungeon.SetMenuOpen(true);
        }

        public override void OnClose()
        {
            AR.s.Dungeon.SetMenuOpen(false);
            base.OnClose();
        }

        public async void OnClickSave()
        {
            if (_saving == true) return;
            _saving = true;
            try
            {
                // 결투 중에는 저장 요청을 거부한다 (NPC_COMBAT_DESIGN §7.2)
                bool duelBlocked = AR.s.Npc.IsPlayerInDuel == true;
                bool saved = await AR.s.Data.SaveAsync();
                if (duelBlocked == true)
                    AR.s.UI.SetNotify("결투 중에는 저장할 수 없습니다.");
                else
                    AR.s.UI.SetNotify(saved ? "진행 상황을 저장했습니다." : "저장에 실패했습니다. 다시 시도하세요.");
            }
            finally { _saving = false; }
        }

        public async void OnClickExitDungeon()
        {
            if (_saving == true || AR.s.Dungeon.IsBusy == true) return;
            if (AR.s.Npc.IsPlayerInDuel == true)
            {
                AR.s.UI.SetNotify("결투 중에는 던전에서 나갈 수 없습니다.");
                return;
            }
            _saving = true;
            try { if (await AR.s.Dungeon.ExitAsync() == true) Close(); }
            finally { _saving = false; }
        }

        public void OnClickSettings() => AR.s.UI.Show<UISettings>(AddressablePath.Settings, UIManager.Layer.Main);
        public void OnClickQuickSlots() => AR.s.UI.Show<UIQuickSlots>(AddressablePath.QuickSlots, UIManager.Layer.Main);
        public void OnClickInventory() => AR.s.UI.Show<UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
        public void OnClickSkillBook() => AR.s.UI.Show<UISkillBook>(AddressablePath.SkillBook, UIManager.Layer.Main);
        public void OnClickCharmBoard() => AR.s.UI.Show<UICharmBoard>(AddressablePath.CharmBoard, UIManager.Layer.Main);

        private void AddMenuButton(Button template, string label, float offset, UnityEngine.Events.UnityAction handler)
        {
            var button = Instantiate(template, template.transform.parent);
            button.name = label;
            button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(handler);
            var text = button.GetComponentInChildren<TMP_Text>(); if (text != null) text.text = label;
            var legacy = button.GetComponentInChildren<Text>(); if (legacy != null) legacy.text = label;
            if (template.GetComponentInParent<LayoutGroup>() == null) ((RectTransform)button.transform).anchoredPosition += new Vector2(0, offset);
        }
    }    
}
