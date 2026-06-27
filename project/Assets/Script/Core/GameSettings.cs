using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// オプション設定（PlayerPrefsで永続化）。
    /// ・マスター音量 → AudioListener.volume
    /// ・マウス感度   → StarterAssetsInputs.mouseSensitivity（生ピクセルに掛ける係数）
    /// ・上下/左右反転 → StarterAssetsInputs.invertLookX / invertLookY
    /// </summary>
    public static class GameSettings
    {
        public static float MasterVolume = 0.8f;   // 0..1
        public static float Sensitivity = 0.10f;   // マウス感度（0.01..0.50 推奨）
        public static bool InvertX = false;         // 左右反転
        public static bool InvertY = false;         // 上下反転

        public const float SensMin = 0.01f;
        public const float SensMax = 0.50f;

        private const string KVolume = "opt_master_volume";
        private const string KSensitivity = "opt_mouse_sensitivity_v2";
        private const string KInvertX = "opt_invert_x";
        private const string KInvertY = "opt_invert_y";

        public static void LoadAndApply()
        {
            Load();
            Apply();
        }

        public static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(KVolume, 0.8f);
            Sensitivity = PlayerPrefs.GetFloat(KSensitivity, 0.10f);
            InvertX = PlayerPrefs.GetInt(KInvertX, 0) != 0;
            InvertY = PlayerPrefs.GetInt(KInvertY, 0) != 0;
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(KVolume, MasterVolume);
            PlayerPrefs.SetFloat(KSensitivity, Sensitivity);
            PlayerPrefs.SetInt(KInvertX, InvertX ? 1 : 0);
            PlayerPrefs.SetInt(KInvertY, InvertY ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>現在の設定値をゲームへ即時反映</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);

            var inputs = Object.FindFirstObjectByType<StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.mouseSensitivity = Mathf.Clamp(Sensitivity, SensMin, SensMax);
                inputs.invertLookX = InvertX;
                inputs.invertLookY = InvertY;
            }

            // 感度は StarterAssetsInputs 側で掛けるため RotationSpeed は中立(1)。
            // 移動の出だしを機敏にするため加速レートを高めに固定。
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null)
            {
                fpc.RotationSpeed = 1f;
                fpc.SpeedChangeRate = 30f;
            }
        }
    }
}
