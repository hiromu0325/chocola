using System;
using System.IO;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 襲撃まわりの不具合調査用ログ。
    /// プレイのたびに project/Logs/attack_debug.log へセッション区切り付きで追記される。
    /// （原因究明が終わったら呼び出しごと削除してよい）
    /// </summary>
    public static class AttackDebugLog
    {
        private static string _path;
        private static bool _failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void SessionStart()
        {
            _failed = false;
            try
            {
                string dir = Path.Combine(Application.dataPath, "../Logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "attack_debug.log");
                // 肥大化したら一度リセット
                if (File.Exists(_path) && new FileInfo(_path).Length > 2_000_000)
                    File.Delete(_path);
                File.AppendAllText(_path,
                    $"\n=============== SESSION START {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===============\n");
            }
            catch { _failed = true; }
        }

        /// <summary>1行記録。ゲーム時刻・フレーム・主要状態のスナップショット付き</summary>
        public static void Log(string tag, string msg)
        {
            if (_failed || _path == null) return;
            try
            {
                var bs = BreakerSystem.Instance;
                string ctx = bs != null
                    ? $"down={bs.DownRoomId ?? "-"} hunt={bs.HuntLeft:0.0} loc={LoopRooms.CurrentRoomId ?? "corridor"}"
                    : "bs=null";
                File.AppendAllText(_path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] [t={Time.time:0.0} f={Time.frameCount}] [{tag}] {msg}  |  {ctx}\n");
            }
            catch { _failed = true; }
        }
    }
}
