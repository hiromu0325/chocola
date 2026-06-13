using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 敵AI（NavMesh不要のシンプル操舵。CharacterControllerで壁ずり）
    ///
    /// [EnemyStateMachine]
    /// ├── Entering   入口→部屋内へ → Patrol
    /// ├── Patrol     ウェイポイント巡回。知覚チェック
    /// ├── Investigate（聴覚型）音の発生地点へ移動 → 到達/時間切れで Patrol
    /// ├── Chase      プレイヤー追跡。捕獲距離で捕殺 / 見失うと Patrol
    /// └── Leaving    出口へ → 退場（Destroy）
    ///
    /// 知覚はタイプごとに切替：
    ///   Sight  : FOV70° 距離12m + 視線Raycast。隠れ中は不可視
    ///   Sound  : 視覚なし。PlayerStatus.CurrentNoiseRadius と単発ノイズに反応
    ///   Motion : FOV160° 距離14m + 視線Raycast。ただし IsMoving のときのみ検知
    /// </summary>
    public class EnemyController : MonoBehaviour
    {
        private enum State { Entering, Patrol, Investigate, Chase, Leaving }

        [Header("タイプ")]
        public EnemyType Type;

        [Header("移動")]
        [SerializeField] private float _patrolSpeed = 1.6f;
        [SerializeField] private float _chaseSpeed = 3.4f;
        [SerializeField] private float _turnSpeed = 360f;

        [Header("捕獲")]
        [SerializeField] private float _catchRadius = 1.1f;

        [Header("知覚（タイプにより使用項目が変わる）")]
        [SerializeField] private float _sightRange = 12f;
        [SerializeField] private float _sightFovDeg = 70f;
        [SerializeField] private float _motionFovDeg = 160f;
        [SerializeField] private float _loseTargetSeconds = 2.5f;
        [SerializeField] private LayerMask _occlusionMask = ~0;

        // 外部から注入（Spawnerが設定）
        [HideInInspector] public Transform[] PatrolPoints;
        [HideInInspector] public Vector3 ExitPoint;

        private State _state = State.Entering;
        private PlayerStatus _player;
        private CharacterController _cc;
        private AudioSource _footAudio;
        private Light _eyeLight;

        private int _patrolIndex;
        private Vector3 _investigatePos;
        private float _investigateTimer;
        private float _lostTimer;
        private float _footstepTimer;
        private float _eyeHeight = 1.5f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                _cc.height = 2f;
                _cc.radius = 0.45f;
                _cc.center = new Vector3(0f, 1f, 0f);
            }
            _footAudio = gameObject.AddComponent<AudioSource>();
            _footAudio.spatialBlend = 1f;
            _footAudio.maxDistance = 30f;
            _footAudio.rolloffMode = AudioRolloffMode.Linear;
            _eyeLight = GetComponentInChildren<Light>();
        }

        private void OnEnable()
        {
            GameEvents.OnNoiseEmitted += HandleNoise;
            GameEvents.OnVisitEnd += BeginLeave;
        }

        private void OnDisable()
        {
            GameEvents.OnNoiseEmitted -= HandleNoise;
            GameEvents.OnVisitEnd -= BeginLeave;
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerStatus>();
        }

        private void Update()
        {
            if (_player == null) return;

            switch (_state)
            {
                case State.Entering: TickEntering(); break;
                case State.Patrol: TickPatrol(); break;
                case State.Investigate: TickInvestigate(); break;
                case State.Chase: TickChase(); break;
                case State.Leaving: TickLeaving(); break;
            }
        }

        // ============================== States ==============================

        private void TickEntering()
        {
            Vector3 target = PatrolPoints != null && PatrolPoints.Length > 0
                ? PatrolPoints[0].position : transform.position;
            MoveTowards(target, _patrolSpeed);
            if (HorizontalDistance(target) < 0.6f) _state = State.Patrol;
            CheckPerception();
        }

        private void TickPatrol()
        {
            if (PatrolPoints != null && PatrolPoints.Length > 0)
            {
                Vector3 target = PatrolPoints[_patrolIndex % PatrolPoints.Length].position;
                MoveTowards(target, _patrolSpeed);
                if (HorizontalDistance(target) < 0.6f) _patrolIndex++;
            }
            CheckPerception();
        }

        private void TickInvestigate()
        {
            MoveTowards(_investigatePos, _chaseSpeed * 0.85f);
            _investigateTimer -= Time.deltaTime;
            if (HorizontalDistance(_investigatePos) < 0.7f || _investigateTimer <= 0f)
                _state = State.Patrol;

            // 調査中も知覚は生きている（接近すれば Chase へ）
            CheckPerception();
            TryCatch();
        }

        private void TickChase()
        {
            MoveTowards(_player.transform.position, _chaseSpeed);
            TryCatch();

            if (CanPerceivePlayer())
            {
                _lostTimer = 0f;
            }
            else
            {
                _lostTimer += Time.deltaTime;
                if (_lostTimer > _loseTargetSeconds)
                {
                    // 聴覚型は最後に聞いた位置を調べてから戻る
                    if (Type == EnemyType.Sound)
                    {
                        _investigatePos = _player.transform.position;
                        _investigateTimer = 4f;
                        _state = State.Investigate;
                    }
                    else
                    {
                        _state = State.Patrol;
                    }
                }
            }
        }

        private void TickLeaving()
        {
            MoveTowards(ExitPoint, _patrolSpeed * 1.4f);
            if (HorizontalDistance(ExitPoint) < 0.8f)
                Destroy(gameObject);
        }

        /// <summary>来訪フェーズ終了 → 退場（知覚停止）</summary>
        public void BeginLeave()
        {
            if (_state == State.Leaving) return;
            _state = State.Leaving;
        }

        // ============================== Perception ==============================

        private void CheckPerception()
        {
            if (CanPerceivePlayer())
            {
                _lostTimer = 0f;
                _state = State.Chase;
            }
        }

        private bool CanPerceivePlayer()
        {
            if (_player == null || GameManager.Instance != null && GameManager.Instance.IsGameEnded)
                return false;

            switch (Type)
            {
                case EnemyType.Sight:
                    // 隠れていたら不可視
                    if (_player.IsHidden) return false;
                    return InFovWithLos(_sightFovDeg, _sightRange);

                case EnemyType.Sound:
                    // 継続ノイズ（足音）に反応
                    float dist = HorizontalDistance(_player.transform.position);
                    return _player.CurrentNoiseRadius > 0f && dist <= _player.CurrentNoiseRadius;

                case EnemyType.Motion:
                    // 隠れていれば安全。視界内でも静止していれば検知されない
                    if (_player.IsHidden) return false;
                    if (!_player.IsMoving) return false;
                    return InFovWithLos(_motionFovDeg, _sightRange + 2f);
            }
            return false;
        }

        private bool InFovWithLos(float fovDeg, float range)
        {
            Vector3 eye = transform.position + Vector3.up * _eyeHeight;
            Vector3 targetPos = _player.transform.position + Vector3.up * 1.4f;
            Vector3 to = targetPos - eye;
            if (to.magnitude > range) return false;

            Vector3 flat = to; flat.y = 0f;
            if (Vector3.Angle(transform.forward, flat) > fovDeg * 0.5f) return false;

            // 遮蔽チェック（プレイヤー自身に当たればOK）
            if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, range, _occlusionMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.transform == _player.transform ||
                       hit.transform.IsChildOf(_player.transform);
            }
            return true;
        }

        /// <summary>単発ノイズ（ロッカー開閉等）への反応：聴覚型のみ</summary>
        private void HandleNoise(Vector3 pos, float radius)
        {
            if (Type != EnemyType.Sound) return;
            if (_state == State.Leaving || _state == State.Chase) return;
            if (HorizontalDistance(pos) > radius) return;

            _investigatePos = pos;
            _investigateTimer = 6f;
            _state = State.Investigate;
        }

        // ============================== Movement / Catch ==============================

        private void MoveTowards(Vector3 target, float speed)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look,
                    _turnSpeed * Time.deltaTime);

                Vector3 motion = to.normalized * speed * Time.deltaTime;
                motion.y = -2f * Time.deltaTime; // 接地
                _cc.Move(motion);

                // 足音（プレイヤーに敵接近を知らせるゲームプレイ要素）
                _footstepTimer -= Time.deltaTime * (speed / _patrolSpeed);
                if (_footstepTimer <= 0f)
                {
                    _footAudio.PlayOneShot(ProceduralAudio.Footstep(),
                        _state == State.Chase ? 1f : 0.7f);
                    _footstepTimer = 0.55f;
                }
            }

            if (_eyeLight != null)
                _eyeLight.color = _state == State.Chase ? Color.red : GetTypeColor(Type);
        }

        private void TryCatch()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameEnded) return;
            if (HorizontalDistance(_player.transform.position) < _catchRadius)
            {
                GameEvents.RaisePlayerCaught();
                Destroy(gameObject); // 捕獲後は即退場（GameManagerがサイクル再開）
            }
        }

        private float HorizontalDistance(Vector3 pos)
        {
            Vector3 a = transform.position; a.y = 0f;
            pos.y = 0f;
            return Vector3.Distance(a, pos);
        }

        public static Color GetTypeColor(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Sight: return new Color(1f, 0.85f, 0.2f);  // 黄
                case EnemyType.Sound: return new Color(0.3f, 0.7f, 1f);   // 青
                case EnemyType.Motion: return new Color(0.5f, 1f, 0.4f);  // 緑
            }
            return Color.white;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            float fov = Type == EnemyType.Motion ? _motionFovDeg : _sightFovDeg;
            Vector3 l = Quaternion.Euler(0f, -fov * 0.5f, 0f) * transform.forward;
            Vector3 r = Quaternion.Euler(0f, fov * 0.5f, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position + Vector3.up * _eyeHeight, l * _sightRange);
            Gizmos.DrawRay(transform.position + Vector3.up * _eyeHeight, r * _sightRange);
        }
    }
}
