using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// UIの設計トークンと部品（全画面で共用）。
    ///
    /// 方針：ホラー。UIは「古い研究記録の余白」に置かれたような控えめな文字と細い罫線だけ。
    ///   墨の黒・骨の白・褪せた真鍮色の3色が基本、危険だけ乾いた血の赤。常時出す情報は最小にし、
    ///   変わった時だけゆっくり浮かび、ゆっくり消える。
    ///   やらないこと：角丸・グラデーション・発光・原色・常時表示の明るいパネル・装飾の多い枠。
    /// モチーフ：1本の細罫（Rule）。見出しの下、選択中の項目の左、帯の上下にだけ使う。
    /// </summary>
    public static class UiTheme
    {
        // ============================== 色 ==============================
        public static readonly Color Bg = Hex(0x0B0B0D);                   // 画面背景（メニュー）
        public static readonly Color Panel = Hex(0x111114, 0.94f);         // パネル面
        public static readonly Color PanelDim = Hex(0x1A1A1E, 0.94f);      // 非アクティブ面・ボタンの地
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f); // 文字の背後に敷く暗幕
        public static readonly Color Text = Hex(0xE6E1D6);                 // 主テキスト（骨の白）
        public static readonly Color TextSub = Hex(0x9C978C);              // 副テキスト（約65%）
        public static readonly Color TextFaint = Hex(0x5E5B55);            // 無効・注記
        public static readonly Color Accent = Hex(0xB9924F);               // 主アクセント（褪せた真鍮）
        public static readonly Color Danger = Hex(0xB23A2E);               // 危険・警報（乾いた血）
        public static readonly Color Positive = Hex(0x86A89A);             // 回復・完了（くすんだ青緑）
        public static readonly Color Focus = Hex(0xE6E1D6);                // フォーカス（アクセントと別に持つ）

        // ============================== 文字 ==============================
        public const int FsTitle = 96;     // タイトル
        public const int FsHeading = 44;   // 画面見出し
        public const int FsMenu = 30;      // メニュー項目
        public const int FsBody = 26;      // 本文
        public const int FsHud = 24;       // HUD（1080p基準で20px以上）
        public const int FsSmall = 20;     // 注記・ボタンガイド

        // ============================== 余白（1系列のみ） ==============================
        public const float Sp1 = 4f, Sp2 = 8f, Sp3 = 12f, Sp4 = 16f, Sp5 = 24f, Sp6 = 32f, Sp7 = 48f, Sp8 = 64f;
        /// <summary>細罫の太さ。1 だと 720p 以下で線が画素の間に落ちて消えることがあるので 2</summary>
        public const float Hairline = 2f;
        /// <summary>画面の端から UI を離す量（1920x1080 で 5% 相当）</summary>
        public const float SafeX = 96f, SafeY = 54f;

        // ============================== 動き ==============================
        public const float FadeIn = 0.18f, FadeOut = 0.3f, Hover = 0.1f;

        private static Font _body, _display;

        /// <summary>本文・HUD用（ゴシック）</summary>
        public static Font BodyFont => _body != null ? _body : (_body = FontProvider.Get());

        /// <summary>見出し用（明朝。無ければ本文と同じ）</summary>
        public static Font DisplayFont
        {
            get
            {
                if (_display != null) return _display;
                string[] candidates = { "Yu Mincho", "Yu Mincho Demibold", "YuMincho", "游明朝", "MS PMincho", "MS Mincho", "Noto Serif JP", "Noto Serif CJK JP" };
                var installed = Font.GetOSInstalledFontNames();
                foreach (var name in candidates)
                    if (Array.IndexOf(installed, name) >= 0)
                    {
                        _display = Font.CreateDynamicFontFromOSFont(name, 48);
                        if (_display != null) return _display;
                    }
                return _display = BodyFont;
            }
        }

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static string Rgb(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ============================== 部品 ==============================

        /// <summary>1920x1080 基準で拡縮するオーバーレイのキャンバス</summary>
        public static Canvas Canvas(Transform parent, string name, int order, bool raycast = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // 縦を基準に（横長・4:3でも文字の大きさが変わらない）
            if (raycast) go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Fill(Transform parent, string name, Color color, bool raycast = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>細罫（モチーフ）。横なら width×1、縦なら 1×height</summary>
        public static Image Rule(Transform parent, Color color, bool vertical = false)
        {
            var img = Fill(parent, "Rule", color);
            img.rectTransform.sizeDelta = vertical ? new Vector2(Hairline, 0f) : new Vector2(0f, Hairline);
            return img;
        }

        /// <summary>
        /// 文字。shadow=true なら明るい背景の上でも読めるよう影を付ける（HUD用）。
        /// 装飾の多い輪郭線（Outline）は使わない
        /// </summary>
        public static Text Label(Transform parent, string name, int size, TextAnchor anchor,
                                 Color? color = null, bool display = false, bool shadow = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = display ? DisplayFont : BodyFont;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color ?? Text;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.15f;
            if (shadow)
            {
                var s = go.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.85f);
                s.effectDistance = new Vector2(0f, -2f);
            }
            return t;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// メニューの項目ボタン。地は透明、選択中は左に細罫が伸び・文字が骨の白→真鍮・右へ少しずれる
        /// （色だけに頼らない3つの変化）。無効は薄く表示したまま（隠さない）
        /// </summary>
        public static Button MenuItem(Transform parent, string label, Action onClick, float width = 460f, float height = 56f,
                                      int fontSize = FsMenu)
        {
            var go = new GameObject("Item_" + label);
            go.transform.SetParent(parent, false);
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);   // 当たり判定だけ
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            if (onClick != null) btn.onClick.AddListener(() => { UiSound.Decide(); onClick(); });

            var text = Label(go.transform, "Label", fontSize, TextAnchor.MiddleLeft, Text, display: false);
            Stretch(text.rectTransform, left: 28f);
            text.text = label;

            var bar = Fill(go.transform, "Mark", Accent);
            Place(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 2f));

            go.AddComponent<UiItemFx>().Init(btn, text, bar.rectTransform);
            return btn;
        }

        /// <summary>
        /// 箱のボタン（テンキー・装置の決定／閉じる）。選択中は地が少し明るくなり、下辺に細罫が伸び、
        /// 文字が真鍮色になる（色だけに頼らない）
        /// </summary>
        public static Button BoxButton(Transform parent, string label, Vector2 size, int fontSize, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = PanelDim;
            ((RectTransform)go.transform).sizeDelta = size;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => { UiSound.Decide(); onClick(); });

            var text = Label(go.transform, "Label", fontSize, TextAnchor.MiddleCenter, Text, shadow: false);
            Stretch(text.rectTransform);
            text.text = label;

            var mark = Fill(go.transform, "Mark", Accent);
            mark.rectTransform.anchorMin = new Vector2(0f, 0f);
            mark.rectTransform.anchorMax = new Vector2(1f, 0f);
            mark.rectTransform.pivot = new Vector2(0.5f, 0f);
            mark.rectTransform.anchoredPosition = Vector2.zero;
            mark.rectTransform.sizeDelta = new Vector2(0f, 2f);
            go.AddComponent<UiItemFx>().InitBox(btn, text, mark.rectTransform, img);
            return btn;
        }

        /// <summary>上下キー・スティックで順に移る（端から端へは回り込む）</summary>
        public static void ChainVertical(System.Collections.Generic.IList<Selectable> items)
        {
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = items[(i - 1 + n) % n],
                    selectOnDown = items[(i + 1) % n],
                };
                items[i].navigation = nav;
            }
        }

        /// <summary>
        /// 設定の行（見出し＋細い軌道のスライダー＋数値）。左右キーで step ずつ動く。
        /// 選択中は見出しが真鍮色になり、左に細罫が伸びる（メニュー項目と同じ）
        /// </summary>
        public static UiSlider SliderRow(Transform parent, string label, float min, float max, float value, float step,
                                         Action<float> onChanged, out Text valueText, float width = 900f)
        {
            var row = new GameObject("Row_" + label);
            row.transform.SetParent(parent, false);
            var hit = row.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = false;   // 行のどこを押しても値が端へ飛ばないよう、当たり判定は軌道の周りだけ
            ((RectTransform)row.transform).sizeDelta = new Vector2(width, 56f);
            var le = row.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 56f;

            var name = Label(row.transform, "Label", FsMenu, TextAnchor.MiddleLeft);
            Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(300f, 56f));
            name.text = label;

            // 軌道（細い線）＋塗り＋つまみ
            var track = Fill(row.transform, "Track", WithAlpha(TextFaint, 0.8f), raycast: true);
            Place(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(340f, 0f), new Vector2(400f, 2f));
            var fillArea = Rect(track.transform, "FillArea");
            Stretch(fillArea);
            var fill = Fill(fillArea, "Fill", Accent);
            Stretch(fill.rectTransform);
            var handleArea = Rect(track.transform, "HandleArea");
            Stretch(handleArea);
            var handle = Fill(handleArea, "Handle", Text);
            handle.rectTransform.sizeDelta = new Vector2(4f, 26f);

            var slider = row.AddComponent<UiSlider>();
            slider.Step = step;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = hit;
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            // 細い軌道でもつかみやすいよう、当たり判定を上下に広げる
            var grab = Fill(track.transform, "Grab", new Color(0f, 0f, 0f, 0f), raycast: true);
            Stretch(grab.rectTransform, -8f, -8f, -20f, -20f);

            valueText = Label(row.transform, "Value", FsMenu, TextAnchor.MiddleRight);
            Place(valueText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(760f, 0f), new Vector2(120f, 56f));

            var mark = Fill(row.transform, "Mark", Accent);
            Place(mark.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 2f));
            row.AddComponent<UiItemFx>().Init(slider, name, mark.rectTransform);
            return slider;
        }

        /// <summary>小さなキー表記の枠（[E] など）。キーボードとゲームパッドで表記を変える</summary>
        public static string Key(string keyboard, string pad = null)
        {
            string k = UiInput.UsingPad && !string.IsNullOrEmpty(pad) ? pad : keyboard;
            return $"<color={Rgb(Accent)}>[{k}]</color>";
        }

        /// <summary>CanvasGroup をフェードする（unscaled。ポーズ中でも動く）</summary>
        public static IEnumerator FadeGroup(CanvasGroup g, float target, float dur)
        {
            float from = g.alpha;
            if (dur <= 0f) { g.alpha = target; yield break; }
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                k = target > from ? 1f - (1f - k) * (1f - k) * (1f - k) : k * k;   // 出る時は ease-out、消える時は ease-in
                g.alpha = Mathf.Lerp(from, target, k);
                yield return null;
            }
            g.alpha = target;
        }
    }

    /// <summary>ゲームパッドを最後に触ったか（ボタン表記の切り替え用）</summary>
    public static class UiInput
    {
        public static bool UsingPad
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var pad = UnityEngine.InputSystem.Gamepad.current;
                var kb = UnityEngine.InputSystem.Keyboard.current;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (pad == null) return false;
                double p = pad.lastUpdateTime;
                double k = Math.Max(kb != null ? kb.lastUpdateTime : 0, mouse != null ? mouse.lastUpdateTime : 0);
                return p > k;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>
    /// メニュー項目の見た目の状態（通常／ホバー・選択／押下／無効）。
    /// ホバーしたら選択状態にもして、マウスとゲームパッドで見た目を揃える
    /// </summary>
    public class UiItemFx : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Selectable _btn;
        private Text _text;
        private RectTransform _mark;
        private float _k, _press;
        private bool _selected, _down;
        private Vector2 _textBase;

        private Image _box;   // 箱のボタンの地（選択中は少し明るく）

        public void Init(Selectable b, Text t, RectTransform mark)
        {
            _btn = b; _text = t; _mark = mark;
            _textBase = t.rectTransform.offsetMin;
        }

        /// <summary>箱のボタン用：下辺の細罫が左右に伸び、地が少し明るくなる</summary>
        public void InitBox(Selectable b, Text t, RectTransform underline, Image box)
        {
            _btn = b; _text = t; _mark = underline; _box = box;
            _textBase = t.rectTransform.offsetMin;
            _mark.localScale = new Vector3(0f, 1f, 1f);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (_btn != null && _btn.interactable && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }
        public void OnSelect(BaseEventData e)
        {
            if (!_selected) UiSound.Move();
            _selected = true;
        }
        public void OnDeselect(BaseEventData e) { _selected = false; _down = false; }
        public void OnPointerDown(PointerEventData e) => _down = true;
        public void OnPointerUp(PointerEventData e) => _down = false;

        private void OnDisable() { _selected = false; _down = false; _k = 0f; Apply(); }

        private void Update()
        {
            bool on = _selected && _btn != null && _btn.interactable;
            _k = Mathf.MoveTowards(_k, on ? 1f : 0f, Time.unscaledDeltaTime / UiTheme.Hover);
            _press = Mathf.MoveTowards(_press, _down && on ? 1f : 0f, Time.unscaledDeltaTime / 0.06f);
            Apply();
        }

        private void Apply()
        {
            if (_text == null) return;
            bool enabled = _btn == null || _btn.interactable;
            float k = 1f - (1f - _k) * (1f - _k);
            _text.color = !enabled ? UiTheme.TextFaint : Color.Lerp(UiTheme.Text, UiTheme.Accent, k) * (1f - 0.15f * _press);
            if (_box != null)
            {
                _box.color = Color.Lerp(UiTheme.PanelDim, UiTheme.Hex(0x2A2723, 0.97f), k) * (1f - 0.2f * _press);
                if (_mark != null) _mark.localScale = new Vector3(k, 1f, 1f);
                return;
            }
            _text.rectTransform.offsetMin = _textBase + new Vector2(10f * k, 0f);
            if (_mark != null) _mark.sizeDelta = new Vector2(18f * k, 2f);
        }
    }

    /// <summary>UIの効果音（カーソル移動・決定・取り消し・誤り を別の音に）</summary>
    public static class UiSound
    {
        /// <summary>画面を開いた直後の自動選択では移動音を鳴らさない</summary>
        public static float MuteMoveUntil;

        private static Vector3 Ear => Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        public static void Move()
        {
            if (Time.unscaledTime < MuteMoveUntil) return;
            ProceduralAudio.PlayAt(ProceduralAudio.DialTick(), Ear, 0.18f, spatial: false);
        }
        public static void Decide() => ProceduralAudio.PlayAt(ProceduralAudio.Click(), Ear, 0.45f, spatial: false);
        public static void Cancel() => ProceduralAudio.PlayAt(ProceduralAudio.DoorLatch(), Ear, 0.2f, spatial: false);
        public static void Error() => ProceduralAudio.PlayAt(ProceduralAudio.Beep(), Ear, 0.35f, spatial: false);
    }

    /// <summary>左右キー・スティックで決まった刻み（Step）ずつ動くスライダー</summary>
    public class UiSlider : Slider
    {
        public float Step = 0.05f;

        public override void OnMove(AxisEventData e)
        {
            if (!IsActive() || !IsInteractable() ||
                (e.moveDir != MoveDirection.Left && e.moveDir != MoveDirection.Right))
            {
                base.OnMove(e);
                return;
            }
            value = Mathf.Clamp(value + (e.moveDir == MoveDirection.Right ? Step : -Step), minValue, maxValue);
            UiSound.Move();
            e.Use();
        }
    }
}
