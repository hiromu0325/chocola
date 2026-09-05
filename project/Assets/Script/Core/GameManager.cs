using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>ゲーム全体の状態（フロー）</summary>
    public enum GameState
    {
        Title,    // タイトル画面
        Playing,  // プレイ中
        Paused,   // ポーズ中（オプション等）
        Ended     // ゲームオーバー / クリア
    }

    /// <summary>
    /// ゲーム全体統括＆フロー制御。
    /// タイトル → (はじめから/つづきから) → プレイ ⇄ ポーズ → 終了。
    /// 陶器人形（残機5体）、死亡→ホワイトアウト→人形破壊→12時から再開。
    /// 人形が全て壊れた状態で死ぬとゲームオーバー。「そして誰もいなくなった」
    /// チェックポイント（ギミック解除・来訪生存・リスポーン）で自動セーブ。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("陶器の人形（残機）")]
        [SerializeField] private int _dolls = 5;

        [Header("プレイヤー")]
        [SerializeField] private Transform _player;
        [SerializeField] private Transform _respawnPoint;

        public int Dolls => _dolls;
        public GameState State { get; private set; } = GameState.Title;
        public bool IsGameEnded => State == GameState.Ended;
        public bool IsRespawning { get; private set; }

        private int _defaultDolls;
        private StarterAssetsInputs _inputs;
        private FirstPersonController _fpc;
        private InteractionController _interaction;

        private void Awake()
        {
            Instance = this;
            _defaultDolls = _dolls;
        }

        private void Start()
        {
            if (_player == null)
            {
                var pc = FindFirstObjectByType<CharacterController>();
                if (pc != null) _player = pc.transform;
            }
            if (_player != null)
            {
                _inputs = _player.GetComponent<StarterAssetsInputs>();
                _fpc = _player.GetComponent<FirstPersonController>();
                _interaction = _player.GetComponent<InteractionController>();
            }

            GameEvents.OnPlayerCaught += HandleDeath;
            GameEvents.OnSpecialDeath += HandleSpecialDeath;
            GameEvents.OnGimmickSolved += HandleGimmickSolved;
            GameEvents.OnVisitEnd += HandleVisitEnd;
            GameEvents.OnPcAccessed += HandlePuzzleProgress;
            GameEvents.OnKeyFound += HandlePuzzleProgress;
            GameEvents.OnPanelUnlocked += HandlePuzzleProgress;
            GameEvents.OnRepairCodeAccepted += HandlePuzzleProgress;
            GameEvents.OnPowerRestored += HandlePuzzleProgress;
            GameEvents.OnSafeOpened += HandlePuzzleProgress;

            GameSettings.LoadAndApply();

            EnterTitle();
        }

        private void OnDestroy()
        {
            GameEvents.OnPlayerCaught -= HandleDeath;
            GameEvents.OnSpecialDeath -= HandleSpecialDeath;
            GameEvents.OnGimmickSolved -= HandleGimmickSolved;
            GameEvents.OnVisitEnd -= HandleVisitEnd;
            GameEvents.OnPcAccessed -= HandlePuzzleProgress;
            GameEvents.OnKeyFound -= HandlePuzzleProgress;
            GameEvents.OnPanelUnlocked -= HandlePuzzleProgress;
            GameEvents.OnRepairCodeAccepted -= HandlePuzzleProgress;
            GameEvents.OnPowerRestored -= HandlePuzzleProgress;
            GameEvents.OnSafeOpened -= HandlePuzzleProgress;
            if (Instance == this) Instance = null;
        }

        // ============================== フロー ==============================

        private void SetState(GameState s)
        {
            State = s;
            GameEvents.RaiseGameStateChanged(s);
        }

        public void EnterTitle()
        {
            IsRespawning = false;
            Time.timeScale = 0f;
            SetPlayerControl(false);
            ShowCursor(true);
            SetState(GameState.Title);
        }

        /// <summary>はじめから</summary>
        public void NewGame()
        {
            _dolls = _defaultDolls;
            GameEvents.RaiseDollsChanged(_dolls);
            Notebook.Clear();
            if (PuzzleState.Instance != null) PuzzleState.Instance.NewGame();
            BeginPlay();
        }

        /// <summary>つづきから（セーブが無ければ新規開始）</summary>
        public void ContinueGame()
        {
            var data = SaveSystem.Load();
            if (data == null || !data.valid) { NewGame(); return; }

            _dolls = Mathf.Clamp(data.dolls, 0, _defaultDolls);
            GameEvents.RaiseDollsChanged(_dolls);
            if (data.solvedGimmicks != null)
                GimmickBase.ApplySolved(new HashSet<string>(data.solvedGimmicks));
            if (PuzzleState.Instance != null) PuzzleState.Instance.LoadFrom(data);
            Notebook.LoadFrom(data.notes);

            // ループ回廊（ストーリープロトタイプ）の進行を復元
            LoopRooms.Stage = Mathf.Max(LoopRooms.Stage, data.loopStage);
            LoopProgress.ImportFound(data.loopFound);
            StoryProgress.ImportVisited(data.loopVisited);
            StoryProgress.IntroPlayed = data.loopIntroPlayed || data.loopStage > 0;
            foreach (var f in FindObjectsByType<LoopFindable>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                f.RefreshFound();

            BeginPlay();
        }

        private void BeginPlay()
        {
            Time.timeScale = 1f;
            SetPlayerControl(true);
            ShowCursor(false);
            SetState(GameState.Playing);
            GameEvents.RaiseGameStarted();   // PhaseManager がフェーズループを開始
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            Time.timeScale = 0f;
            SetPlayerControl(false);
            ShowCursor(true);
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            Time.timeScale = 1f;
            SetPlayerControl(true);
            ShowCursor(false);
            SetState(GameState.Playing);
        }

        public void SaveNow()
        {
            SaveSystem.Save(BuildSaveData());
        }

        /// <summary>ポーズメニュー「タイトルへ戻る」：セーブしてシーンを再読込</summary>
        public void QuitToTitle()
        {
            if (State == GameState.Playing || State == GameState.Paused) SaveNow();
            Time.timeScale = 1f;
            RestartGame();
        }

        // ============================== セーブ ==============================

        private SaveData BuildSaveData()
        {
            var data = new SaveData
            {
                dolls = _dolls,
                solvedGimmicks = new List<string>(GimmickBase.GetSolvedNames()),
                savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm")
            };
            if (PuzzleState.Instance != null) PuzzleState.Instance.WriteTo(data);
            data.notes = Notebook.ToList();
            data.loopStage = LoopRooms.Stage;
            data.loopFound = LoopProgress.ExportFound();
            data.loopVisited = StoryProgress.ExportVisited();
            data.loopIntroPlayed = StoryProgress.IntroPlayed;
            return data;
        }

        private void AutoSave()
        {
            if (State != GameState.Playing) return;
            SaveSystem.Save(BuildSaveData());
        }

        private void HandleGimmickSolved(int solved, int total) => AutoSave();
        private void HandleVisitEnd() => AutoSave();
        private void HandlePuzzleProgress() => AutoSave();

        /// <summary>資料閲覧・コード入力中はプレイヤー操作を止めカーソルを表示（探索者は動き続ける）</summary>
        public void SetBusy(bool busy)
        {
            if (State != GameState.Playing) return;
            SetPlayerControl(!busy);
            ShowCursor(busy);   // テンキー等をマウスで操作できるようカーソルを出す
        }

        // ============================== 死亡 / リスポーン ==============================

        private void HandleSpecialDeath(string id) => HandleDeath();

        private void HandleDeath()
        {
            if (State != GameState.Playing) return;
            if (IsRespawning) return;

            // 人形が残っていなければゲームオーバー
            if (_dolls <= 0)
            {
                SetState(GameState.Ended);
                Time.timeScale = 1f;
                GameEvents.RaiseWhiteout(1f);
                GameEvents.RaiseGameOver();
                SaveSystem.Delete();   // 全滅でセーブ消去
                StartCoroutine(UnlockCursorDelayed());
                return;
            }

            IsRespawning = true;
            _dolls--;
            GameEvents.RaiseWhiteout(1f);          // ホワイトアウト
            GameEvents.RaiseDollsChanged(_dolls);  // 人形が1体壊れる
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(1.6f); // ホワイトアウトの間

            if (_player != null && _respawnPoint != null)
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.SetPositionAndRotation(_respawnPoint.position, _respawnPoint.rotation);
                if (cc != null) cc.enabled = true;
            }
            var status = _player != null ? _player.GetComponent<PlayerStatus>() : null;
            if (status != null) status.ForceExitHiding();

            // 12時（来訪直後）から仕切り直し
            if (PhaseManager.Instance != null)
                PhaseManager.Instance.RestartCycleAtTwelve();

            IsRespawning = false;
            AutoSave();
        }

        public void NotifyEscaped()
        {
            if (IsGameEnded) return;
            SetState(GameState.Ended);
            Time.timeScale = 1f;
            GameEvents.RaiseGameClear();
            SaveSystem.Delete();   // クリアでセーブ消去
            StartCoroutine(UnlockCursorDelayed());
        }

        private IEnumerator UnlockCursorDelayed()
        {
            yield return new WaitForSeconds(0.5f);
            ShowCursor(true);
        }

        public void RestartGame()
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        // ============================== ヘルパー ==============================

        private void SetPlayerControl(bool on)
        {
            if (_fpc != null) _fpc.enabled = on;
            if (_interaction != null) _interaction.enabled = on;
            if (_inputs != null)
            {
                _inputs.cursorInputForLook = on;
                if (!on) { _inputs.move = Vector2.zero; _inputs.look = Vector2.zero; }
            }
        }

        private void ShowCursor(bool show)
        {
            Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = show;
            if (_inputs != null) _inputs.cursorLocked = !show;
        }
    }
}
