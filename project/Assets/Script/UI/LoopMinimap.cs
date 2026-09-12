using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 回廊のミニマップ（画面右下）。四角い回廊と各部屋の扉を描き、
    /// 「どの扉が開いているか／今どこか／警報はどこか／断片が残っている部屋」を色で示す。
    ///   灰=未解放  白=開いている  黄=現在地  赤(点滅)=警報  緑=完了  桃=娘のおもちゃが残っている
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

        private static readonly Color Locked = new Color(0.35f, 0.35f, 0.38f, 0.9f);
        private static readonly Color Open = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color Current = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color Alarm = new Color(1f, 0.25f, 0.2f, 1f);
        private static readonly Color Done = new Color(0.45f, 0.85f, 0.5f, 1f);
        private static readonly Color Toy = new Color(1f, 0.6f, 0.8f, 1f);
        private static readonly Color Next = new Color(0.55f, 0.8f, 1f, 1f);

        private void Awake()
        {
            _font = FontProvider.Get();
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
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;
            _panel = bg.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(1f, 0f);
            _panel.anchoredPosition = new Vector2(-24f, 24f);
            _panel.sizeDelta = new Vector2(Size, Size + 34f);

            // 回廊の帯（内壁〜外壁の間）を4辺のリングで描く
            float inner = LoopCorridorLayout.InnerHalf * Scale, outer = LoopCorridorLayout.OuterHalf * Scale;
            float band = outer - inner;
            var ring = new Color(0.6f, 0.6f, 0.65f, 0.5f);
            MakeRect(_panel, new Vector2(0f, outer - band * 0.5f), new Vector2(outer * 2f, band), ring);      // 北
            MakeRect(_panel, new Vector2(0f, -outer + band * 0.5f), new Vector2(outer * 2f, band), ring);     // 南
            MakeRect(_panel, new Vector2(outer - band * 0.5f, 0f), new Vector2(band, outer * 2f), ring);      // 東
            MakeRect(_panel, new Vector2(-outer + band * 0.5f, 0f), new Vector2(band, outer * 2f), ring);     // 西
            var n = MakeLabel(_panel, "N", 14, new Vector2(0f, outer + 10f));
            n.color = new Color(0.8f, 0.8f, 0.85f);

            _player = MakeRect(_panel, Vector2.zero, new Vector2(8f, 8f), Current);
            _player.transform.SetAsLastSibling();

            _label = MakeLabel(_panel, "", 14, new Vector2(0f, -outer - 12f));
            _legend = MakeLabel(_panel, "", 11, new Vector2(0f, -outer - 26f));
            _legend.text = "<color=#FFD94C>■</color>現在地 <color=#F5F5F5>■</color>開 <color=#595960>■</color>未解放 <color=#73D980>■</color>完了 <color=#FF4033>■</color>警報";
            _legend.color = Color.white;
            _built = true;
        }

        private void Update()
        {
            if (!_built) return;
            var gm = GameManager.Instance;
            bool show = gm != null && gm.State == GameState.Playing && LoopProgress.NotebookOwned;
            if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
            if (!show) return;

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
                _label.text = next != null ? $"次: {next.Name}{LoopObjective.DoorHint(next)}" : "回廊";
            }
            _label.color = Color.white;
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
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos + new Vector2(0f, 17f);
            rt.sizeDelta = new Vector2(Size, 20f);
            return t;
        }
    }
}
