using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ゲームフェーズのステートマシン
    ///
    /// [PhaseStateMachine]
    /// ├── Exploration（探索）
    /// │   ├── OnEnter: 次の敵タイプを抽選 → モニターに通知
    /// │   └── 時間経過 → Warning
    /// ├── Warning（警告）
    /// │   ├── OnEnter: モニター点滅・警告音
    /// │   └── 時間経過 → Visit
    /// └── Visit（来訪）
    ///     ├── OnEnter: 敵スポーン / ギミック強制中断（GimmickBase側）
    ///     ├── OnExit:  敵撤収指示
    ///     └── 時間経過 → Exploration（ループ）
    /// </summary>
    public class PhaseManager : MonoBehaviour
    {
        public static PhaseManager Instance { get; private set; }

        [Header("フェーズ時間（秒）※仕様は探索600秒。テスト用に短縮済み")]
        [Tooltip("探索フェーズの長さ。本仕様では 600（=10分に一回敵が来る）")]
        [SerializeField] private float _explorationDuration = 90f;
        [SerializeField] private float _warningDuration = 12f;
        [SerializeField] private float _visitDuration = 40f;

        [Header("敵タイプの出現順（空ならランダム）")]
        [SerializeField] private EnemyType[] _enemySequence;

        public GamePhase CurrentPhase { get; private set; } = GamePhase.Exploration;
        public EnemyType NextEnemyType { get; private set; }
        /// <summary>現在フェーズの残り時間（秒）</summary>
        public float PhaseRemaining { get; private set; }
        /// <summary>敵来訪までの残り時間（探索+警告の合計。モニター表示用）</summary>
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
        private Coroutine _loop;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            GameEvents.OnGameOver += StopLoop;
            GameEvents.OnGameClear += StopLoop;
            _loop = StartCoroutine(PhaseLoop());
        }

        private void OnDestroy()
        {
            GameEvents.OnGameOver -= StopLoop;
            GameEvents.OnGameClear -= StopLoop;
            if (Instance == this) Instance = null;
        }

        private void StopLoop()
        {
            _running = false;
            if (_loop != null) StopCoroutine(_loop);
        }

        private IEnumerator PhaseLoop()
        {
            while (_running)
            {
                // ---- 探索フェーズ ----
                NextEnemyType = PickNextEnemy();
                SetPhase(GamePhase.Exploration);
                GameEvents.RaiseNextEnemyAnnounced(NextEnemyType);
                yield return Countdown(_explorationDuration);
                if (!_running) yield break;

                // ---- 警告フェーズ ----
                SetPhase(GamePhase.Warning);
                yield return Countdown(_warningDuration);
                if (!_running) yield break;

                // ---- 来訪フェーズ ----
                SetPhase(GamePhase.Visit);
                GameEvents.RaiseVisitStart(NextEnemyType);
                yield return Countdown(_visitDuration);
                GameEvents.RaiseVisitEnd();
            }
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

        private EnemyType PickNextEnemy()
        {
            if (_enemySequence != null && _enemySequence.Length > 0)
            {
                var t = _enemySequence[_sequenceIndex % _enemySequence.Length];
                _sequenceIndex++;
                return t;
            }
            return (EnemyType)Random.Range(0, 3);
        }

        /// <summary>捕まった時など、サイクルを探索フェーズから仕切り直す</summary>
        public void RestartCycleFromExploration()
        {
            if (!_running) return;
            if (_loop != null) StopCoroutine(_loop);
            GameEvents.RaiseVisitEnd(); // 念のため敵撤収
            _loop = StartCoroutine(PhaseLoop());
        }
    }
}
