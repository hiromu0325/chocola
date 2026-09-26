using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 調べる画面の実物に、資料の文章を実際に載せる（紙の印字・手書き・画面の表示・カセットのラベル）。
    /// 載せる場所はモデルごとの「印字面」（Resources/InspectPrintAreas.json。Blender の hq/build_inspect.py が書き出す）。
    /// 文字は印字面に貼った World Space のキャンバスで描く（Inspect レイヤー＝調べる画面のカメラだけが描く）。
    /// </summary>
    public static class InspectPrint
    {
        [Serializable] private class Rect3 { public float[] center, size, normal, up; }
        [Serializable] private class Item { public string name; public Rect3[] rects; public float[] bounds; }
        [Serializable] private class FileData { public Item[] items; }

        private static Dictionary<string, Item> _areas;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _areas = null;

        private const float Unit = 0.0005f;   // キャンバスの1単位＝0.5mm（文字の大きさを細かく決められるように）

        private static Dictionary<string, Item> Areas
        {
            get
            {
                if (_areas != null) return _areas;
                _areas = new Dictionary<string, Item>();
                var ta = Resources.Load<TextAsset>("InspectPrintAreas");
                if (ta == null) return _areas;
                var data = JsonUtility.FromJson<FileData>(ta.text);
                if (data?.items != null)
                    foreach (var it in data.items)
                        if (it != null && !string.IsNullOrEmpty(it.name)) _areas[it.name] = it;
                return _areas;
            }
        }

        /// <summary>実物の印字面に、見出しと本文（または音声記録のラベル）を載せる</summary>
        public static void Apply(GameObject item, InspectView.Request req, float scale)
        {
            if (!Areas.TryGetValue(req.Info.Kind.ToString(), out var area) || area.rects == null || area.rects.Length == 0) return;
            string title = req.Title ?? "";
            string body = req.Info.Audio ? "" : (req.Body ?? "");
            var ink = req.Info.Ink;

            switch (req.Info.Kind)
            {
                case InspectKind.Cassette:
                    Put(item.transform, area.rects[0], "", title, InkStyle.Hand, 0, labelOnly: true);
                    return;
                case InspectKind.Recorder:
                    Put(item.transform, area.rects[0], "", "▶ VOICE 03", InkStyle.Screen, 0, labelOnly: true);
                    return;
                case InspectKind.Card:
                    Put(item.transform, area.rects[0], "", body.Replace("《職員証》", "").Trim(), ink, 0);
                    return;
                case InspectKind.Drawing:
                    // 子供の絵：クレヨンの言葉（「」の中）だけを大きく
                    int a = body.IndexOf('「'), b = body.LastIndexOf('」');
                    string words = a >= 0 && b > a ? body.Substring(a + 1, b - a - 1) : body;
                    Put(item.transform, area.rects[0], "", words, InkStyle.Crayon, 0, labelOnly: true);
                    return;
                case InspectKind.Monitor when req.Info.Audio:
                    Put(item.transform, area.rects[0], title, "▶ 再生待機中", InkStyle.Screen, 0);
                    return;
            }

            if (area.rects.Length >= 2)
            {
                // 見開き（手帳）：本文を左右のページに分ける
                var lines = body.Replace("\r", "").Split('\n');
                int half = Mathf.CeilToInt(lines.Length * 0.5f);
                Put(item.transform, area.rects[0], title, string.Join("\n", lines, 0, half), ink, 0);
                Put(item.transform, area.rects[1], "", string.Join("\n", lines, half, lines.Length - half), ink, 1);
            }
            else Put(item.transform, area.rects[0], title, body, ink, 0);
        }

        private static void Put(Transform item, Rect3 r, string title, string body, InkStyle ink, int index, bool labelOnly = false)
        {
            if (r?.center == null || r.size == null) return;
            var go = new GameObject("Print" + index, typeof(RectTransform));
            go.transform.SetParent(item, false);
            var rt = (RectTransform)go.transform;
            var n = V(r.normal, Vector3.back);
            var up = V(r.up, Vector3.up);
            rt.localPosition = V(r.center, Vector3.zero);
            rt.localRotation = Quaternion.LookRotation(-n, up);   // キャンバスの奥向き＝紙の裏側
            rt.localScale = Vector3.one * Unit;
            float w = r.size[0] / Unit, h = r.size[1] / Unit;
            rt.sizeDelta = new Vector2(w, h);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;   // 小さな文字でもにじまない程度に細かく（上げすぎると文字の画像置き場があふれる）

            var font = FontFor(ink);
            var color = InkColor(ink);
            float pad = Mathf.Min(w, h) * 0.06f;

            if (labelOnly)
            {
                var t = MakeText(rt, "Label", font, color, TextAnchor.MiddleCenter,
                                 Fit(body, w - pad, h - pad, 1.1f, (int)(h * 0.8f)));
                Stretch(t.rectTransform, pad * 0.5f);
                t.text = body;
                return;
            }

            float titleH = string.IsNullOrEmpty(title) ? 0f : Mathf.Clamp(h * 0.09f, 12f, 40f);
            if (titleH > 0f)
            {
                var tt = MakeText(rt, "Title", font, color, ink == InkStyle.Screen ? TextAnchor.UpperLeft : TextAnchor.UpperCenter,
                                  Fit(title, w - pad * 2f, titleH * 1.2f, 1f, (int)titleH));
                tt.rectTransform.anchorMin = new Vector2(0f, 1f); tt.rectTransform.anchorMax = new Vector2(1f, 1f);
                tt.rectTransform.pivot = new Vector2(0.5f, 1f);
                tt.rectTransform.offsetMin = new Vector2(pad, -pad - titleH * 1.3f);
                tt.rectTransform.offsetMax = new Vector2(-pad, -pad);
                tt.text = title;
                if (ink != InkStyle.Hand && ink != InkStyle.Crayon) tt.fontStyle = FontStyle.Bold;
            }
            float bodyH = h - pad * 2f - (titleH > 0f ? titleH * 1.6f : 0f);
            float spacing = ink == InkStyle.Hand ? 1.25f : 1.1f;
            int maxSize = ink == InkStyle.Crayon ? (int)(h * 0.2f) : (int)Mathf.Clamp(h * 0.055f, 8f, 24f);
            var bt = MakeText(rt, "Body", font, color, TextAnchor.UpperLeft, Fit(body, w - pad * 2f, bodyH, spacing, maxSize));
            Stretch(bt.rectTransform, pad);
            bt.rectTransform.offsetMax = new Vector2(-pad, -pad - (titleH > 0f ? titleH * 1.6f : 0f));
            bt.lineSpacing = spacing;
            if (ink == InkStyle.Crayon) bt.alignment = TextAnchor.MiddleCenter;
            bt.text = body;
        }

        /// <summary>印字の大きさ：最大から少しずつ小さくして、収まる大きさを1つだけ選ぶ（4段階）</summary>
        private static int Fit(string text, float w, float h, float spacing, int max)
        {
            max = Mathf.Max(3, max);
            return UiTheme.FitFontSize(text, w, h, spacing, max, Mathf.Max(3, max * 3 / 4), Mathf.Max(3, max / 2), Mathf.Max(3, max / 3));
        }

        private static Text MakeText(RectTransform parent, string name, Font font, Color color, TextAnchor anchor, int size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = false;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            t.fontSize = size;
            return t;
        }

        private static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        private static Vector3 V(float[] a, Vector3 fallback) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

        // ============================== 書体と色 ==============================

        private static Font FontFor(InkStyle ink)
        {
            switch (ink)
            {
                case InkStyle.Hand: return OsFont("UD Digi Kyokasho N-R", "UD デジタル 教科書体 N-R", "Yu Mincho");
                case InkStyle.Crayon: return OsFont("UD Digi Kyokasho N-B", "UD デジタル 教科書体 N-B", "Yu Gothic UI");
                case InkStyle.Screen: return OsFont("BIZ UDGothic", "MS Gothic", "Yu Gothic UI");
                default: return OsFont("Yu Mincho", "MS Mincho", "Yu Gothic UI");
            }
        }

        private static Font OsFont(params string[] names)
        {
            string key = string.Join("|", names);
            if (Fonts.TryGetValue(key, out var f) && f != null) return f;
            var installed = Font.GetOSInstalledFontNames();
            foreach (var n in names)
                if (Array.IndexOf(installed, n) >= 0)
                {
                    f = Font.CreateDynamicFontFromOSFont(n, 32);
                    if (f != null) break;
                }
            if (f == null) f = UiTheme.BodyFont;
            Fonts[key] = f;
            return f;
        }

        private static Color InkColor(InkStyle ink)
        {
            switch (ink)
            {
                case InkStyle.Hand: return new Color(0.05f, 0.07f, 0.16f, 1f);          // 青黒いボールペン
                case InkStyle.Crayon: return new Color(0.72f, 0.2f, 0.14f, 0.95f);      // 赤いクレヨン
                case InkStyle.Screen: return new Color(0.66f, 0.95f, 0.78f, 1f);        // 画面の緑がかった光
                default: return new Color(0.06f, 0.055f, 0.05f, 1f);                    // 印刷の黒
            }
        }
    }
}
