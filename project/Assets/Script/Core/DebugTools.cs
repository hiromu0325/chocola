using System.Collections;
using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// MCP（execute_code）から呼ぶデバッグAPI。プレイモード中に
    /// `return EscapeProto.DebugGimmicks.Pc();` のように使う。
    /// すべて正解値で即時解除する（デバッグ専用）。
    /// </summary>
    public static class DebugGimmicks
    {
        /// <summary>タイトルをスキップして新規プレイ開始</summary>
        public static string StartGame()
        {
            if (GameManager.Instance == null) return "no GameManager";
            GameManager.Instance.NewGame();
            return "game started";
        }

        /// <summary>PCログイン（正しい社員番号＋生年月日を自動入力）</summary>
        public static string Pc()
        {
            var ps = PuzzleState.Instance;
            if (ps == null || ps.TargetEmployee == null) return "no PuzzleState";
            return ps.TryPcLogin(ps.TargetEmployee.number, ps.TargetEmployee.birthdate)
                ? $"pc ok (id={ps.TargetEmployee.number})" : "pc FAILED";
        }

        /// <summary>配電室の鍵を入手（鍵保管者の個室のキャビネットを正解扱い）</summary>
        public static string TakeKey()
        {
            var ps = PuzzleState.Instance;
            if (ps == null || ps.KeyHolder == null) return "no PuzzleState";
            return ps.TryTakeKey(ps.KeyHolder.number)
                ? $"key ok (holder={ps.KeyHolder.number})" : "key FAILED（入手済み?）";
        }

        /// <summary>配電盤キーパッド解除</summary>
        public static string Panel()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "no PuzzleState";
            return ps.TryUnlockPanel(ps.KeypadPassword) ? "panel ok" : "panel FAILED";
        }

        /// <summary>復旧コード受理＋復旧作業完了（電力復旧まで一気に）</summary>
        public static string Repair()
        {
            var ps = PuzzleState.Instance;
            if (ps == null || ps.ActiveModel == null) return "no PuzzleState";
            bool code = ps.TryAcceptRepairCode(ps.ActiveModel.code);
            ps.CompleteRepair();
            return code && ps.PowerRestored ? "power restored" : $"repair FAILED (code={code})";
        }

        /// <summary>壁金庫を開錠</summary>
        public static string Safe()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "no PuzzleState";
            return ps.TryOpenSafe(ps.SafeCombo) ? "safe ok" : "safe FAILED";
        }

        /// <summary>ギミック扉を1枚即開（Idは Doors() で確認）</summary>
        public static string OpenDoor(string id) =>
            GimmickDoor.DebugOpen(id) ? $"opened {id}" : $"not found/already open: {id}";

        /// <summary>全ギミック扉を即開</summary>
        public static string OpenAllDoors() => $"opened {GimmickDoor.DebugOpenAll()} doors";

        /// <summary>扉一覧と状態</summary>
        public static string Doors() => GimmickDoor.DebugList();

        /// <summary>謎解き進行の一覧</summary>
        public static string Status()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "no PuzzleState";
            return $"pc:{ps.PcAccessed} key:{ps.HasPowerRoomKey} panel:{ps.PanelUnlocked} " +
                   $"repair:{ps.RepairCodeAccepted} power:{ps.PowerRestored} safe:{ps.SafeOpened} " +
                   $"target:{(ps.TargetEmployee != null ? ps.TargetEmployee.number : "-")} " +
                   $"keyHolder:{(ps.KeyHolder != null ? ps.KeyHolder.number : "-")}";
        }
    }

    // ※MonoBehaviour（DebugPlayerDriver）は
    //   シーン保存時のスクリプト解決のため、クラス名と同名の個別ファイルにある。
}
