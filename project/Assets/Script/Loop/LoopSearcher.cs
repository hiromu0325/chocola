using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ループ回廊の襲撃者。ブレイカー降下中だけ存在する。
    ///
    /// [空間管理]
    /// 襲撃者は「回廊」か「特定の部屋」のどちらかの空間に居る（_space。null=回廊）。
    /// プレイヤーと違う空間に居るあいだは描画も物理も止める（フリーズ）。
    /// これをしないと、プレイヤーが入室して回廊が非表示になった瞬間に
    /// 回廊の床コライダーが消え、襲撃者が落下して消滅してしまう。
    ///
    /// [行動]
    /// ・回廊のリング中心線を周回して徘徊
    /// ・プレイヤーが回廊に居れば視認で追跡、捕まえると既存の死亡フローへ
    /// ・プレイヤーが部屋に居れば、少し間を置いてその部屋へ「入ってくる」
    ///   （進行度で入れない部屋・最初の部屋には入らない）
    /// ・ブレイカー復旧で最寄りの扉へ向かい、到達すると消える
    /// </summary>
    public class LoopSearcher : MonoBehaviour
    {
        private enum State { Roam, InspectDoor, ChaseCorridor, InRoom, Retreat, Entering, Leaving }

        private const float RoamSpeed = 1.7f;
        private const float ChaseSpeed = 3.2f;
        private const float CatchRadius = 1.1f;
        private const float SightRange = 11f;
        /// <summary>プレイヤーが部屋に入ってから襲撃者が追って入室するまでの猶予</summary>
        private const float EnterRoomDelay = 6f;
        /// <summary>入場演出：扉が開いてから踏み出しきるまでの秒数（この間は追ってこない）</summary>
        private const float EnterMotionSeconds = 2.6f;
        /// <summary>入場演出で扉から室内へ踏み込む距離</summary>
        private const float EnterStrideDistance = 1.6f;
        /// <summary>退場演出：扉をくぐって消えるまでの秒数</summary>
        private const float LeaveMotionSeconds = 1.4f;

        private CharacterController _cc;
        private Renderer[] _renderers;
        private Light[] _lights;
        private State _state = State.Roam;
        private int _dir = 1;
        private float _decideTimer;
        private float _inspectTimer;
        private LoopRoomRoot _inspectRoom;
        private float _enterRoomTimer;
        private float _orphanTimer;

        /// <summary>襲撃者が居る空間（null=回廊、それ以外=部屋Id）</summary>
        private string _space;
        private bool _frozen;
        private float _retreatTimer;     // 退場が長引いたら強制消滅させる保険
        private State _prevLoggedState;  // 調査ログ用（状態遷移の記録）
        // 入退場演出（扉から踏み出す／扉へ引っ込む）
        private float _motionTimer;
        private float _stepTimer;
        private Vector3 _enterFrom, _enterTo, _enterDir;
        private bool _leaveThenVanish;   // 退場演出のあと消滅する（ブレイカー復旧時）

        private static float C => (LoopCorridorLayout.InnerHalf + LoopCorridorLayout.OuterHalf) * 0.5f;

        public static LoopSearcher Spawn(Vector3 pos)
        {
            var root = new GameObject("LoopSearcher");
            root.transform.position = pos;
            EnemySpawner.BuildVisualInto(root, (SearcherType)Random.Range(0, 3));
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0f, 0.95f, 0f);
            var s = root.AddComponent<LoopSearcher>();
            s._cc = cc;
            s._dir = Random.value < 0.5f ? 1 : -1;
            s._decideTimer = Random.Range(5f, 10f);
            AttackDebugLog.Log("searcher", $"スポーン pos={pos.ToString("F1")}");
            return s;
        }

        /// <summary>退場（消滅歩行）中か（ウォッチドッグはこれを残骸と数えない）</summary>
        public bool IsRetreating => _state == State.Retreat;

        /// <summary>調査ログ用の1行要約</summary>
        public string DebugBrief() =>
            $"{_state}{(_frozen ? "/凍結" : "")} space={_space ?? "廊下"} pos={transform.position.ToString("F0")}";

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _lights = GetComponentsInChildren<Light>(true);
        }

        /// <summary>ブレイカー復旧：最寄りの扉へ向かい退場する</summary>
        public void Retreat()
        {
            if (_space != null)
            {
                // 同じ部屋にプレイヤーが居るなら、消える前に扉へ引き返す姿を見せる
                if (_space == LoopRooms.CurrentRoomId && _state != State.Leaving)
                {
                    BeginLeaving();
                    _leaveThenVanish = true;
                    return;
                }
                AttackDebugLog.Log("searcher", "退場指示: 部屋の中のため即消滅 " + DebugBrief());
                Destroy(gameObject);
                return;
            }
            AttackDebugLog.Log("searcher", "退場指示: 扉へ歩き出す " + DebugBrief());
            _state = State.Retreat;
            _retreatTimer = 0f;
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            // ---- 空間の同期（プレイヤーと違う空間なら凍結して描画も止める）----
            bool sameSpace = _space == LoopRooms.CurrentRoomId;
            if (sameSpace == _frozen) SetFrozen(!sameSpace);
            if (!sameSpace)
            {
                // 退場中に空間が分かれたら、もう姿は見えていないのでそのまま消える。
                // （凍結したまま入室待ちに入ると「退場中なのに部屋へ入り直して
                //   壁に張り付いたまま残る」＝襲撃が終わらない不具合になる）
                if (_state == State.Retreat)
                {
                    AttackDebugLog.Log("searcher", "退場中に空間分離 → 即消滅 " + DebugBrief());
                    Destroy(gameObject);
                    return;
                }
                // 演出中に見られなくなったら、演出を見せる意味がないので即座に決着させる
                if (_state == State.Leaving) { FinishLeaving(); return; }
                if (_state == State.Entering) { _state = State.InRoom; }
                // 部屋に入っていた襲撃者は、プレイヤーが出たら回廊へ戻る
                if (_space != null) FinishLeaving();
                else TickWaitToEnterRoom();   // 回廊で待機しつつ、入室の機を伺う
                return;
            }

            // 調査ログ：状態が変わったら記録する
            if (_state != _prevLoggedState)
            {
                AttackDebugLog.Log("searcher", $"状態 {_prevLoggedState}→{_state} " + DebugBrief());
                _prevLoggedState = _state;
            }

            // 安全網：床から落ちたら退場扱い（角のすり抜け等の保険）
            if (transform.position.y < -3f)
            {
                AttackDebugLog.Log("searcher", "床から落下 → 消滅 " + DebugBrief());
                Destroy(gameObject);
                return;
            }

            // 安全網：警報も徘徊フェーズも無いのに残っていたら退場する
            if (_state != State.Retreat &&
                (BreakerSystem.Instance == null ||
                 (BreakerSystem.Instance.DownRoomId == null && !BreakerSystem.Instance.HuntActive)))
            {
                _orphanTimer += Time.deltaTime;
                if (_orphanTimer > 2f)
                {
                    AttackDebugLog.Log("searcher", "孤児セーフティネット発動（警報も徘徊も無い）→ 退場");
                    Retreat();
                    return;
                }
            }
            else _orphanTimer = 0f;

            var player = GameObject.FindGameObjectWithTag("Player");
            switch (_state)
            {
                case State.Roam: TickRoam(player); break;
                case State.InspectDoor: TickInspect(player); break;
                case State.ChaseCorridor: TickChase(player); break;
                case State.InRoom: TickInRoom(player); break;
                case State.Entering: TickEntering(); break;
                case State.Leaving: TickLeaving(); break;
                case State.Retreat: TickRetreat(); break;
            }
        }

        // ============= 空間の切替 =============

        private void SetFrozen(bool frozen)
        {
            _frozen = frozen;
            foreach (var r in _renderers) if (r != null) r.enabled = !frozen;
            foreach (var l in _lights) if (l != null) l.enabled = !frozen;
            if (_cc != null) _cc.enabled = !frozen;   // 凍結中は重力も当たりも止める
        }

        /// <summary>回廊に居るとき：プレイヤーが入った部屋へ追って入室する</summary>
        private void TickWaitToEnterRoom()
        {
            string playerRoom = LoopRooms.CurrentRoomId;
            if (string.IsNullOrEmpty(playerRoom)) { _enterRoomTimer = 0f; return; }

            // 最初の部屋（チュートリアル）には入らない＝スポーンしても素通りする仕様
            if (playerRoom == LoopProgress.StartRoomId || !LoopRooms.IsUnlocked(playerRoom))
            { _enterRoomTimer = 0f; return; }

            _enterRoomTimer += Time.deltaTime;
            if (_enterRoomTimer < EnterRoomDelay) return;

            var room = LoopRooms.Get(playerRoom);
            if (room == null) return;
            Vector3 spawn = room.EntrySpawn != null ? room.EntrySpawn.position : room.transform.position;
            AttackDebugLog.Log("searcher", $"プレイヤーの部屋（{playerRoom}）へ入室開始 " + DebugBrief());
            _space = playerRoom;
            _inspectRoom = room;
            _enterRoomTimer = 0f;
            SetFrozen(false);

            // ---- 入場演出 ----
            // いきなり部屋の中に現れて追ってくると避けようがないので、
            // 扉の位置から室内へゆっくり踏み出す時間を挟む（この間は追跡しない）。
            // 扉→部屋の奥へ向かうベクトルを進入方向とする
            Vector3 inward = room.transform.position - spawn;
            inward.y = 0f;
            _enterFrom = spawn;
            _enterDir = inward.sqrMagnitude > 0.01f ? inward.normalized : room.transform.forward;
            _enterTo = _enterFrom + _enterDir * EnterStrideDistance;
            _motionTimer = 0f;
            _state = State.Entering;

            Teleport(_enterFrom);
            transform.rotation = Quaternion.LookRotation(_enterDir);
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), spawn, 0.6f);   // 扉が開く音
        }

        /// <summary>
        /// 入場演出：扉の前で一拍おいてから、ゆっくり室内へ踏み出す。
        /// 演出中は追跡しないので、プレイヤーは逃げる/隠れる猶予を得られる。
        /// </summary>
        private void TickEntering()
        {
            _motionTimer += Time.deltaTime;
            float k = Mathf.Clamp01(_motionTimer / EnterMotionSeconds);

            // 前半は扉の影で立ち止まり、後半でゆっくり踏み出す
            float stride = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, k));
            Vector3 target = Vector3.Lerp(_enterFrom, _enterTo, stride);
            if (_cc != null && _cc.enabled)
            {
                Vector3 delta = target - transform.position;
                delta.y = 0f;
                _cc.Move(delta + Vector3.down * 4f * Time.deltaTime);
            }

            // 踏み出しに合わせて低く軋む足音（気配で位置を知らせる）
            _stepTimer -= Time.deltaTime;
            if (stride > 0f && _stepTimer <= 0f)
            {
                _stepTimer = 0.75f;
                ProceduralAudio.PlayAt(ProceduralAudio.Footstep(), transform.position, 0.55f);
            }

            if (_motionTimer >= EnterMotionSeconds)
            {
                AttackDebugLog.Log("searcher", "入場演出おわり → 追跡開始 " + DebugBrief());
                _state = State.InRoom;
            }
        }

        /// <summary>退場演出：扉へ向き直り、くぐって消える</summary>
        private void TickLeaving()
        {
            _motionTimer += Time.deltaTime;
            float k = Mathf.Clamp01(_motionTimer / LeaveMotionSeconds);
            Vector3 target = Vector3.Lerp(_enterFrom, _enterTo, Mathf.SmoothStep(0f, 1f, k));
            if (_cc != null && _cc.enabled)
            {
                Vector3 delta = target - transform.position;
                delta.y = 0f;
                _cc.Move(delta + Vector3.down * 4f * Time.deltaTime);
            }
            if (_motionTimer >= LeaveMotionSeconds) FinishLeaving();
        }

        /// <summary>退場演出の完了：回廊へ実際に戻す（プレイヤーが先に出た場合もここへ来る）</summary>
        private void FinishLeaving()
        {
            // ブレイカー復旧による退場なら、扉をくぐった時点で姿を消す
            if (_leaveThenVanish)
            {
                AttackDebugLog.Log("searcher", "退場演出おわり → 消滅 " + DebugBrief());
                Destroy(gameObject);
                return;
            }
            var room = LoopRooms.Get(_space);
            _space = null;
            _state = State.Roam;
            _decideTimer = Random.Range(4f, 8f);
            if (room != null)
            {
                SetFrozen(LoopRooms.CurrentRoomId != null);
                Teleport(LoopCorridorLayout.DoorFrontPosition(room.Side, room.Slot));
            }
            AttackDebugLog.Log("searcher", "退場演出おわり → 回廊へ " + DebugBrief());
        }

        /// <summary>部屋からプレイヤーが出た：回廊の扉前へ戻る</summary>
        /// <summary>
        /// 部屋から立ち去り始める（プレイヤーが見ている前で扉へ引っ込む演出）。
        /// 姿が見えない状況では演出を挟まずそのまま回廊へ戻す。
        /// </summary>
        private void BeginLeaving()
        {
            var room = LoopRooms.Get(_space);
            if (room == null || _space != LoopRooms.CurrentRoomId) { FinishLeaving(); return; }

            Vector3 door = room.EntrySpawn != null ? room.EntrySpawn.position : room.transform.position;
            Vector3 outward = door - room.transform.position;
            outward.y = 0f;
            _enterFrom = transform.position;
            _enterTo = door + (outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.zero) * 0.6f;
            _enterTo.y = _enterFrom.y;
            _motionTimer = 0f;
            _state = State.Leaving;
            Vector3 look = _enterTo - _enterFrom;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            AttackDebugLog.Log("searcher", "退場演出開始（扉へ引っ込む） " + DebugBrief());
        }

        // ============= 回廊での行動 =============

        private void TickRoam(GameObject player)
        {
            if (TrySeePlayer(player)) { _state = State.ChaseCorridor; return; }
            MoveTowards(NextCorner(transform.position, _dir), RoamSpeed);

            _decideTimer -= Time.deltaTime;
            if (_decideTimer <= 0f)
            {
                _decideTimer = Random.Range(6f, 12f);
                _inspectRoom = NearestAccessibleRoom(3.5f);
                if (_inspectRoom != null) { _inspectTimer = 2.5f; _state = State.InspectDoor; }
            }
        }

        /// <summary>扉の前で立ち止まって調べる（雰囲気づけ）</summary>
        private void TickInspect(GameObject player)
        {
            if (TrySeePlayer(player)) { _state = State.ChaseCorridor; return; }
            if (_inspectRoom == null) { _state = State.Roam; return; }

            Vector3 doorPos = LoopCorridorLayout.DoorFrontPosition(_inspectRoom.Side, _inspectRoom.Slot);
            if (MoveTowards(doorPos, RoamSpeed) < 0.4f)
            {
                _inspectTimer -= Time.deltaTime;
                Face(LoopCorridorLayout.DoorPosition(_inspectRoom.Side, _inspectRoom.Slot));
                if (_inspectTimer <= 0f) _state = State.Roam;
            }
        }

        private void TickChase(GameObject player)
        {
            if (player == null || !LoopRooms.InCorridor) { _state = State.Roam; return; }
            float d = MoveTowards(player.transform.position, ChaseSpeed);
            if (d < CatchRadius) { Catch(); return; }
            if (d > SightRange * 1.3f) _state = State.Roam;
        }

        // ============= 部屋での行動 =============

        private void TickInRoom(GameObject player)
        {
            if (player == null) return;
            float d = MoveTowards(player.transform.position, ChaseSpeed);
            if (d < CatchRadius) Catch();
        }

        private void Catch()
        {
            AttackDebugLog.Log("searcher", "プレイヤー捕獲 " + DebugBrief());
            GameEvents.RaisePlayerCaught();   // 既存の死亡→人形破壊→リスポーンフロー
            Destroy(gameObject);
        }

        // ============= 退場 =============

        private void TickRetreat()
        {
            // 保険：退場歩行が長引いたら（引っかかり等）その場で消える
            _retreatTimer += Time.deltaTime;
            if (_retreatTimer > 12f)
            {
                AttackDebugLog.Log("searcher", "退場が12秒を超過 → 強制消滅 " + DebugBrief());
                Destroy(gameObject);
                return;
            }
            if (MoveTowards(NearestDoorPosition(), ChaseSpeed) < 0.5f)
            {
                AttackDebugLog.Log("searcher", "退場完了（扉に到達して消滅）");
                Destroy(gameObject);
            }
        }

        // ============= 移動ヘルパー =============

        private static Vector3 NextCorner(Vector3 p, int dir)
        {
            float c = C;
            bool onNS = Mathf.Abs(p.z) > Mathf.Abs(p.x);
            if (onNS)
            {
                float z = p.z > 0 ? c : -c;
                float x = (p.z > 0 ? dir : -dir) > 0 ? c : -c;
                return new Vector3(x, 0, z);
            }
            float px = p.x > 0 ? c : -c;
            float pz = (p.x > 0 ? -dir : dir) > 0 ? c : -c;
            return new Vector3(px, 0, pz);
        }

        private float MoveTowards(Vector3 target, float speed)
        {
            if (_cc == null || !_cc.enabled) return float.MaxValue;
            Vector3 to = target - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.05f)
            {
                Vector3 dir = to / dist;
                Face(transform.position + dir);
                _cc.Move((dir * speed + Vector3.down * 9f) * Time.deltaTime);
            }
            return dist;
        }

        private void Face(Vector3 at)
        {
            Vector3 to = at - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(to.normalized), 480f * Time.deltaTime);
        }

        private void Teleport(Vector3 pos)
        {
            bool wasEnabled = _cc != null && _cc.enabled;
            if (_cc != null) _cc.enabled = false;
            transform.position = pos;
            if (_cc != null) _cc.enabled = wasEnabled;
        }

        // ============= 知覚・検索 =============

        private bool TrySeePlayer(GameObject player)
        {
            if (player == null || !LoopRooms.InCorridor) return false;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            Vector3 tgt = player.transform.position + Vector3.up * 1.4f;
            Vector3 to = tgt - eye;
            if (to.magnitude > SightRange) return false;
            if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude,
                    ~0, QueryTriggerInteraction.Ignore))
                return hit.transform == player.transform || hit.transform.IsChildOf(player.transform);
            return true;
        }

        private LoopRoomRoot NearestAccessibleRoom(float maxDist)
        {
            LoopRoomRoot best = null;
            float bestD = maxDist * maxDist;
            foreach (var r in LoopRooms.Accessible())
            {
                Vector3 p = LoopCorridorLayout.DoorFrontPosition(r.Side, r.Slot);
                float d = (p - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        private Vector3 NearestDoorPosition()
        {
            Vector3 p = transform.position;
            Vector3 best = LoopCorridorLayout.DoorFrontPosition(0, 0);
            float bestD = float.MaxValue;
            for (int side = 0; side < 4; side++)
                for (int slot = 0; slot < LoopCorridorLayout.DoorsPerSide; slot++)
                {
                    Vector3 dp = LoopCorridorLayout.DoorFrontPosition(side, slot);
                    float d = (dp - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = dp; }
                }
            return best;
        }
    }
}
