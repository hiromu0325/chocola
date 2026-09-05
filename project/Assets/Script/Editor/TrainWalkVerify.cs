using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 電車車内の通行性をプレイモードなしで検証する。
    /// プレイヤーのCharacterController（半径0.3・身長1.8）に相当する
    /// カプセルを通路に沿って動かし、什器に引っかからないか調べる。
    /// </summary>
    public static class TrainWalkVerify
    {
        [MenuItem("Tools/EscapePrototype/Debug/電車の通行性を検証")]
        public static void Verify()
        {
            EditorSceneManager.OpenScene("Assets/EscapePrototype/LoopPrototype.unity");
            var corridor = GameObject.Find("Corridor");
            if (corridor != null) corridor.SetActive(false);
            LoopRoomRoot train = null;
            foreach (var r in Object.FindObjectsByType<LoopRoomRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                r.gameObject.SetActive(r.Id == "train");
                if (r.Id == "train") train = r;
            }
            if (train == null) { Debug.LogError("Room_train が見つかりません"); return; }

            Physics.SyncTransforms();

            const float radius = 0.38f;   // CC半径0.3 + 余裕
            const float half = 0.9f;      // 身長1.8の半分
            var entry = train.EntrySpawn.position;
            var exit = train.ExitSpawn.position;

            int blocked = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[TrainWalk] 入口{entry.ToString("F1")} → 出口{exit.ToString("F1")}");

            // 通路中心を0.25m刻みで進み、各点でカプセルが什器に埋まっていないか調べる
            int steps = Mathf.CeilToInt(Vector3.Distance(entry, exit) / 0.25f);
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(entry, exit, i / (float)steps);
                var bottom = p + Vector3.up * (half - radius + 0.1f);
                var top = p + Vector3.up * (2f * half - radius);
                var hits = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.GetComponentInParent<LoopRoomDoor>() != null) continue;
                    if (h.name == "Floor" || h.name == "Ceiling") continue;
                    blocked++;
                    sb.AppendLine($"  ✗ z={p.z:F2} で接触: {h.name} ({h.transform.parent?.name})");
                    break;
                }
            }
            sb.AppendLine(blocked == 0 ? "  ✓ 通路に障害物なし" : $"  合計 {blocked} 地点で接触");

            // 中吊り広告の下をくぐれるか（頭頂 1.8m がクリアするか）
            foreach (var f in train.GetComponentsInChildren<LoopFindable>(true))
            {
                var col = f.GetComponent<Collider>();
                float bottomY = col.bounds.min.y;
                sb.AppendLine($"  {f.DisplayName}: 下端 {bottomY:F2}m " + (bottomY >= 1.85f ? "→ 頭上クリア ✓" : "→ 頭に当たる ✗"));
            }

            // ブレイカーの前に立てるか
            var brk = train.Breaker.transform.position;
            var stand = brk + new Vector3(-0.75f, 0f, 0f);
            var sb2 = Physics.OverlapCapsule(stand + Vector3.up * (half - radius + 0.1f),
                                             stand + Vector3.up * (2f * half - radius), radius, ~0, QueryTriggerInteraction.Ignore);
            string brkNames = "";
            foreach (var h in sb2) if (h.name != "Floor") brkNames += h.name + " ";
            sb.AppendLine($"  ブレイカー前 {stand.ToString("F1")}: " + (brkNames == "" ? "立てる ✓" : "接触: " + brkNames));

            Debug.Log(sb.ToString());
        }
    }
}
