using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// デバッグ用の配置ツール（シーンビュー。プレイ中も使える）。物を左ボタンで押さえたまま動かすと、床の上を滑って付いてくる。
    /// 壁や他の家具の当たり判定にはめり込まず、押し戻されて沿うように滑る。低い段差（Climb まで）は乗り越え、
    /// 端から外れれば下の面へ降りる（机の上の物を机の外へ出せば床に落ちる）。
    /// ・クリックした物の「家具ひとまとまり」（部屋の直下の物。机なら上の資料やランプごと）を動かす。
    ///   Ctrl を押しながらだと、そのひとまとまりの上に載っている物（机の上のランプなど）だけを動かす
    /// ・押さえている間はホイールで回す（15°刻み。Shift で 5°）
    /// ・離すと配置を記録する（LoopLayout）。シーンを生成し直しても記録した位置に置き直される。プレイ中に動かした物は、
    ///   プレイを止めた後のシーンにも反映する
    /// ・部屋の中なら、離した後に入口から出口まで歩いて通れるかを確かめ、ふさいでいたら知らせる
    /// 床・壁・天井・扉・部屋の外殻は動かさない
    /// </summary>
    [EditorTool("床を滑らせて置く")]
    public class FloorPlaceTool : EditorTool
    {
        /// <summary>乗り越える段差の高さ（m）。これより高い面（机の天板など）には乗らない</summary>
        private const float Climb = 0.35f;
        /// <summary>1回に動かす量（m）。壁を突き抜けないよう、細かく区切って押し戻しながら進める</summary>
        private const float SubStep = 0.04f;

        private static readonly string[] Fixed = { "Floor", "Ceiling", "Wall", "AccentBand", "Shell", "EntrySpawn", "ExitSpawn" };

        private GUIContent _icon;
        public override GUIContent toolbarIcon => _icon ??= new GUIContent("床置き", "床を滑らせて置く（デバッグ）");

        private Transform _unit;
        private Vector3 _grab;          // 押さえた所から物の基準点まで（水平）
        private float _baseY;           // 物の下端が乗っている面の高さ
        private int _undoGroup;
        private string _status = "";
        private bool _warn;
        private Transform _collectedFor;   // 当たり判定を集めた物
        private readonly List<Collider> _own = new List<Collider>();
        private readonly HashSet<Collider> _ownSet = new HashSet<Collider>();
        private readonly List<(MeshCollider src, BoxCollider box)> _proxies = new List<(MeshCollider, BoxCollider)>();
        private static readonly Collider[] Overlaps = new Collider[64];
        private readonly Dictionary<Collider, Vector3> _pushes = new Dictionary<Collider, Vector3>();
        private static readonly RaycastHit[] Sweeps = new RaycastHit[32];

        [MenuItem("Tools/EscapePrototype/配置/床を滑らせて置く")]
        private static void Activate() => ToolManager.SetActiveTool<FloorPlaceTool>();

        public override void OnWillBeDeactivated()
        {
            if (_unit != null) Release();
            ClearProxies();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);   // 何も無い所をクリックしても選択を外さない
                    break;
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt) break;
                    if (Begin(e.mousePosition, e.control || e.command)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    Drag(e.mousePosition);
                    e.Use();
                    break;
                case EventType.ScrollWheel:
                    if (GUIUtility.hotControl != id) break;
                    Rotate(e.delta.y > 0f ? 1f : -1f, e.shift ? 5f : 15f);
                    Drag(e.mousePosition);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id || e.button != 0) break;
                    GUIUtility.hotControl = 0;
                    Release();
                    e.Use();
                    break;
                case EventType.Repaint:
                    DrawOverlay(view);
                    break;
            }
        }

        // ============================== 押さえる・動かす・離す ==============================

        private bool Begin(Vector2 mouse, bool onlyTop)
        {
            var picked = HandleUtility.PickGameObject(mouse, false);
            if (picked == null) return false;
            var unit = FindUnit(picked.transform, onlyTop);
            if (unit == null) return false;
            CollectColliders(unit);
            if (!Movable(unit, out string why))
            {
                ClearProxies();
                _status = $"{unit.name}：{why}";
                _warn = true;
                return false;
            }
            Grab(unit);
            Selection.activeTransform = unit;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            _grab = new Plane(Vector3.up, new Vector3(0f, _baseY, 0f)).Raycast(ray, out float enter)
                ? unit.position - ray.GetPoint(enter) : Vector3.zero;
            _grab.y = 0f;
            return true;
        }

        /// <summary>動かし始める（当たり判定は CollectColliders 済みであること）。コードから試す時は Grab → SlideTo → Release</summary>
        public void Grab(Transform unit)
        {
            if (_collectedFor != unit) CollectColliders(unit);
            _unit = unit;
            HoldPushables(unit);
            _grab = Vector3.zero;
            _baseY = OwnBounds().min.y;
            if (!Application.isPlaying)
            {
                Undo.IncrementCurrentGroup();
                _undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(unit, "床を滑らせて置く");
            }
            _status = $"動かしている：{LoopLayout.IdOf(unit)}";
            _warn = false;
        }

        /// <summary>今の結果（記録した道のり・通れるかの知らせ）</summary>
        public string Status => _status;

        private readonly List<PushableProp> _held = new List<PushableProp>();

        /// <summary>
        /// プレイ中は、動かす物そのものと、動かす机に付いている押せる椅子を、掴んでいる間だけ物理から外す
        /// （離した時に、その位置から机と離れてよい距離を測り直す）
        /// </summary>
        private void HoldPushables(Transform unit)
        {
            if (!Application.isPlaying) return;
            foreach (var p in Object.FindObjectsByType<PushableProp>(FindObjectsSortMode.None))
                if (p.transform.IsChildOf(unit) || (p.Desk != null && p.Desk.IsChildOf(unit)))
                {
                    p.SetHeld(true);
                    _held.Add(p);
                }
        }

        private void Drag(Vector2 mouse)
        {
            if (_unit == null) return;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (!new Plane(Vector3.up, new Vector3(0f, _baseY, 0f)).Raycast(ray, out float enter)) return;
            SlideTo(ray.GetPoint(enter) + _grab);
        }

        /// <summary>基準点を target（水平の位置）へ向けて、当たり判定に沿って滑らせる</summary>
        public void SlideTo(Vector3 target)
        {
            if (_unit == null) return;
            if (!Application.isPlaying) Undo.RecordObject(_unit, "床を滑らせて置く");

            // 毎回 target へ向け直して少しずつ進む（家具に押されて回り込んだ後も、押さえている所へ向かう）
            int stalls = 0;
            for (int i = 0; i < 600; i++)
            {
                Vector3 before = _unit.position;
                Vector3 delta = target - before;
                delta.y = 0f;
                float left = delta.magnitude;
                if (left < 0.002f) break;
                _unit.position = before + delta * (Mathf.Min(SubStep, left) / left);
                Settle();
                // 壁に正面から押し付けていて進めない時は、そこで止める（沿って滑れる間は進む）
                Vector3 moved = _unit.position - before;
                moved.y = 0f;
                if (Vector3.Dot(moved, delta / left) < 0.0005f) { if (++stalls >= 3) break; }
                else stalls = 0;
            }
        }

        public void Rotate(float sign, float degrees)
        {
            if (_unit == null) return;
            if (!Application.isPlaying) Undo.RecordObject(_unit, "床を滑らせて置く");
            _unit.Rotate(0f, sign * degrees, 0f, Space.World);
            Settle();
        }

        /// <summary>離す：配置を記録し、部屋の中なら通れるかを確かめる</summary>
        public void Release()
        {
            ClearProxies();
            if (_unit == null) return;
            var unit = _unit;
            _unit = null;
            foreach (var p in _held) if (p != null) p.SetHeld(false);
            _held.Clear();
            if (!Application.isPlaying)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                EditorSceneManager.MarkSceneDirty(unit.gameObject.scene);
            }
            string id = LoopLayout.Record(unit);
            _status = $"記録した：{id}";
            _warn = false;
            var room = unit.GetComponentInParent<LoopRoomRoot>();
            if (room != null && !RoomPassage.EntryToExitOpen(room, out string msg))
            {
                _status += "\n" + msg;
                _warn = true;
                Debug.LogWarning($"[床を滑らせて置く] {room.Id}：{msg}（{id}）", unit);
            }
        }

        // ============================== 当たり判定 ==============================

        /// <summary>高さを足元の面に合わせ、他の物へのめり込みを横へ押し戻す</summary>
        private void Settle()
        {
            SyncProxies();
            Physics.SyncTransforms();

            // 1) 足元の形（少し小さめの薄い箱）を段差の高さから下ろし、最初に当たる上向きの面に乗せる
            var b = OwnBounds();
            var half = new Vector3(Mathf.Max(0.01f, b.extents.x * 0.8f), 0.005f, Mathf.Max(0.01f, b.extents.z * 0.8f));
            var from = new Vector3(b.center.x, _baseY + Climb, b.center.z);
            if (SupportBelow(from, half, Climb + 3f, out float surface))
            {
                _unit.position += Vector3.up * (surface - b.min.y);
                _baseY = surface;
                SyncProxies();
                Physics.SyncTransforms();
            }

            // 2) めり込んでいる分だけ横へ押し戻す（上向きの押し戻し＝乗っている面は数えない）
            for (int it = 0; it < 4; it++)
            {
                // 同じ相手に自分の判定がいくつ当たっていても、その相手からは一番大きい分だけ押し戻す（二重に押さない）
                _pushes.Clear();
                foreach (var c in _own)
                {
                    if (c == null) continue;
                    var cb = c.bounds;
                    int n = Physics.OverlapBoxNonAlloc(cb.center, cb.extents, Overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        var o = Overlaps[i];
                        if (o == null || _ownSet.Contains(o) || o is CharacterController) continue;
                        if (!Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                                o, o.transform.position, o.transform.rotation, out Vector3 dir, out float dist)) continue;
                        var h = new Vector3(dir.x, 0f, dir.z);
                        if (h.magnitude < 0.35f) continue;
                        var add = h.normalized * (dist * h.magnitude + 0.001f);
                        if (!_pushes.TryGetValue(o, out var had) || add.sqrMagnitude > had.sqrMagnitude) _pushes[o] = add;
                    }
                }
                Vector3 push = Vector3.zero;
                foreach (var v in _pushes.Values) push += v;
                if (push.sqrMagnitude < 1e-10f) break;
                _unit.position += push;
                SyncProxies();
                Physics.SyncTransforms();
            }
        }

        private bool SupportBelow(Vector3 from, Vector3 half, float dist, out float surface)
        {
            surface = 0f;
            int n = Physics.BoxCastNonAlloc(from, half, Vector3.down, Sweeps, Quaternion.identity, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Sweeps[i];
                // 自分・最初から重なっている物（高い家具の中から下ろし始めた時など）・壁の側面は数えない
                if (h.collider == null || _ownSet.Contains(h.collider) || h.collider is CharacterController) continue;
                if (h.distance <= 0f || h.normal.y < 0.5f || h.distance >= best) continue;
                best = h.distance;
                surface = h.point.y;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// 動かす物の当たり判定を集める。凹んだ形のメッシュの判定は押し戻しの計算に使えないので、
        /// 形を囲む箱の代わりを置く（見えない一時の物。シーンには保存されない）
        /// </summary>
        private void CollectColliders(Transform unit)
        {
            ClearProxies();
            _own.Clear();
            _ownSet.Clear();
            _collectedFor = unit;
            foreach (var c in unit.GetComponentsInChildren<Collider>(true))
            {
                _ownSet.Add(c);
                if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c is MeshCollider mc && !mc.convex)
                {
                    if (mc.sharedMesh == null) continue;
                    var go = new GameObject("__FloorPlaceProbe") { hideFlags = HideFlags.HideAndDontSave };
                    var box = go.AddComponent<BoxCollider>();
                    box.center = mc.sharedMesh.bounds.center;
                    box.size = mc.sharedMesh.bounds.size;
                    _proxies.Add((mc, box));
                    _ownSet.Add(box);
                    _own.Add(box);
                }
                else _own.Add(c);
            }
            SyncProxies();
            Physics.SyncTransforms();
        }

        private void SyncProxies()
        {
            foreach (var (src, box) in _proxies)
            {
                if (src == null || box == null) continue;
                var t = box.transform;
                t.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                t.localScale = src.transform.lossyScale;
            }
        }

        private void ClearProxies()
        {
            foreach (var (_, box) in _proxies)
                if (box != null) DestroyImmediate(box.gameObject);
            _proxies.Clear();
            _collectedFor = null;
        }

        /// <summary>動かす物の外形（当たり判定。無ければ見た目）</summary>
        private Bounds OwnBounds() => OwnBounds(_unit);

        private Bounds OwnBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var c in _own)
            {
                if (c == null) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (any || root == null) return b;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(root.position, Vector3.zero);
            return b;
        }

        // ============================== 何を動かすか ==============================

        /// <summary>
        /// 押さえた物の「家具ひとまとまり」：部屋（LoopRoomRoot）の直下の物、部屋の外なら最上位の入れ物の直下の物。
        /// onlyTop なら、そのひとまとまりの1つ下（机の上のランプなど）
        /// </summary>
        private static Transform FindUnit(Transform picked, bool onlyTop)
        {
            Transform unit = null;
            for (var p = picked; p != null; p = p.parent)
            {
                var parent = p.parent;
                if (parent == null || parent.GetComponent<LoopRoomRoot>() != null || parent.parent == null) { unit = p; break; }
            }
            if (unit == null || !onlyTop) return unit;
            for (var p = picked; p != null && p != unit; p = p.parent)
                if (p.parent == unit) return p;
            return unit;
        }

        private bool Movable(Transform u, out string why)
        {
            why = null;
            if (u.GetComponent<LoopRoomRoot>() != null) { why = "部屋そのものは動かせない"; return false; }
            foreach (var f in Fixed)
                if (u.name.StartsWith(f) || u.name.EndsWith("_" + f)) { why = "床・壁・天井・外殻は動かせない"; return false; }
            if (u.GetComponentInChildren<DoorSwing>(true) != null || u.GetComponentInChildren<LoopRoomDoor>(true) != null ||
                u.GetComponentInChildren<LoopDoor>(true) != null)
            { why = "扉は動かせない"; return false; }
            var b = OwnBounds(u);
            if (Mathf.Max(b.size.x, b.size.z) > 4f) { why = "大きすぎる（部屋の外殻や床の一部）"; return false; }
            return true;
        }

        // ============================== 画面の案内 ==============================

        private void DrawOverlay(SceneView view)
        {
            if (_unit != null)
            {
                var b = OwnBounds();
                Handles.color = new Color(0.73f, 0.57f, 0.31f, 0.9f);
                Handles.DrawWireCube(b.center, b.size);
            }
            Handles.BeginGUI();
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, richText = false, wordWrap = true };
            string help = "床を滑らせて置く：左ボタンで押さえて動かす／押さえたままホイールで回す（Shift で細かく）／Ctrl+押さえるで上に載っている物だけ";
            string text = string.IsNullOrEmpty(_status) ? help : help + "\n" + _status;
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(10f, 10f, Mathf.Min(size.x + 12f, view.position.width - 20f), 0f);
            rect.height = style.CalcHeight(new GUIContent(text), rect.width) + 4f;
            var prev = GUI.color;
            if (_warn) GUI.color = new Color(1f, 0.75f, 0.7f);
            GUI.Label(rect, text, style);
            GUI.color = prev;
            Handles.EndGUI();
        }
    }

    /// <summary>
    /// 部屋の入口から、出口・両方の扉の前まで、プレイヤーの体（半径0.38m・高さ0.36〜1.88m）が通れるかを 0.1m の格子で確かめる
    /// </summary>
    internal static class RoomPassage
    {
        private const float Cell = 0.1f, Radius = 0.38f, Low = 0.36f, Top = 1.5f;
        private static readonly Collider[] Hits = new Collider[16];

        public static bool EntryToExitOpen(LoopRoomRoot room, out string msg)
        {
            msg = null;
            if (room.EntrySpawn == null || room.ExitSpawn == null) return true;
            var floor = room.transform.Find("Floor");
            var fc = floor != null ? floor.GetComponent<Collider>() : null;
            if (fc == null) return true;
            Physics.SyncTransforms();
            var b = fc.bounds;
            float y = b.max.y;
            int nx = Mathf.Clamp(Mathf.CeilToInt(b.size.x / Cell), 1, 300), nz = Mathf.Clamp(Mathf.CeilToInt(b.size.z / Cell), 1, 300);
            var free = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var p = new Vector3(b.min.x + (i + 0.5f) * Cell, y, b.min.z + (k + 0.5f) * Cell);
                    free[i, k] = !Blocked(p);
                }
            if (!Nearest(free, b, room.EntrySpawn.position, out int si, out int sk))
            {
                msg = "入口から入った所に立てる所が無い";
                return false;
            }
            var seen = new bool[nx, nz];
            var q = new Queue<(int, int)>();
            q.Enqueue((si, sk));
            seen[si, sk] = true;
            while (q.Count > 0)
            {
                var (i, k) = q.Dequeue();
                for (int di = -1; di <= 1; di++)
                    for (int dk = -1; dk <= 1; dk++)
                    {
                        int a = i + di, c = k + dk;
                        if ((di == 0 && dk == 0) || a < 0 || c < 0 || a >= nx || c >= nz || seen[a, c] || !free[a, c]) continue;
                        seen[a, c] = true;
                        q.Enqueue((a, c));
                    }
            }
            // 行けなければならない所：出口から入った所と、両方の扉のすぐ前（扉を開けられる所に立てるか）
            var goals = new List<(Vector3 at, string what)> { (room.ExitSpawn.position, "出口から入った所") };
            foreach (var door in room.GetComponentsInChildren<LoopRoomDoor>(true))
            {
                var inward = room.transform.position - door.transform.position;
                inward.y = 0f;
                goals.Add((door.transform.position + inward.normalized * 0.55f, door.IsExitDoor ? "出口の扉の前" : "入口の扉の前"));
            }
            foreach (var (at, what) in goals)
            {
                if (Nearest(free, b, at, out int gi, out int gk) && seen[gi, gk]) continue;
                msg = $"入口から{what}まで歩いて行けない（体の幅 0.76m が通る隙間が無い）";
                return false;
            }
            return true;
        }

        private static bool Blocked(Vector3 foot)
        {
            int n = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * (Low + Radius), foot + Vector3.up * Top, Radius, Hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Hits[i] != null && !(Hits[i] is CharacterController) && !Hits[i].name.StartsWith("__FloorPlaceProbe")) return true;
            return false;
        }

        /// <summary>その位置から 0.6m 以内で一番近い、立てる所</summary>
        private static bool Nearest(bool[,] free, Bounds b, Vector3 at, out int bi, out int bk)
        {
            bi = bk = -1;
            float best = 0.6f * 0.6f;
            for (int i = 0; i < free.GetLength(0); i++)
                for (int k = 0; k < free.GetLength(1); k++)
                {
                    if (!free[i, k]) continue;
                    float dx = b.min.x + (i + 0.5f) * Cell - at.x, dz = b.min.z + (k + 0.5f) * Cell - at.z;
                    float d = dx * dx + dz * dz;
                    if (d <= best) { best = d; bi = i; bk = k; }
                }
            return bi >= 0;
        }
    }
}
