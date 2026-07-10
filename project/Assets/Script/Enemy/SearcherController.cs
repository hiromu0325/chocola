using UnityEngine;
using UnityEngine.AI;

namespace EscapeProto
{
    /// <summary>
    /// 探索者AI（NavMeshAgentによる移動。NavMeshが未構築の場合はCharacterController+手動操舵にフォールバック）
    ///
    /// [状態機械] Entering → Patrol ⇄ Investigate ⇄ Chase → Leaving
    ///
    /// 知覚（種類で切替）：
    ///   視覚型 : 部屋が明るい or 懐中電灯ON のときのみ FOV+LOS で発見。隠れ中は不可視
    ///   聴覚型 : 足音/物音に反応。自身は鈴を鳴らして歩く
    ///   嗅覚型 : 距離内なら視線不問で発見。ただし香水で消臭中は不可。隠れると半径半減
    ///
    /// 特殊死：来訪直後40秒の無音中に懐中電灯を点けると目の前に出現して即死
    /// </summary>
    public class SearcherController : MonoBehaviour
    {
        private enum State { Entering, Patrol, Investigate, Chase, Leaving }

        [Header("種類")]
        public SearcherType Type;

        [Header("移動")]
        [SerializeField] private float _patrolSpeed = 1.5f;
        [SerializeField] private float _chaseSpeed = 3.3f;
        [SerializeField] private float _turnSpeed = 360f;

        [Header("捕獲")]
        [SerializeField] private float _catchRadius = 1.1f;
        [Tooltip("この垂直差(m)を超えたら別フロア扱いにして捕まえない（2階の真下等）")]
        [SerializeField] private float _catchMaxVerticalDiff = 1.5f;

        [Header("知覚")]
        [SerializeField] private float _sightRange = 12f;
        [SerializeField] private float _sightFovDeg = 75f;
        [SerializeField] private float _smellRange = 7f;
        [SerializeField] private float _loseTargetSeconds = 2.5f;
        [SerializeField] private LayerMask _occlusionMask = ~0;

        [Header("入室後の無音・懐中電灯死の猶予（秒）")]
        [SerializeField] private float _silenceWindow = 40f;

        [HideInInspector] public Transform[] PatrolPoints;
        [HideInInspector] public Vector3 ExitPoint;

        private State _state = State.Entering;
        private PlayerStatus _player;
        private CharacterController _cc;
        private NavMeshAgent _agent;
        private bool _useNavMesh;
        private AudioSource _audio;
        private Light _eyeLight;

        private int _patrolIndex;
        private Vector3 _investigatePos;
        private float _investigateTimer;
        private float _lostTimer;
        private float _stepTimer;
        private float _spawnTime;
        private float _eyeHeight = 1.5f;
        private bool _specialDeathFired;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.radius = 0.3f;
            _agent.height = 1.8f;
            _agent.angularSpeed = _turnSpeed;
            _agent.acceleration = 16f;
            _agent.speed = _patrolSpeed;
            _agent.autoBraking = false;
            // NavMesh未構築の可能性があるため、ここでは無効化しておき、
            // Start()でNavMesh上への配置に成功した場合のみ有効にする。
            _agent.enabled = false;

            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                _cc.height = 2f; _cc.radius = 0.45f;
                _cc.center = new Vector3(0f, 1f, 0f);
            }
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.maxDistance = 30f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _eyeLight = GetComponentInChildren<Light>();
            _spawnTime = Time.time;
        }

        private void OnEnable()
        {
            GameEvents.OnNoiseEmitted += HandleNoise;
            GameEvents.OnVisitEnd += BeginLeave;
            GameEvents.OnPerfumeUsed += HandlePerfumeUsed;
        }
        private void OnDisable()
        {
            GameEvents.OnNoiseEmitted -= HandleNoise;
            GameEvents.OnVisitEnd -= BeginLeave;
            GameEvents.OnPerfumeUsed -= HandlePerfumeUsed;
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerStatus>();

            // ScriptableObject の設定値で移動速度を上書き
            var cfg = GameBalanceConfig.Instance;
            if (cfg != null)
            {
                _patrolSpeed = cfg.searcherPatrolSpeed;
                _chaseSpeed = cfg.searcherChaseSpeed;
            }

            TrySnapToNavMesh();
        }

        /// <summary>NavMeshへのスナップを試み、成功すればAgent移動、失敗すれば旧CharacterController移動にフォールバック</summary>
        private void TrySnapToNavMesh()
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _agent.enabled = true;
                if (_agent.Warp(hit.position))
                {
                    _useNavMesh = true;
                    if (_cc != null) _cc.enabled = false;
                    return;
                }
                _agent.enabled = false;
            }
            _useNavMesh = false;
            if (_agent != null) _agent.enabled = false;
            if (_cc != null) _cc.enabled = true;
        }

        private void Update()
        {
            if (_player == null) return;
            CheckFlashlightDeath();

            // NavMeshが未構築だった場合（ベイクがまだ間に合っていない等）、
            // 毎フレーム再試行してNavMesh移動へ復帰する。
            if (!_useNavMesh) TrySnapToNavMesh();

            switch (_state)
            {
                case State.Entering: TickEntering(); break;
                case State.Patrol: TickPatrol(); break;
                case State.Investigate: TickInvestigate(); break;
                case State.Chase: TickChase(); break;
                case State.Leaving: TickLeaving(); break;
            }
        }

        // ============= 懐中電灯による特殊死 =============
        private void CheckFlashlightDeath()
        {
            if (_specialDeathFired) return;
            if (_state == State.Leaving) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameEnded) return;

            bool inSilence = Time.time - _spawnTime < _silenceWindow;
            if (inSilence && _player.FlashlightOn)
            {
                _specialDeathFired = true;
                // プレイヤーの目の前に瞬間移動して顔を見せる
                Vector3 front = _player.transform.position + _player.transform.forward * 1.0f;
                front.y = _player.transform.position.y;
                if (_useNavMesh && _agent != null && _agent.enabled)
                {
                    _agent.ResetPath();
                    _agent.Warp(front);
                }
                else
                {
                    if (_cc != null) _cc.enabled = false;
                    transform.position = front;
                }
                transform.rotation = Quaternion.LookRotation(
                    (_player.transform.position - front).normalized);
                GameEvents.RaiseSpecialDeath("flashlight");
            }
        }

        // ============= States =============
        private void TickEntering()
        {
            Vector3 t = PatrolPoints != null && PatrolPoints.Length > 0
                ? PatrolPoints[0].position : transform.position;
            MoveTowards(t, _patrolSpeed);
            if (HasReached(t, 0.6f)) _state = State.Patrol;
            CheckPerception();
        }

        private void TickPatrol()
        {
            if (PatrolPoints != null && PatrolPoints.Length > 0)
            {
                Vector3 t = PatrolPoints[_patrolIndex % PatrolPoints.Length].position;
                MoveTowards(t, _patrolSpeed);
                if (HasReached(t, 0.6f)) _patrolIndex++;
            }
            CheckPerception();
        }

        private void TickInvestigate()
        {
            MoveTowards(_investigatePos, _chaseSpeed * 0.85f);
            _investigateTimer -= Time.deltaTime;
            if (HasReached(_investigatePos, 0.7f) || _investigateTimer <= 0f) _state = State.Patrol;
            CheckPerception();
            TryCatch();
        }

        private void TickChase()
        {
            MoveTowards(_player.transform.position, _chaseSpeed);
            TryCatch();
            if (CanPerceive()) _lostTimer = 0f;
            else
            {
                _lostTimer += Time.deltaTime;
                if (_lostTimer > _loseTargetSeconds)
                {
                    if (Type == SearcherType.Sound || Type == SearcherType.Smell)
                    {
                        _investigatePos = _player.transform.position;
                        _investigateTimer = 4f;
                        _state = State.Investigate;
                    }
                    else _state = State.Patrol;
                }
            }
        }

        private void TickLeaving()
        {
            MoveTowards(ExitPoint, _patrolSpeed * 1.4f);
            if (HasReached(ExitPoint, 0.8f)) Destroy(gameObject);
        }

        public void BeginLeave()
        {
            if (_state != State.Leaving) _state = State.Leaving;
        }

        /// <summary>香水使用：Chase中の探索者は即座に見失う（種類問わず）。
        /// 音/嗅覚型は使用地点付近を調べに行く動きにする。</summary>
        private void HandlePerfumeUsed(float duration)
        {
            if (_state != State.Chase) return;

            _lostTimer = 0f;
            if (Type == SearcherType.Sound || Type == SearcherType.Smell)
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

        // ============= Perception =============
        private void CheckPerception()
        {
            if (CanPerceive()) { _lostTimer = 0f; _state = State.Chase; }
        }

        private bool CanPerceive()
        {
            if (_player == null) return false;
            if (GameManager.Instance != null && GameManager.Instance.IsGameEnded) return false;

            switch (Type)
            {
                case SearcherType.Sight:
                {
                    if (_player.IsHidden) return false;
                    // 暗闇では見えない（電気OFFかつ懐中電灯OFF）
                    bool lit = (RoomLightController.Instance == null || RoomLightController.Instance.LightsOn)
                               || _player.FlashlightOn;
                    if (!lit) return false;
                    return InFovWithLos(_sightFovDeg, _sightRange);
                }
                case SearcherType.Sound:
                {
                    float d = HDist(_player.transform.position);
                    return _player.CurrentNoiseRadius > 0f && d <= _player.CurrentNoiseRadius;
                }
                case SearcherType.Smell:
                {
                    if (_player.IsScentMasked) return false;          // 香水で消臭
                    float range = _player.IsHidden ? _smellRange * 0.5f : _smellRange;
                    return HDist(_player.transform.position) <= range; // 視線不問
                }
            }
            return false;
        }

        private bool InFovWithLos(float fovDeg, float range)
        {
            Vector3 eye = transform.position + Vector3.up * _eyeHeight;
            Vector3 tgt = _player.transform.position + Vector3.up * 1.4f;
            Vector3 to = tgt - eye;
            if (to.magnitude > range) return false;
            Vector3 flat = to; flat.y = 0f;
            if (Vector3.Angle(transform.forward, flat) > fovDeg * 0.5f) return false;
            if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, range, _occlusionMask,
                    QueryTriggerInteraction.Ignore))
                return hit.transform == _player.transform || hit.transform.IsChildOf(_player.transform);
            return true;
        }

        private void HandleNoise(Vector3 pos, float radius)
        {
            if (Type != SearcherType.Sound) return;
            if (_state == State.Leaving || _state == State.Chase) return;
            if (HDist(pos) > radius) return;
            _investigatePos = pos; _investigateTimer = 6f;
            _state = State.Investigate;
        }

        // ============= Movement / Catch =============
        private void MoveTowards(Vector3 target, float speed)
        {
            bool moving;

            if (_useNavMesh && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.speed = speed;
                _agent.SetDestination(target);
                moving = _agent.velocity.sqrMagnitude > 0.01f;

                // 回頭はAgentのRotationに任せず、見た目を進行方向へ向ける（updateRotation運用と同等の挙動）
                if (moving)
                {
                    Vector3 flatVel = _agent.velocity; flatVel.y = 0f;
                    if (flatVel.sqrMagnitude > 0.0001f)
                    {
                        Quaternion look = Quaternion.LookRotation(flatVel.normalized);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Time.deltaTime);
                    }
                }
            }
            else
            {
                // フォールバック：NavMesh未構築時の従来移動（CharacterController）
                Vector3 to = target - transform.position; to.y = 0f;
                moving = to.sqrMagnitude > 0.0001f;
                if (moving)
                {
                    Quaternion look = Quaternion.LookRotation(to.normalized);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Time.deltaTime);
                    Vector3 m = to.normalized * speed * Time.deltaTime; m.y = -2f * Time.deltaTime;
                    if (_cc != null && _cc.enabled) _cc.Move(m);
                }
            }

            if (moving)
            {
                _stepTimer -= Time.deltaTime * (speed / _patrolSpeed);
                if (_stepTimer <= 0f)
                {
                    // 聴覚型は鈴、その他は足音
                    if (Type == SearcherType.Sound)
                        _audio.PlayOneShot(ProceduralAudio.Bell(), _state == State.Chase ? 1f : 0.7f);
                    else
                        _audio.PlayOneShot(ProceduralAudio.Footstep(), _state == State.Chase ? 1f : 0.7f);
                    _stepTimer = 0.55f;
                }
            }
            if (_eyeLight != null)
                _eyeLight.color = _state == State.Chase ? Color.red : GetTypeColor(Type);
        }

        /// <summary>目的地に到達したか（NavMesh経路使用時はremainingDistance、フォールバック時は水平距離）</summary>
        private bool HasReached(Vector3 target, float threshold)
        {
            if (_useNavMesh && _agent != null && _agent.enabled && _agent.isOnNavMesh)
                return !_agent.pathPending && _agent.remainingDistance < threshold;
            return HDist(target) < threshold;
        }

        private void TryCatch()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameEnded) return;
            // 別フロア（2階の真下等）にいる場合は水平距離が近くても捕まえない
            if (Mathf.Abs(transform.position.y - _player.transform.position.y) > _catchMaxVerticalDiff) return;
            if (HDist(_player.transform.position) < _catchRadius)
            {
                GameEvents.RaisePlayerCaught();
                Destroy(gameObject);
            }
        }

        private float HDist(Vector3 pos)
        {
            Vector3 a = transform.position; a.y = 0f; pos.y = 0f;
            return Vector3.Distance(a, pos);
        }

        public static Color GetTypeColor(SearcherType t)
        {
            switch (t)
            {
                case SearcherType.Sight: return new Color(1f, 0.85f, 0.2f);
                case SearcherType.Sound: return new Color(0.6f, 0.4f, 0.9f);
                case SearcherType.Smell: return new Color(0.4f, 1f, 0.5f);
            }
            return Color.white;
        }
    }
}
