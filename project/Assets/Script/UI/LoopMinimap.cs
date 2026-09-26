using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 回廊のミニマップ（画面右下）。四角い回廊を細い線で描き、各部屋の扉を小さな印で示す。
    /// 回廊にいる時と警報中だけ出す（部屋の中では消える）。
    ///   印：暗い=未解放  灰=開いている  真鍮=次に行く部屋（娘のおもちゃが残る部屋）  青緑=完了  赤(点滅)=警報
    ///   骨の白の四角=自分
    /// </summary>
    public class LoopMinimap : MonoBehaviour
    {
        private const float Size = 230f;          // パネル一辺（px）
        private const float Scale = Size * 0.45f / LoopCorridorLayout.OuterHalf;   // m → px

        private RectTransform _panel;
        private Image _player;
        private Text _legend, _label;
        private readonly Dictionary<LoopRoomRoot, (Image entry, Image exit)> _marks = new Dictionary<LoopRoomRoot, (Image, Image)>();
        private Font _font;
        private bool _built;

        private static readonly Color Locked = UiTheme.WithAlpha(UiTheme.TextFaint, 0.8f);
        private static readonly Color Open = UiTheme.TextSub;
        private static readonly Color Current = UiTheme.Accent;
        private static readonly Color Alarm = UiTheme.Danger;
        private static readonly Color Done = UiTheme.WithAlpha(UiTheme.Positive, 0.85f);
        private static readonly Color Toy = UiTheme.Accent;
        private static readonly Color Next = UiTheme.Accent;
        private CanvasGroup _group;

        private void Awake()
        {
            _font = UiTheme.BodyFont;
            Build();
        }

        private void Build()
        {
            var canvasGo = new GameObject("MinimapCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            var bg = panelGo.AddComponent<Image>();
            bg.color = UiTheme.WithAlpha(Color.black, 0.35f);
            bg.raycastTarget = false;
            _panel = bg.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(1f, 0f);
            _panel.anchoredPosition = new Vector2(-UiTheme.SafeX, UiTheme.SafeY);
            _panel.sizeDelta = new Vector2(Size, Size + 34f);
            _group = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // 回廊の外壁と内壁を細い線で描く（面で塗らない）
            float inner = LoopCorridorLayout.InnerHalf * Scale, outer = LoopCorridorLayout.OuterHalf * Scale;
            var line = UiTheme.WithAlpha(UiTheme.TextSub, 0.35f);
            foreach (float h in new[] { outer, inner })
            {
                float w = UiTheme.Hairline;
                MakeRect(_panel, new Vector2(0f, h), new Vector2(h * 2f + w, w), line);
                MakeRect(_panel, new Vector2(0f, -h), new Vector2(h * 2f + w, w), line);
                MakeRect(_panel, new Vector2(h, 0f), new Vector2(w, h * 2f + w), line);
                MakeRect(_panel, new Vector2(-h, 0f), new Vector2(w, h * 2f + w), line);
            }
            var n = MakeLabel(_panel, "北", 16, new Vector2(0f, outer + 12f));
            n.color = UiTheme.TextSub;

            _player = MakeRect(_panel, Vector2.zero, new Vector2(8f, 8f), UiTheme.Text);
            _player.transform.SetAsLastSibling();

            _label = MakeLabel(_panel, "", 18, new Vector2(0f, -outer - 16f));
            _built = true;
        }

        private void Update()
        {
            if (!_built) return;
            var gm = GameManager.Instance;
            bool active = gm != null && gm.State == GameState.Playing && LoopProgress.NotebookOwned;
            if (_panel.gameObject.activeSelf != active) _panel.gameObject.SetActive(active);
            if (!active) return;
            // 回廊にいる時と警報中だけ出す（部屋の中の探索では画面を空ける）
            var bs0 = BreakerSystem.Instance;
            bool want = LoopRooms.InCorridor || (bs0 != null && bs0.DownRoomId != null);
            float target = want ? 1f : 0f;
            if (!Mathf.Approximately(_group.alpha, target))
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / (want ? 0.3f : 0.6f));

            // 部屋の印（遅延生成）
            foreach (var r in LoopRooms.All)
            {
                if (r == null || _marks.ContainsKey(r)) continue;
                var e = MakeRect(_panel, ToMap(LoopCorridorLayout.DoorPosition(r.Side, r.Slot)), new Vector2(9f, 9f), Locked);
                var x = MakeRect(_panel, ToMap(LoopCorridorLayout.DoorPosition((r.Side + 2) % 4, r.Slot)), new Vector2(6f, 6f), Locked);
                _marks[r] = (e, x);
                _player.transform.SetAsLastSibling();
            }

            var bs = BreakerSystem.Instance;
            var fin = LoopFinale.Instance;
            var next = LoopObjective.NextRoom();
            string cur = LoopRooms.CurrentRoomId;
            float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 8f);

            foreach (var kv in _marks)
            {
                var r = kv.Key;
                Color c;
                bool unlocked = r.UnlockStage <= LoopRooms.Stage;
                if (bs != null && bs.DownRoomId == r.Id) c = Alarm * blink + Color.black * (1f - blink);
                else if (cur == r.Id) c = Current;
                else if (!unlocked) c = Locked;
                else if (fin != null && fin.Started && !LoopProgress.IsFound(r.Id, "toy") && r.Id != "son_room") c = Toy;
                else if (LoopProgress.IsRoomComplete(r)) c = Done;
                else if (next == r) c = Next;
                else c = Open;
                kv.Value.entry.color = c;
                kv.Value.exit.color = new Color(c.r, c.g, c.b, c.a * 0.7f);
                // 開いている扉は少し大きく
                float s = unlocked ? 10f : 7f;
                kv.Value.entry.rectTransform.sizeDelta = new Vector2(s, s);
            }

            // プレイヤー：回廊なら実座標、部屋の中ならその部屋の入口扉の位置
            var player = GameObject.FindGameObjectWithTag("Player");
            var room = LoopRooms.Get(cur);
            if (room != null)
            {
                _player.rectTransform.anchoredPosition = ToMap(LoopCorridorLayout.DoorPosition(room.Side, room.Slot));
                _label.text = room.Name;
            }
            else if (player != null)
            {
                var p = player.transform.position;
                _player.rectTransform.anchoredPosition = new Vector2(
                    Mathf.Clamp(p.x, -LoopCorridorLayout.OuterHalf, LoopCorridorLayout.OuterHalf) * Scale,
                    Mathf.Clamp(p.z, -LoopCorridorLayout.OuterHalf, LoopCorridorLayout.OuterHalf) * Scale);
                _label.text = next != null ? $"次　{next.Name}" : "回廊";
            }
            _label.color = UiTheme.Text;
        }

        private static Vector2 ToMap(Vector3 world) => new Vector2(world.x * Scale, world.z * Scale);

        private static Image MakeRect(RectTransform parent, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("M");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos + new Vector2(0f, 17f);   // 下のラベル分だけ上寄せ
            rt.sizeDelta = size;
            return img;
        }

        private Text MakeLabel(RectTransform parent, string text, int size, Vector2 pos)
        {
            var go = new GameObject("L");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.text = text; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.supportRichText = true; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(0f, -2f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos + new Vector2(0f, 17f);
            rt.sizeDelta = new Vector2(Size, 24f);
            return t;
        }
    }
}
