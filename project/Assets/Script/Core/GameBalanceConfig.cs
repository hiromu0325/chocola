using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ゲームバランス調整用の ScriptableObject（単一アセット）。
    /// 来訪までの時間・滞在時間・警告時間・ギミック解除時間・
    /// プレイヤー/探索者の移動速度を Editor から静的に調整できる。
    ///
    /// アセットは Resources/GameBalanceConfig.asset に置く。
    /// （Tools > EscapePrototype > Create Game Balance Config で生成）
    /// 各システムは実行時に <see cref="Instance"/> を読み込んで適用する。
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalanceConfig",
                     menuName = "EscapePrototype/Game Balance Config", order = 0)]
    public class GameBalanceConfig : ScriptableObject
    {
        [Header("フェーズ時間（秒）")]
        [Tooltip("探索フェーズ＝来訪までの時間。仕様では 900（15分に一回来訪）")]
        public float explorationDuration = 900f;
        [Tooltip("警告フェーズ＝モニターに接近映像が流れる時間。仕様では 60（1分後に到達）")]
        public float warningDuration = 60f;
        [Tooltip("来訪（滞在）フェーズ＝探索者が在室している時間")]
        public float visitDuration = 45f;

        [Header("笑い声イベント")]
        [Tooltip("探索フェーズの何割経過で抽選するか（0.5＝半分経過後）")]
        [Range(0f, 1f)] public float eventTriggerRatio = 0.5f;
        [Tooltip("イベント発生確率")]
        [Range(0f, 1f)] public float eventChance = 0.5f;

        [Header("ギミック解除")]
        [Tooltip("各ギミックの基準解除時間に掛かる倍率（1=設計値どおり / 0.5=半分の時間で解ける / 2=倍かかる）")]
        public float gimmickSolveTimeMultiplier = 1f;
        [Tooltip("Shift併用『急ぎ解除』の進行速度倍率")]
        public float gimmickRushMultiplier = 2f;

        [Header("配電盤の復旧作業")]
        [Tooltip("コード入力後、長押しで復旧が完了するまでの作業秒数")]
        public float boardRepairDuration = 12f;

        [Header("プレイヤー移動速度（m/s）")]
        public float playerMoveSpeed = 2.4f;
        public float playerSprintSpeed = 4.6f;

        [Header("探索者（敵）移動速度（m/s）")]
        public float searcherPatrolSpeed = 1.5f;
        public float searcherChaseSpeed = 3.3f;

        // ============ 静的アクセサ（Resources から単一アセットをロード）============
        public const string ResourcePath = "GameBalanceConfig";
        private static GameBalanceConfig _instance;

        public static GameBalanceConfig Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = Resources.Load<GameBalanceConfig>(ResourcePath);
                if (_instance == null)
                {
                    Debug.LogWarning(
                        $"[GameBalanceConfig] Resources/{ResourcePath}.asset が見つかりません。既定値で動作します。" +
                        "\nTools > EscapePrototype > Create Game Balance Config でアセットを作成してください。");
                    _instance = CreateInstance<GameBalanceConfig>();
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => _instance = null;
    }
}
