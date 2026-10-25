#nullable enable
using System;
using ARPG.Data;
using ARPG.Input;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARPG.Manager
{
    /// <summary>Machine settings live independently of character/world saves.</summary>
    public sealed class GameSettingsManager
    {
        private const string SaveKey = "ARPG.GameSettings.v1";
        private ArpgInput _input = null!;
        public GameSettingsData Current { get; private set; } = new();

        public void Initialize(ArpgInput input)
        {
            _input = input;
            GameSettingsData defaults = new() { Width = Screen.width, Height = Screen.height, ScreenMode = Screen.fullScreenMode };
            try { Current = JsonConvert.DeserializeObject<GameSettingsData>(PlayerPrefs.GetString(SaveKey, "")) ?? defaults; }
            catch (Exception exception) { Debug.LogWarning("[Settings] 설정을 읽지 못해 기본값을 사용합니다: " + exception.Message); Current = defaults; }
            Current.Normalize();
            ApplyRuntime(Current, true);
        }

        public bool Save(GameSettingsData settings)
        {
            settings.Normalize();
            try
            {
                PlayerPrefs.SetString(SaveKey, JsonConvert.SerializeObject(settings));
                PlayerPrefs.Save();
                Current = settings.Copy();
                ApplyRuntime(Current, true);
                return true;
            }
            catch (Exception exception) { Debug.LogError("[Settings] 설정 저장 실패: " + exception.Message); return false; }
        }

        public void ApplyRuntime(GameSettingsData settings, bool bindings)
        {
            PreviewAudio(settings);
            _input.MovementDeadzone = settings.MoveDeadzone;
            QualitySettings.vSyncCount = settings.VSync == true ? 1 : 0;
            Application.targetFrameRate = settings.FrameLimit;
            if (Application.isEditor == false)
                Screen.SetResolution(settings.Width, settings.Height, settings.ScreenMode);
            if (bindings == true)
            {
                _input.Asset.Disable();
                _input.Asset.RemoveAllBindingOverrides();
                try
                {
                    if (string.IsNullOrEmpty(settings.BindingOverrides) == false)
                        _input.Asset.LoadBindingOverridesFromJson(settings.BindingOverrides);
                }
                catch (Exception exception) { Debug.LogWarning("[Settings] 잘못된 키 설정을 초기화합니다: " + exception.Message); _input.Asset.RemoveAllBindingOverrides(); }
                _input.RestoreActionMaps();
            }
        }

        public void PreviewAudio(GameSettingsData settings)
        {
            AudioListener.volume = settings.Muted == true ? 0f : settings.MasterVolume;
            _input.Audio.SetVolumes(settings.MusicVolume, settings.EffectsVolume, settings.UIVolume);
        }

        public void Reset() { _input.StopVibration(); ApplyRuntime(Current, true); }
    }
}
