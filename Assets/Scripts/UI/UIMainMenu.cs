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

        public override void OnOpen()
        {
            base.OnOpen();
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
                        ((RectTransform)_exitDungeon.transform).anchoredPosition += new Vector2(0, -65);
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
    }    
}
