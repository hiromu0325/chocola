using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ゲーム全体統括：陶器人形（残機5体）、死亡→ホワイトアウト→人形破壊→12時から再開
    /// 人形が全て壊れた状態で死ぬとゲームオーバー。「そして誰もいなくなった」
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
        public bool IsGameEnded { get; private set; }
        public bool IsRespawning { get; private set; }

        private void Awake() { Instance = this; }

        private void Start()
        {
            if (_player == null)
            {
                var pc = FindFirstObjectByType<CharacterController>();
                if (pc != null) _player = pc.transform;
            }
            GameEvents.OnPlayerCaught += HandleDeath;
            GameEvents.OnSpecialDeath += HandleSpecialDeath;
            GameEvents.RaiseDollsChanged(_dolls);
        }

        private void OnDestroy()
        {
            GameEvents.OnPlayerCaught -= HandleDeath;
            GameEvents.OnSpecialDeath -= HandleSpecialDeath;
            if (Instance == this) Instance = null;
        }

        private void HandleSpecialDeath(string id) => HandleDeath();

        private void HandleDeath()
        {
            if (IsGameEnded || IsRespawning) return;

            // 人形が残っていなければゲームオーバー
            if (_dolls <= 0)
            {
                IsGameEnded = true;
                GameEvents.RaiseWhiteout(1f);
                GameEvents.RaiseGameOver();
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
        }

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

        public void RestartGame()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
