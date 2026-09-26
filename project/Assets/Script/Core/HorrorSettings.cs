using UnityEngine;

namespace EscapeProto
{
    /// <summary>恐怖演出の強さ（デバッグ用）</summary>
    public enum HorrorLevel
    {
        Full,      // 通常
        Reduced,   // 軽減：襲撃（停電・警報）は起きるが異形は出ない。激しい点滅・ジャンプスケア無し、襲撃の音は半分
        Off        // なし：襲撃そのものを飛ばす（資料を読み終えたら次の扉がすぐ開く）。終章の包囲も無し
    }

    /// <summary>
    /// 恐怖演出の強さの切り替え（デバッグパネル・DebugLoop.Horror から）。
    /// エディタと開発ビルドだけで効き、製品ビルドでは常に「通常」。値は PlayerPrefs に残る。
    /// </summary>
    public static class HorrorSettings
    {
        private const string Key = "debug_horror_level";
        private static HorrorLevel? _level;

        public static HorrorLevel Level
        {
            get
            {
                if (!Debug.isDebugBuild) return HorrorLevel.Full;
                if (_level == null) _level = (HorrorLevel)Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, 2);
                return _level.Value;
            }
            set
            {
                _level = value;
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>脚本襲撃・終章の包囲が起きる</summary>
        public static bool Attacks => Level != HorrorLevel.Off;
        /// <summary>襲撃で異形が現れる（捕まる）</summary>
        public static bool Searchers => Level == HorrorLevel.Full;
        /// <summary>照明・画面の激しい点滅（予兆のちらつき、復旧の瞬き）</summary>
        public static bool Flicker => Level == HorrorLevel.Full;
        /// <summary>ジャンプスケア（顔・悲鳴）</summary>
        public static bool JumpScares => Level == HorrorLevel.Full;
        /// <summary>警報・襲撃BGM・電気の唸りの音量倍率</summary>
        public static float Volume => Level == HorrorLevel.Full ? 1f : 0.5f;

        public static string Label(HorrorLevel l) =>
            l == HorrorLevel.Full ? "通常" : l == HorrorLevel.Reduced ? "軽減" : "なし";

        public static string Describe(HorrorLevel l) =>
            l == HorrorLevel.Full ? "すべての恐怖演出が入る"
            : l == HorrorLevel.Reduced ? "停電と警報だけ。異形・激しい点滅・ジャンプスケア無し、音は半分"
            : "襲撃を飛ばして次の扉がすぐ開く。終章の包囲も無し";
    }
}
