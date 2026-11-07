using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// HDRP の見た目の確認用の撮影（プレイ中に呼ぶ）。同じ位置・向きから撮り、画面の UI は隠す。
    /// Begin(tag) → E:/chocola-hdrp-tmp/shots/{tag}_{name}.png。進み具合は Status
    /// </summary>
    public static class HdrpShots
    {
        public struct Shot
        {
            public string Name, Room;
            public bool InRoom, Flashlight;
            public float Yaw, Pitch;     // 部屋の入口の向きからの差（度）と上下
            public Vector3 Offset;       // 入口の位置からのずれ（入口の向きに合わせた向き：x 右・z 前）
        }

        public static readonly List<Shot> Shots = new List<Shot>
        {
            new Shot { Name = "dim_a", Room = "dim", InRoom = true, Yaw = 0f, Pitch = 4f },
            new Shot { Name = "dim_b", Room = "dim", InRoom = true, Yaw = 140f, Pitch = 2f, Offset = new Vector3(0.6f, 0f, 2.2f) },
            new Shot { Name = "dim_flash", Room = "dim", InRoom = true, Flashlight = true, Yaw = 140f, Pitch = 2f, Offset = new Vector3(0.6f, 0f, 2.2f) },
            new Shot { Name = "corridor", Room = "dim", InRoom = false, Yaw = 0f, Pitch = 0f },
        };

        public static string Status = "idle";
        private static string _tag;
        private static int _index, _frames;
        private static bool _placed;
        private static readonly List<Canvas> _hidden = new List<Canvas>();
        public const string Dir = "E:/chocola-hdrp-tmp/shots";

        /// <summary>入口から部屋の奥を向いた1枚ずつ（薄暗い部屋以外の13部屋）</summary>
        public static readonly string[] OtherRooms =
        {
            "train", "lab", "study", "analysis", "saeki_home", "ward", "core_ante",
            "mizuno_apart", "data_room", "system_room", "kuroda_home", "core_main", "son_room",
        };

        private static List<Shot> _list = Shots;
        private static bool _measure;
        private static readonly List<double> _cpu = new List<double>(), _gpu = new List<double>(), _dt = new List<double>();
        private static readonly FrameTiming[] _timing = new FrameTiming[1];

        public static string BeginRooms(string tag)
        {
            var list = new List<Shot>();
            foreach (var id in OtherRooms)
                list.Add(new Shot { Name = "room_" + id, Room = id, InRoom = true, Yaw = 0f, Pitch = 2f, Offset = new Vector3(0f, 0f, 0.6f) });
            return Begin(tag, list);
        }

        /// <summary>撮る代わりに、置いてから落ち着いた後の 180 コマの CPU・GPU の時間を測る（{tag}_timing.txt に追記）</summary>
        public static string Measure(string tag, List<Shot> list = null)
        {
            var r = Begin(tag, list);
            _measure = true;
            File.WriteAllText($"{Dir}/{tag}_timing.txt", "");
            return r;
        }

        public static string Begin(string tag, List<Shot> list = null)
        {
            if (!Application.isPlaying) return "プレイ中に呼ぶ";
            Directory.CreateDirectory(Dir);
            _list = list ?? Shots;
            _measure = false;
            _tag = tag;
            _index = 0;
            _frames = 0;
            _placed = false;
            // 懐中電灯を拾った扱いにする（部屋の解放などの処理は通さない）
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var keys = (HashSet<string>)typeof(LoopProgress).GetField("FoundKeys", bf).GetValue(null);
            keys.Add((string)typeof(LoopProgress).GetMethod("Key", bf).Invoke(null, new object[] { LoopProgress.StartRoomId, "flashlight" }));
            Status = "running";
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return "started";
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { Finish("プレイが止まった"); return; }
            if (_index >= _list.Count) { Finish("done"); return; }
            var s = _list[_index];
            if (!_placed)
            {
                // プレイ中にスクリプトを読み直すと、部屋の移動の仕組みの参照が消える（毎コマ失敗し続けないように止める）
                if (RoomTransitionSystem.Instance == null || GameObject.FindGameObjectWithTag("Player") == null)
                {
                    Finish("部屋の移動の仕組みかプレイヤーが見つからない（プレイをやり直してから呼ぶ）");
                    return;
                }
                Place(s);
                _placed = true;
                _frames = 0;
                _cpu.Clear(); _gpu.Clear(); _dt.Clear();
                return;
            }
            _frames++;
            if (_measure)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (_frames > 90 && FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    _cpu.Add(_timing[0].cpuFrameTime);
                    _gpu.Add(_timing[0].gpuFrameTime);
                }
                if (_frames > 90) _dt.Add(Time.unscaledDeltaTime * 1000.0);
                if (_frames == 270)
                {
                    File.AppendAllText($"{Dir}/{_tag}_timing.txt",
                        $"{s.Name}\tcpu {Avg(_cpu):F2}ms\tgpu {Avg(_gpu):F2}ms\tframe {Avg(_dt):F2}ms\tsamples {_gpu.Count}\n");
                    _index++;
                    _placed = false;
                    Status = $"measure {_index}/{_list.Count}";
                }
                return;
            }
            if (_frames == 90) // 体積の霧・反射の撮り直し・露出が落ち着くまで待つ
            {
                HideUi(true);
            }
            else if (_frames == 93)
            {
                ScreenCapture.CaptureScreenshot($"{Dir}/{_tag}_{s.Name}.png");
            }
            else if (_frames == 100)
            {
                HideUi(false);
                _index++;
                _placed = false;
                Status = $"shot {_index}/{_list.Count}";
            }
        }

        private static double Avg(List<double> v)
        {
            if (v.Count == 0) return 0;
            double s = 0;
            foreach (var x in v) s += x;
            return s / v.Count;
        }

        private static void Place(Shot s)
        {
            var rts = RoomTransitionSystem.Instance;
            rts.DebugPlace(s.Room, s.InRoom);
            var player = GameObject.FindGameObjectWithTag("Player");
            var fpc = player.GetComponent<StarterAssets.FirstPersonController>();
            float baseYaw = player.transform.eulerAngles.y;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position += Quaternion.Euler(0f, baseYaw, 0f) * s.Offset;
            if (cc != null) cc.enabled = true;
            fpc.SetLook(baseYaw + s.Yaw, s.Pitch);
            var fl = Object.FindFirstObjectByType<Flashlight>();
            if (fl != null) fl.SetOn(s.Flashlight);
        }

        private static void HideUi(bool hide)
        {
            if (hide)
            {
                _hidden.Clear();
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; _hidden.Add(c); }
            }
            else
            {
                foreach (var c in _hidden) if (c != null) c.enabled = true;
                _hidden.Clear();
            }
        }

        private static void Finish(string status)
        {
            HideUi(false);
            EditorApplication.update -= Tick;
            Status = status;
        }
    }
}
