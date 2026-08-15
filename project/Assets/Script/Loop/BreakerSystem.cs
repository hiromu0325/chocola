using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ブレイカーサイクルの統括。
    /// ・チュートリアルでブレイカーを上げ、部屋を出た時点からカウント開始（初回のみ30秒）
    /// ・以降は上げてから15分で、侵入可能な部屋のどれか1つのブレイカーが自動降下し警報音を発する
    /// ・降下と同時に、プレイヤーから最も遠い侵入可能な部屋（警報の部屋を除く）に襲撃者がスポーン
    /// ・プレイヤーがブレイカーを上げると襲撃者は最寄りの扉から退場し、次のサイクルへ
    /// </summary>
    public class BreakerSystem : MonoBehaviour
    {
        public static BreakerSystem Instance { get; private set; }

        [Tooltip("通常サイクル（秒）。仕様では15分")]
        public float CycleSeconds = 900f;
        [Tooltip("チュートリアル直後の特殊サイクル（秒）")]
        public float TutorialSeconds = 30f;

        /// <summary>現在降下中の部屋Id（無ければnull）</summary>
        public string DownRoomId { get; private set; }
        public float TimeLeft => _timeLeft;

        private float _timeLeft = -1f;   // <0 = 停止中
        private bool _tutorialTimerStarted;
        private GameObject _corridorAlarm;   // 回廊側（対象部屋の扉前）の警報音源
        private Light _alarmLight;           // 警報扉の赤い点滅ライト
        private LoopSearcher _searcher;
        private float _sweepTimer;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            // 警報扉の赤ライトを点滅させる
            if (_alarmLight != null)
                _alarmLight.intensity = 0.6f + Mathf.PingPong(Time.time * 4f, 1.6f);

            // 保険：降下中の部屋が無いのに警報が残っていたら1秒ごとに掃除する
            if (DownRoomId == null)
            {
                _sweepTimer += Time.deltaTime;
                if (_sweepTimer >= 1f)
                {
                    _sweepTimer = 0f;
                    if (AnyAlarmPlaying()) StopAllAlarms();
                }
            }

            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            // チュートリアルのブレイカーを上げて初めて回廊に出た時からカウント開始（30秒）
            if (!_tutorialTimerStarted)
            {
                var tutorial = LoopRooms.Get(LoopProgress.StartRoomId);
                if (tutorial != null && tutorial.Breaker != null && tutorial.Breaker.IsUp && LoopRooms.InCorridor)
                {
                    _tutorialTimerStarted = true;
                    _timeLeft = TutorialSeconds;
                }
                return;
            }

            if (_timeLeft < 0f) return;

            // 部屋の中にいる間は襲撃が始まらない（探索中は安全。回廊に出ると再開）
            if (!LoopRooms.InCorridor) return;

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f && DownRoomId == null) Drop();
        }

        /// <summary>侵入可能な部屋から1つ選んでブレイカーを降下させ、襲撃者を放つ</summary>
        public void Drop()
        {
            if (DownRoomId != null) return;   // 二重降下（音源の取り残し）を防ぐ
            _timeLeft = -1f;
            var rooms = LoopRooms.Accessible();
            if (rooms.Count == 0) { _timeLeft = CycleSeconds; return; }

            // 対象: 侵入可能な部屋からランダム（ブレイカー持ちのみ。
            // 最初の部屋は再入場できないため降下対象から除外＝詰み防止）
            var candidates = rooms.FindAll(r => r.Breaker != null && r.Id != LoopProgress.StartRoomId);
            if (candidates.Count == 0) { _timeLeft = CycleSeconds; return; }
            var target = candidates[Random.Range(0, candidates.Count)];
            DownRoomId = target.Id;
            target.Breaker.SetUp(false);

            // 回廊側にも警報音源（部屋モデルは非表示のため、扉前に置いて音で導く）
            // 可聴距離を絞って「近づくほど大きい」方向の手がかりにする
            Vector3 doorPos = LoopCorridorLayout.DoorFrontPosition(target.Side, target.Slot);
            _corridorAlarm = new GameObject("BreakerAlarm_" + target.Id);
            _corridorAlarm.transform.position = doorPos + Vector3.up * 1.6f;
            var src = _corridorAlarm.AddComponent<AudioSource>();
            src.clip = ProceduralAudio.Alarm();
            src.loop = true;
            src.spatialBlend = 1f;
            src.maxDistance = 28f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();

            // 該当の扉が視覚でも分かるよう、赤い点滅ライトを添える
            _alarmLight = _corridorAlarm.AddComponent<Light>();
            _alarmLight.type = LightType.Point;
            _alarmLight.color = new Color(1f, 0.15f, 0.1f);
            _alarmLight.range = 5f;

            SpawnSearcher(target);
            Debug.Log($"[BreakerSystem] ブレイカー降下: {target.DisplayName}（{target.Id}）");
        }

        /// <summary>プレイヤーから最も遠い侵入可能な部屋の扉前にスポーン（警報の部屋は除外）</summary>
        private void SpawnSearcher(LoopRoomRoot exclude)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 refPos = player != null ? player.transform.position : Vector3.zero;
            // プレイヤーが部屋の中に居る場合は、その部屋の回廊扉を基準にする
            var curRoom = LoopRooms.Get(LoopRooms.CurrentRoomId);
            if (curRoom != null)
                refPos = LoopCorridorLayout.DoorFrontPosition(curRoom.Side, curRoom.Slot);

            LoopRoomRoot farthest = null;
            float best = -1f;
            foreach (var r in LoopRooms.Accessible())
            {
                if (r == exclude) continue;   // 警報の鳴っている部屋にはスポーンしない
                Vector3 p = LoopCorridorLayout.DoorFrontPosition(r.Side, r.Slot);
                float d = (p - refPos).sqrMagnitude;
                if (d > best) { best = d; farthest = r; }
            }

            // 最初の部屋にスポーンする場合も扉前に現れる（＝すぐ部屋を出た扱い）
            Vector3 spawn = farthest != null
                ? LoopCorridorLayout.DoorFrontPosition(farthest.Side, farthest.Slot)
                : refPos;

            // 理不尽な即捕獲を防ぐ：近すぎる場合は回廊上でプレイヤーから最も遠い地点にする
            const float MinSpawnDistance = 14f;
            if ((spawn - refPos).magnitude < MinSpawnDistance)
                spawn = FarthestCorridorPoint(refPos);

            _searcher = LoopSearcher.Spawn(spawn);
            Debug.Log($"[BreakerSystem] 襲撃者スポーン: {(farthest != null ? farthest.DisplayName : "回廊")} " +
                      $"付近 {spawn.ToString("F1")}");
        }

        /// <summary>回廊の全扉前候補のうち、基準点から最も遠い位置</summary>
        private static Vector3 FarthestCorridorPoint(Vector3 refPos)
        {
            Vector3 best = refPos;
            float bestD = -1f;
            for (int side = 0; side < 4; side++)
                for (int slot = 0; slot < LoopCorridorLayout.DoorsPerSide; slot += 3)
                {
                    Vector3 p = LoopCorridorLayout.DoorFrontPosition(side, slot);
                    float d = (p - refPos).sqrMagnitude;
                    if (d > bestD) { bestD = d; best = p; }
                }
            return best;
        }

        /// <summary>BreakerSwitchから：上げられた</summary>
        public void NotifyRaised(string roomId)
        {
            // 対象外の部屋（最初の部屋の初回上げ等）でも、
            // 全ブレイカーが上がっているなら警報は残さず必ず止める
            if (roomId != DownRoomId)
            {
                if (AllBreakersUp()) StopAllAlarms();
                return;
            }

            DownRoomId = null;
            StopAllAlarms();
            // 参照ズレがあっても取り漏らさないよう、シーン上の全襲撃者に退場を指示する
            foreach (var s in FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                s.Retreat();
            _searcher = null;
            _timeLeft = CycleSeconds;   // ここから次のサイクルが必ず始まる
            Debug.Log($"[BreakerSystem] ブレイカー復旧。次のサイクルまで {CycleSeconds} 秒");
        }

        /// <summary>ブレイカー警報がどこかで鳴っているか</summary>
        private static bool AnyAlarmPlaying()
        {
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (src == null || !src.isPlaying) continue;
                if (src.gameObject.name.StartsWith("BreakerAlarm_")) return true;
                if (src.GetComponent<BreakerSwitch>() != null) return true;
            }
            return false;
        }

        private static bool AllBreakersUp()
        {
            foreach (var r in LoopRooms.All)
                if (r.Breaker != null && !r.Breaker.IsUp) return false;
            return true;
        }

        /// <summary>回廊側の音源と、取り残された警報音源をまとめて停止する</summary>
        private void StopAllAlarms()
        {
            if (_corridorAlarm != null) { Destroy(_corridorAlarm); _corridorAlarm = null; }
            _alarmLight = null;
            // 名前で残骸を掃除（二重降下や参照ズレの保険）
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (src == null) continue;
                if (src.gameObject.name.StartsWith("BreakerAlarm_")) { Destroy(src.gameObject); continue; }
                // 上がっているブレイカー本体の音源が鳴り続けていたら止める
                var sw = src.GetComponent<BreakerSwitch>();
                if (sw != null && sw.IsUp && src.isPlaying) src.Stop();
            }
        }

        // ============= デバッグAPI（MCP用） =============
        public string DebugStatus() =>
            $"tutorialStarted:{_tutorialTimerStarted} timeLeft:{_timeLeft:0.0} down:{DownRoomId ?? "-"} " +
            $"searcher:{(_searcher != null)} stage:{LoopRooms.Stage} loc:{LoopRooms.CurrentRoomId ?? "corridor"}";

        public string DebugDrop() { _timeLeft = -1f; Drop(); return DebugStatus(); }

        public string DebugRaise()
        {
            var room = LoopRooms.Get(DownRoomId);
            if (room != null && room.Breaker != null) { room.Breaker.SetUp(true); NotifyRaised(room.Id); return "raised"; }
            return "no down breaker";
        }
    }
}
