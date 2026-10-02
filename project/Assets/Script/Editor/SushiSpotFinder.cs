using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EscapeProto;
using StarterAssets;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 隠しミニゲーム「わさび当て」の寿司下駄の隠し場所を、全部屋から部屋ごとに3か所選ぶ（Resources/SushiSpots.json）。
/// 開いている回廊のシーンで使う（シーンは保存しない）。
/// ・部屋の見た目（家具など）に一時的に当たり判定を付け、下駄（25×12cm）が水平に置けて、上にめくったネタが立つ空き
///   （13cm）のある所を総当たりで探す（床・棚・机の下・椅子の座面なども）
/// ・プレイヤーが立てる所（当たり判定の箱に当たらず、入口から歩いて行ける所）から、目の高さ 1.55m で見えるかを数え、
///   見える立ち位置が少ないほど「分かりづらい」とする。ただし 1.7m 以内に見える立ち位置が無い所（近くでめくれない）は除く
/// ・入口から見える所は減点。調べられる物・扉・入口の近く、目の高さより上は使わない
/// ・互いに 1.2m 以上離す（部屋の家具は1つのメッシュにまとまっていることが多いので、物ではなく距離で散らす）
/// </summary>
public static class SushiSpotFinder
{
    private const float Eye = 1.55f;
    private const int Probe = 31;                       // 一時的な当たり判定の層（使っていない層）
    private const float HalfX = 0.068f, HalfZ = 0.132f; // 下駄の置き場（奥行 0.12 × 長さ 0.25 ＋ 少し）
    private const float Clear = 0.13f;                  // 上に要る空き（めくったネタが立つ高さ）
    private const float NearDist = 1.7f, ViewDist = 7f;
    private const int PerRoom = 3;
    private const float MinApart = 1.2f;
    private const string OutPath = "Assets/Resources/SushiSpots.json";

    [System.Serializable] private class Out { public SushiGame.Spot[] spots; }

    private class Cand
    {
        public Vector3 p;            // 置く所（下駄の底の中心、ワールド）
        public float yaw;            // ワールドの向き（下駄の長手 = ローカル Z）
        public Object owner;         // 乗っている物（記録用）
        public int visible, near;
        public bool fromEntry;
        public float hidden;
        public Vector3 faceDir;
    }

    [MenuItem("Tools/EscapePrototype/Hidden/寿司の隠し場所を探す")]
    public static void Find()
    {
        var rooms = Object.FindObjectsByType<LoopRoomRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .OrderBy(r => r.Id).ToArray();
        if (rooms.Length == 0) { Debug.LogWarning("[SushiSpot] 部屋が見つからない（回廊のシーンを開いて）"); return; }
        var wasActive = rooms.ToDictionary(r => r, r => r.gameObject.activeSelf);
        var probes = new List<GameObject>();
        var spots = new List<SushiGame.Spot>();
        var log = new StringBuilder();
        try
        {
            foreach (var r in rooms) r.gameObject.SetActive(true);
            probes = AddProbes(rooms);
            foreach (var r in rooms)
            {
                EditorUtility.DisplayProgressBar("寿司の隠し場所", r.Id, (float)System.Array.IndexOf(rooms, r) / rooms.Length);
                spots.AddRange(FindInRoom(r, log));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            foreach (var g in probes) if (g != null) Object.DestroyImmediate(g);
            foreach (var kv in wasActive) if (kv.Key != null) kv.Key.gameObject.SetActive(kv.Value);
            Physics.SyncTransforms();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllText(OutPath, JsonUtility.ToJson(new Out { spots = spots.ToArray() }, true));
        AssetDatabase.ImportAsset(OutPath);
        Debug.Log($"[SushiSpot] {spots.Count} か所を {OutPath} に書いた\n{log}");
    }

    private static List<SushiGame.Spot> FindInRoom(LoopRoomRoot room, StringBuilder log)
    {
        var T = room.transform;
        float floorY = T.position.y;
        int all = ~0, walls = ~(1 << Probe);

        // 部屋の広さ（見た目の外接箱。窓の外の景色は床の判定で除かれる）
        var b = new Bounds(T.position, Vector3.zero);
        foreach (var mr in T.GetComponentsInChildren<MeshRenderer>()) if (mr.enabled) b.Encapsulate(mr.bounds);

        bool Inside(Vector3 xz, out float ceil)
        {
            ceil = 0f;
            var o = new Vector3(xz.x, floorY + 1.0f, xz.z);
            if (!Physics.Raycast(o, Vector3.up, out var up, 10f, all, QueryTriggerInteraction.Ignore) || !up.transform.IsChildOf(T)) return false;   // 天井の高い部屋もある
            if (!Physics.Raycast(o, Vector3.down, out var dn, 1.2f, all, QueryTriggerInteraction.Ignore) || !dn.transform.IsChildOf(T)) return false;
            ceil = up.point.y;
            return true;
        }

        // ---- 立てる所（0.1m 格子＝ベッドの脇などの狭い通り道も途切れない）。入口から歩いて行ける所だけ ----
        const float step = 0.1f;
        int nx = Mathf.CeilToInt(b.size.x / step), nz = Mathf.CeilToInt(b.size.z / step);
        var free = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                var p = new Vector3(b.min.x + (i + 0.5f) * step, floorY, b.min.z + (k + 0.5f) * step);
                if (!Inside(p, out _)) continue;
                free[i, k] = !Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.5f, 0.3f, walls, QueryTriggerInteraction.Ignore);
            }
        var reach = new bool[nx, nz];
        var start = room.EntrySpawn != null ? room.EntrySpawn.position : T.position;
        int si = Mathf.Clamp(Mathf.FloorToInt((start.x - b.min.x) / step), 0, nx - 1), sk = Mathf.Clamp(Mathf.FloorToInt((start.z - b.min.z) / step), 0, nz - 1);
        // 入口のすぐ近くで一番近い立てる所から歩き始める
        float bestD = float.MaxValue;
        for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
            if (free[i, k]) { float d = (i - si) * (i - si) + (k - sk) * (k - sk); if (d < bestD) { bestD = d; si = i; sk = k; } }
        // 歩ける所のつながり（8方向）。入口の近くのつながりが全体の 1/4 より小さい時（入口が狭い区画に切り離されて
        // 見える時）は、一番広いつながりを使う
        var comp = new int[nx, nz];
        var sizes = new List<int> { 0 };
        for (int i0 = 0; i0 < nx; i0++)
            for (int k0 = 0; k0 < nz; k0++)
            {
                if (!free[i0, k0] || comp[i0, k0] != 0) continue;
                int id = sizes.Count, size = 0;
                var q = new Queue<(int, int)>();
                q.Enqueue((i0, k0)); comp[i0, k0] = id;
                while (q.Count > 0)
                {
                    var (i, k) = q.Dequeue();
                    size++;
                    foreach (var (di, dk) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                    {
                        int a = i + di, c = k + dk;
                        if (a < 0 || c < 0 || a >= nx || c >= nz || comp[a, c] != 0 || !free[a, c]) continue;
                        comp[a, c] = id;
                        q.Enqueue((a, c));
                    }
                }
                sizes.Add(size);
            }
        int totalFree = sizes.Sum();
        int use = bestD < float.MaxValue ? comp[si, sk] : 0;
        if (use == 0 || sizes[use] * 4 < totalFree)
            use = Enumerable.Range(1, sizes.Count - 1).DefaultIfEmpty(0).OrderByDescending(id => sizes[id]).First();
        for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++) reach[i, k] = use != 0 && comp[i, k] == use;
        // 見えるかを数える立ち位置は 0.2m おき（歩ける所のうち）
        var eyes = new List<Vector3>();
        for (int i = 0; i < nx; i += 2) for (int k = 0; k < nz; k += 2)
            if (reach[i, k]) eyes.Add(new Vector3(b.min.x + (i + 0.5f) * step, floorY + Eye, b.min.z + (k + 0.5f) * step));
        if (eyes.Count == 0) { log.AppendLine($"{room.Id}: 立てる所が無い"); return new List<SushiGame.Spot>(); }
        var entryEye = start + Vector3.up * Eye;

        // 調べられる物・扉・入口の近くは使わない
        var avoid = new List<Bounds>();
        foreach (var mb in T.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!(mb is IInteractable)) continue;
            foreach (var col in mb.GetComponentsInChildren<Collider>(true))
            {
                var ab = col.bounds; ab.Expand(1.0f); avoid.Add(ab);
            }
        }
        foreach (var sp in new[] { room.EntrySpawn, room.ExitSpawn })
            if (sp != null) avoid.Add(new Bounds(sp.position, new Vector3(2.6f, 4f, 2.6f)));

        bool Excluded(Collider c)
        {
            var t = c.transform;
            if (t.GetComponentInParent<IInteractable>() != null || t.GetComponentInParent<SushiGame>() != null) return true;
            var mr = t.parent != null ? t.parent.GetComponent<MeshRenderer>() : null;
            if (mr != null)
                foreach (var m in mr.sharedMaterials)
                    if (m != null && (m.name.Contains("Glass") || m.name.Contains("Night") || m.name.Contains("Bulb") || m.name.Contains("Shade") || m.name.Contains("Lamp")))
                        return true;
            string n = t.parent != null ? t.parent.name : t.name;
            return n.Contains("Door") || n.Contains("Echo") || n.Contains("Lamps");
        }

        // ---- 置ける面を総当たり（0.1m 格子。同じ物の下の面も見るため、当たった所の少し下から何度も撃ち直す） ----
        var cells = new Dictionary<(int, int, int), (Vector3 p, Collider c)>();
        for (float x = b.min.x + 0.05f; x < b.max.x; x += 0.1f)
            for (float z = b.min.z + 0.05f; z < b.max.z; z += 0.1f)
            {
                if (!Inside(new Vector3(x, 0f, z), out float ceil)) continue;
                var o = new Vector3(x, ceil - 0.02f, z);
                for (int n = 0; n < 24; n++)
                {
                    if (!Physics.Raycast(o, Vector3.down, out var h, o.y - (floorY - 0.05f), all, QueryTriggerInteraction.Ignore)) break;
                    o = h.point + Vector3.down * 0.003f;
                    if (h.normal.y < 0.96f || !h.transform.IsChildOf(T)) continue;
                    if (h.point.y > floorY + Eye - 0.15f || h.point.y < floorY - 0.03f) continue;
                    if (avoid.Any(a => a.Contains(h.point)) || Excluded(h.collider)) continue;
                    var key = (Mathf.FloorToInt(h.point.x / 0.2f), Mathf.FloorToInt(h.point.z / 0.2f), Mathf.FloorToInt((h.point.y - floorY) / 0.05f));
                    if (!cells.ContainsKey(key)) cells[key] = (h.point, h.collider);
                }
            }

        // ---- 下駄が水平に乗り、上が空いているか（向きは 0/45/90/135 度） ----
        var cands = new List<Cand>();
        foreach (var (p, c) in cells.Values)
        {
            foreach (float yaw in new[] { 0f, 45f, 90f, 135f })
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                bool ok = true;
                foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f), (0f, 0f), (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f) })
                {
                    var s = p + rot * new Vector3(sx * HalfX, 0f, sz * HalfZ);
                    if (!Physics.Raycast(s + Vector3.up * 0.18f, Vector3.down, out var h, 0.22f, all, QueryTriggerInteraction.Ignore)
                        || Mathf.Abs(h.point.y - p.y) > 0.007f || h.normal.y < 0.96f) { ok = false; break; }
                }
                if (!ok) continue;
                if (Physics.CheckBox(p + Vector3.up * (0.008f + Clear / 2f), new Vector3(HalfX, Clear / 2f, HalfZ), rot, all, QueryTriggerInteraction.Ignore))
                    continue;
                cands.Add(new Cand { p = p, yaw = yaw, owner = c.transform.parent != null ? (Object)c.transform.parent : c });
                break;
            }
        }

        // ---- 見えるか（目の高さから、ネタの上 6cm と 3cm の2点）。見える立ち位置が少ないほど分かりづらい ----
        foreach (var cd in cands)
        {
            var t1 = cd.p + Vector3.up * 0.06f;
            var t2 = cd.p + Vector3.up * 0.035f;
            var face = Vector3.zero;
            foreach (var e in eyes)
            {
                float d = Vector3.Distance(e, t1);
                if (d > ViewDist) continue;
                bool v1 = !Physics.Linecast(e, t1 + (e - t1).normalized * 0.02f, all, QueryTriggerInteraction.Ignore);
                bool v2 = !Physics.Linecast(e, t2 + (e - t2).normalized * 0.02f, all, QueryTriggerInteraction.Ignore);
                if (!(v1 && v2)) continue;
                cd.visible++;
                if (d <= NearDist) { cd.near++; var dir = e - cd.p; dir.y = 0f; face += dir.normalized; }
            }
            cd.fromEntry = Vector3.Distance(entryEye, t1) <= ViewDist &&
                           !Physics.Linecast(entryEye, t1 + (entryEye - t1).normalized * 0.02f, all, QueryTriggerInteraction.Ignore);
            cd.hidden = 1f - (float)cd.visible / eyes.Count - (cd.fromEntry ? 0.15f : 0f);
            cd.faceDir = face;
        }

        // ---- 分かりづらい順に、互いに離して3か所 ----
        // 床ばかりにならないよう、床の高さは2か所まで（高さのある所にそこそこ分かりづらい候補があれば）
        // 高さのある所は十分に分かりづらい時（0.75 以上）だけ。足りなければ決まりを緩めて埋める
        var picked = new List<Cand>();
        var usable = cands.Where(c => c.near >= 2 && c.visible >= 2).OrderByDescending(c => c.hidden).ToList();
        bool IsFloor(Cand c) => c.p.y - floorY <= 0.15f;
        bool raisedAvailable = usable.Any(c => !IsFloor(c) && c.hidden >= 0.75f);
        foreach (var cd in usable)
        {
            if (picked.Count >= PerRoom) break;
            if (picked.Any(p => Vector3.Distance(p.p, cd.p) < MinApart)) continue;
            if (IsFloor(cd) && raisedAvailable && picked.Count(IsFloor) >= 2) continue;
            if (!IsFloor(cd) && cd.hidden < 0.75f) continue;
            picked.Add(cd);
        }
        foreach (var cd in usable)
        {
            if (picked.Count >= PerRoom) break;
            if (picked.Contains(cd) || picked.Any(p => Vector3.Distance(p.p, cd.p) < MinApart)) continue;
            picked.Add(cd);
        }

        var result = new List<SushiGame.Spot>();
        log.AppendLine($"{room.Id}: 立てる所 {eyes.Count}、置ける所 {cands.Count}（近くでめくれる {usable.Count}）、選んだ {picked.Count}");
        foreach (var cd in picked)
        {
            // 握りの手前（ローカル +X）を、近くで見る立ち位置の方へ向ける（下駄の向きは 180 度回しても置き場は同じ）
            float yaw = cd.yaw;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            if (cd.faceDir.sqrMagnitude > 0.0001f && Vector3.Dot(fwd, cd.faceDir) < 0f) yaw += 180f;
            var lp = T.InverseTransformPoint(cd.p);
            float ly = Mathf.Repeat(yaw - T.eulerAngles.y, 360f);
            result.Add(new SushiGame.Spot
            {
                room = room.Id, pos = new[] { Round(lp.x), Round(lp.y), Round(lp.z) }, yaw = ly,
                hidden = Round(cd.hidden), near = cd.near,
            });
            log.AppendLine($"  ({lp.x:F2}, {lp.y:F2}, {lp.z:F2}) 向き {ly:F0}  分かりづらさ {cd.hidden:F2}  見える立ち位置 {cd.visible}/{eyes.Count}（近く {cd.near}）" +
                           $"{(cd.fromEntry ? "  入口から見える" : "")}  上: {(cd.owner != null ? cd.owner.name : "-")}");
        }
        return result;
    }

    private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

    /// <summary>部屋の見た目に一時的な当たり判定（保存しない）を付ける。見えるか・置けるかを実際の形で調べるため</summary>
    private static List<GameObject> AddProbes(IEnumerable<LoopRoomRoot> rooms)
    {
        var probes = new List<GameObject>();
        foreach (var r in rooms)
            foreach (var mr in r.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.enabled || mr.GetComponentInParent<SushiGame>() != null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject("__SushiProbe") { hideFlags = HideFlags.HideAndDontSave, layer = Probe };
                go.transform.SetParent(mr.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                probes.Add(go);
            }
        Physics.SyncTransforms();
        return probes;
    }

    /// <summary>寿司が見える近くの立ち位置（0.7〜1.6m。立てる所で、目から見通せる所。握りの手前の向きに近いほど良い）</summary>
    private static Vector3? ViewPoint(Vector3 spot, Vector3 front, float floorY)
    {
        int all = ~0, walls = ~(1 << Probe);
        var t1 = spot + Vector3.up * 0.06f;
        Vector3? best = null;
        float bestScore = float.MinValue;
        for (float r = 0.7f; r <= 1.6f; r += 0.15f)
            for (int a = 0; a < 24; a++)
            {
                float ang = a * 15f;
                var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                var foot = new Vector3(spot.x, floorY, spot.z) + dir * r;
                if (Physics.CheckCapsule(foot + Vector3.up * 0.4f, foot + Vector3.up * 1.5f, 0.3f, walls, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(foot + Vector3.up * 1.0f, Vector3.down, 1.2f, all, QueryTriggerInteraction.Ignore)) continue;
                var eye = foot + Vector3.up * Eye;
                if (Physics.Linecast(eye, t1 + (eye - t1).normalized * 0.02f, all, QueryTriggerInteraction.Ignore)) continue;
                float score = Vector3.Dot(dir, front) - Mathf.Abs(r - 1.1f);
                if (score > bestScore) { bestScore = score; best = eye; }
            }
        return best;
    }

    // ============================== 確認の写真 ==============================

    /// <summary>
    /// 隠し場所ごとに、寿司を置いた様子を2枚撮る（Temp/SushiSpots/）。
    /// near = 握りの手前 1m・目の高さから（懐中電灯つき）、far = 部屋の入口から（どれくらい目に入るか）
    /// </summary>
    [MenuItem("Tools/EscapePrototype/Hidden/寿司の隠し場所を撮る")]
    public static void Shoot()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Models/HQ/Sushi/Sushi.prefab");
        if (asset == null || prefab == null) { Debug.LogWarning("[SushiSpot] 隠し場所か寿司の Prefab が無い"); return; }
        var list = JsonUtility.FromJson<Out>(asset.text);
        var rooms = Object.FindObjectsByType<LoopRoomRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var byId = rooms.ToDictionary(r => r.Id, r => r);
        var wasActive = rooms.ToDictionary(r => r, r => r.gameObject.activeSelf);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SushiSpots"));
        Directory.CreateDirectory(dir);

        var camGo = new GameObject("__SushiCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 40f;
        var lightGo = new GameObject("__SushiTorch") { hideFlags = HideFlags.HideAndDontSave };
        lightGo.transform.SetParent(camGo.transform, false);
        var torch = lightGo.AddComponent<Light>();
        torch.type = LightType.Spot; torch.range = 8f; torch.spotAngle = 55f; torch.intensity = 2.5f;
        var rt = new RenderTexture(960, 540, 24);
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        var probes = new List<GameObject>();
        try
        {
            foreach (var r in rooms) r.gameObject.SetActive(true);
            probes = AddProbes(rooms);
            for (int i = 0; i < list.spots.Length; i++)
            {
                var s = list.spots[i];
                if (!byId.TryGetValue(s.room, out var room)) continue;
                var inst = (GameObject)Object.Instantiate(prefab);
                inst.hideFlags = HideFlags.DontSave;
                SushiGame.Arrange(inst.transform, new[] { SushiGame.Kind.Tamago, SushiGame.Kind.Anago, SushiGame.Kind.Maguro,
                                                          SushiGame.Kind.Tai, SushiGame.Kind.Salmon, SushiGame.Kind.Ika });
                inst.transform.SetParent(room.transform, false);
                inst.transform.localPosition = new Vector3(s.pos[0], s.pos[1], s.pos[2]);
                inst.transform.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
                var p = inst.transform.position + Vector3.up * 0.04f;
                float floorY = room.transform.position.y;
                var fwd = inst.transform.right; fwd.y = 0f; fwd.Normalize();

                var vp = ViewPoint(inst.transform.position, fwd, floorY);
                var near = vp ?? new Vector3(p.x, floorY + Eye, p.z) + fwd * 1.0f;
                Snap(cam, torch, near, p, Path.Combine(dir, $"{i:00}_{s.room}_near{(vp == null ? "_NOVIEW" : "")}.png"), rt, tex, true);
                var entry = (room.EntrySpawn != null ? room.EntrySpawn.position : room.transform.position) + Vector3.up * Eye;
                Snap(cam, torch, entry, p, Path.Combine(dir, $"{i:00}_{s.room}_far.png"), rt, tex, false);
                Object.DestroyImmediate(inst);
            }
        }
        finally
        {
            foreach (var g in probes) if (g != null) Object.DestroyImmediate(g);
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            foreach (var kv in wasActive) if (kv.Key != null) kv.Key.gameObject.SetActive(kv.Value);
        }
        Debug.Log($"[SushiSpot] {list.spots.Length} か所を撮った: {dir}");
    }

    private static void Snap(Camera cam, Light torch, Vector3 eye, Vector3 at, string path, RenderTexture rt, Texture2D tex, bool torchOn)
    {
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation(at - eye, Vector3.up);
        cam.fieldOfView = torchOn ? 50f : 70f;
        torch.enabled = torchOn;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }
}
