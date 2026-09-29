using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 資料を調べる画面（くるくる回す）。
    /// ・拾った資料の実物（InspectLibrary の種類別モデル）を目の前に浮かべ、ドラッグ／スティックで回し、ホイールで寄せる。
    ///   背景はゲーム画面を暗くして残す（UIの板は出さない）。紙面には資料の文章を実際に載せる
    /// ・[Space]「読む」で本文の画面へ。本文はマウスでなぞるとラインマーカーが引け、手帳の「メモ」に書き写される
    /// ・音声記録は [Space]「再生」。字幕で台詞が流れ、最後まで聞くと書き起こしを読めるようになる
    /// ・初めて見た資料には、主人公のひと言が字幕で出る（UIの板ではなく字幕）
    /// 描画：メインカメラに重ねる URP のオーバーレイカメラが Inspect レイヤーだけを描く（常に手前に出る）。
    ///       専用の光は Rendering Layer で実物だけを照らす（部屋は照らさない）。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class InspectView : MonoBehaviour
    {
        public static InspectView Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;
        /// <summary>このフレームで閉じたか（同じキーで手帳やポーズが続けて反応しないように）</summary>
        public static bool ClosedThisFrame => Instance != null && Instance._closedFrame == Time.frameCount;
        private int _closedFrame = -1;

        /// <summary>実物を描くレイヤー（ビルダーが「Inspect」と名付ける。無ければ8番）</summary>
        public static int Layer { get { int l = LayerMask.NameToLayer("Inspect"); return l >= 0 ? l : 8; } }
        /// <summary>実物と専用の光だけが持つ Rendering Layer</summary>
        public const uint RenderingBit = 1u << 7;

        /// <summary>開き方</summary>
        public class Request
        {
            public string EntryId, RoomId, DocId, Title, Body;
            public DocInfo Info;
            public AudioClip[] Clips;
            public bool FromNotebook;
            public bool StartInRead;                 // 読む画面から開く（メモから元の資料へ飛んだ時）
            public Action<bool> OnClosed;            // 引数: true = 手帳ごと閉じる（Tab）
        }

        private enum Mode { Model, Read }

        private const float OpenDistance = 0.5f, MinDistance = 0.28f, MaxDistance = 0.78f;
        private const float TargetSize = 0.3f;       // 実物の長辺を何mに見せるか（0.5m先で画面の約半分）

        private bool _open;
        private Mode _mode;
        private Request _req;
        private float _openedAt;

        // ---- 3D ----
        private Camera _cam, _mainCam;
        private Transform _pivot, _spin;
        private GameObject _item;
        private RawImage _dim;
        private GameObject _dimCanvas;   // 閉じている間は消す（カメラが止まると画面全体に重なって描かれるため）
        private float _yaw, _pitch, _distance = OpenDistance;
        private Vector2 _spinVel;

        // ---- UI ----
        private Canvas _canvas;
        private CanvasGroup _group, _readGroup;
        private Text _title, _guide, _status, _readTitle, _readNote;
        private MarkerText _marker;
        private Image _guideBar;
        private GameObject _readPanel;

        // ---- 字幕（閉じた後も最後まで流す） ----
        private CanvasGroup _subGroup;
        private Text _subText;
        private Image _subPlate;
        private Coroutine _subRoutine;
        private readonly Queue<string> _subQueue = new Queue<string>();
        private string _lastCommentEntry;

        // ---- 音声 ----
        private AudioSource _voice, _hiss;
        private Coroutine _play;
        private bool _playing;
        private float _playStart;

        private bool _tookControl;

        // ---- カセットレコーダー（テープを入れると見た目をレコーダーに替え、再生中はリールが回る） ----
        private bool _loaded;
        private Transform _reelL, _reelR;

        /// <summary>カセットテープ（レコーダーで聞く音声記録）か</summary>
        private bool IsTape => _req != null && _req.Info.Audio && _req.Info.Kind == InspectKind.Cassette;
        /// <summary>読める本文があるか（道具は実物を見るだけ）</summary>
        private bool Readable => _req != null && (_req.Info.Audio || !string.IsNullOrEmpty(_req.Body));

        // ============================== 開く・閉じる ==============================

        /// <summary>部屋で拾った資料を開く</summary>
        public void OpenFindable(LoopFindable f)
        {
            var info = DocCatalog.Get(f.RoomId, f.Id);
            var rec = f.GetComponent<AudioRecord>();
            Open(new Request
            {
                EntryId = $"{f.RoomId}_{f.Id}", RoomId = f.RoomId, DocId = f.Id,
                Title = string.IsNullOrEmpty(f.Title) ? f.Name : f.Title, Body = f.Body,
                Info = info, Clips = rec != null ? rec.Clips : null,
            });
        }

        public void Open(Request req)
        {
            if (_open || req == null) return;
            // 前の資料のひと言が残っていたら打ち切る（別の資料の上に前の独白が流れないように）
            if (_subRoutine != null && _lastCommentEntry != req.EntryId)
            {
                StopCoroutine(_subRoutine);
                _subRoutine = null;
                _subQueue.Clear();
                HideSubtitleNow();
            }
            _req = req;
            _open = true;
            _openedAt = Time.unscaledTime;
            EnsureRig();

            var gm = GameManager.Instance;
            _tookControl = gm != null && gm.State == GameState.Playing;
            if (_tookControl) gm.SetBusy(true);

            _title.text = req.Title ?? "";
            _loaded = false;
            _reelL = _reelR = null;
            BuildItem(req.Info, req.Title, req.Body);
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _canvas.gameObject.SetActive(true);
            SetMode(req.StartInRead || _item == null ? Mode.Read : Mode.Model);
            UpdateStatus();

            // 初めて見た資料：主人公のひと言（音声記録は聞き終えてから）
            if (!req.Info.Audio) QueueComment(0.7f);
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), Ear, 0.3f, spatial: false);
        }

        /// <summary>閉じる。closeAll = 手帳ごと閉じる</summary>
        public void Close(bool closeAll = false)
        {
            if (!_open) return;
            _open = false;
            _closedFrame = Time.frameCount;
            StopPlayback(false);
            PlaceSubtitle(false);   // 閉じた後も流れる独白は通常の高さで
            if (_item != null) { Destroy(_item); _item = null; }
            if (_dimCanvas != null) _dimCanvas.SetActive(false);
            if (_cam != null) _cam.enabled = false;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _readPanel.SetActive(false);
            _canvas.gameObject.SetActive(false);
            if (_tookControl && GameManager.Instance != null && !closeAll && _req != null && _req.FromNotebook)
            {
                // 手帳へ戻る：操作は手帳が止めたまま
            }
            else if (_tookControl) GameManager.Instance?.SetBusy(false);
            _tookControl = false;
            UiSound.Cancel();
            var cb = _req?.OnClosed;
            _req = null;
            cb?.Invoke(closeAll);
        }

        private static Vector3 Ear => Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        private void Awake()
        {
            Instance = this;
            BuildUI();
        }

        private void OnEnable() => GameEvents.OnWhiteout += OnWhiteout;
        private void OnDisable() => GameEvents.OnWhiteout -= OnWhiteout;
        private void OnWhiteout(float _) { if (_open) Close(true); }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ============================== 3D の準備 ==============================

        private void EnsureRig()
        {
            // 起床の場面の直後などは Camera.main が取れない瞬間がある。その時は有効な通常のカメラを探す
            var main = Camera.main;
            if (main == null)
                foreach (var c in Camera.allCameras)
                    if (c != _cam && c.isActiveAndEnabled && c.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base)
                    { main = c; break; }
            if (main != null && main != _mainCam)
            {
                _mainCam = main;
                if (_cam == null)
                {
                    var go = new GameObject("InspectCamera");
                    _cam = go.AddComponent<Camera>();
                    var od = _cam.GetUniversalAdditionalCameraData();
                    od.renderType = CameraRenderType.Overlay;
                    od.renderShadows = false;
                    _cam.clearFlags = CameraClearFlags.Depth;
                    _cam.nearClipPlane = 0.01f;
                    _cam.farClipPlane = 4f;
                    _cam.cullingMask = 1 << Layer;
                    BuildStage(go.transform);
                }
                _cam.transform.SetParent(main.transform, false);
                _cam.transform.localPosition = Vector3.zero;
                _cam.transform.localRotation = Quaternion.identity;
                var md = main.GetUniversalAdditionalCameraData();
                if (!md.cameraStack.Contains(_cam)) md.cameraStack.Add(_cam);
                _cam.GetUniversalAdditionalCameraData().renderPostProcessing = md.renderPostProcessing;
                main.cullingMask &= ~(1 << Layer);   // メインカメラには描かせない
            }
            if (_cam != null)
            {
                _cam.fieldOfView = _mainCam != null ? _mainCam.fieldOfView : 60f;
                _cam.enabled = true;
            }
            if (_dimCanvas != null) _dimCanvas.SetActive(true);
        }

        /// <summary>実物の置き台（回転の中心）・背景の暗幕・専用の光</summary>
        private void BuildStage(Transform cam)
        {
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(cam, false);
            _pivot.localPosition = new Vector3(0f, 0f, OpenDistance);
            _spin = new GameObject("Spin").transform;
            _spin.SetParent(_pivot, false);

            // 暗幕：実物の奥に置く画面いっぱいの板（ゲーム画面を暗く残す。周辺ほど暗い）。
            // このカメラが描く Screen Space - Camera のキャンバスにする（URP の重ねたカメラでは、
            // 実行時に作った半透明の材質がうまく混ざらないため。UI の描き方なら確実に重なる）
            var dimGo = new GameObject("DimCanvas");
            dimGo.layer = Layer;
            dimGo.transform.SetParent(cam, false);
            var dc = dimGo.AddComponent<Canvas>();
            dc.renderMode = RenderMode.ScreenSpaceCamera;
            dc.worldCamera = cam.GetComponent<Camera>();
            dc.planeDistance = 1.5f;
            var img = new GameObject("Dim", typeof(RectTransform)).AddComponent<RawImage>();
            img.gameObject.layer = Layer;
            img.transform.SetParent(dimGo.transform, false);
            img.texture = VignetteTexture();
            img.color = new Color(0f, 0f, 0f, 0.72f);
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            _dim = img;
            _dimCanvas = dimGo;

            // 専用の光（実物だけを照らす）：斜め上の主光・下からの弱い補助光・奥からの縁取り
            AddLight(cam, "Key", new Vector3(0.28f, 0.34f, 0.05f), new Color(1f, 0.95f, 0.86f), 0.45f, LightType.Spot);
            AddLight(cam, "Fill", new Vector3(-0.32f, -0.12f, 0.18f), new Color(0.7f, 0.78f, 0.9f), 0.16f, LightType.Point);
            AddLight(cam, "Rim", new Vector3(0.05f, 0.3f, 0.95f), new Color(1f, 0.88f, 0.7f), 0.22f, LightType.Point);
        }

        private void AddLight(Transform cam, string name, Vector3 pos, Color color, float intensity, LightType type)
        {
            var go = new GameObject("InspectLight_" + name);
            go.layer = Layer;   // 調べる画面のカメラは Inspect レイヤーの光しか拾わない（他のレイヤーの光は間引かれる）
            go.transform.SetParent(cam, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, OpenDistance) - pos);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = 2f;
            l.spotAngle = 70f;
            l.shadows = LightShadows.None;
            // URP は Light.renderingLayerMask ではなく追加データの renderingLayers を見る（既定は Default＝部屋を照らしてしまう）
            l.GetUniversalAdditionalLightData().renderingLayers = RenderingBit;
        }

        private static Texture2D VignetteTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.7f + dy * dy);
                    float a = Mathf.Lerp(0.8f, 1f, Mathf.SmoothStep(0.2f, 1.1f, r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>実物を置く（種類のモデル＋紙面の文章）。モデルが無ければ何も置かない</summary>
        private void BuildItem(DocInfo info, string title, string body)
        {
            if (_item != null) Destroy(_item);
            _item = null;
            if (_spin == null) return;   // カメラが無く3Dを用意できない時は、文章だけで見せる
            var lib = InspectLibrary.Instance;
            var prefab = lib != null ? lib.Get(info.Kind) : null;
            if (prefab == null) return;

            _item = Instantiate(prefab, _spin, false);
            _item.name = "Item";
            foreach (var c in _item.GetComponentsInChildren<Collider>(true)) Destroy(c);
            float scale = info.Scale > 0f ? info.Scale : 1f;
            InspectPrint.Apply(_item, new Request { Title = title, Body = body, Info = info }, scale);
            SetLayer(_item.transform, Layer);
            foreach (var r in _item.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = RenderingBit;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (info.Tint != Color.white) TintPaper(r, info.Tint);
            }
            foreach (var t in _item.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "ReelL") _reelL = t;
                else if (t.name == "ReelR") _reelR = t;
            }

            // 大きさをそろえ、見た目の中心を回転の中心に
            var b = Bounds(_item);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z * 0.6f);
            float k = longest > 0.0001f ? TargetSize * Mathf.Lerp(1f, scale, 0.5f) / longest : 1f;
            _item.transform.localScale *= k;
            b = Bounds(_item);
            _item.transform.position -= b.center - _spin.position;

            _yaw = -14f; _pitch = 10f; _distance = OpenDistance;
            _spinVel = Vector2.zero;
            _pivot.localPosition = new Vector3(0f, -0.12f, OpenDistance);
            _spin.localRotation = Quaternion.Euler(28f, -40f, 0f);
        }

        private static void TintPaper(Renderer r, Color tint)
        {
            var mats = r.materials;
            foreach (var m in mats)
                if (m != null && m.name.Contains("Paper") && m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
            r.materials = mats;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ============================== 毎フレーム ==============================

        private void Update()
        {
            if (!_open) return;
            bool fresh = Time.unscaledTime - _openedAt < 0.2f;   // 開いた瞬間の [E] で閉じない

            if (_mode == Mode.Model) UpdateModel();

            if (!fresh)
            {
                if (Pressed(Act.Close) || (_mode == Mode.Model && Pressed(Act.CloseAlt)))
                {
                    if (_mode == Mode.Read && _item != null && !_req.StartInRead) { SetMode(Mode.Model); UiSound.Cancel(); }
                    else Close();
                }
                else if (Pressed(Act.CloseAll) && _req.FromNotebook) Close(true);
                else if (Pressed(Act.Primary))
                {
                    if (_mode == Mode.Model)
                    {
                        if (_req.Info.Audio) TogglePlayback();
                        else if (Readable) SetMode(Mode.Read);
                    }
                    else if (_item != null) SetMode(Mode.Model);
                }
                else if (Pressed(Act.Secondary) && _mode == Mode.Model && _req.Info.Audio && DocState.Heard(_req.EntryId))
                    SetMode(Mode.Read);
            }
            if (_playing)
            {
                UpdateStatus();
                // 再生中はリールが回る（巻き取る右は少し速く）
                float dt = Time.unscaledDeltaTime;
                if (_reelL != null) _reelL.Rotate(0f, 0f, 150f * dt, Space.Self);
                if (_reelR != null) _reelR.Rotate(0f, 0f, 175f * dt, Space.Self);
            }
        }

        private void UpdateModel()
        {
            if (_pivot == null || _spin == null) return;
            // 置き台を所定の位置へ（開いた時は下から浮かび上がる）
            var target = new Vector3(0f, 0f, _distance);
            _pivot.localPosition = Vector3.Lerp(_pivot.localPosition, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));

            Vector2 rot = RotateInput();
            if (rot.sqrMagnitude > 0.0001f) _spinVel = rot;
            else _spinVel = Vector2.Lerp(_spinVel, Vector2.zero, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
            _yaw += _spinVel.x;
            _pitch = Mathf.Clamp(_pitch + _spinVel.y, -85f, 85f);
            var want = Quaternion.Euler(_pitch, _yaw, 0f);
            _spin.localRotation = Quaternion.Slerp(_spin.localRotation, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));

            _distance = Mathf.Clamp(_distance - ZoomInput(), MinDistance, MaxDistance);
        }

        private void SetMode(Mode m)
        {
            _mode = m;
            bool read = m == Mode.Read;
            _readPanel.SetActive(read);
            _status.gameObject.SetActive(!read);
            PlaceSubtitle(read);
            if (read)
            {
                string body = _req.Info.Audio ? DocCatalog.Transcript(_req.RoomId, _req.DocId) : _req.Body;
                _readTitle.text = _req.Title ?? "";
                // 文字の大きさは3通りから選ぶ（自動調整は文字の画像を作りすぎるので使わない）
                var bt = _marker.Text;
                bt.fontSize = UiTheme.FitFontSize(body, bt.rectTransform.rect.width, bt.rectTransform.rect.height, bt.lineSpacing,
                                                  UiTheme.FsBody, 22, 19);
                _marker.SetBody(_req.EntryId, body);
                _readNote.text = _req.Info.Audio ? "書き起こし" : "";
                Notebook.Unflag(_req.EntryId);
            }
            if (_dim != null) _dim.color = new Color(0f, 0f, 0f, read ? 0.85f : 0.72f);
            UpdateGuide();
        }

        private void UpdateGuide()
        {
            string back = _req != null && _req.FromNotebook ? "戻る" : "閉じる";
            string closeAll = _req != null && _req.FromNotebook ? $"　　{UiTheme.Key("Tab", "View")} 手帳を閉じる" : "";
            if (_mode == Mode.Read)
            {
                string toModel = _item != null && !(_req?.StartInRead ?? false) ? $"{UiTheme.Key("Space", "Y")} 実物に戻る　　" : "";
                _guide.text = $"なぞる 線を引く（被った文字をメモへ）　　線をクリック 消す　　{toModel}{UiTheme.Key("Esc", "B")} {back}{closeAll}";
            }
            else
            {
                string primary = _req.Info.Audio
                    ? (_playing ? "停止" : IsTape && !LoopProgress.TapePlayerOwned ? "再生（レコーダーが要る）" : IsTape && !_loaded ? "レコーダーで再生" : "再生")
                    : "読む";
                string primaryKey = _req.Info.Audio || Readable ? $"{UiTheme.Key("Space", "Y")} {primary}　　" : "";
                string transcript = _req.Info.Audio && DocState.Heard(_req.EntryId) ? $"{UiTheme.Key("R", "X")} 書き起こし　　" : "";
                _guide.text = $"ドラッグ 回す　　ホイール 寄せる　　{primaryKey}{transcript}{UiTheme.Key("E", "B")} {back}{closeAll}";
            }
            _guideBar.rectTransform.sizeDelta = new Vector2(_guide.preferredWidth + 80f, 52f);
        }

        private void UpdateStatus()
        {
            if (_req == null || !_req.Info.Audio) { _status.text = ""; return; }
            if (_playing)
            {
                int s = Mathf.FloorToInt(Time.unscaledTime - _playStart);
                bool dot = Mathf.FloorToInt(Time.unscaledTime) % 2 == 0;   // 1秒ごと（点滅は1Hz）
                _status.text = $"<color={UiTheme.Rgb(dot ? UiTheme.Danger : UiTheme.TextFaint)}>●</color> 再生中　{s / 60:00}:{s % 60:00}";
            }
            else _status.text = DocState.Heard(_req.EntryId) ? "■ 停止　（聞き終えた）" : "■ 停止";
        }

        // ============================== 音声記録 ==============================

        private void TogglePlayback()
        {
            if (_playing) { StopPlayback(true); UpdateGuide(); return; }
            if (IsTape)
            {
                // カセットテープはレコーダーが要る（最初の部屋の机にある）
                if (!LoopProgress.TapePlayerOwned)
                {
                    UiSound.Error();
                    ToastUI.Show("再生する機械がない……（最初の部屋の机に、カセットレコーダーがあったはず）");
                    return;
                }
                LoadIntoRecorder();
            }
            _play = StartCoroutine(Playback());
        }

        /// <summary>テープをレコーダーに入れる：実物の見た目をカセットレコーダー（窓からこのテープのラベルが見える）に替える</summary>
        private void LoadIntoRecorder()
        {
            if (_loaded) return;
            _loaded = true;
            var info = new DocInfo { Kind = InspectKind.TapeRecorder, Ink = InkStyle.Hand, Scale = 1f, Tint = Color.white, Audio = true };
            if (InspectLibrary.Instance == null || InspectLibrary.Instance.Get(InspectKind.TapeRecorder) == null) return;
            BuildItem(info, _req.Title, "");
            ProceduralAudio.PlayAt(ProceduralAudio.DoorLatch(), Ear, 0.35f, spatial: false);   // カセットを差し込む「カチャ」
        }

        private IEnumerator Playback()
        {
            _playing = true;
            _playStart = Time.unscaledTime;
            UpdateGuide();
            EnsureAudio();
            ProceduralAudio.PlayAt(ProceduralAudio.TapeButton(), Ear, 0.5f, spatial: false);
            _hiss.Play();
            yield return new WaitForSecondsRealtime(0.6f);

            var lines = DocCatalog.Tape(_req.RoomId, _req.DocId);
            for (int i = 0; i < lines.Count && _playing; i++)
            {
                var line = lines[i];
                var clip = _req.Clips != null && i < _req.Clips.Length ? _req.Clips[i] : null;
                string shown = string.IsNullOrEmpty(line.Speaker)
                    ? $"<color={UiTheme.Rgb(UiTheme.TextSub)}>{line.Text}</color>"
                    : $"<color={UiTheme.Rgb(UiTheme.TextSub)}>{line.Speaker}</color>　{line.Text}";
                ShowSubtitleNow(shown);
                float dur;
                if (clip != null)
                {
                    _voice.clip = clip;
                    _voice.Play();
                    dur = clip.length;
                }
                else
                {
                    PlaySfx(line.Text);
                    dur = Mathf.Max(1.4f, line.Text.Length * 0.09f);
                }
                for (float t = 0f; t < dur + 0.35f && _playing; t += Time.unscaledDeltaTime) yield return null;
            }
            if (!_playing) yield break;
            HideSubtitleNow();
            yield return new WaitForSecondsRealtime(0.4f);
            _hiss.Stop();
            ProceduralAudio.PlayAt(ProceduralAudio.TapeButton(), Ear, 0.4f, spatial: false);
            _playing = false;
            _play = null;
            bool first = !DocState.Heard(_req.EntryId);
            DocState.MarkHeard(_req.EntryId);
            if (first) ToastUI.Show("書き起こしを読めるようになった");
            UpdateGuide();
            UpdateStatus();
            QueueComment(0.5f);
        }

        private void StopPlayback(bool clearSubtitle)
        {
            if (_play != null) StopCoroutine(_play);
            _play = null;
            if (_playing && clearSubtitle) HideSubtitleNow();
            _playing = false;
            if (_voice != null) _voice.Stop();
            if (_hiss != null) _hiss.Stop();
        }

        /// <summary>物音の行：それらしい音を鳴らす</summary>
        private void PlaySfx(string text)
        {
            if (text.Contains("ドア")) ProceduralAudio.PlayAt(ProceduralAudio.DoorLatch(), Ear, 0.5f, spatial: false);
            else if (text.Contains("発信音")) ProceduralAudio.PlayAt(ProceduralAudio.Beep(), Ear, 0.35f, spatial: false);
            else if (text.Contains("破損") || text.Contains("終わって")) ProceduralAudio.PlayAt(ProceduralAudio.StaticHiss(), Ear, 0.25f, spatial: false);
        }

        private void EnsureAudio()
        {
            if (_voice != null) return;
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;
            _voice.volume = 0.95f;
            _voice.ignoreListenerPause = true;
            // 古い録音らしく：低音と高音を削る
            var hp = gameObject.AddComponent<AudioHighPassFilter>(); hp.cutoffFrequency = 320f;
            var lp = gameObject.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 4200f;
            var hissGo = new GameObject("TapeHiss");
            hissGo.transform.SetParent(transform, false);
            _hiss = hissGo.AddComponent<AudioSource>();
            _hiss.clip = ProceduralAudio.StaticHiss();
            _hiss.loop = true;
            _hiss.spatialBlend = 0f;
            _hiss.volume = 0.05f;
            _hiss.playOnAwake = false;
            _hiss.ignoreListenerPause = true;
        }

        // ============================== 主人公のひと言（字幕） ==============================

        private void QueueComment(float delay)
        {
            if (_req == null || DocState.Commented(_req.EntryId)) return;
            var lines = DocCatalog.Comment(_req.RoomId, _req.DocId);
            if (lines.Length == 0) return;
            DocState.MarkCommented(_req.EntryId);
            _lastCommentEntry = _req.EntryId;
            foreach (var l in lines) _subQueue.Enqueue(l);
            if (_subRoutine == null) _subRoutine = StartCoroutine(PumpSubtitles(delay));
        }

        /// <summary>字幕を順に出す。資料を閉じた後も最後まで流す</summary>
        private IEnumerator PumpSubtitles(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            while (_subQueue.Count > 0)
            {
                while (_playing) yield return null;          // 再生中の台詞とは重ねない
                string line = _subQueue.Dequeue();
                ShowSubtitleNow(line);
                float hold = Mathf.Clamp(1.6f + line.Length * 0.11f, 2.4f, 7f);
                for (float t = 0f; t < hold && !_playing; t += Time.unscaledDeltaTime) yield return null;
                if (!_playing) HideSubtitleNow();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            _subRoutine = null;
        }

        private void ShowSubtitleNow(string text)
        {
            _subText.text = text;
            _subPlate.rectTransform.sizeDelta = new Vector2(Mathf.Min(_subText.preferredWidth, 1400f) + 64f, _subText.preferredHeight + 26f);
            _subGroup.alpha = 1f;
        }

        private void HideSubtitleNow() => _subGroup.alpha = 0f;

        /// <summary>字幕の高さ：読む画面の間は本文の窓の下（操作ガイドとの間）、それ以外は少し上</summary>
        private void PlaceSubtitle(bool reading)
        {
            float y = UiTheme.SafeY + (reading ? 62f : 120f);
            _subPlate.rectTransform.anchoredPosition = new Vector2(0f, y);
            _subText.rectTransform.anchoredPosition = new Vector2(0f, y);
        }

        // ============================== 入力 ==============================

        private enum Act { Primary, Secondary, Close, CloseAlt, CloseAll }

        private static bool Pressed(Act a)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            switch (a)
            {
                case Act.Primary: return (kb != null && kb.spaceKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame);
                case Act.Secondary: return (kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame);
                case Act.Close: return (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
                case Act.CloseAlt: return kb != null && kb.eKey.wasPressedThisFrame;
                case Act.CloseAll: return (kb != null && kb.tabKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
            }
            return false;
#else
            switch (a)
            {
                case Act.Primary: return Input.GetKeyDown(KeyCode.Space);
                case Act.Secondary: return Input.GetKeyDown(KeyCode.R);
                case Act.Close: return Input.GetKeyDown(KeyCode.Escape);
                case Act.CloseAlt: return Input.GetKeyDown(KeyCode.E);
                case Act.CloseAll: return Input.GetKeyDown(KeyCode.Tab);
            }
            return false;
#endif
        }

        /// <summary>回す量（度／フレーム）。左ドラッグ・WASD／矢印・スティック</summary>
        private static Vector2 RotateInput()
        {
            Vector2 v = Vector2.zero;
            float dt = Time.unscaledDeltaTime;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                var d = mouse.delta.ReadValue();
                v += new Vector2(-d.x, d.y) * 0.35f;
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                v += new Vector2(-x, y) * 150f * dt;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var s = gp.leftStick.ReadValue() + gp.rightStick.ReadValue();
                if (s.sqrMagnitude > 0.04f) v += new Vector2(-s.x, s.y) * 180f * dt;
            }
#else
            if (Input.GetMouseButton(0)) v += new Vector2(-Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 6f;
#endif
            return v;
        }

        private static float ZoomInput()
        {
#if ENABLE_INPUT_SYSTEM
            float z = 0f;
            var mouse = Mouse.current;
            if (mouse != null) z += mouse.scroll.ReadValue().y * 0.0004f;
            var gp = Gamepad.current;
            if (gp != null) z += (gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue()) * 0.6f * Time.unscaledDeltaTime;
            return z;
#else
            return Input.mouseScrollDelta.y * 0.04f;
#endif
        }

        // ============================== UI ==============================

        private void BuildUI()
        {
            _canvas = UiTheme.Canvas(transform, "InspectCanvas", 58);
            var root = _canvas.transform;
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // 見出し（上中央。小さく）
            _title = UiTheme.Label(root, "Title", UiTheme.FsHud, TextAnchor.UpperCenter, UiTheme.Text, display: true);
            UiTheme.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -UiTheme.SafeY), new Vector2(1200f, 40f));
            var rule = UiTheme.Fill(root, "TitleRule", UiTheme.WithAlpha(UiTheme.Accent, 0.6f));
            UiTheme.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -UiTheme.SafeY - 46f), new Vector2(90f, UiTheme.Hairline));
            _status = UiTheme.Label(root, "Status", UiTheme.FsSmall, TextAnchor.UpperCenter, UiTheme.TextSub);
            UiTheme.Place(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -UiTheme.SafeY - 58f), new Vector2(600f, 30f));

            // 操作ガイド（下中央の暗い帯）
            _guideBar = UiTheme.Fill(root, "GuideBar", UiTheme.WithAlpha(Color.black, 0.55f));
            UiTheme.Place(_guideBar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 10f), new Vector2(900f, 52f));
            _guide = UiTheme.Label(root, "Guide", UiTheme.FsSmall, TextAnchor.MiddleCenter, UiTheme.Text);
            UiTheme.Place(_guide.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 10f), new Vector2(1700f, 40f));
            _guide.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 読む画面
            _readPanel = UiTheme.Fill(root, "Read", UiTheme.Panel, raycast: true).gameObject;
            var prt = (RectTransform)_readPanel.transform;
            UiTheme.Place(prt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(1040f, 800f));
            _readTitle = UiTheme.Label(prt, "Title", 32, TextAnchor.UpperLeft, UiTheme.Text, display: true);
            UiTheme.Place(_readTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(920f, 50f));
            _readNote = UiTheme.Label(prt, "Note", UiTheme.FsSmall, TextAnchor.UpperRight, UiTheme.TextSub);
            UiTheme.Place(_readNote.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(920f, 30f));
            var rrule = UiTheme.Fill(prt, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.7f));
            UiTheme.Place(rrule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(-460f, -96f), new Vector2(120f, UiTheme.Hairline));
            var body = UiTheme.Label(prt, "Body", UiTheme.FsBody, TextAnchor.UpperLeft, UiTheme.Text, shadow: false);
            UiTheme.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(920f, 640f));
            body.lineSpacing = 1.35f;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            _marker = MarkerText.Create(prt, body);
            _marker.Marker = UiTheme.WithAlpha(UiTheme.Accent, 0.4f);
            _marker.OnAdded = s => { UiSound.Decide(); ToastUI.Show("メモに書き写した"); };
            _marker.OnRemoved = () => UiSound.Cancel();
            _readPanel.SetActive(false);

            // 字幕（閉じた後も流れるよう、別の入れ物に）
            var subCanvas = UiTheme.Canvas(transform, "MonologueCanvas", 65, raycast: false);   // 手帳（60）より上
            _subGroup = subCanvas.gameObject.AddComponent<CanvasGroup>();
            _subGroup.alpha = 0f;
            _subGroup.blocksRaycasts = false;
            _subPlate = UiTheme.Fill(subCanvas.transform, "Scrim", UiTheme.WithAlpha(Color.black, 0.6f));
            UiTheme.Place(_subPlate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 120f), new Vector2(600f, 56f));
            _subText = UiTheme.Label(subCanvas.transform, "Text", UiTheme.FsBody, TextAnchor.MiddleCenter, UiTheme.Text);
            UiTheme.Place(_subText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, UiTheme.SafeY + 120f), new Vector2(1400f, 80f));

            _canvas.gameObject.SetActive(false);
        }
    }
}
