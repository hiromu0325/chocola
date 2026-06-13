using System;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>ゲームフェーズ</summary>
    public enum GamePhase
    {
        Exploration,    // 探索フェーズ（ギミック解除可能）
        Warning,        // 警告フェーズ（まもなく敵来訪）
        Visit           // 来訪フェーズ（敵が部屋にいる・要隠れ）
    }

    /// <summary>敵タイプ</summary>
    public enum EnemyType
    {
        Sight,      // 視覚型：視界に入ると追ってくる
        Sound,      // 聴覚型：足音を頼りに追ってくる（視覚なし）
        Motion      // 動体型：視界は広いが、動いていないと追ってこない
    }

    /// <summary>
    /// グローバルイベントバス（疎結合用）
    /// 各システムはここを経由して通知し合う
    /// </summary>
    public static class GameEvents
    {
        // ---- フェーズ系 ----
        public static event Action<GamePhase> OnPhaseChanged;
        /// <summary>次に来る敵タイプが決定された（モニター表示用）</summary>
        public static event Action<EnemyType> OnNextEnemyAnnounced;
        /// <summary>来訪フェーズ開始（敵スポーン指示）</summary>
        public static event Action<EnemyType> OnVisitStart;
        /// <summary>来訪フェーズ終了（敵撤収指示）</summary>
        public static event Action OnVisitEnd;

        // ---- プレイヤー系 ----
        /// <summary>ノイズ発生（位置, 聞こえる半径）: 聴覚型の敵が使用</summary>
        public static event Action<Vector3, float> OnNoiseEmitted;
        /// <summary>敵に捕まった</summary>
        public static event Action OnPlayerCaught;

        // ---- ギミック系 ----
        /// <summary>ギミック解除完了（解除済み数, 総数）</summary>
        public static event Action<int, int> OnGimmickSolved;
        /// <summary>ジャンプスケア発生要求（強度 0-1）</summary>
        public static event Action<float> OnJumpScare;

        // ---- ゲーム進行 ----
        public static event Action<int> OnLivesChanged;   // 残り人形数
        public static event Action OnGameOver;
        public static event Action OnGameClear;

        public static void RaisePhaseChanged(GamePhase p) => OnPhaseChanged?.Invoke(p);
        public static void RaiseNextEnemyAnnounced(EnemyType t) => OnNextEnemyAnnounced?.Invoke(t);
        public static void RaiseVisitStart(EnemyType t) => OnVisitStart?.Invoke(t);
        public static void RaiseVisitEnd() => OnVisitEnd?.Invoke();
        public static void RaiseNoiseEmitted(Vector3 pos, float radius) => OnNoiseEmitted?.Invoke(pos, radius);
        public static void RaisePlayerCaught() => OnPlayerCaught?.Invoke();
        public static void RaiseGimmickSolved(int solved, int total) => OnGimmickSolved?.Invoke(solved, total);
        public static void RaiseJumpScare(float intensity) => OnJumpScare?.Invoke(intensity);
        public static void RaiseLivesChanged(int lives) => OnLivesChanged?.Invoke(lives);
        public static void RaiseGameOver() => OnGameOver?.Invoke();
        public static void RaiseGameClear() => OnGameClear?.Invoke();

        /// <summary>Domain Reload 無効環境でのハンドラ残留対策</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnPhaseChanged = null; OnNextEnemyAnnounced = null;
            OnVisitStart = null; OnVisitEnd = null;
            OnNoiseEmitted = null; OnPlayerCaught = null;
            OnGimmickSolved = null; OnJumpScare = null;
            OnLivesChanged = null; OnGameOver = null; OnGameClear = null;
        }

        /// <summary>敵タイプの表示名・特徴（モニター/メモ共用）</summary>
        public static string GetEnemyName(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Sight: return "視覚型『ウォッチャー』";
                case EnemyType.Sound: return "聴覚型『リスナー』";
                case EnemyType.Motion: return "動体型『トラッカー』";
            }
            return "不明";
        }

        public static string GetEnemyHint(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Sight:
                    return "目で見て獲物を探す。視界に入ると追ってくる。\n→ ロッカーに隠れて視線を切れ。";
                case EnemyType.Sound:
                    return "目は見えない。足音や物音を頼りに追ってくる。\n→ 動かず静かにしろ。ロッカーの扉の音にも反応する。";
                case EnemyType.Motion:
                    return "視界は非常に広いが、動くものしか見えない。\n→ 視界内でも『完全に静止』していれば気付かれない。";
            }
            return "";
        }
    }
}
