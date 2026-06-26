using System;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EscapeProto
{
    /// <summary>
    /// タイトル / ポーズ / オプションのメニューUI（実行時にUGUIをコード生成）。
    /// ・タイトル：はじめから / つづきから / オプション / 終了
    /// ・ポーズ（Esc）：再開 / オプション / セーブ / タイトルへ戻る
    /// ・オプション：マスター音量・マウス感度（GameSettingsへ反映・保存）
    /// 状態は GameManager が管理し、本クラスは表示の切替と入力受付のみ行う。
    /// </summary>
    public class MenuManager : MonoBehaviour
    {
        private Font _font;
        private GameObject _titlePanel, _pausePanel, _optionsPanel;
        private Button _continueButton;
        private Text _saveInfoText, _pauseStatusText;
        private Slider _volumeSlider, _sensSlider;
        private Text _volumeValue, _sensValue;

        private bool _optionsOpen;
        private GameState _state = GameState.Title;

        private void Awake()
        {
            _font = FontProvider.Get();
            BuildUI();
            Refresh();
        }

        private void OnEnable() => GameEvents.OnGameStateChanged += HandleStateChanged;
        private void OnDisable() => GameEvents.OnGameStateChanged -= HandleStateChanged;

        private void HandleStateChanged(GameState s)
        {
            _state = s;
            if (s == GameState.Playing || s == GameState.Ended) _optionsOpen = false;
            Refresh();
        }

        private void Update()
        {
            // 資料/コード入力中は PuzzleUI が Esc を処理するので競合させない
            if (PuzzleUI.Instance != null && PuzzleUI.Instance.IsOpen) return;

            if (WasEscPressed())
            {
                if (_optionsOpen) { CloseOptions(); return; }
                if (_state == GameState.Playing) GameManager.Instance?.Pause();
                else if (_state == GameState.Paused) GameManager.Instance?.Resume();
            }
        }

        private bool WasEscPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ============================== 表示制御 ==============================

        private void Refresh()
        {
            bool title = _state == GameState.Title && !_optionsOpen;
            bool pause = _state == GameState.Paused && !_optionsOpen;

            _titlePanel.SetActive(title);
            _pausePanel.SetActive(pause);
            _optionsPanel.SetActive(_optionsOpen);

            if (title)
            {
                var d = SaveSystem.Load();
                bool has = d != null && d.valid;
                _continueButton.interactable = has;
                _saveInfoText.text = has
                    ? $"セーブデータ: {d.savedAt}　人形 {d.dolls}　解除 {d.solvedGimmicks.Count}/3"
                    : "セーブデータなし";
            }
            if (pause) _pauseStatusText.text = "";
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
        private void OnContinue() => GameManager.Instance?.ContinueGame();
        private void OnResume() => GameManager.Instance?.Resume();
        private void OnQuitToTitle() => GameManager.Instance?.QuitToTitle();

        private void OnSave()
        {
            GameManager.Instance?.SaveNow();
            if (_pauseStatusText != null) _pauseStatusText.text = "<color=#80FFA0>セーブしました</color>";
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
            var canvasGo = new GameObject("MenuCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // HUDより前面
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            BuildTitlePanel(canvasGo.transform);
            BuildPausePanel(canvasGo.transform);
            BuildOptionsPanel(canvasGo.transform);
        }

        private void BuildTitlePanel(Transform parent)
        {
            _titlePanel = Panel(parent, "TitlePanel", new Color(0.02f, 0.02f, 0.04f, 0.96f));

            var title = MakeText(_titlePanel.transform, "Title", 84, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, Center, Center, new Vector2(0, 320), new Vector2(1400, 160));
            title.text = "<color=#C03030>地下室からの脱出</color>";

            var sub = MakeText(_titlePanel.transform, "Sub", 30, TextAnchor.MiddleCenter);
            SetRect(sub.rectTransform, Center, Center, new Vector2(0, 220), new Vector2(1400, 60));
            sub.text = "― そして誰もいなくなった ―";
            sub.color = new Color(0.7f, 0.7f, 0.75f);

            MakeButton(_titlePanel.transform, "はじめから", new Vector2(0, 80), OnNewGame);
            _continueButton = MakeButton(_titlePanel.transform, "つづきから", new Vector2(0, 0), OnContinue);
            MakeButton(_titlePanel.transform, "オプション", new Vector2(0, -80), OpenOptions);
            MakeButton(_titlePanel.transform, "終了", new Vector2(0, -160), OnQuitApp);

            _saveInfoText = MakeText(_titlePanel.transform, "SaveInfo", 24, TextAnchor.MiddleCenter);
            SetRect(_saveInfoText.rectTransform, Center, Center, new Vector2(0, -250), new Vector2(1400, 50));
            _saveInfoText.color = new Color(0.6f, 0.6f, 0.65f);

            var hint = MakeText(_titlePanel.transform, "Hint", 22, TextAnchor.LowerCenter);
            SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(1600, 40));
            hint.text = "WASD移動 / Shift走 / E長押し解除・調べる / F懐中電灯 / Tab手帳 / Escポーズ";
            hint.color = new Color(0.5f, 0.5f, 0.55f);
        }

        private void BuildPausePanel(Transform parent)
        {
            _pausePanel = Panel(parent, "PausePanel", new Color(0.02f, 0.02f, 0.04f, 0.82f));

            var title = MakeText(_pausePanel.transform, "PauseTitle", 60, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, Center, Center, new Vector2(0, 240), new Vector2(1000, 120));
            title.text = "ポーズ";

            MakeButton(_pausePanel.transform, "再開", new Vector2(0, 100), OnResume);
            MakeButton(_pausePanel.transform, "オプション", new Vector2(0, 20), OpenOptions);
            MakeButton(_pausePanel.transform, "セーブ", new Vector2(0, -60), OnSave);
            MakeButton(_pausePanel.transform, "タイトルへ戻る", new Vector2(0, -140), OnQuitToTitle);

            _pauseStatusText = MakeText(_pausePanel.transform, "PauseStatus", 26, TextAnchor.MiddleCenter);
            SetRect(_pauseStatusText.rectTransform, Center, Center, new Vector2(0, -210), new Vector2(900, 50));
        }

        private void BuildOptionsPanel(Transform parent)
        {
            _optionsPanel = Panel(parent, "OptionsPanel", new Color(0.03f, 0.03f, 0.05f, 0.97f));

            var title = MakeText(_optionsPanel.transform, "OptTitle", 56, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, Center, Center, new Vector2(0, 240), new Vector2(1000, 110));
            title.text = "オプション";

            // マスター音量
            MakeLabel(_optionsPanel.transform, "音量", new Vector2(-360, 90));
            _volumeSlider = MakeSlider(_optionsPanel.transform, new Vector2(60, 90), 0f, 1f,
                GameSettings.MasterVolume, v =>
                {
                    GameSettings.MasterVolume = v; GameSettings.Apply(); UpdateOptionLabels();
                });
            _volumeValue = MakeText(_optionsPanel.transform, "VolVal", 28, TextAnchor.MiddleLeft);
            SetRect(_volumeValue.rectTransform, Center, Center, new Vector2(330, 90), new Vector2(120, 50));

            // マウス感度
            MakeLabel(_optionsPanel.transform, "マウス感度", new Vector2(-360, 10));
            _sensSlider = MakeSlider(_optionsPanel.transform, new Vector2(60, 10), 0.2f, 3f,
                GameSettings.Sensitivity, v =>
                {
                    GameSettings.Sensitivity = v; GameSettings.Apply(); UpdateOptionLabels();
                });
            _sensValue = MakeText(_optionsPanel.transform, "SensVal", 28, TextAnchor.MiddleLeft);
            SetRect(_sensValue.rectTransform, Center, Center, new Vector2(330, 10), new Vector2(120, 50));

            MakeButton(_optionsPanel.transform, "戻る", new Vector2(0, -150), CloseOptions);
            UpdateOptionLabels();
        }

        private void UpdateOptionLabels()
        {
            if (_volumeValue != null) _volumeValue.text = $"{Mathf.RoundToInt(GameSettings.MasterVolume * 100)}";
            if (_sensValue != null) _sensValue.text = $"{GameSettings.Sensitivity:0.0}";
        }

        // ============================== UGUIヘルパー ==============================

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private static GameObject Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            SetRect(img.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return go;
        }

        private Button MakeButton(Transform parent, string label, Vector2 pos, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.16f, 0.2f, 0.95f);
            SetRect(img.rectTransform, Center, Center, pos, new Vector2(420, 64));

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(0.8f, 0.55f, 0.55f, 1f);
            colors.pressedColor = new Color(0.5f, 0.3f, 0.3f, 1f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var text = MakeText(go.transform, "Label", 32, TextAnchor.MiddleCenter);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            text.text = label;
            return btn;
        }

        private void MakeLabel(Transform parent, string label, Vector2 pos)
        {
            var t = MakeText(parent, "Lbl_" + label, 32, TextAnchor.MiddleRight);
            SetRect(t.rectTransform, Center, Center, pos, new Vector2(280, 50));
            t.text = label;
        }

        private Slider MakeSlider(Transform parent, Vector2 pos, float min, float max, float value, Action<float> onChanged)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.12f, 1f);
            SetRect(bg.rectTransform, Center, Center, pos, new Vector2(520, 24));

            var slider = go.AddComponent<Slider>();
            slider.minValue = min; slider.maxValue = max;

            var fillArea = new GameObject("Fill");
            fillArea.transform.SetParent(go.transform, false);
            var fillImg = fillArea.AddComponent<Image>();
            fillImg.color = new Color(0.8f, 0.4f, 0.4f, 1f);
            var fillRt = fillImg.rectTransform;
            fillRt.anchorMin = new Vector2(0, 0); fillRt.anchorMax = new Vector2(1, 1);
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(go.transform, false);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(0.95f, 0.9f, 0.9f, 1f);
            var handleRt = handleImg.rectTransform;
            handleRt.sizeDelta = new Vector2(18, 36);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.SetValueWithoutNotify(value);
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        private Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.alignment = anchor;
            text.color = Color.white; text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
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
    }
}
