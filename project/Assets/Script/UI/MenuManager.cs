using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EscapeProto
{
    /// <summary>
    /// タイトル / 一時停止 / オプションのメニューUI（実行時にUGUIをコード生成。見た目は UiTheme）。
    /// ・タイトル：はじめから / つづきから / オプション / 終了
    /// ・一時停止（Esc）：再開 / オプション / 記録する / タイトルへ戻る。ゲーム画面は暗くして後ろに残す
    /// ・オプション：音量・マウス感度・左右反転・上下反転（GameSettingsへ反映・保存）
    /// 左寄せの縦並び。選択中の項目は左に細罫が伸び、文字が真鍮色になり、少し右へずれる。
    /// 状態は GameManager が管理し、本クラスは表示の切替と入力受付のみ行う。
    /// </summary>
    public class MenuManager : MonoBehaviour
    {
        private const float ColumnX = 160f;   // 左の列の文字の開始位置（セーフエリアの内側）

        private GameObject _titlePanel, _pausePanel, _optionsPanel, _loadPanel;
        private Button _loadAuto, _loadManual, _loadBack;
        private Text _loadAutoInfo, _loadManualInfo;
        private bool _loadOpen;   // 「つづきから」の記録を選ぶ画面
        private Button _continueButton;
        private Text _continueNote, _saveInfoText, _pauseStatusText, _pausePlace, _pauseObjective, _pauseDolls, _guide;
        private Slider _volumeSlider, _sensSlider;
        private Text _volumeValue, _sensValue;
        private Text _invertXValue, _invertYValue, _rubyValue;
        private GameObject _guideRoot;
        private bool _guidePad;

        private bool _optionsOpen;
        private GameState _state = GameState.Title;

        private void Awake()
        {
            BuildUI();
            Refresh();
        }

        private void OnEnable() => GameEvents.OnGameStateChanged += HandleStateChanged;
        private void OnDisable() => GameEvents.OnGameStateChanged -= HandleStateChanged;

        private void HandleStateChanged(GameState s)
        {
            _state = s;
            if (s == GameState.Playing || s == GameState.Ended) { _optionsOpen = false; _loadOpen = false; }
            Refresh();
        }

        private void Update()
        {
            // 資料/コード入力中は PuzzleUI が Esc を処理するので競合させない
            if (PuzzleUI.Instance != null && PuzzleUI.Instance.IsOpen) return;
            // 調べる画面・手帳は自分で Esc を受ける（閉じる／戻る）
            if (InspectView.IsOpen || NotebookUI.IsOpen || InspectView.ClosedThisFrame) return;

            if (WasEscPressed())
            {
                if (_optionsOpen) { UiSound.Cancel(); CloseOptions(); return; }
                if (_loadOpen) { UiSound.Cancel(); CloseLoad(); return; }
                if (_state == GameState.Playing) GameManager.Instance?.Pause();
                else if (_state == GameState.Paused) { UiSound.Cancel(); GameManager.Instance?.Resume(); }
            }

            // キーボード／ゲームパッドで操作ガイドの表記を切り替える
            bool pad = UiInput.UsingPad;
            if (pad != _guidePad) { _guidePad = pad; UpdateGuide(); }
        }

        private bool WasEscPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame)) return true;
            return false;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        /// <summary>ゲームパッドでも選べるよう、表示中パネルの先頭の項目を選択状態にする（その時は移動音を鳴らさない）</summary>
        private void SelectFirst(GameObject panel)
        {
            if (panel == null) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return;
            Selectable first = null;
            foreach (var s in panel.GetComponentsInChildren<Selectable>(false))
                if (s.interactable) { first = s; break; }
            UiSound.MuteMoveUntil = Time.unscaledTime + 0.1f;
            es.SetSelectedGameObject(first != null ? first.gameObject : null);
        }

        // ============================== 表示制御 ==============================

        private void Refresh()
        {
            bool load = _state == GameState.Title && _loadOpen && !_optionsOpen;
            bool title = _state == GameState.Title && !_optionsOpen && !_loadOpen;
            bool pause = _state == GameState.Paused && !_optionsOpen;

            _titlePanel.SetActive(title);
            _pausePanel.SetActive(pause);
            _optionsPanel.SetActive(_optionsOpen);
            _loadPanel.SetActive(load);
            _guideRoot.SetActive(title || pause || _optionsOpen || load);

            if (title)
            {
                var d = SaveSystem.LoadLatest(out var slot);
                bool has = d != null && d.valid;
                _continueButton.interactable = has;
                // 無効の項目は隠さずに薄く出し、理由を隣に書く
                _continueNote.text = has ? "" : "記録がありません";
                _saveInfoText.text = has ? $"最後の記録　{SlotName(slot)}　{SaveInfo(d)}" : "";
            }
            if (load) RefreshLoad();
            if (pause) RefreshPause();
            UpdateGuide();

            // ゲームパッド操作用：表示中パネルの先頭ボタンを選択
            if (_optionsOpen) SelectFirst(_optionsPanel);
            else if (load) SelectFirst(_loadPanel);
            else if (title) SelectFirst(_titlePanel);
            else if (pause) SelectFirst(_pausePanel);
        }

        /// <summary>一時停止の画面に今の状況（場所・目標・人形）を出す</summary>
        private void RefreshPause()
        {
            _pauseStatusText.text = "";
            var room = LoopRooms.Get(LoopRooms.CurrentRoomId);
            string place = room != null ? room.Name : LoopObjective.IsLoopScene ? "回廊" : "";
            string chapter = room != null ? room.ChapterLabel : null;
            _pausePlace.text = string.IsNullOrEmpty(chapter) ? place : $"{chapter.Replace("　", " ")}　／　{place}";
            // 一時停止中は GameManager.State が Paused なので、目標は直前の値を HUD と同じ規則で作り直す
            _pauseObjective.text = LoopObjective.IsLoopScene ? ObjectiveWhilePaused() : "";
            var g = GameManager.Instance;
            _pauseDolls.text = g != null ? $"陶器の人形　{HUDManager.DollGlyphs(g.Dolls, 5)}" : "";
        }

        /// <summary>一時停止中の目標（HUD と同じ文。警報中は「警報」）</summary>
        private static string ObjectiveWhilePaused()
        {
            string s = LoopObjective.Plain(out var tone, anyState: true);
            if (string.IsNullOrEmpty(s)) return "";
            var c = tone == LoopObjective.Tone.Warning ? UiTheme.Danger : UiTheme.Accent;
            return $"<color={UiTheme.Rgb(c)}>{(tone == LoopObjective.Tone.Warning ? "警報" : "目標")}</color>　{s}";
        }

        private void UpdateGuide()
        {
            if (_guide == null) return;
            string back = _state == GameState.Paused && !_optionsOpen ? "再開" : "戻る";
            _guide.text = _state == GameState.Title && !_optionsOpen && !_loadOpen
                ? $"{UiTheme.Key("Enter", "A")} 決定"
                : $"{UiTheme.Key("Enter", "A")} 決定　　{UiTheme.Key("Esc", "B")} {back}";
        }

        private void OpenOptions()
        {
            _volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            _sensSlider.SetValueWithoutNotify(GameSettings.Sensitivity);
            UpdateOptionLabels();
            _optionsOpen = true;
            Refresh();
        }

        private void CloseOptions()
        {
            GameSettings.Save();
            _optionsOpen = false;
            Refresh();
        }

        // ============================== ボタン処理 ==============================

        private void OnNewGame() => GameManager.Instance?.NewGame();
        /// <summary>「つづきから」：オートとマニュアルのどちらの記録から始めるか選ぶ画面を開く</summary>
        private void OnContinue()
        {
            _loadOpen = true;
            Refresh();
        }

        private void CloseLoad()
        {
            _loadOpen = false;
            Refresh();
        }

        private void LoadFrom(SaveSlot slot)
        {
            _loadOpen = false;
            GameManager.Instance?.ContinueGame(slot);
        }

        /// <summary>記録を選ぶ画面：それぞれの記録の日時・場所・人形を出し、無い記録は薄くして選べなくする</summary>
        private void RefreshLoad()
        {
            var auto = SaveSystem.Load(SaveSlot.Auto);
            var manual = SaveSystem.Load(SaveSlot.Manual);
            _loadAuto.interactable = auto != null;
            _loadManual.interactable = manual != null;
            _loadAutoInfo.text = SaveInfo(auto);
            _loadManualInfo.text = SaveInfo(manual);
            // 選べる項目だけを上下でつなぐ（無い記録で止まらないように）
            var items = new List<Selectable>();
            if (_loadAuto.interactable) items.Add(_loadAuto);
            if (_loadManual.interactable) items.Add(_loadManual);
            items.Add(_loadBack);
            UiTheme.ChainVertical(items);
        }

        private static string SlotName(SaveSlot slot) => slot == SaveSlot.Auto ? "オート" : "マニュアル";

        /// <summary>記録の一行の説明（日時・場所・残りの人形）。無ければ「記録がありません」</summary>
        private static string SaveInfo(SaveData d)
        {
            if (d == null) return "記録がありません";
            string place = string.IsNullOrEmpty(d.place) ? "" : $"　{d.place}";
            return $"{d.savedAt}{place}　陶器の人形 {d.dolls}";
        }
        private void OnResume() => GameManager.Instance?.Resume();
        private void OnQuitToTitle() => GameManager.Instance?.QuitToTitle();

        private void OnSave()
        {
            GameManager.Instance?.SaveNow();
            if (_pauseStatusText != null)
                _pauseStatusText.text = $"<color={UiTheme.Rgb(UiTheme.Positive)}>記録しました</color>";
        }

        private void OnQuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ============================== UI構築 ==============================

        private void BuildUI()
        {
            var canvas = UiTheme.Canvas(transform, "MenuCanvas", 100);   // HUDより前面
            EnsureEventSystem();

            BuildTitlePanel(canvas.transform);
            BuildPausePanel(canvas.transform);
            BuildOptionsPanel(canvas.transform);
            BuildLoadPanel(canvas.transform);

            // 操作ガイド（右下。全メニュー共通）
            _guideRoot = UiTheme.Rect(canvas.transform, "Guide").gameObject;
            UiTheme.Stretch((RectTransform)_guideRoot.transform);
            _guide = UiTheme.Label(_guideRoot.transform, "Text", UiTheme.FsSmall, TextAnchor.LowerRight, UiTheme.TextSub);
            UiTheme.Place(_guide.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiTheme.SafeX, UiTheme.SafeY), new Vector2(900, 30));
        }

        /// <summary>メニューの地：ゲーム画面を暗くして後ろに残す</summary>
        private static GameObject Backdrop(Transform parent, string name, float dim)
        {
            var bg = UiTheme.Fill(parent, name, UiTheme.WithAlpha(UiTheme.Bg, dim), raycast: true);
            UiTheme.Stretch(bg.rectTransform);
            return bg.gameObject;
        }

        /// <summary>見出し（明朝）＋その下の細罫</summary>
        private static void Heading(Transform parent, string text, float y, int size)
        {
            var t = UiTheme.Label(parent, "Heading", size, TextAnchor.LowerLeft, UiTheme.Text, display: true);
            UiTheme.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(ColumnX, y), new Vector2(760f, size * 1.6f));
            t.text = text;
            var rule = UiTheme.Fill(parent, "HeadingRule", UiTheme.WithAlpha(UiTheme.Accent, 0.75f));
            UiTheme.Place(rule.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ColumnX, y - 20f), new Vector2(120f, UiTheme.Hairline));
        }

        /// <summary>縦に並ぶ項目の入れ物（上端を y に合わせる）</summary>
        private static RectTransform Column(Transform parent, float y, float width = 520f)
        {
            var col = UiTheme.Rect(parent, "Items");
            UiTheme.Place(col, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX - 28f, y), new Vector2(width, 400f));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = UiTheme.Sp2;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            return col;
        }

        private void BuildTitlePanel(Transform parent)
        {
            _titlePanel = Backdrop(parent, "TitlePanel", 0.72f);
            var p = _titlePanel.transform;

            var title = UiTheme.Label(p, "Title", UiTheme.FsTitle, TextAnchor.LowerLeft, UiTheme.Text, display: true);
            UiTheme.Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(ColumnX - 6f, 170f), new Vector2(1000f, 140f));
            title.text = "R E N A S C I T A";
            var rule = UiTheme.Fill(p, "TitleRule", UiTheme.WithAlpha(UiTheme.Accent, 0.8f));
            UiTheme.Place(rule.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ColumnX, 150f), new Vector2(180f, UiTheme.Hairline));
            var sub = UiTheme.Label(p, "Sub", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub);
            UiTheme.Place(sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, 132f), new Vector2(700f, 30f));
            sub.text = "リ ナ シ ー タ";

            var col = Column(p, 40f);
            var items = new List<Selectable>
            {
                UiTheme.MenuItem(col, "はじめから", OnNewGame),
                (_continueButton = UiTheme.MenuItem(col, "つづきから", OnContinue)),
                UiTheme.MenuItem(col, "オプション", OpenOptions),
                UiTheme.MenuItem(col, "終了", OnQuitApp),
            };
            UiTheme.ChainVertical(items);
            // 「つづきから」が押せない理由（項目の右に）
            _continueNote = UiTheme.Label(_continueButton.transform, "Note", UiTheme.FsSmall, TextAnchor.MiddleLeft, UiTheme.TextFaint);
            UiTheme.Place(_continueNote.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210f, 0f), new Vector2(300f, 40f));

            _saveInfoText = UiTheme.Label(p, "SaveInfo", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub);
            UiTheme.Place(_saveInfoText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, -236f), new Vector2(700f, 30f));

            var hint = UiTheme.Label(p, "Controls", UiTheme.FsSmall, TextAnchor.LowerLeft, UiTheme.TextFaint);
            UiTheme.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ColumnX, UiTheme.SafeY), new Vector2(1100f, 30f));
            hint.text = "WASD 移動　Shift 走る　E 調べる（長押し）　F 懐中電灯　Tab 手帳　Esc 一時停止";
        }

        private void BuildPausePanel(Transform parent)
        {
            _pausePanel = Backdrop(parent, "PausePanel", 0.72f);
            var p = _pausePanel.transform;
            Heading(p, "一時停止", 250f, UiTheme.FsHeading + 8);

            _pausePlace = UiTheme.Label(p, "Place", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub);
            UiTheme.Place(_pausePlace.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, 200f), new Vector2(620f, 30f));
            _pauseObjective = UiTheme.Label(p, "Objective", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.Text);
            UiTheme.Place(_pauseObjective.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, 166f), new Vector2(620f, 60f));
            _pauseObjective.lineSpacing = 1.3f;

            var col = Column(p, 60f);
            var items = new List<Selectable>
            {
                UiTheme.MenuItem(col, "再開", OnResume),
                UiTheme.MenuItem(col, "オプション", OpenOptions),
                UiTheme.MenuItem(col, "記録する", OnSave),
                UiTheme.MenuItem(col, "タイトルへ戻る", OnQuitToTitle),
            };
            UiTheme.ChainVertical(items);

            _pauseStatusText = UiTheme.Label(p, "Status", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub);
            UiTheme.Place(_pauseStatusText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, -210f), new Vector2(620f, 30f));
            _pauseDolls = UiTheme.Label(p, "Dolls", UiTheme.FsSmall, TextAnchor.LowerLeft, UiTheme.TextSub);
            UiTheme.Place(_pauseDolls.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ColumnX, UiTheme.SafeY), new Vector2(620f, 30f));
        }

        private void BuildOptionsPanel(Transform parent)
        {
            _optionsPanel = Backdrop(parent, "OptionsPanel", 0.82f);
            var p = _optionsPanel.transform;
            Heading(p, "オプション", 250f, UiTheme.FsHeading + 8);

            var col = Column(p, 150f, 900f);
            _volumeSlider = UiTheme.SliderRow(col, "音量", 0f, 1f, GameSettings.MasterVolume, 0.05f, v =>
            {
                GameSettings.MasterVolume = v; GameSettings.Apply(); UpdateOptionLabels();
            }, out _volumeValue);
            _sensSlider = UiTheme.SliderRow(col, "マウス感度", GameSettings.SensMin, GameSettings.SensMax, GameSettings.Sensitivity, 0.005f, v =>
            {
                GameSettings.Sensitivity = v; GameSettings.Apply(); UpdateOptionLabels();
            }, out _sensValue);
            var invX = UiTheme.MenuItem(col, "左右反転", ToggleInvertX, 900f);
            _invertXValue = ValueOnRight(invX);
            var invY = UiTheme.MenuItem(col, "上下反転", ToggleInvertY, 900f);
            _invertYValue = ValueOnRight(invY);
            var ruby = UiTheme.MenuItem(col, "ルビ（読み仮名）", ToggleRuby, 900f);
            _rubyValue = ValueOnRight(ruby);
            var back = UiTheme.MenuItem(col, "戻る", () => { CloseOptions(); }, 900f);
            UiTheme.ChainVertical(new List<Selectable> { _volumeSlider, _sensSlider, invX, invY, ruby, back });
            UpdateOptionLabels();
        }

        /// <summary>「つづきから」の記録を選ぶ画面（オート／マニュアル）</summary>
        private void BuildLoadPanel(Transform parent)
        {
            _loadPanel = Backdrop(parent, "LoadPanel", 0.82f);
            var p = _loadPanel.transform;
            Heading(p, "記録を選ぶ", 250f, UiTheme.FsHeading + 8);

            var col = Column(p, 150f, 1100f);
            _loadAuto = UiTheme.MenuItem(col, "オートセーブ", () => LoadFrom(SaveSlot.Auto), 1100f);
            _loadAutoInfo = InfoOnRight(_loadAuto);
            _loadManual = UiTheme.MenuItem(col, "マニュアルセーブ", () => LoadFrom(SaveSlot.Manual), 1100f);
            _loadManualInfo = InfoOnRight(_loadManual);
            _loadBack = UiTheme.MenuItem(col, "戻る", CloseLoad, 1100f);
            UiTheme.ChainVertical(new List<Selectable> { _loadAuto, _loadManual, _loadBack });

            var note = UiTheme.Label(p, "Note", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextFaint);
            UiTheme.Place(note.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(ColumnX, -60f), new Vector2(1200f, 70f));
            note.lineSpacing = 1.4f;
            note.text = "オート：扉が開いた時・終章が始まった時・タイトルへ戻る時・捕まって戻った後に、自動で記録する\n" +
                        "マニュアル：最初の部屋の記録端末と、一時停止の「記録する」で記録する";
        }

        /// <summary>記録の項目の右側に、その記録の日時・場所・人形を小さく出す</summary>
        private static Text InfoOnRight(Button item)
        {
            var t = UiTheme.Label(item.transform, "Info", UiTheme.FsSmall, TextAnchor.MiddleRight, UiTheme.TextSub);
            UiTheme.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(760f, 56f));
            return t;
        }

        /// <summary>切り替え項目の右端に値（オン／オフ）を出す</summary>
        private static Text ValueOnRight(Button item)
        {
            var t = UiTheme.Label(item.transform, "Value", UiTheme.FsMenu, TextAnchor.MiddleRight);
            UiTheme.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(760f, 0f), new Vector2(120f, 56f));
            return t;
        }

        private void ToggleInvertX()
        {
            GameSettings.InvertX = !GameSettings.InvertX; GameSettings.Apply(); UpdateOptionLabels();
        }
        private void ToggleRuby()
        {
            GameSettings.RubyOn = !GameSettings.RubyOn; UpdateOptionLabels();
        }

        private void ToggleInvertY()
        {
            GameSettings.InvertY = !GameSettings.InvertY; GameSettings.Apply(); UpdateOptionLabels();
        }

        private void UpdateOptionLabels()
        {
            if (_volumeValue != null) _volumeValue.text = $"{Mathf.RoundToInt(GameSettings.MasterVolume * 100)}";
            if (_sensValue != null) _sensValue.text = $"{GameSettings.Sensitivity:0.000}";
            if (_invertXValue != null) _invertXValue.text = OnOff(GameSettings.InvertX);
            if (_invertYValue != null) _invertYValue.text = OnOff(GameSettings.InvertY);
            if (_rubyValue != null) _rubyValue.text = OnOff(GameSettings.RubyOn);
        }

        private static string OnOff(bool on) =>
            on ? $"<color={UiTheme.Rgb(UiTheme.Accent)}>オン</color>" : $"<color={UiTheme.Rgb(UiTheme.TextSub)}>オフ</color>";

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
    }
}
