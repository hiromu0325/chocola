using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// オプション設定（PlayerPrefsで永続化）。
    /// ・マスター音量 → AudioListener.volume
    /// ・マウス感度   → FirstPersonController.RotationSpeed
    /// </summary>
    public static class GameSettings
    {
        public static float MasterVolume = 0.8f;   // 0..1
        public static float Sensitivity = 1.0f;    // 0.2..3

        private const string KVolume = "opt_master_volume";
        private const string KSensitivity = "opt_sensitivity";

        public static void LoadAndApply()
        {
            Load();
            Apply();
        }

        public static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(KVolume, 0.8f);
            Sensitivity = PlayerPrefs.GetFloat(KSensitivity, 1.0f);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(KVolume, MasterVolume);
            PlayerPrefs.SetFloat(KSensitivity, Sensitivity);
            PlayerPrefs.Save();
        }

        /// <summary>現在の設定値をゲームへ即時反映</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);

            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.RotationSpeed = Mathf.Clamp(Sensitivity, 0.1f, 5f);
        }
    }
}
