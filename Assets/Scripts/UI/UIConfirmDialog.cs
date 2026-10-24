#nullable enable
using System;
using ARPG.Base;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 공용 확인 창 (PLAYER_LEVEL_CHARM_DESIGN.md §8.9). 포인트 되돌리기와 참이 박힌 판 판매가 같은 창을 쓴다.
    /// 호출자: AR.s.UI.Show&lt;UIConfirmDialog&gt;(AddressablePath.ConfirmDialog, Layer.Popup)?.Setup(문구, 확인 시 할 일).
    /// [확인]은 창을 닫은 뒤 할 일을 실행하고, [취소]·ESC는 아무것도 하지 않고 닫는다.
    /// </summary>
    public class UIConfirmDialog : UIBaseForm
    {
        private UIDocument? _document;
        private VisualElement? _lastRoot;
        private Text? _messageText;
        private Button? _okButton;
        private Button? _cancelButton;
        private Action? _onConfirm;
        private Action _okClickedHandler = null!;
        private Action _cancelClickedHandler = null!;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
            _document = GetComponent<UIDocument>();
            _okClickedHandler = OnClickOk;
            _cancelClickedHandler = OnClickCancel;
            if (_document == null)
            {
                Debug.LogError("[UIConfirmDialog] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }
            EnsureBound();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            AR.s.Tooltip.Hide();
        }

        public override void OnClose()
        {
            _onConfirm = null;
            base.OnClose();
        }

        public void Setup(string message, Action onConfirm)
        {
            EnsureBound();
            _onConfirm = onConfirm;
            if (_messageText != null)
                _messageText.text = message;
        }

        private void EnsureBound()
        {
            VisualElement? root = _document?.rootVisualElement;
            if (root == null || root == _lastRoot)
                return;

            if (_okButton != null) _okButton.clicked -= _okClickedHandler;
            if (_cancelButton != null) _cancelButton.clicked -= _cancelClickedHandler;

            _lastRoot = root;
            _messageText = root.Q<Text>("message-text");
            _okButton = root.Q<Button>("ok-btn");
            _cancelButton = root.Q<Button>("cancel-btn");
            if (_okButton != null) _okButton.clicked += _okClickedHandler;
            if (_cancelButton != null) _cancelButton.clicked += _cancelClickedHandler;
        }

        private void OnClickOk()
        {
            Action? onConfirm = _onConfirm;
            _onConfirm = null;
            Close();
            onConfirm?.Invoke();
        }

        private void OnClickCancel()
        {
            _onConfirm = null;
            Close();
        }
    }
}
