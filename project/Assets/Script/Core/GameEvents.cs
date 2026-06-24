using System;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>ゲームフェーズ</summary>
    public enum GamePhase
    {
        Exploration,    // 探索フェーズ（謎解き可能・時計が進む）
        Warning,        // 警告フェーズ（モニターに接近映像／1分後に来訪）
        Visit           // 来訪フェーズ（探索者が在室・机以外の操作不可・時計停止）
    }

    /// <summary>
    /// 探索者の種類（仕様の対策に対応）
    /// 視覚型 → 隠れる / 電気を消す
    /// 聴覚型 → 動かない（足音を立てない）／鈴を鳴らして歩く
    /// 嗅覚型 → 香水で臭いを消す
    /// </summary>
    public enum SearcherType
    {
        Sight,  // 視覚型
        Sound,  // 聴覚型
        Smell   // 嗅覚型
    }

    /// <summary>
    /// グローバルイベントバス（疎結合用）
    /// </summary>
    public static class GameEvents
    {
        // ---- フェーズ系 ----
        public static event Action<GamePhase> OnPhaseChanged;
        /// <summary>次に来る探索者が決定された（手帳照合用に種類を伏せたまま特徴IDも渡す）</summary>
        public static event Action<SearcherType> OnNextSearcherAnnounced;
        /// <summary>来訪フェーズ開始（探索者スポーン）</summary>
        public static event Action<SearcherType> OnVisitStart;
        public static event Action OnVisitEnd;
        /// <summary>古時計が一周して鐘が鳴った</summary>
        public static event Action OnClockChime;

        // ---- 笑い声イベント（日本人形イベント）----
        public static event Action OnLaughterEventStart;
        public static event Action OnLaughterEventEnd;

        // ---- プレイヤー知覚状態 ----
        /// <summary>ノイズ発生（位置, 半径）：聴覚型が使用</summary>
        public static event Action<Vector3, float> OnNoiseEmitted;
        public static event Action OnPlayerCaught;
        /// <summary>特殊死（懐中電灯を点けてしまった等）。引数=演出ID</summary>
        public static event Action<string> OnSpecialDeath;

        // ---- 環境 ----
        public static event Action<bool> OnRoomLightsChanged;     // 部屋の電気 on/off
        public static event Action<bool> OnFlashlightChanged;     // 懐中電灯 on/off
        public static event Action<float> OnPerfumeUsed;          // 香水使用（効果秒数）

        // ---- ギミック ----
        public static event Action<int, int> OnGimmickSolved;
        public static event Action<float> OnJumpScare;            // 強度0-1
        public static event Action<float> OnWhiteout;             // ホワイトアウト（死亡演出）

        // ---- ゲーム進行 ----
        public static event Action<int> OnDollsChanged;           // 残り陶器人形
        public static event Action OnGameOver;
        public static event Action OnGameClear;

        public static void RaisePhaseChanged(GamePhase p) => OnPhaseChanged?.Invoke(p);
        public static void RaiseNextSearcherAnnounced(SearcherType t) => OnNextSearcherAnnounced?.Invoke(t);
        public static void RaiseVisitStart(SearcherType t) => OnVisitStart?.Invoke(t);
        public static void RaiseVisitEnd() => OnVisitEnd?.Invoke();
        public static void RaiseClockChime() => OnClockChime?.Invoke();
        public static void RaiseLaughterEventStart() => OnLaughterEventStart?.Invoke();
        public static void RaiseLaughterEventEnd() => OnLaughterEventEnd?.Invoke();
        public static void RaiseNoiseEmitted(Vector3 pos, float radius) => OnNoiseEmitted?.Invoke(pos, radius);
        public static void RaisePlayerCaught() => OnPlayerCaught?.Invoke();
        public static void RaiseSpecialDeath(string id) => OnSpecialDeath?.Invoke(id);
        public static void RaiseRoomLightsChanged(bool on) => OnRoomLightsChanged?.Invoke(on);
        public static void RaiseFlashlightChanged(bool on) => OnFlashlightChanged?.Invoke(on);
        public static void RaisePerfumeUsed(float dur) => OnPerfumeUsed?.Invoke(dur);
        public static void RaiseGimmickSolved(int s, int t) => OnGimmickSolved?.Invoke(s, t);
        public static void RaiseJumpScare(float i) => OnJumpScare?.Invoke(i);
        public static void RaiseWhiteout(float i) => OnWhiteout?.Invoke(i);
        public static void RaiseDollsChanged(int n) => OnDollsChanged?.Invoke(n);
        public static void RaiseGameOver() => OnGameOver?.Invoke();
        public static void RaiseGameClear() => OnGameClear?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnPhaseChanged = null; OnNextSearcherAnnounced = null;
            OnVisitStart = null; OnVisitEnd = null; OnClockChime = null;
            OnLaughterEventStart = null; OnLaughterEventEnd = null;
            OnNoiseEmitted = null; OnPlayerCaught = null; OnSpecialDeath = null;
            OnRoomLightsChanged = null; OnFlashlightChanged = null; OnPerfumeUsed = null;
            OnGimmickSolved = null; OnJumpScare = null; OnWhiteout = null;
            OnDollsChanged = null; OnGameOver = null; OnGameClear = null;
        }

        // ====== 探索者の名称・特徴・対策（モニター／手帳で共用）======

        public static string GetSearcherName(SearcherType t)
        {
            switch (t)
            {
                case SearcherType.Sight: return "視覚の探索者";
                case SearcherType.Sound: return "鈴の探索者";
                case SearcherType.Smell: return "臭いの探索者";
            }
            return "不明";
        }

        /// <summary>モニター映像で観察できる「特徴」（種類を伏せたヒント）</summary>
        public static string GetSearcherFeature(SearcherType t)
        {
            switch (t)
            {
                case SearcherType.Sight:
                    return "・落ち窪んだ眼で部屋中を見回している\n・暗がりでは立ち止まる";
                case SearcherType.Sound:
                    return "・鈴を鳴らしながら歩いている\n・しきりに耳を澄ませている";
                case SearcherType.Smell:
                    return "・地を這うように鼻を鳴らしている\n・空気の匂いを嗅いでいる";
            }
            return "";
        }

        /// <summary>手帳に書かれた対策（特徴と紐づく）</summary>
        public static string GetSearcherCounter(SearcherType t)
        {
            switch (t)
            {
                case SearcherType.Sight:
                    return "【視覚で探す者】\n見られたら追われる。\n→ ロッカー/ベッド下に隠れて視線を切る。\n→ 部屋の電気を消せば暗闇では見つからない。";
                case SearcherType.Sound:
                    return "【音で探す者】\n足音や物音に反応する。鈴の音が近づく合図。\n→ その場で動かず、息をひそめる。\n→ 隠れる時もロッカーの扉の音に注意。";
                case SearcherType.Smell:
                    return "【臭いで探す者】\nお前の残り香を辿る。隠れても匂いは消えない。\n→ 香水を使って臭いを消すしかない。";
            }
            return "";
        }
    }
}
