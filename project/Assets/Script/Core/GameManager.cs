using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ゲーム全体統括：残機（人形）管理、死亡→リスポーン、クリア/ゲームオーバー
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("残機（テーブル上の人形の数と連動）")]
        [SerializeField] private int _lives = 3;

        [Header("プレイヤー")]
        [SerializeField] private Transform _player;
        [SerializeField] private Transform _respawnPoint;

        public int Lives => _lives;
        public bool IsGameEnded { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (_player == null)
            {
                var pc = FindFirstObjectByType<CharacterController>();
                if (pc != null) _player = pc.transform;
            }
            GameEvents.OnPlayerCaught += HandleCaught;
            GameEvents.RaiseLivesChanged(_lives);
        }

        private void OnDestroy()
        {
            GameEvents.OnPlayerCaught -= HandleCaught;
            if (Instance == this) Instance = null;
        }

        private void HandleCaught()
        {
            if (IsGameEnded) return;

            _lives--;
            GameEvents.RaiseJumpScare(1f);           // 捕獲時は最大強度のホラー演出
            GameEvents.RaiseLivesChanged(_lives);    // DollRack が人形を1体破壊する

            if (_lives <= 0)
            {
                IsGameEnded = true;
                GameEvents.RaiseGameOver();
                StartCoroutine(UnlockCursorDelayed());
                return;
            }

            // リスポーン & サイクル仕切り直し
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(1.2f); // ジャンプスケアを見せる時間

            if (_player != null && _respawnPoint != null)
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.SetPositionAndRotation(_respawnPoint.position, _respawnPoint.rotation);
                if (cc != null) cc.enabled = true;
            }
            // 隠れ中に捕まった場合の解除
            var status = _player != null ? _player.GetComponent<PlayerStatus>() : null;
            if (status != null) status.ForceExitHiding();

            if (PhaseManager.Instance != null)
                PhaseManager.Instance.RestartCycleFromExploration();
        }

        /// <summary>脱出成功（ExitDoor から呼ばれる）</summary>
        public void NotifyEscaped()
        {
            if (IsGameEnded) return;
            IsGameEnded = true;
            GameEvents.RaiseGameClear();
            StartCoroutine(UnlockCursorDelayed());
        }

        private IEnumerator UnlockCursorDelayed()
        {
            yield return new WaitForSeconds(0.5f);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>リスタート（HUDのボタン等から）</summary>
        public void RestartGame()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
