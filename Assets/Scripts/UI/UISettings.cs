#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Data;
using ARPG.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ARPG.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class UISettings : UIBaseForm
    {
        private UIDocument _document = null!;
        private VisualElement _root = null!;
        private ScrollView _content = null!;
        private Label _status = null!;
        private VisualElement _confirmation = null!;
        private GameSettingsData _draft = null!;
        private InputActionAsset _bindings = null!;
        private InputActionRebindingExtensions.RebindingOperation? _capture;
        private Action<InputActionRebindingExtensions.RebindingOperation> _completed = null!;
        private Action<InputActionRebindingExtensions.RebindingOperation> _cancelled = null!;
        private InputAction? _captureAction;
        private int _captureIndex;
        private string _previousPath = string.Empty;
        private InputAction? _conflictAction;
        private int _conflictIndex;
        private float _videoDeadline;
        private int _shownSeconds;
        private float _ignoreCancelUntil;
        private int _tab;
        private bool _pad;
        public bool IsCapturing => _capture != null;
        public VisualElement? ControllerFocusRoot => _videoDeadline > 0f || _conflictAction != null ? _confirmation : null;

        public override void Initialize(string name, bool isForm = false)
        {
            base.Initialize(name, isForm);
            _document = GetComponent<UIDocument>();
            _completed ??= OnCaptureCompleted;
            _cancelled ??= OnCaptureCancelled;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            _draft = AR.s.UI.Settings.Current.Copy();
            _conflictAction = null; _captureAction = null;
            _bindings = Instantiate(AR.s.UI.Input.Asset);
            _bindings.Disable();
            _root = _document.rootVisualElement.Q("settings-root");
            _root.Clear();
            VisualElement panel = new() { name = "settings-panel" };
            panel.AddToClassList("settings-panel");
            _root.Add(panel);
            Label title = new("설정"); title.AddToClassList("settings-title"); panel.Add(title);
            VisualElement tabs = Row(); panel.Add(tabs);
            AddButton(tabs, "화면", () => SelectTab(0));
            AddButton(tabs, "사운드", () => SelectTab(1));
            AddButton(tabs, "조작", () => SelectTab(_pad == true ? 3 : 2));
            _content = new ScrollView(); _content.AddToClassList("settings-content"); panel.Add(_content);
            _status = new Label(); _status.AddToClassList("settings-status"); panel.Add(_status);
            _confirmation = Row(); _confirmation.AddToClassList("settings-confirmation"); panel.Add(_confirmation);
            VisualElement footer = Row(); panel.Add(footer);
            AddButton(footer, "현재 탭 기본값", ResetTab);
            AddButton(footer, "취소", CancelAndClose);
            AddButton(footer, "적용", Apply);
            _videoDeadline = 0f;
            SelectTab(_tab);
        }

        public override void OnClose()
        {
            _capture?.Cancel(); _capture?.Dispose(); _capture = null;
            _videoDeadline = 0f;
            AR.s.UI.Settings.ApplyRuntime(AR.s.UI.Settings.Current, false);
            if (_bindings != null) Destroy(_bindings);
            base.OnClose();
        }

        public override bool UpdateInput(ArpgInput input)
        {
            if (_capture != null)
            {
                if (Gamepad.current?.startButton.wasPressedThisFrame == true) _capture.Cancel();
                return true;
            }
            if (Time.unscaledTime < _ignoreCancelUntil) return true;
            if (input.UI.Cancel.WasPressedThisFrame() == true)
            {
                if (_conflictAction != null) RejectConflict(); else CancelAndClose();
                return true;
            }
            return true;
        }

        private void Update()
        {
            if (_videoDeadline <= 0f) return;
            int seconds = Mathf.CeilToInt(_videoDeadline - Time.unscaledTime);
            if (seconds <= 0) { RevertVideo(); return; }
            if (seconds != _shownSeconds) { _shownSeconds = seconds; _status.text = $"화면 설정을 유지하시겠습니까? {seconds}초 후 복구됩니다."; }
        }

        private void SelectTab(int tab)
        {
            if (_capture != null || _videoDeadline > 0f || _conflictAction != null) return;
            _tab = tab; _pad = tab == 3; _content.Clear(); _confirmation.Clear(); _status.text = string.Empty;
            if (tab == 0) BuildDisplay();
            else if (tab == 1) BuildSound();
            else BuildControls();
        }

        private void BuildDisplay()
        {
            var modes = new List<string> { "전체 화면", "테두리 없는 전체 화면", "창 모드" };
            int mode = _draft.ScreenMode == FullScreenMode.ExclusiveFullScreen ? 0 : _draft.ScreenMode == FullScreenMode.Windowed ? 2 : 1;
            DropdownField screenMode = new("화면 모드", modes, mode); _content.Add(screenMode);
            screenMode.RegisterValueChangedCallback(evt => _draft.ScreenMode = evt.newValue == modes[0] ? FullScreenMode.ExclusiveFullScreen : evt.newValue == modes[2] ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
            List<string> resolutions = new();
            Resolution[] supported = Screen.resolutions;
            for (int i = 0; i < supported.Length; i++)
            {
                string value = supported[i].width + " × " + supported[i].height;
                if (resolutions.Contains(value) == false) resolutions.Add(value);
            }
            string current = _draft.Width + " × " + _draft.Height;
            if (resolutions.Contains(current) == false) resolutions.Add(current);
            DropdownField resolution = new("해상도", resolutions, resolutions.IndexOf(current)); _content.Add(resolution);
            resolution.RegisterValueChangedCallback(evt => { string[] size = evt.newValue.Split('×'); _draft.Width = int.Parse(size[0]); _draft.Height = int.Parse(size[1]); });
            Toggle vsync = new("수직 동기화") { value = _draft.VSync }; _content.Add(vsync);
            vsync.RegisterValueChangedCallback(evt => _draft.VSync = evt.newValue);
            List<string> rates = new() { "제한 없음", "30", "60", "90", "120", "144", "165", "240", "360" };
            string rate = _draft.FrameLimit <= 0 ? rates[0] : _draft.FrameLimit.ToString();
            if (rates.Contains(rate) == false) rates.Add(rate);
            DropdownField fps = new("최대 FPS", rates, rates.IndexOf(rate)); _content.Add(fps);
            fps.RegisterValueChangedCallback(evt => _draft.FrameLimit = evt.newValue == rates[0] ? -1 : int.Parse(evt.newValue));
        }

        private void BuildSound()
        {
            AddVolume("전체 음량", _draft.MasterVolume, value => _draft.MasterVolume = value);
            AddVolume("배경음", _draft.MusicVolume, value => _draft.MusicVolume = value);
            AddVolume("효과음", _draft.EffectsVolume, value => _draft.EffectsVolume = value);
            AddVolume("UI 음량", _draft.UIVolume, value => _draft.UIVolume = value);
            Toggle mute = new("음소거") { value = _draft.Muted }; _content.Add(mute);
            mute.RegisterValueChangedCallback(evt => { _draft.Muted = evt.newValue; AR.s.UI.Settings.PreviewAudio(_draft); });
            VisualElement previews = Row(); _content.Add(previews);
            AddButton(previews, "배경음 미리 듣기", () => AR.s.UI.Input.Audio.Preview(GameAudioChannel.Music));
            AddButton(previews, "효과음 미리 듣기", () => AR.s.UI.Input.Audio.Preview(GameAudioChannel.Effects));
            AddButton(previews, "UI 미리 듣기", () => AR.s.UI.Input.Audio.Preview(GameAudioChannel.UI));
        }

        private void BuildControls()
        {
            VisualElement devices = Row(); _content.Add(devices);
            AddButton(devices, "키보드·마우스", () => SelectTab(2));
            AddButton(devices, "컨트롤러", () => SelectTab(3));
            _content.Add(new Label(_pad == true ? "왼쪽 스틱 이동 · 자동 조준 · LB를 누르면 보조 동작" : "변경할 키를 선택한 뒤 새 키를 누르세요."));
            if (_pad == true)
            {
                Slider deadzone = new("이동 스틱 데드존", 0.05f, 0.5f) { value = _draft.MoveDeadzone, showInputField = true };
                deadzone.RegisterValueChangedCallback(evt => _draft.MoveDeadzone = evt.newValue); _content.Add(deadzone);
                Toggle vibration = new("진동") { value = _draft.Vibration }; _content.Add(vibration);
                vibration.RegisterValueChangedCallback(evt => _draft.Vibration = evt.newValue);
            }
            for (int i = 0; i < GameInputBindings.Definitions.Length; i++)
            {
                var definition = GameInputBindings.Definitions[i];
                InputAction action = _bindings.FindAction("Player/" + definition.Name, true);
                int index = GameInputBindings.BindingIndex(action, _pad);
                if (index < 0) continue;
                VisualElement row = Row(); row.AddToClassList("binding-row");
                Label label = new(definition.Label); label.AddToClassList("binding-name"); row.Add(label);
                AddBindingButton(row, action, index, _pad == true && definition.Modified == true);
                if (_pad == false)
                {
                    int second = GameInputBindings.BindingIndex(action, false, 1);
                    if (second >= 0) AddBindingButton(row, action, second, false);
                }
                _content.Add(row);
            }
            _content.Add(new Label("메뉴 / 취소 키와 패드의 LB 보조 버튼은 고정입니다."));
        }

        private void AddBindingButton(VisualElement row, InputAction action, int index, bool modified)
        {
            string key = action.GetBindingDisplayString(index);
            if (key.Length == 0) key = "미지정";
            AddButton(row, (modified == true ? "LB + " : "") + key, () => Capture(action, index));
        }

        private void Capture(InputAction action, int index)
        {
            if (_capture != null || _conflictAction != null || _videoDeadline > 0f) return;
            _captureAction = action; _captureIndex = index; _previousPath = action.bindings[index].effectivePath;
            _status.text = "새 키·버튼을 누르세요. ESC 또는 패드 메뉴 버튼으로 취소합니다.";
            _capture = action.PerformInteractiveRebinding(index).WithCancelingThrough("<Keyboard>/escape")
                .WithExpectedControlType("Button")
                .WithControlsExcluding("<Gamepad>/start").WithControlsExcluding("<Gamepad>/leftShoulder")
                .OnMatchWaitForAnother(0.15f).OnComplete(_completed).OnCancel(_cancelled);
            if (_pad == true) _capture.WithControlsHavingToMatchPath("<Gamepad>/*");
            else _capture.WithControlsHavingToMatchPath("<Keyboard>/*").WithControlsHavingToMatchPath("<Mouse>/*");
            _capture.Start();
        }

        private void OnCaptureCompleted(InputActionRebindingExtensions.RebindingOperation operation)
        {
            operation.Dispose(); _capture = null; _ignoreCancelUntil = Time.unscaledTime + 0.25f;
            string path = _captureAction!.bindings[_captureIndex].effectivePath;
            for (int i = 0; i < GameInputBindings.Definitions.Length; i++)
            {
                var definition = GameInputBindings.Definitions[i];
                InputAction other = _bindings.FindAction("Player/" + definition.Name, true);
                if (_pad == true && definition.Modified != GameInputBindings.IsModified(_captureAction.name)) continue;
                for (int j = 0; j < other.bindings.Count; j++)
                {
                    var binding = other.bindings[j];
                    if ((other == _captureAction && j == _captureIndex) || binding.isComposite == true || binding.name == "Modifier"
                        || binding.groups != (_pad == true ? GameInputBindings.GamepadGroup : GameInputBindings.KeyboardGroup)
                        || string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase) == false) continue;
                    _conflictAction = other; _conflictIndex = j;
                    _status.text = $"「{definition.Label}」에 이미 사용 중입니다. 키를 교환하시겠습니까?";
                    _confirmation.Clear(); AddButton(_confirmation, "서로 교환", AcceptConflict); AddButton(_confirmation, "취소", RejectConflict); return;
                }
            }
            SelectTab(_tab);
        }

        private void OnCaptureCancelled(InputActionRebindingExtensions.RebindingOperation operation)
        {
            operation.Dispose(); _capture = null; _ignoreCancelUntil = Time.unscaledTime + 0.25f;
            _status.text = "키 변경을 취소했습니다.";
        }

        private void AcceptConflict()
        {
            _conflictAction!.ApplyBindingOverride(_conflictIndex, _previousPath);
            _conflictAction = null; SelectTab(_tab);
        }

        private void RejectConflict()
        {
            _captureAction!.ApplyBindingOverride(_captureIndex, _previousPath);
            _conflictAction = null; SelectTab(_tab);
        }

        private void Apply()
        {
            if (_capture != null || _conflictAction != null || _videoDeadline > 0f) return;
            _draft.BindingOverrides = _bindings.SaveBindingOverridesAsJson();
            var current = AR.s.UI.Settings.Current;
            bool video = current.Width != _draft.Width || current.Height != _draft.Height || current.ScreenMode != _draft.ScreenMode;
            if (video == true)
            {
                AR.s.UI.Settings.ApplyRuntime(_draft, false);
                _videoDeadline = Time.unscaledTime + 15f; _shownSeconds = -1;
                _content.SetEnabled(false);
                _confirmation.Clear(); AddButton(_confirmation, "화면 설정 유지", KeepVideo); AddButton(_confirmation, "복구", RevertVideo);
                return;
            }
            SaveDraft();
        }

        private void KeepVideo() { _videoDeadline = 0f; _content.SetEnabled(true); _confirmation.Clear(); SaveDraft(); }
        private void SaveDraft() { _status.text = AR.s.UI.Settings.Save(_draft) == true ? "설정을 저장했습니다." : "설정 저장에 실패했습니다."; }
        private void RevertVideo()
        {
            _videoDeadline = 0f; var current = AR.s.UI.Settings.Current;
            _content.SetEnabled(true);
            _draft.Width = current.Width; _draft.Height = current.Height; _draft.ScreenMode = current.ScreenMode;
            AR.s.UI.Settings.ApplyRuntime(current, false); SelectTab(_tab); _status.text = "이전 화면 설정으로 복구했습니다.";
        }
        private void CancelAndClose() { if (_capture != null) _capture.Cancel(); else Close(); }

        private void ResetTab()
        {
            if (_capture != null || _videoDeadline > 0f || _conflictAction != null) return;
            var defaults = new GameSettingsData();
            if (_tab == 0) { _draft.Width = Screen.currentResolution.width; _draft.Height = Screen.currentResolution.height; _draft.ScreenMode = defaults.ScreenMode; _draft.FrameLimit = defaults.FrameLimit; _draft.VSync = defaults.VSync; }
            else if (_tab == 1) { _draft.MasterVolume = defaults.MasterVolume; _draft.MusicVolume = defaults.MusicVolume; _draft.EffectsVolume = defaults.EffectsVolume; _draft.UIVolume = defaults.UIVolume; _draft.Muted = false; AR.s.UI.Settings.PreviewAudio(_draft); }
            else
            {
                string group = _pad == true ? GameInputBindings.GamepadGroup : GameInputBindings.KeyboardGroup;
                foreach (InputAction action in _bindings)
                    for (int i = 0; i < action.bindings.Count; i++) if (action.bindings[i].groups == group) action.RemoveBindingOverride(i);
                if (_pad == true) { _draft.MoveDeadzone = defaults.MoveDeadzone; _draft.Vibration = defaults.Vibration; }
            }
            SelectTab(_tab);
        }

        private void AddVolume(string label, float value, Action<float> setter)
        {
            Slider slider = new(label, 0f, 1f) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(evt => { setter(evt.newValue); AR.s.UI.Settings.PreviewAudio(_draft); }); _content.Add(slider);
        }

        private static VisualElement Row() { VisualElement row = new(); row.AddToClassList("settings-row"); return row; }
        private static Button AddButton(VisualElement parent, string text, Action callback)
        { Button button = new(callback) { text = text }; parent.Add(button); return button; }
    }
}
