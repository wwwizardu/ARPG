#nullable enable
using System;
using UnityEngine;

namespace ARPG.Data
{
    [Serializable]
    public sealed class GameSettingsData
    {
        public int Version = 1;
        public int Width = 1920;
        public int Height = 1080;
        public FullScreenMode ScreenMode = FullScreenMode.FullScreenWindow;
        public bool VSync = true;
        public int FrameLimit = 120;
        public float MasterVolume = 1f;
        public float MusicVolume = 0.7f;
        public float EffectsVolume = 1f;
        public float UIVolume = 0.8f;
        public bool Muted;
        public float MoveDeadzone = 0.2f;
        public bool Vibration = true;
        public string BindingOverrides = string.Empty;

        public GameSettingsData Copy() => (GameSettingsData)MemberwiseClone();

        public void Normalize()
        {
            Width = Mathf.Clamp(Width, 640, 16384);
            Height = Mathf.Clamp(Height, 480, 16384);
            if (Enum.IsDefined(typeof(FullScreenMode), ScreenMode) == false) ScreenMode = FullScreenMode.FullScreenWindow;
            FrameLimit = FrameLimit <= 0 ? -1 : Mathf.Clamp(FrameLimit, 30, 360);
            MasterVolume = SafeVolume(MasterVolume);
            MusicVolume = SafeVolume(MusicVolume);
            EffectsVolume = SafeVolume(EffectsVolume);
            UIVolume = SafeVolume(UIVolume);
            MoveDeadzone = float.IsNaN(MoveDeadzone) == true ? 0.2f : Mathf.Clamp(MoveDeadzone, 0.05f, 0.5f);
            BindingOverrides ??= string.Empty;
        }

        private static float SafeVolume(float value) => float.IsNaN(value) == true ? 1f : Mathf.Clamp01(value);
    }
}
