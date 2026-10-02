using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// デバッグパネル（F1で開閉。エディタと開発ビルドのみ）。
    /// ・恐怖演出の強さ（通常／軽減／なし）
    /// ・章の途中（部屋ごと・終章の2段階）から始める
    /// ・今の部屋を完了／ブレイカー復旧／全部屋を開ける
    /// タイトル画面でもプレイ中でも使える。シーンを読み直しても残る（DontDestroyOnLoad）。
    /// ※クラス名とファイル名の一致が必須
    /// </summary>
    public class DebugPanel : MonoBehaviour
    {
        private static DebugPanel _instance;

        private bool _open;
        private bool _tookControl;               // 開いた時にプレイヤー操作を止めたか
        private UnityEngine.EventSystems.EventSystem _mutedEvents;   // 開いている間は uGUI へのクリックを止める
        private Vector2 _scroll;
        private string _lastResult = "";
        private int _pendingFrames;
        private List<DebugStart.Point> _points;
        private GUIStyle _box, _label, _small, _button, _on, _head;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (!Debug.isDebugBuild || _instance != null) return;
            var go = new GameObject("DebugPanel");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DebugPanel>();
        }

        private void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            _points = null;           // 部屋の登録し直しに合わせて作り直す
            _tookControl = false;
            _mutedEvents = null;
            _pendingFrames = 2;       // GameManager がタイトル画面にするのを待ってから始める
        }

        private void Update()
        {
            if (TogglePressed()) SetOpen(!_open);

            // 読み直し後に途中から始める
            if (DebugStart.Pending != null)
            {
                if (_pendingFrames > 0) { _pendingFrames--; return; }
                var gm = GameManager.Instance;
                if (gm != null && gm.State == GameState.Title && RoomTransitionSystem.Instance != null)
                    _lastResult = DebugStart.ApplyPending();
            }
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.f1Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1);
#endif
        }

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            var gm = GameManager.Instance;
            if (open)
            {
                // 探索中に開いたら操作を止めてカーソルを出す（資料・手帳・扉の出入りで既に止まっている時はそのまま）
                var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
                _tookControl = gm != null && gm.State == GameState.Playing && fpc != null && fpc.enabled;
                if (_tookControl) gm.SetBusy(true);
                _mutedEvents = UnityEngine.EventSystems.EventSystem.current;
                if (_mutedEvents != null) _mutedEvents.enabled = false;
            }
            else
            {
                if (_tookControl && gm != null) gm.SetBusy(false);
                _tookControl = false;
                if (_mutedEvents != null) _mutedEvents.enabled = true;
                _mutedEvents = null;
            }
        }

        // ============================== 描画（IMGUI：開発用の道具なので見た目は最小限） ==============================

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float h = Screen.height / scale;

            if (!_open)
            {
                // 恐怖演出を変えている時だけ、左下に小さく出しておく（通常のまま遊んでいると勘違いしないように）
                if (HorrorSettings.Level != HorrorLevel.Full)
                    GUI.Label(new Rect(12, h - 30, 400, 24),
                        $"DEBUG  恐怖演出: {HorrorSettings.Label(HorrorSettings.Level)}　[F1]", _small);
                return;
            }

            var area = new Rect(16, 16, 480, h - 32);
            GUI.Box(area, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(area.x + 14, area.y + 12, area.width - 28, area.height - 24));
            GUILayout.Label("デバッグ　（F1で閉じる）", _head);
            GUILayout.Space(6);

            // ---- 恐怖演出 ----
            GUILayout.Label("恐怖演出", _label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 3; i++)
            {
                var lv = (HorrorLevel)i;
                if (GUILayout.Button(HorrorSettings.Label(lv), lv == HorrorSettings.Level ? _on : _button, GUILayout.Height(32)))
                    _lastResult = DebugLoop.Horror(i);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(HorrorSettings.Describe(HorrorSettings.Level), _small);
            GUILayout.Space(10);

            // ---- 途中から始める ----
            GUILayout.Label("途中から始める（前の部屋は完了済み・起床の場面は飛ばす）", _label);
            if (_points == null || _points.Count == 0) _points = DebugStart.Points();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            string chapter = null;
            foreach (var p in _points)
            {
                if (p.Chapter != chapter)
                {
                    chapter = p.Chapter;
                    GUILayout.Space(4);
                    GUILayout.Label(chapter, _small);
                }
                bool here = GameManager.Instance != null && GameManager.Instance.State != GameState.Title &&
                            p.Id == LoopRooms.CurrentRoomId;
                if (GUILayout.Button(p.Label, here ? _on : _button, GUILayout.Height(28)))
                {
                    _lastResult = DebugStart.StartAt(p.Id);
                    SetOpen(false);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);

            // ---- その場の操作 ----
            var gm = GameManager.Instance;
            bool playing = gm != null && gm.State != GameState.Title;
            GUI.enabled = playing;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("この部屋を完了", _button, GUILayout.Height(28)))
                _lastResult = LoopRooms.InCorridor ? "回廊にいる" : DebugLoop.Complete(LoopRooms.CurrentRoomId);
            if (GUILayout.Button("ブレイカー復旧", _button, GUILayout.Height(28)))
                _lastResult = DebugLoop.Raise();
            if (GUILayout.Button("全部屋を開ける", _button, GUILayout.Height(28)))
                _lastResult = DebugLoop.UnlockAll();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            string loc = LoopRooms.InCorridor ? "回廊" : LoopRooms.CurrentRoomId;
            var bs = BreakerSystem.Instance;
            GUILayout.Label($"段階 {LoopRooms.Stage}　現在地 {loc}　停電 {(bs != null && bs.DownRoomId != null ? bs.DownRoomId : "-")}", _small);
            if (!string.IsNullOrEmpty(SushiGame.Where)) GUILayout.Label($"隠し寿司の置き場所: {SushiGame.Where}", _small);
            if (!string.IsNullOrEmpty(_lastResult)) GUILayout.Label(_lastResult, _small);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            // 単色のテクスチャはシーン読み直し（未使用アセットの解放）で消えないよう HideAndDontSave にし、消えていたら作り直す
            if (_box != null && _box.normal.background != null) return;
            var font = FontProvider.Get();
            var bg = Solid(new Color(0.04f, 0.04f, 0.05f, 0.92f));
            var btn = Solid(new Color(0.16f, 0.16f, 0.18f, 1f));
            var hover = Solid(new Color(0.26f, 0.26f, 0.29f, 1f));
            var sel = Solid(new Color(0.55f, 0.36f, 0.12f, 1f));
            var text = new Color(0.9f, 0.88f, 0.84f);

            _box = new GUIStyle(GUI.skin.box);
            _box.normal.background = bg;
            _label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15 };
            _label.normal.textColor = new Color(0.92f, 0.9f, 0.86f);
            _small = new GUIStyle(_label) { fontSize = 13, wordWrap = true };
            _small.normal.textColor = new Color(0.7f, 0.68f, 0.64f);
            _head = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _button = new GUIStyle(GUI.skin.button)
            {
                font = font, fontSize = 14, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 4, 4), border = new RectOffset(0, 0, 0, 0),
            };
            _button.normal.background = btn; _button.normal.textColor = text;
            _button.hover.background = hover; _button.hover.textColor = Color.white;
            _button.active.background = sel; _button.active.textColor = Color.white;
            _on = new GUIStyle(_button);
            _on.normal.background = sel; _on.normal.textColor = Color.white;
            _on.hover.background = sel; _on.hover.textColor = Color.white;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
