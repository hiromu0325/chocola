using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EscapeProto
{
    /// <summary>
    /// HUD（実行時にUGUIをコード生成。アセット不要）。見た目は UiTheme の方針に従う。
    /// ・常時出すのは中央の小さな点だけ。目標（左上）は変わった時・部屋を移った時に浮かび、しばらくして消える
    ///   （警報中・アップロード中は出したまま）。人形（右上）は数が変わった時だけ出る
    /// ・調べられる物に向くと、点の下に操作の案内（[E] …）と長押しの進み具合
    /// ・タイトル・一時停止・終了画面では HUD を隠す
    /// ・手帳パネル / 死亡ホワイトアウト / 会話ダイアログ / 終了画面
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        public static HUDManager Instance { get; private set; }

        /// <summary>目標が変わってから出しておく秒数／部屋を移った時に出しておく秒数／人形の数が変わった時</summary>
        private const float ObjectiveHold = 8f, ObjectiveOnMove = 5f, DollsHold = 6f;

        private Font _font;
        private Text _stateText, _promptText, _endTitle, _endBody, _dialogText, _subtitle;
        private GameObject _memoPanel, _endPanel, _scarePanel, _dialogPanel;
        private NotebookUI _notebook;
        private RawImage _scareFace;
        private Image _scareFlash, _whiteout, _crosshair, _promptPlate, _subtitlePlate;
        private RectTransform _progressFill;
        private GameObject _progressRoot;
        private InteractionController _interaction;
        private PlayerStatus _player;

        // 目標・人形・案内
        private CanvasGroup _hudGroup, _objGroup, _dollsGroup, _promptGroup;
        private Text _objLabel, _objText, _dollsText;
        private Image _objPlate, _objRule;
        private string _objLast;
        private LoopObjective.Tone _objTone;
        private float _objUntil, _dollsUntil;
        private string _roomLast = "\0";
        private int _dollsLast = -1;
        private string _promptLast;
        private Component _promptTarget;   // 案内を出している物（縁取る）
        private InteractOutline _outline;
        private Button _endButton;

        private bool _memoOpen, _gameEnded;
        private Action<int> _dialogCallback;
        private int _dialogChoiceCount;

        private void Awake()
        {
            Instance = this;
            _font = UiTheme.BodyFont;
            BuildCanvas();
            gameObject.AddComponent<InspectView>();   // 資料を調べる画面（くるくる回す）
            _outline = gameObject.AddComponent<InteractOutline>();   // E で何かできる物の縁取り
        }

        private void Start()
        {
            _interaction = FindFirstObjectByType<InteractionController>();
            _player = FindFirstObjectByType<PlayerStatus>();
        }

        private void OnEnable()
        {
            GameEvents.OnJumpScare += PlayJumpScare;
            GameEvents.OnWhiteout += PlayWhiteout;
            GameEvents.OnGameOver += ShowGameOver;
            GameEvents.OnGameClear += ShowGameClear;
            GameEvents.OnGameStarted += HideEndPanel;
            GameEvents.OnLaughterEventStart += OnEventStart;
            GameEvents.OnLaughterEventEnd += OnEventEnd;
        }
        private void OnDisable()
        {
            GameEvents.OnJumpScare -= PlayJumpScare;
            GameEvents.OnWhiteout -= PlayWhiteout;
            GameEvents.OnGameOver -= ShowGameOver;
            GameEvents.OnGameClear -= ShowGameClear;
            GameEvents.OnGameStarted -= HideEndPanel;
            GameEvents.OnLaughterEventStart -= OnEventStart;
            GameEvents.OnLaughterEventEnd -= OnEventEnd;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // タイトル・一時停止・終了画面では HUD を隠す（状況はメニュー側が出す）
            var gm = GameManager.Instance;
            bool playing = (gm == null || gm.State == GameState.Playing) && !InspectView.IsOpen && !NotebookUI.IsOpen;
            Fade(_hudGroup, playing ? 1f : 0f, playing ? UiTheme.FadeIn : 0.12f);
            UpdateStatus();
            UpdatePrompt();
            // 案内が出ている物だけを縁取る（資料・手帳・一時停止などで HUD を隠している間は出さない）
            if (_outline != null) _outline.SetTarget(playing ? _promptTarget : null);
            HandleKeys();
        }

        /// <summary>CanvasGroup を目標の不透明度へ少しずつ寄せる（unscaled。ポーズ中でも動く）</summary>
        private static void Fade(CanvasGroup g, float target, float seconds)
        {
            if (g == null || Mathf.Approximately(g.alpha, target)) return;
            g.alpha = Mathf.MoveTowards(g.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
        }

        // ============= 入力 =============
        private void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (kb == null && gp == null) return;
            bool tab = kb != null && kb.tabKey.wasPressedThisFrame;
            bool restart = kb != null && kb.rKey.wasPressedThisFrame;
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (gp != null)
            {
                if (gp.selectButton.wasPressedThisFrame) tab = true;       // 手帳
                if (gp.startButton.wasPressedThisFrame) restart = true;    // 終了画面でリスタート
            }
            // 調べる画面が開いている間（と閉じた瞬間）は、Tab・Esc はそちらが受ける
            bool inspecting = InspectView.IsOpen || InspectView.ClosedThisFrame;
            if (tab && !inspecting) ToggleMemo();
            else if (esc && _memoOpen && !inspecting && !NotebookUI.DocWindowBusy) ToggleMemo();   // 引用元の文章を開いている時はそちらが閉じる
            if (_gameEnded && restart) GameManager.Instance?.RestartGame();
            if (_dialogPanel.activeSelf)
            {
                bool one = kb != null && kb.digit1Key.wasPressedThisFrame;
                bool two = kb != null && kb.digit2Key.wasPressedThisFrame;
                bool three = kb != null && kb.digit3Key.wasPressedThisFrame;
                if (gp != null)
                {
                    one |= gp.buttonSouth.wasPressedThisFrame;   // A
                    two |= gp.buttonEast.wasPressedThisFrame;    // B
                    three |= gp.buttonWest.wasPressedThisFrame;  // X
                }
                if (one) PickDialog(0);
                else if (two) PickDialog(1);
                else if (three) PickDialog(2);
            }
#else
            bool inspecting = InspectView.IsOpen || InspectView.ClosedThisFrame;
            if (Input.GetKeyDown(KeyCode.Tab) && !inspecting) ToggleMemo();
            else if (Input.GetKeyDown(KeyCode.Escape) && _memoOpen && !inspecting && !NotebookUI.DocWindowBusy) ToggleMemo();
            if (_gameEnded && Input.GetKeyDown(KeyCode.R)) GameManager.Instance?.RestartGame();
            if (_dialogPanel.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) PickDialog(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) PickDialog(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) PickDialog(2);
            }
#endif
        }

        public void ToggleMemo()
        {
            // ループ回廊：手帳は最初の部屋の机で拾うまで開けない
            //（回廊の部屋が登録されていないシーン＝施設マップ等では従来どおり）
            if (!_memoOpen &&
                LoopRooms.Get(LoopProgress.StartRoomId) != null && !LoopProgress.NotebookOwned)
            {
                ToastUI.Show("手帳を持っていない（最初の部屋の机にあったはず）");
                return;
            }

            var gm = GameManager.Instance;
            if (!_memoOpen && gm != null && gm.State != GameState.Playing) return;
            _memoOpen = !_memoOpen;
            _memoPanel.SetActive(_memoOpen);
            if (_memoOpen)
            {
                _notebook.Show();              // 今いる部屋の章から開く
                gm?.SetBusy(true);             // 手帳を開いている間は歩かない（カーソルを出す）
                UiSound.Decide();
            }
            else
            {
                _notebook.Hide();
                gm?.SetBusy(false);
                UiSound.Cancel();
            }
        }

        /// <summary>
        /// 調べる画面から手帳の「メモ」だけを開く（閉じると onClosed → 調べる画面へ戻る）。
        /// 手帳を持っていなければ開かない（false）
        /// </summary>
        public bool OpenMemoOverlay(Action onClosed)
        {
            if (LoopRooms.Get(LoopProgress.StartRoomId) != null && !LoopProgress.NotebookOwned)
            {
                ToastUI.Show("手帳を持っていない（最初の部屋の机にあったはず）");
                UiSound.Error();
                return false;
            }
            _memoPanel.SetActive(true);
            _notebook.ShowOverlay(() =>
            {
                if (!_memoOpen) _memoPanel.SetActive(false);   // 手帳から資料を開いていた時は手帳を残す
                onClosed?.Invoke();
            });
            UiSound.Decide();
            return true;
        }

        public void CloseMemoOverlay() => _notebook.CloseOverlay();

        // ============= 目標・人形 =============
        private static readonly System.Text.RegularExpressions.Regex Digits =
            new System.Text.RegularExpressions.Regex("[0-9０-９%％]");
        private static readonly System.Text.RegularExpressions.Regex KeyTag =
            new System.Text.RegularExpressions.Regex(@"\[([^\]]{1,8})\]");

        private void UpdateStatus()
        {
            float now = Time.unscaledTime;
            var gm = GameManager.Instance;

            // ---- 目標（左上） ----
            string text;
            var tone = LoopObjective.Tone.Normal;
            if (LoopObjective.IsLoopScene) text = LoopObjective.Plain(out tone);
            else text = ObjectiveText();
            if (text != _objLast || tone != _objTone)
            {
                // 数字だけの変化（アップロードの％など）では出し直さない
                bool meaningful = _objLast == null || tone != _objTone ||
                                  Digits.Replace(text, "") != Digits.Replace(_objLast, "");
                SetObjective(text, tone);
                if (meaningful) _objUntil = now + ObjectiveHold;
            }
            string room = LoopRooms.CurrentRoomId ?? "";
            if (room != _roomLast) { _roomLast = room; _objUntil = Mathf.Max(_objUntil, now + ObjectiveOnMove); }
            var fin = LoopFinale.Instance;
            bool urgent = tone == LoopObjective.Tone.Warning || (fin != null && fin.Uploading && !fin.Completed);
            bool showObj = !string.IsNullOrEmpty(text) && (urgent || now < _objUntil);
            Fade(_objGroup, showObj ? 1f : 0f, showObj ? 0.35f : 0.9f);

            // ---- 人形（右上）：数が変わった時だけ ----
            int dolls = gm != null ? gm.Dolls : 0;
            if (dolls != _dollsLast)
            {
                if (_dollsLast >= 0) _dollsUntil = now + DollsHold;
                _dollsLast = dolls;
                _dollsText.text = DollGlyphs(dolls, 5);
            }
            bool showDolls = now < _dollsUntil;
            Fade(_dollsGroup, showDolls ? 1f : 0f, showDolls ? 0.3f : 0.9f);

            // ---- 旧施設マップの状態（回廊のシーンでは出さない） ----
            string st = "";
            if (!LoopObjective.IsLoopScene)
            {
                var pm = PhaseManager.Instance;
                string phase = (pm != null && pm.EventActive) ? $"<color={UiTheme.Rgb(UiTheme.Danger)}>…笑い声がする</color>　" : "";
                string lights = (RoomLightController.Instance == null || RoomLightController.Instance.LightsOn) ? "電気 点" : "電気 消";
                string flash = (_player != null && _player.FlashlightOn) ? "懐中電灯 点" : "懐中電灯 消";
                string scent = (_player != null && _player.IsScentMasked) ? "　消臭中" : "";
                st = $"{phase}{lights}　{flash}{scent}";
            }
            if (_stateText.text != st) _stateText.text = st;
        }

        /// <summary>残りの人形は ◆、砕けた人形は ◇（色だけでなく形でも分かるように）</summary>
        public static string DollGlyphs(int left, int total)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < total; i++)
                sb.Append(i < left ? "◆" : $"<color={UiTheme.Rgb(UiTheme.TextFaint)}>◇</color>");
            return sb.ToString();
        }

        private void SetObjective(string text, LoopObjective.Tone tone)
        {
            _objLast = text;
            _objTone = tone;
            bool warn = tone == LoopObjective.Tone.Warning;
            bool done = tone == LoopObjective.Tone.Done;
            _objLabel.text = warn ? "警報" : done ? "完了" : "目標";
            _objLabel.color = warn ? UiTheme.Danger : done ? UiTheme.Positive : UiTheme.Accent;
            _objRule.color = _objLabel.color;
            _objText.text = HighlightKeys(text ?? "");
            // 文の長さに合わせて暗幕の大きさを変える
            float w = Mathf.Min(_objText.preferredWidth, 760f);
            float h = _objText.preferredHeight;
            _objPlate.rectTransform.sizeDelta = new Vector2(w + 48f, h + 62f);
        }

        /// <summary>[E] のような操作キーを真鍮色に</summary>
        private static string HighlightKeys(string s) =>
            KeyTag.Replace(s, $"<color={UiTheme.Rgb(UiTheme.Accent)}>[$1]</color>");

        /// <summary>旧施設マップの目標</summary>
        private static string ObjectiveText()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "";
            if (!ps.PcAccessed) return "社員情報を集めPCにログイン";
            if (!ps.HasPowerRoomKey) return "貸出記録の社員の個室(2階)で鍵を探す";
            if (!ps.PowerRestored) return "配電室を開け配電盤を復旧";
            return "脱出口へ向かえ";
        }

        private void UpdatePrompt()
        {
            string prompt = ""; float progress = -1f;
            var target = _interaction != null ? _interaction.GetCurrentInteractable() : null;
            if (target is IPromptProvider p) { prompt = p.GetPrompt(); progress = p.GetProgress01(); }
            else if (target != null && target.CanInteract) prompt = "[E] 使う";

            if (prompt != _promptLast)
            {
                _promptLast = prompt;
                if (!string.IsNullOrEmpty(prompt))
                {
                    _promptText.text = HighlightKeys(prompt);
                    _promptPlate.rectTransform.sizeDelta = new Vector2(_promptText.preferredWidth + 48f, 46f);
                }
            }
            bool has = !string.IsNullOrEmpty(prompt);
            _promptTarget = has ? target as Component : null;
            Fade(_promptGroup, has ? 1f : 0f, has ? 0.1f : 0.2f);
            // 調べられる物に向いている時は点も真鍮色に（案内の文と合わせて2つの手がかり）
            var want = has ? UiTheme.WithAlpha(UiTheme.Accent, 0.9f) : UiTheme.WithAlpha(UiTheme.Text, 0.5f);
            if (_crosshair.color != want) _crosshair.color = want;
            bool showBar = progress > 0.001f;
            if (_progressRoot.activeSelf != showBar) _progressRoot.SetActive(showBar);
            if (showBar) _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        // ============= 会話ダイアログ =============
        public void ShowDialogue(string speaker, string body, string[] choices, Action<int> onChoice)
        {
            _dialogCallback = onChoice;
            _dialogChoiceCount = choices != null ? choices.Length : 0;
            string c = "";
            if (choices != null)
                for (int i = 0; i < choices.Length; i++)
                    c += $"\n<color={UiTheme.Rgb(UiTheme.Accent)}>[{i + 1}]</color> {choices[i]}";
            _dialogText.text = $"<color={UiTheme.Rgb(UiTheme.TextSub)}>{speaker}</color>\n\n{body}\n{c}";
            _dialogPanel.SetActive(true);
        }

        public void HideDialogue()
        {
            _dialogPanel.SetActive(false);
            _dialogCallback = null;
        }

        private void PickDialog(int idx)
        {
            if (idx >= _dialogChoiceCount) return;
            var cb = _dialogCallback;
            HideDialogue();
            cb?.Invoke(idx);
        }

        /// <summary>画面下部に字幕を表示（数秒で消える）</summary>
        public void ShowSubtitle(string text, float seconds = 3f)
        {
            StopCoroutine(nameof(SubtitleRoutine));
            StartCoroutine(SubtitleRoutine(text, seconds));
        }
        private IEnumerator SubtitleRoutine(string text, float seconds)
        {
            _subtitle.text = text;
            _subtitlePlate.gameObject.SetActive(true);
            _subtitlePlate.rectTransform.sizeDelta = new Vector2(Mathf.Min(_subtitle.preferredWidth, 1400f) + 64f,
                                                                 _subtitle.preferredHeight + 28f);
            yield return new WaitForSeconds(seconds);
            if (_subtitle.text == text) { _subtitle.text = ""; _subtitlePlate.gameObject.SetActive(false); }
        }

        // ============= イベント演出 =============
        private void OnEventStart()
        {
            ProceduralAudio.PlayAt(ProceduralAudio.Laugh(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, 0.9f, false);
            ShowSubtitle("…どこかで女の子が笑っている。時計が止まった。", 5f);
        }
        private void OnEventEnd() => HideDialogue();

        // ============= ジャンプスケア =============
        private Coroutine _scareCoroutine;
        private Vector3 _camBaseLocal;
        private bool _camBaseCaptured;

        private void PlayJumpScare(float intensity)
        {
            if (!HorrorSettings.JumpScares) return;   // 恐怖演出の軽減・なし（デバッグ）
            if (_scareCoroutine != null) StopCoroutine(_scareCoroutine);
            _scareCoroutine = StartCoroutine(ScareRoutine(intensity));
        }

        private IEnumerator ScareRoutine(float intensity)
        {
            _scarePanel.SetActive(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Scream(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, intensity, false);

            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null && !_camBaseCaptured) { _camBaseLocal = cam.localPosition; _camBaseCaptured = true; }
            Vector3 camLocal = _camBaseCaptured ? _camBaseLocal : Vector3.zero;

            float dur = 0.55f + intensity * 0.35f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime; float k = 1f - t / dur;
                _scareFlash.color = new Color(0.6f, 0f, 0f, 0.55f * k);
                _scareFace.color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 2f));
                _scareFace.rectTransform.localScale = Vector3.one * (1f + UnityEngine.Random.value * 0.12f * intensity);
                if (cam != null) cam.localPosition = camLocal + (Vector3)UnityEngine.Random.insideUnitCircle * 0.06f * intensity * k;
                yield return null;
            }
            if (cam != null) cam.localPosition = camLocal;
            _scarePanel.SetActive(false);
            _scareCoroutine = null;
        }

        // ============= 死亡ホワイトアウト =============
        private void PlayWhiteout(float intensity)
        {
            StopCoroutine(nameof(WhiteoutRoutine));
            StartCoroutine(WhiteoutRoutine());
        }
        private IEnumerator WhiteoutRoutine()
        {
            // 一瞬で白へ → ゆっくり戻る（再開しない＝GameOver時はそのまま白を残す）
            _whiteout.gameObject.SetActive(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Scream(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, 0.7f, false);
            float t = 0f;
            while (t < 0.15f) { t += Time.deltaTime; _whiteout.color = new Color(1, 1, 1, t / 0.15f); yield return null; }
            _whiteout.color = Color.white;

            // ゲームオーバーなら白いまま終了演出へ任せる
            if (_gameEnded) yield break;
            yield return new WaitForSeconds(1.0f);
            t = 0f;
            while (t < 0.6f) { t += Time.deltaTime; _whiteout.color = new Color(1, 1, 1, 1f - t / 0.6f); yield return null; }
            _whiteout.gameObject.SetActive(false);
        }

        private void ShowGameOver()
        {
            _gameEnded = true;
            ShowEnd(UiTheme.Danger, "そして誰もいなくなった", "陶器の人形は、すべて砕けた。");
        }

        /// <summary>クリア/ゲームオーバー後にタイトルから「はじめから」「つづきから」した時、終了パネルを消す</summary>
        private void HideEndPanel()
        {
            _gameEnded = false;
            if (_endPanel != null) _endPanel.SetActive(false);
        }

        private void ShowGameClear()
        {
            _gameEnded = true;
            if (LoopObjective.IsLoopScene)
                ShowEnd(UiTheme.Text, "アップロード完了", "娘の記憶の断片は、すべてリナシータへ送られた。\n（終章の分岐 END A/B/C は未実装）");
            else
                ShowEnd(UiTheme.Text, "脱出", "地下室から脱出した。");
        }

        private void ShowEnd(Color titleColor, string title, string body)
        {
            _endTitle.text = title;
            _endTitle.color = titleColor;
            _endBody.text = body;
            _endPanel.SetActive(true);
            StartCoroutine(SelectEndButton());
        }

        /// <summary>ホワイトアウトが明けてからボタンを選ぶ（ゲームパッドでも押せるように）</summary>
        private IEnumerator SelectEndButton()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _endPanel.activeSelf)
            {
                UiSound.MuteMoveUntil = Time.unscaledTime + 0.1f;
                es.SetSelectedGameObject(_endButton.gameObject);
            }
        }

        // ============= UI構築 =============
        private void BuildCanvas()
        {
            var canvas = UiTheme.Canvas(transform, "HUDCanvas", 0);
            var root = canvas.transform;
            EnsureEventSystem();

            // ---- 探索中の HUD（まとめて隠せるように1つの親に） ----
            var hud = UiTheme.Rect(root, "Hud");
            UiTheme.Stretch(hud);
            _hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            _hudGroup.blocksRaycasts = false;
            _hudGroup.interactable = false;

            _crosshair = UiTheme.Fill(hud, "Crosshair", UiTheme.WithAlpha(UiTheme.Text, 0.5f));
            UiTheme.Place(_crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4, 4));

            // 目標（左上。セーフエリアの内側）
            var obj = UiTheme.Rect(hud, "Objective");
            UiTheme.Place(obj, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(UiTheme.SafeX, -UiTheme.SafeY), new Vector2(760f, 120f));
            _objGroup = obj.gameObject.AddComponent<CanvasGroup>();
            _objGroup.alpha = 0f;
            _objPlate = UiTheme.Fill(obj, "Scrim", UiTheme.WithAlpha(Color.black, 0.7f));   // 回廊の白壁の前でも読める濃さ
            UiTheme.Place(_objPlate.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-24f, 18f), new Vector2(400f, 90f));
            _objRule = UiTheme.Fill(obj, "Rule", UiTheme.Accent);
            UiTheme.Place(_objRule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -13f), new Vector2(24f, 2f));
            _objLabel = UiTheme.Label(obj, "Label", UiTheme.FsSmall, TextAnchor.MiddleLeft, UiTheme.Accent);
            UiTheme.Place(_objLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, 0f), new Vector2(200f, 26f));
            _objText = UiTheme.Label(obj, "Text", UiTheme.FsHud, TextAnchor.UpperLeft);
            UiTheme.Place(_objText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -34f), new Vector2(760f, 80f));
            _objText.lineSpacing = 1.3f;

            // 人形（右上）。数が変わった時だけ
            var dolls = UiTheme.Rect(hud, "Dolls");
            UiTheme.Place(dolls, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiTheme.SafeX, -UiTheme.SafeY), new Vector2(300f, 60f));
            _dollsGroup = dolls.gameObject.AddComponent<CanvasGroup>();
            _dollsGroup.alpha = 0f;
            var dl = UiTheme.Label(dolls, "Label", UiTheme.FsSmall, TextAnchor.UpperRight, UiTheme.TextSub);
            UiTheme.Place(dl.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(300f, 26f));
            dl.text = "陶器の人形";
            _dollsText = UiTheme.Label(dolls, "Glyphs", UiTheme.FsHud, TextAnchor.UpperRight);
            UiTheme.Place(_dollsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), new Vector2(300f, 32f));

            // 操作の案内（点の下）＋長押しの進み具合
            var prompt = UiTheme.Rect(hud, "Prompt");
            UiTheme.Place(prompt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(1200f, 46f));
            _promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            _promptGroup.alpha = 0f;
            _promptPlate = UiTheme.Fill(prompt, "Scrim", UiTheme.WithAlpha(Color.black, 0.7f));
            UiTheme.Place(_promptPlate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 46f));
            _promptText = UiTheme.Label(prompt, "Text", UiTheme.FsHud, TextAnchor.MiddleCenter);
            UiTheme.Stretch(_promptText.rectTransform);
            _promptText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _progressRoot = UiTheme.Rect(prompt, "Progress").gameObject;
            var prt = (RectTransform)_progressRoot.transform;
            UiTheme.Place(prt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -36f), new Vector2(280f, 3f));
            var track = UiTheme.Fill(prt, "Track", UiTheme.WithAlpha(UiTheme.TextFaint, 0.8f));
            UiTheme.Stretch(track.rectTransform);
            var fill = UiTheme.Fill(prt, "Fill", UiTheme.Accent);
            _progressFill = fill.rectTransform;
            _progressFill.anchorMin = Vector2.zero; _progressFill.anchorMax = new Vector2(0, 1);
            _progressFill.offsetMin = Vector2.zero; _progressFill.offsetMax = Vector2.zero;
            _progressFill.pivot = new Vector2(0, 0.5f);
            _progressRoot.SetActive(false);

            // 旧施設マップ用の状態表示（回廊のシーンでは空）
            _stateText = UiTheme.Label(hud, "StateBar", UiTheme.FsSmall, TextAnchor.UpperRight, UiTheme.TextSub);
            UiTheme.Place(_stateText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiTheme.SafeX, -UiTheme.SafeY - 70f), new Vector2(700f, 30f));

            // 字幕（下中央。暗幕つき）
            _subtitlePlate = UiTheme.Fill(root, "SubtitleScrim", UiTheme.WithAlpha(Color.black, 0.7f));
            UiTheme.Place(_subtitlePlate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 110f), new Vector2(600f, 60f));
            _subtitlePlate.gameObject.SetActive(false);
            _subtitle = UiTheme.Label(root, "Subtitle", UiTheme.FsBody, TextAnchor.MiddleCenter);
            UiTheme.Place(_subtitle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 110f), new Vector2(1400f, 80f));

            // 手帳（見開き2ページ。左右クリックでページ送り）
            // ※専用キャンバス（sortingOrder=60）に載せ、PuzzleUI（50）より前面に出す。
            //   社内PCのテンキー等を開いたままTabで手帳を重ねて確認できる（数字入力はキーボードで可能）
            var memoCanvas = UiTheme.Canvas(transform, "MemoCanvas", 60);
            var memoDim = UiTheme.Fill(memoCanvas.transform, "Dim", UiTheme.WithAlpha(Color.black, 0.5f));
            UiTheme.Stretch(memoDim.rectTransform);
            _memoPanel = memoDim.gameObject;
            var memoBg = UiTheme.Fill(_memoPanel.transform, "Book", UiTheme.Panel, raycast: true);
            UiTheme.Place(memoBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 860));

            // 見出しは右上（左上から並ぶ章のタブと重ならないように）
            var memoTitle = UiTheme.Label(memoBg.transform, "MemoTitle", 26, TextAnchor.UpperRight, UiTheme.TextSub, display: true);
            UiTheme.Place(memoTitle.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32, -18), new Vector2(300, 40));
            memoTitle.text = "手 帳";

            // 資料の一覧とメモ（ラインマーカーで切り取った文）は NotebookUI が担当
            _notebook = memoBg.gameObject.AddComponent<NotebookUI>();
            _notebook.Init((RectTransform)memoBg.transform);
            _notebook.RequestClose = () => { if (_memoOpen) ToggleMemo(); };

            _memoPanel.SetActive(false);

            // 会話ダイアログ（旧施設マップ）
            _dialogPanel = UiTheme.Fill(root, "DialogPanel", UiTheme.Panel, raycast: true).gameObject;
            UiTheme.Place((RectTransform)_dialogPanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(1200, 360));
            var dRule = UiTheme.Fill(_dialogPanel.transform, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.6f));
            UiTheme.Place(dRule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1200, UiTheme.Hairline));
            _dialogText = UiTheme.Label(_dialogPanel.transform, "DialogText", UiTheme.FsBody, TextAnchor.UpperLeft);
            UiTheme.Place(_dialogText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120, 300));
            _dialogPanel.SetActive(false);

            // ジャンプスケア
            _scarePanel = new GameObject("ScarePanel"); _scarePanel.transform.SetParent(root, false);
            _scareFlash = _scarePanel.AddComponent<Image>(); _scareFlash.color = Color.clear; _scareFlash.raycastTarget = false;
            UiTheme.Stretch(_scareFlash.rectTransform);
            var faceGo = new GameObject("Face"); faceGo.transform.SetParent(_scarePanel.transform, false);
            _scareFace = faceGo.AddComponent<RawImage>(); _scareFace.texture = BuildScareFaceTexture(); _scareFace.raycastTarget = false;
            UiTheme.Place(_scareFace.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
            _scarePanel.SetActive(false);

            // ホワイトアウト
            _whiteout = UiTheme.Fill(root, "Whiteout", Color.clear);
            UiTheme.Stretch(_whiteout.rectTransform);
            _whiteout.gameObject.SetActive(false);

            // 終了画面（ゲームオーバー／クリア）
            _endPanel = UiTheme.Fill(root, "EndPanel", UiTheme.WithAlpha(UiTheme.Bg, 0.9f), raycast: true).gameObject;
            UiTheme.Stretch((RectTransform)_endPanel.transform);
            _endTitle = UiTheme.Label(_endPanel.transform, "Title", 64, TextAnchor.LowerCenter, UiTheme.Text, display: true);
            UiTheme.Place(_endTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(1400, 100));
            var endRule = UiTheme.Fill(_endPanel.transform, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.7f));
            UiTheme.Place(endRule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 66), new Vector2(360, UiTheme.Hairline));
            _endBody = UiTheme.Label(_endPanel.transform, "Body", UiTheme.FsBody, TextAnchor.UpperCenter, UiTheme.TextSub);
            UiTheme.Place(_endBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, 40), new Vector2(1200, 120));
            _endBody.lineSpacing = 1.6f;
            _endButton = UiTheme.MenuItem(_endPanel.transform, "タイトルへ戻る", () => GameManager.Instance?.RestartGame(), 360f);
            UiTheme.Place((RectTransform)_endButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(360, 56));
            var endGuide = UiTheme.Label(_endPanel.transform, "Guide", UiTheme.FsSmall, TextAnchor.LowerRight, UiTheme.TextSub);
            UiTheme.Place(endGuide.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiTheme.SafeX, UiTheme.SafeY), new Vector2(800, 30));
            endGuide.text = $"{UiTheme.Key("Enter")} 決定　{UiTheme.Key("R")} リスタート";
            _endPanel.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        private static Texture2D BuildScareFaceTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - size * 0.5f) / (size * 0.5f), ny = (y - size * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    Color c = Color.clear;
                    if (r < 0.95f) { float s = Mathf.Clamp01(1f - r) * 0.25f; c = new Color(s, s * 0.85f, s * 0.8f, 1f); }
                    c = DrawEye(c, nx, ny, -0.35f, 0.25f);
                    c = DrawEye(c, nx, ny, 0.35f, 0.25f);
                    if (ny < -0.25f && ny > -0.45f)
                    {
                        float mouth = Mathf.Abs(nx) - (0.55f - Mathf.Abs(ny + 0.35f) * 2.2f);
                        float jag = Mathf.PerlinNoise(x * 0.25f, 0f) * 0.08f;
                        if (mouth + jag < 0f) c = new Color(0.05f, 0f, 0f, 1f);
                    }
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        private static Color DrawEye(Color b, float nx, float ny, float cx, float cy)
        {
            float dx = (nx - cx) / 0.22f, dy = (ny - cy) / 0.30f, d = dx * dx + dy * dy;
            if (d < 1f) b = new Color(0.95f, 0.93f, 0.9f, 1f);
            if (d < 0.06f) b = new Color(0.02f, 0f, 0f, 1f);
            return b;
        }
    }
}
