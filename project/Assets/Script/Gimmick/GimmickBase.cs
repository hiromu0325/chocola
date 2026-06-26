using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>HUDがインタラクト対象の説明/進捗を表示するためのインターフェース</summary>
    public interface IPromptProvider
    {
        string GetPrompt();
        /// <summary>進捗 0-1。進捗表示が不要なら負値を返す</summary>
        float GetProgress01();
    }

    /// <summary>
    /// 脱出ギミック：インタラクト長押しで時間をかけて解除する
    /// ・既存 InteractionController は押下中毎フレーム OnInteract() を呼ぶ仕様
    ///   → 呼び出しの途切れで「離した」を判定する
    /// ・Shift（Sprint入力）併用で「急いで解除」：進行2倍、ただし毎秒抽選で
    ///   ホラー演出（ジャンプスケア）が発生し短時間操作不能になる
    /// ・来訪フェーズに入ると強制中断（進捗は保持＝時間をかければ必ず解ける）
    /// </summary>
    public class GimmickBase : MonoBehaviour, IInteractable, IPromptProvider
    {
        private static readonly List<GimmickBase> All = new List<GimmickBase>();
        public static int TotalCount => All.Count;
        public static int SolvedCount
        {
            get { int n = 0; foreach (var g in All) if (g.IsSolved) n++; return n; }
        }
        public static bool AllSolved => All.Count > 0 && SolvedCount == All.Count;

        [Header("ギミック設定")]
        [SerializeField] private string _displayName = "ギミック";
        [Tooltip("解除に必要な合計ホールド時間（秒）")]
        [SerializeField] private float _requiredSeconds = 12f;

        [Header("急かし（Rush）設定")]
        [SerializeField] private float _rushMultiplier = 2f;
        [Tooltip("急かし中、1秒あたりのジャンプスケア発生確率")]
        [Range(0f, 1f)]
        [SerializeField] private float _scareChancePerSecond = 0.22f;
        [Tooltip("ジャンプスケア後の操作不能時間（秒）")]
        [SerializeField] private float _scareLockout = 2.0f;
        [Tooltip("ビビり絶叫のノイズ半径（聴覚型に聞こえる）")]
        [SerializeField] private float _scareNoiseRadius = 12f;

        [Header("見た目（任意）：解除済みで色が変わるRenderer")]
        [SerializeField] private Renderer _statusRenderer;

        public bool IsSolved { get; private set; }
        public string DisplayName => _displayName;
        public float Progress01 => Mathf.Clamp01(_progress / _requiredSeconds);

        /// <summary>セーブ用：解除済みギミック名の列挙</summary>
        public static IEnumerable<string> GetSolvedNames()
        {
            foreach (var g in All) if (g.IsSolved) yield return g._displayName;
        }

        /// <summary>ロード用：名前が一致するギミックを解除済みに復元</summary>
        public static void ApplySolved(ICollection<string> names)
        {
            if (names == null) return;
            foreach (var g in All)
                if (!g.IsSolved && names.Contains(g._displayName)) g.MarkSolvedSilently();
        }

        private void MarkSolvedSilently()
        {
            IsSolved = true;
            _progress = _requiredSeconds;
            UpdateVisual();
            GameEvents.RaiseGimmickSolved(SolvedCount, TotalCount);
        }

        private float _progress;
        private float _lastHoldCallTime = -10f;
        private float _lockoutUntil;
        private bool _heldThisFrame;
        private AudioSource _audio;
        private PlayerStatus _player;
        private StarterAssetsInputs _playerInputs;
        private float _clickTimer;
        private GamePhase _phase = GamePhase.Exploration;

        // ---- IInteractable ----
        public bool CanInteract =>
            !IsSolved &&
            _phase != GamePhase.Visit &&            // 来訪フェーズ中は操作不可
            !(PhaseManager.Instance != null && PhaseManager.Instance.EventActive) && // 笑い声イベント中も停止
            Time.time >= _lockoutUntil &&
            (GameManager.Instance == null || !GameManager.Instance.IsGameEnded);

        /// <summary>InteractionController から押下中毎フレーム呼ばれる</summary>
        public void OnInteract()
        {
            _lastHoldCallTime = Time.time;
        }

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.maxDistance = 15f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            GameEvents.OnPhaseChanged += HandlePhaseChanged;
        }

        private void OnDisable()
        {
            All.Remove(this);
            GameEvents.OnPhaseChanged -= HandlePhaseChanged;
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerStatus>();
            if (_player != null)
                _playerInputs = _player.GetComponent<StarterAssetsInputs>();

            // ScriptableObject の設定値で調整（各ギミックの基準時間に倍率を掛ける）
            var cfg = GameBalanceConfig.Instance;
            if (cfg != null)
            {
                _requiredSeconds *= Mathf.Max(0.01f, cfg.gimmickSolveTimeMultiplier);
                _rushMultiplier = cfg.gimmickRushMultiplier;
            }

            UpdateVisual();
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            _phase = phase;
            if (phase == GamePhase.Visit && IsHolding())
            {
                // 強制中断（進捗は保持）
                _lastHoldCallTime = -10f;
                Debug.Log($"[Gimmick] {_displayName}: 来訪フェーズにより解除が強制中断された");
            }
        }

        private bool IsHolding() => Time.time - _lastHoldCallTime < 0.15f;

        private void Update()
        {
            _heldThisFrame = !IsSolved && CanInteract && IsHolding();
            if (!_heldThisFrame) return;

            bool rushing = _playerInputs != null && _playerInputs.sprint;
            float rate = rushing ? _rushMultiplier : 1f;
            _progress += Time.deltaTime * rate;

            // 操作音（カチカチ）。急かし中は速く鳴る → 聴覚的フィードバック
            _clickTimer -= Time.deltaTime * rate;
            if (_clickTimer <= 0f)
            {
                _audio.PlayOneShot(ProceduralAudio.Click(), 0.8f);
                _clickTimer = 0.4f;
            }

            // 急かし中：ジャンプスケア抽選
            if (rushing && Random.value < _scareChancePerSecond * Time.deltaTime)
                TriggerScare();

            if (_progress >= _requiredSeconds)
                Solve();
        }

        private void TriggerScare()
        {
            _lockoutUntil = Time.time + _scareLockout;
            _lastHoldCallTime = -10f;
            GameEvents.RaiseJumpScare(0.6f);
            // ビビって声が出る → 聴覚型の敵に聞こえる（来訪前でも履歴的に意味を持たせるならここで）
            if (_player != null) _player.EmitNoise(_scareNoiseRadius);
            Debug.Log($"[Gimmick] {_displayName}: 急かしすぎてホラー演出発生！");
        }

        private void Solve()
        {
            IsSolved = true;
            _audio.PlayOneShot(ProceduralAudio.Unlock(), 1f);
            UpdateVisual();
            GameEvents.RaiseGimmickSolved(SolvedCount, TotalCount);
            Debug.Log($"[Gimmick] {_displayName}: 解除完了 ({SolvedCount}/{TotalCount})");
        }

        private void UpdateVisual()
        {
            if (_statusRenderer == null) _statusRenderer = GetComponentInChildren<Renderer>();
            if (_statusRenderer != null)
                _statusRenderer.material.color = IsSolved
                    ? new Color(0.2f, 0.9f, 0.3f)
                    : new Color(0.85f, 0.25f, 0.2f);
        }

        // ---- IPromptProvider ----
        public string GetPrompt()
        {
            if (IsSolved) return $"{_displayName}（解除済み）";
            if (_phase == GamePhase.Visit) return $"{_displayName}（来訪中は操作不可）";
            if (PhaseManager.Instance != null && PhaseManager.Instance.EventActive)
                return $"{_displayName}（…どこかで誰かが笑っている）";
            if (Time.time < _lockoutUntil) return "（動揺している…）";
            return $"[E] 長押しで {_displayName} を解除（Shift併用で急ぐ：危険）";
        }

        public float GetProgress01() => IsSolved ? -1f : Progress01;
    }
}
