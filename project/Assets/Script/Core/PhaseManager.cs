using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// フェーズのステートマシン
    /// 探索(15分) → 警告(1分・モニターに接近映像) → 来訪 → 12時から探索…
    ///
    /// ・探索の半分経過時点で確率により「笑い声イベント」を発生
    /// ・来訪フェーズ／イベント中は時計が停止し、カウントダウンも一時停止する
    /// </summary>
    public class PhaseManager : MonoBehaviour
    {
        public static PhaseManager Instance { get; private set; }

        [Header("フェーズ時間（秒）※仕様：探索900=15分")]
        [Tooltip("探索フェーズの長さ。本仕様では 900（15分に一回来訪）")]
        [SerializeField] private float _explorationDuration = 120f;   // 確認用に短縮。仕様は900
        [Tooltip("接近映像フェーズ。仕様では 60（1分後に部屋到達）")]
        [SerializeField] private float _warningDuration = 60f;
        [SerializeField] private float _visitDuration = 45f;

        [Header("笑い声イベント")]
        [Tooltip("探索フェーズの何割経過で抽選するか")]
        [Range(0f, 1f)]
        [SerializeField] private float _eventTriggerRatio = 0.5f;
        [Tooltip("抽選確率")]
        [Range(0f, 1f)]
        [SerializeField] private float _eventChance = 0.5f;

        [Header("探索者の出現順（空ならランダム）")]
        [SerializeField] private SearcherType[] _searcherSequence;

        public GamePhase CurrentPhase { get; private set; } = GamePhase.Exploration;
        public SearcherType NextSearcherType { get; private set; }
        public float PhaseRemaining { get; private set; }
        public float ExplorationDuration => _explorationDuration;
        /// <summary>時計が進んでよいか（探索中かつイベント非発生時のみ）</summary>
        public bool IsClockRunning => CurrentPhase == GamePhase.Exploration && !EventActive;
        /// <summary>笑い声イベント進行中（探索停止扱い）</summary>
        public bool EventActive { get; private set; }

        /// <summary>来訪までの残り時間（探索＋警告）</summary>
        public float TimeUntilVisit
        {
            get
            {
                switch (CurrentPhase)
                {
                    case GamePhase.Exploration: return PhaseRemaining + _warningDuration;
                    case GamePhase.Warning: return PhaseRemaining;
                    default: return 0f;
                }
            }
        }

        private int _sequenceIndex;
        private bool _running = true;
        private bool _eventRolledThisCycle;
        private Coroutine _loop;

        private void Awake()
        {
            Instance = this;

            // ScriptableObject の設定値で上書き（存在しなければ Inspector 値を使用）
            var cfg = GameBalanceConfig.Instance;
            if (cfg != null)
            {
                _explorationDuration = cfg.explorationDuration;
                _warningDuration = cfg.warningDuration;
                _visitDuration = cfg.visitDuration;
                _eventTriggerRatio = cfg.eventTriggerRatio;
                _eventChance = cfg.eventChance;
            }
        }

        private void Start()
        {
            GameEvents.OnGameOver += StopLoop;
            GameEvents.OnGameClear += StopLoop;
            GameEvents.OnLaughterEventEnd += HandleEventEnd;
            GameEvents.OnGameStarted += BeginPhases;
            // タイトルでゲーム開始を待つ。OnGameStarted でループ起動。
        }

        private void OnDestroy()
        {
            GameEvents.OnGameOver -= StopLoop;
            GameEvents.OnGameClear -= StopLoop;
            GameEvents.OnLaughterEventEnd -= HandleEventEnd;
            GameEvents.OnGameStarted -= BeginPhases;
            if (Instance == this) Instance = null;
        }

        /// <summary>ゲーム開始（はじめから/つづきから）でフェーズループを起動</summary>
        private void BeginPhases()
        {
            _running = true;
            if (_loop != null) StopCoroutine(_loop);
            _loop = StartCoroutine(PhaseLoop());
        }

        private void StopLoop()
        {
            _running = false;
            if (_loop != null) StopCoroutine(_loop);
        }

        private void HandleEventEnd() => EventActive = false;

        private IEnumerator PhaseLoop()
        {
            while (_running)
            {
                // ---- 探索フェーズ ----
                NextSearcherType = PickNextSearcher();
                _eventRolledThisCycle = false;
                SetPhase(GamePhase.Exploration);
                GameEvents.RaiseNextSearcherAnnounced(NextSearcherType);
                yield return ExplorationCountdown();
                if (!_running) yield break;

                // ---- 警告フェーズ（モニターに接近映像）----
                SetPhase(GamePhase.Warning);
                GameEvents.RaiseClockChime();   // 鐘が鳴る
                yield return Countdown(_warningDuration);
                if (!_running) yield break;

                // ---- 来訪フェーズ ----
                SetPhase(GamePhase.Visit);
                GameEvents.RaiseVisitStart(NextSearcherType);
                yield return Countdown(_visitDuration);
                GameEvents.RaiseVisitEnd();
            }
        }

        /// <summary>探索カウントダウン（イベント中は停止、半分で抽選）</summary>
        private IEnumerator ExplorationCountdown()
        {
            PhaseRemaining = _explorationDuration;
            while (PhaseRemaining > 0f && _running)
            {
                if (!EventActive)
                {
                    PhaseRemaining -= Time.deltaTime;

                    // 半分経過時点でイベント抽選
                    if (!_eventRolledThisCycle &&
                        PhaseRemaining <= _explorationDuration * (1f - _eventTriggerRatio))
                    {
                        _eventRolledThisCycle = true;
                        if (Random.value < _eventChance)
                        {
                            EventActive = true;
                            GameEvents.RaiseLaughterEventStart();
                        }
                    }
                }
                yield return null;
            }
            PhaseRemaining = 0f;
        }

        private IEnumerator Countdown(float duration)
        {
            PhaseRemaining = duration;
            while (PhaseRemaining > 0f && _running)
            {
                PhaseRemaining -= Time.deltaTime;
                yield return null;
            }
            PhaseRemaining = 0f;
        }

        private void SetPhase(GamePhase phase)
        {
            CurrentPhase = phase;
            GameEvents.RaisePhaseChanged(phase);
        }

        private SearcherType PickNextSearcher()
        {
            if (_searcherSequence != null && _searcherSequence.Length > 0)
            {
                var t = _searcherSequence[_sequenceIndex % _searcherSequence.Length];
                _sequenceIndex++;
                return t;
            }
            return (SearcherType)Random.Range(0, 3);
        }

        /// <summary>死亡後、12時（来訪直後）から仕切り直す</summary>
        public void RestartCycleAtTwelve()
        {
            if (!_running) return;
            EventActive = false;
            if (_loop != null) StopCoroutine(_loop);
            GameEvents.RaiseVisitEnd();
            GameEvents.RaiseLaughterEventEnd();
            _loop = StartCoroutine(PhaseLoop());
        }
    }
}
