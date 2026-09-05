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
        [Tooltip("trueならストーリー脚本モード：定期サイクルを止め、ScriptedDropの名指し襲撃のみ")]
        public bool StoryMode = true;

        /// <summary>現在降下中の部屋Id（無ければnull）</summary>
        public string DownRoomId { get; private set; }
        /// <summary>復旧後の徘徊フェーズが進行中か（異形が正当に活動している）</summary>
        public bool HuntActive => _huntLeft > 0f;
        /// <summary>徘徊フェーズ残り秒（ログ用）</summary>
        public float HuntLeft => _huntLeft;
        public float TimeLeft => _timeLeft;

        private float _timeLeft = -1f;   // <0 = 停止中
        private bool _tutorialTimerStarted;
        private GameObject _corridorAlarm;   // 回廊側（対象部屋の扉前）の警報音源
        private Light _alarmLight;           // 警報扉の赤い点滅ライト
        private LoopSearcher _searcher;
        private float _sweepTimer;

        private Color _prevAmbient;
        private bool _lightingDarkened;
        private bool _scriptedActive;    // 脚本襲撃の進行中（警報フェーズ）
        private float _huntLeft = -1f;   // 復旧後の徘徊フェーズ残り秒（<0で停止）
        private AudioSource _bgm;        // 襲撃中の不安BGM（2D・ループ）
        private float _snapshotTimer;    // 調査ログの定期スナップショット
        private int _residueStrikes;     // 残骸検知の連続回数（ウォッチドッグ）

        private void Awake() => Instance = this;
        private void OnEnable() => GameEvents.OnPlayerCaught += HandlePlayerCaught;
        private void OnDisable() => GameEvents.OnPlayerCaught -= HandlePlayerCaught;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// 死亡＝襲撃の終了。人形1体と引き換えに、警報フェーズならブレイカーは復旧扱いになり
        /// 持ち越されていた部屋の解放も実行される。徘徊フェーズならそのまま打ち切る。
        /// </summary>
        private void HandlePlayerCaught()
        {
            bool anyPhase = DownRoomId != null || _huntLeft > 0f;
            if (DownRoomId != null)
            {
                var room = LoopRooms.Get(DownRoomId);
                DownRoomId = null;
                _scriptedActive = false;
                if (room != null && room.Breaker != null) room.Breaker.SetUp(true, silent: true);
                StopAllAlarms();
                _timeLeft = StoryMode ? -1f : CycleSeconds;
                LoopProgress.NotifyBreakerRestored(room != null ? room.Id : null);
            }
            if (anyPhase)
            {
                AttackDebugLog.Log("death", "プレイヤー捕獲 → 襲撃終了処理");
                EndHunt();
                Debug.Log("[BreakerSystem] 死亡により襲撃終了");
            }
        }

        /// <summary>徘徊フェーズを終える（暗闇とBGMもここで解除する）</summary>
        private void EndHunt()
        {
            AttackDebugLog.Log("hunt", "EndHunt: 全異形に退場指示・暗転/BGM解除");
            _huntLeft = -1f;
            foreach (var s in FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                s.Retreat();
            _searcher = null;
            SetAttackLighting(false);
            StopBgm();
            if (_bgm != null) _bgm.pitch = 1f;   // 徘徊フェーズの緊迫ピッチを戻す
        }

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

                    // ウォッチドッグ：襲撃状態でないのに演出や異形が残っていたら
                    // 3秒連続で検知した時点で記録して強制終了する（退場歩行中の個体は対象外）
                    if (!_scriptedActive && _huntLeft <= 0f)
                    {
                        bool fxResidue = _lightingDarkened || (_bgm != null && _bgm.isPlaying);
                        int stuckSearchers = 0;
                        foreach (var s in FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                            if (!s.IsRetreating) stuckSearchers++;
                        if (fxResidue || stuckSearchers > 0)
                        {
                            _residueStrikes++;
                            AttackDebugLog.Log("watchdog",
                                $"残骸検知 {_residueStrikes}/3: 暗転={_lightingDarkened} bgm={(_bgm != null && _bgm.isPlaying)} 非退場の異形={stuckSearchers}");
                            if (_residueStrikes >= 3)
                            {
                                AttackDebugLog.Log("watchdog", "強制終了を実行（ここに来たら異常経路がある）");
                                _residueStrikes = 0;
                                EndHunt();
                            }
                        }
                        else _residueStrikes = 0;
                    }
                }
            }

            // 調査ログ：襲撃に関係する間は3秒ごとに全体スナップショットを残す
            if (DownRoomId != null || _huntLeft > 0f || _scriptedActive)
            {
                _snapshotTimer += Time.deltaTime;
                if (_snapshotTimer >= 3f)
                {
                    _snapshotTimer = 0f;
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"scripted={_scriptedActive} bgm={(_bgm != null && _bgm.isPlaying)} 暗転={_lightingDarkened} 異形:");
                    foreach (var s in FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                        sb.Append($" [{s.DebugBrief()}]");
                    AttackDebugLog.Log("snapshot", sb.ToString());
                }
            }

            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            // ストーリー脚本モード：定期サイクルは回さない（襲撃はScriptedDropのみ）
            if (StoryMode) return;

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
            DropOn(candidates[Random.Range(0, candidates.Count)], spawnSearcherNow: true);
        }

        /// <summary>
        /// ストーリー脚本：部屋を名指しでブレイカー降下させる。
        /// 警報フェーズでは異形は現れず、ブレイカーを上げた瞬間に現れる（NotifyRaised側）
        /// </summary>
        public void ScriptedDrop(string roomId)
        {
            if (DownRoomId != null) return;
            var target = LoopRooms.Get(roomId);
            if (target == null || target.Breaker == null)
            {
                Debug.LogError($"[BreakerSystem] ScriptedDrop: 部屋が見つからない ({roomId})");
                return;
            }
            _timeLeft = -1f;
            _scriptedActive = true;
            AttackDebugLog.Log("drop", $"ScriptedDrop({roomId}) 警報フェーズ開始（異形も同時に放たれる）");
            // 警報中に異形が徘徊し、ブレイカーを上げれば襲撃ごと終わる
            DropOn(target, spawnSearcherNow: true);
        }

        /// <summary>指定部屋のブレイカーを降下させ、警報・照明暗転・BGMを起動する</summary>
        private void DropOn(LoopRoomRoot target, bool spawnSearcherNow)
        {
            DownRoomId = target.Id;
            target.Breaker.SetUp(false);
            SetAttackLighting(true);
            StartBgm();

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

            if (spawnSearcherNow) SpawnSearcher(target);
            Debug.Log($"[BreakerSystem] ブレイカー降下: {target.DisplayName}（{target.Id}）" +
                      (spawnSearcherNow ? "" : "（異形は復旧後に現れる）"));
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
                AttackDebugLog.Log("raise", $"NotifyRaised({roomId}) 不一致（down={DownRoomId ?? "-"}）→ 何もしない");
                if (AllBreakersUp()) StopAllAlarms();
                return;
            }
            AttackDebugLog.Log("raise", $"NotifyRaised({roomId}) 一致 scripted={_scriptedActive}");

            DownRoomId = null;
            _scriptedActive = false;
            StopAllAlarms();
            _timeLeft = StoryMode ? -1f : CycleSeconds;
            // 脚本襲撃で持ち越されていた部屋の解放を実行
            LoopProgress.NotifyBreakerRestored(roomId);

            {
                // ブレイカー復旧＝襲撃は完全に終了する（警報・暗闇・BGM・異形すべて）
                SetAttackLighting(false);
                StopBgm();
                RoomTitleUI.Instance?.Show("ブレイカーを復旧した", "──警報は止み、気配は遠ざかっていく──");
                foreach (var s in FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                    s.Retreat();
                _searcher = null;
                Debug.Log("[BreakerSystem] ブレイカー復旧" +
                          (StoryMode ? "" : $"。次のサイクルまで {CycleSeconds} 秒"));
            }
        }

        // ============= 襲撃BGM =============

        private void StartBgm()
        {
            if (_bgm == null)
            {
                _bgm = gameObject.AddComponent<AudioSource>();
                _bgm.clip = ProceduralAudio.TensionLoop();
                _bgm.loop = true;
                _bgm.playOnAwake = false;
                _bgm.spatialBlend = 0f;   // 2D（どこにいても聞こえる）
                _bgm.volume = 0.5f;
            }
            if (!_bgm.isPlaying) _bgm.Play();
        }

        private void StopBgm()
        {
            if (_bgm != null && _bgm.isPlaying) _bgm.Stop();
        }

        /// <summary>
        /// 襲撃中の照明演出。環境光を非常灯レベルまで落とす（懐中電灯が活きる暗さ）。
        /// 懐中電灯を持っていれば自動で点ける。
        /// </summary>
        private void SetAttackLighting(bool on)
        {
            if (on)
            {
                if (!_lightingDarkened)
                {
                    _prevAmbient = RenderSettings.ambientLight;
                    RenderSettings.ambientLight = new Color(0.10f, 0.03f, 0.03f);
                    _lightingDarkened = true;
                }
                if (LoopProgress.IsFound(LoopProgress.StartRoomId, "flashlight"))
                {
                    var fl = FindFirstObjectByType<Flashlight>();
                    if (fl != null) fl.SetOn(true);
                }
            }
            else if (_lightingDarkened)
            {
                RenderSettings.ambientLight = _prevAmbient;
                _lightingDarkened = false;
            }
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
            $"hunt:{_huntLeft:0.0} bgm:{(_bgm != null && _bgm.isPlaying)} " +
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
