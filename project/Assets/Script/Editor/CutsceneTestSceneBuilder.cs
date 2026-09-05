using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

namespace EscapeProto
{
    /// <summary>
    /// カットシーン（Timeline）のテストシーンを生成する。
    ///
    /// ・簡単なセット（薄暗い部屋＋扉＋人影）を組む
    /// ・TimelineAsset をコードで生成し、以下のトラックを作る
    ///     - Animation : カメラのカット割り（3カット。クリップを並べて瞬時に切り替える）
    ///     - Activation: 人影の出現／消失、扉灯の点灯
    ///     - Subtitle  : 字幕（自作のカスタムトラック）
    /// ・CutsceneDirector が再生・レターボックス・スキップを担当する
    ///
    /// Timelineアセットは Assets/EscapePrototype/Cutscenes/TestCutscene.playable に保存されるので、
    /// 生成後は Timelineウィンドウで自由に編集できる（クリップの追加・字幕の書き換え等）。
    /// </summary>
    public static class CutsceneTestSceneBuilder
    {
        private const string ScenePath = "Assets/EscapePrototype/CutsceneTest.unity";
        private const string CutsceneDir = "Assets/EscapePrototype/Cutscenes";
        private const string TimelinePath = CutsceneDir + "/TestCutscene.playable";
        private const string MatDir = "Assets/EscapePrototype/MoodMaterials";

        private const float RoomW = 10f, RoomD = 12f, RoomH = 3.0f;

        [MenuItem("Tools/EscapePrototype/Cutscene/テストシーンを生成")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MeshCache.Clear();
            EnsureFolders();

            var set = BuildSet();
            var camera = BuildCamera();
            var actor = BuildActor();
            var doorLight = BuildDoorLight();
            var subtitles = BuildSubtitleView();
            BuildLighting();
            BuildHint();

            // Timelineアセットを生成し、シーンのオブジェクトに紐付ける
            var timeline = BuildTimeline(out var camClips);
            var director = BuildDirector(timeline, camera, actor, doorLight, subtitles, camClips);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CutsceneTest] 生成完了\n  シーン: {ScenePath}\n  Timeline: {TimelinePath}\n" +
                      "  Playで自動再生。Esc/Space長押しでスキップ。Timelineウィンドウで編集できます。");
            Selection.activeObject = director;
        }

        /// <summary>
        /// 生成したカットシーンを、プレイモードに入らずに検証する。
        /// Timelineを時刻ごとに評価してカメラの動き・トラック構成を確認し、
        /// 各カットのスクリーンショットを Assets/Screenshots へ保存する。
        /// </summary>
        [MenuItem("Tools/EscapePrototype/Cutscene/テストシーンを生成して検証")]
        public static void BuildAndVerify()
        {
            BuildScene();
            VerifyCutscene();
        }

        [MenuItem("Tools/EscapePrototype/Cutscene/カットシーンを検証（評価＋撮影）")]
        public static void VerifyCutscene()
        {
            var director = Object.FindFirstObjectByType<PlayableDirector>();
            if (director == null || director.playableAsset == null)
            {
                Debug.LogError("[CutsceneTest] PlayableDirector が見つかりません。先にシーンを生成してください。");
                return;
            }
            var timeline = (TimelineAsset)director.playableAsset;

            // --- 構成のサマリ ---
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[CutsceneTest] 検証 — 尺 {timeline.duration:0.0}秒 / トラック {timeline.outputTrackCount}本");
            foreach (var track in timeline.GetOutputTracks())
            {
                var binding = director.GetGenericBinding(track);
                int clips = 0;
                foreach (var _ in track.GetClips()) clips++;
                sb.AppendLine($"  ・{track.name} ({track.GetType().Name}) クリップ{clips}個 " +
                              $"→ バインド: {(binding != null ? binding.name : "★未設定")}");
            }
            Debug.Log(sb.ToString());

            // --- 時刻ごとに評価してカメラの動きを確認＋撮影 ---
            var camera = director.GetGenericBinding(GetTrack(timeline, "Camera")) as GameObject;
            var cam = camera != null ? camera.GetComponent<Camera>() : null;
            string shotDir = "Assets/Screenshots";
            if (!AssetDatabase.IsValidFolder(shotDir)) AssetDatabase.CreateFolder("Assets", "Screenshots");

            double[] samples = { 1.0, 3.5, 5.0, 6.5, 8.5, 11.0 };
            foreach (double t in samples)
            {
                director.time = t;
                director.Evaluate();

                string pos = camera != null
                    ? $"pos={camera.transform.position.ToString("F2")} rot={camera.transform.eulerAngles.ToString("F1")}"
                    : "カメラ未バインド";
                Debug.Log($"[CutsceneTest] t={t:0.0}s  {pos}");

                if (cam != null) Capture(cam, $"{shotDir}/cutscene_t{(t * 10):000}.png");
            }
            director.time = 0d;
            director.Evaluate();
            AssetDatabase.Refresh();
            Debug.Log("[CutsceneTest] 検証完了（スクリーンショットは Assets/Screenshots）");
        }

        private static TrackAsset GetTrack(TimelineAsset timeline, string name)
        {
            foreach (var t in timeline.GetOutputTracks())
                if (t.name == name) return t;
            return null;
        }

        /// <summary>プレイモードに入らずカメラの絵をPNG保存する</summary>
        private static void Capture(Camera cam, string path)
        {
            const int w = 800, h = 450;
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EscapePrototype"))
                AssetDatabase.CreateFolder("Assets", "EscapePrototype");
            if (!AssetDatabase.IsValidFolder(CutsceneDir))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "Cutscenes");
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "MoodMaterials");
        }

        // ============================== セット ==============================

        private static GameObject BuildSet()
        {
            var root = new GameObject("Set");
            var t = root.transform;
            var wall = GetMat("CS_Wall", new Color(0.55f, 0.55f, 0.54f), 0.1f);
            var floor = GetMat("CS_Floor", new Color(0.34f, 0.32f, 0.30f), 0.15f);
            var ceil = GetMat("CS_Ceiling", new Color(0.28f, 0.28f, 0.29f), 0.1f);
            var frame = GetMat("CS_DoorFrame", new Color(0.30f, 0.33f, 0.36f), 0.35f);
            var doorMat = GetMat("CS_Door", new Color(0.42f, 0.47f, 0.52f), 0.42f);

            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;
            Box(t, "Floor", new Vector3(0f, -0.06f, 0f), new Vector3(RoomW, 0.12f, RoomD), floor);
            Box(t, "Ceiling", new Vector3(0f, RoomH + 0.06f, 0f), new Vector3(RoomW, 0.12f, RoomD), ceil);
            Box(t, "Wall_N", new Vector3(0f, RoomH * 0.5f, hd), new Vector3(RoomW, RoomH, 0.15f), wall);
            Box(t, "Wall_S", new Vector3(0f, RoomH * 0.5f, -hd), new Vector3(RoomW, RoomH, 0.15f), wall);
            Box(t, "Wall_E", new Vector3(hw, RoomH * 0.5f, 0f), new Vector3(0.15f, RoomH, RoomD), wall);
            Box(t, "Wall_W", new Vector3(-hw, RoomH * 0.5f, 0f), new Vector3(0.15f, RoomH, RoomD), wall);

            // 北壁の扉（カット2で寄る対象）
            var door = new GameObject("Door");
            door.transform.SetParent(t, false);
            door.transform.localPosition = new Vector3(0f, 0f, hd - 0.1f);
            Box(door.transform, "Panel", new Vector3(0f, 1.08f, 0f), new Vector3(0.96f, 2.16f, 0.07f), doorMat);
            Box(door.transform, "Jamb_L", new Vector3(-0.55f, 1.13f, 0f), new Vector3(0.1f, 2.26f, 0.12f), frame);
            Box(door.transform, "Jamb_R", new Vector3(0.55f, 1.13f, 0f), new Vector3(0.1f, 2.26f, 0.12f), frame);
            Box(door.transform, "Lintel", new Vector3(0f, 2.3f, 0f), new Vector3(1.2f, 0.1f, 0.12f), frame);

            // 小物（画になるように）
            var wood = GetMat("CS_Furniture", new Color(0.35f, 0.26f, 0.19f), 0.2f);
            var desk = new GameObject("Desk");
            desk.transform.SetParent(t, false);
            desk.transform.localPosition = new Vector3(-2.6f, 0f, -1.5f);
            Box(desk.transform, "Top", new Vector3(0f, 0.72f, 0f), new Vector3(1.6f, 0.06f, 0.8f), wood);
            foreach (var (dx, dz) in new[] { (-0.7f, -0.33f), (0.7f, -0.33f), (-0.7f, 0.33f), (0.7f, 0.33f) })
                Box(desk.transform, "Leg", new Vector3(dx, 0.35f, dz), new Vector3(0.07f, 0.7f, 0.07f), wood);

            var paper = GetMat("CS_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            Box(desk.transform, "Note", new Vector3(0.1f, 0.76f, 0f), new Vector3(0.34f, 0.02f, 0.26f), paper);

            return root;
        }

        /// <summary>カットシーン用カメラ（Animatorを付けてTimelineから動かす）</summary>
        private static GameObject BuildCamera()
        {
            var go = new GameObject("CutsceneCamera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(-3.2f, 1.6f, -4.5f);
            go.transform.rotation = Quaternion.Euler(4f, 28f, 0f);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            go.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            go.AddComponent<Animator>();   // AnimationTrackのバインド先に必要
            return go;
        }

        /// <summary>カット3で現れる人影</summary>
        private static GameObject BuildActor()
        {
            var root = new GameObject("Actor");
            root.transform.position = new Vector3(0f, 0f, 4.4f);
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            EnemySpawner.BuildVisualInto(root, SearcherType.Sight);

            // BuildVisualInto はランタイム前提で Object.Destroy を使うため、
            // エディタでの生成ではコライダーが残る。ここで確実に取り除く
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            root.SetActive(false);   // ActivationTrackで出現させる
            return root;
        }

        /// <summary>扉を照らす非常灯（カット2で点灯）。ランプ本体も一緒に出す</summary>
        private static GameObject BuildDoorLight()
        {
            var go = new GameObject("DoorLight");
            // 扉の手前・天井寄りに置き、扉（+Z方向）を照らす
            go.transform.position = new Vector3(0f, 2.5f, RoomD * 0.5f - 1.3f);
            go.transform.rotation = Quaternion.Euler(28f, 0f, 0f);

            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = new Color(1f, 0.3f, 0.24f);
            l.intensity = 14f;
            l.range = 8f;
            l.spotAngle = 78f;
            l.shadows = LightShadows.Soft;

            // 光源が見えるようランプ本体（赤く発光する小球）を添える
            var lampMat = GetMat("CS_EmergencyLamp", new Color(0.9f, 0.2f, 0.15f), 0.3f);
            lampMat.EnableKeyword("_EMISSION");
            lampMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            lampMat.SetColor("_EmissionColor", new Color(1f, 0.25f, 0.18f) * 4f);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(go.transform, false);
            bulb.transform.localPosition = Vector3.zero;
            bulb.transform.localScale = Vector3.one * 0.16f;
            bulb.GetComponent<Renderer>().sharedMaterial = lampMat;
            Object.DestroyImmediate(bulb.GetComponent<Collider>());

            go.SetActive(false);
            return go;
        }

        private static GameObject BuildSubtitleView()
        {
            var go = new GameObject("Subtitles");
            go.AddComponent<CutsceneSubtitleView>();
            return go;
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.10f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fogDensity = 0.035f;

            var root = new GameObject("Lights").transform;
            foreach (var (pos, intensity) in new[]
            {
                (new Vector3(-2.5f, RoomH - 0.4f, -2f), 2.2f),
                (new Vector3(2.5f, RoomH - 0.4f, 2f), 1.4f),
            })
            {
                var go = new GameObject("RoomLight");
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.93f, 0.82f);
                l.intensity = intensity;
                l.range = 10f;
                l.shadows = LightShadows.Soft;
            }
        }

        /// <summary>操作説明（テストシーン用）</summary>
        private static void BuildHint()
        {
            var go = new GameObject("Hint");
            go.AddComponent<CutsceneTestHint>();
        }

        // ============================== Timeline生成 ==============================

        /// <summary>カメラのカット割り・演出・字幕を含むTimelineを組む</summary>
        private static TimelineAsset BuildTimeline(out List<AnimationClip> cameraClips)
        {
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "TestCutscene";
            timeline.editorSettings.frameRate = 60f;
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            cameraClips = new List<AnimationClip>();

            // ---- カメラトラック（3カット）----
            var camTrack = timeline.CreateTrack<AnimationTrack>(null, "Camera");
            // カット1: 部屋を左から右へゆっくりドリー（俯瞰ぎみ）
            var cut1 = CameraClip("Cut1_Dolly",
                new Vector3(-3.4f, 1.65f, -4.6f), new Vector3(-0.6f, 1.65f, -3.6f),
                new Vector3(3f, 22f, 0f), new Vector3(3f, 6f, 0f), 4.0f);
            // カット2: 扉へ寄る（緊張）
            var cut2 = CameraClip("Cut2_Door",
                new Vector3(0f, 1.55f, 0.4f), new Vector3(0f, 1.55f, 2.6f),
                new Vector3(0f, 0f, 0f), new Vector3(-2f, 0f, 0f), 3.5f);
            // カット3: 人影を捉える（引きの構図。ゆっくり寄って傾く）
            var cut3 = CameraClip("Cut3_Actor",
                new Vector3(2.7f, 1.6f, -0.8f), new Vector3(2.1f, 1.6f, 0.3f),
                new Vector3(2f, -27f, 0f), new Vector3(2f, -23f, 1.5f), 4.0f);
            cameraClips.Add(cut1); cameraClips.Add(cut2); cameraClips.Add(cut3);
            foreach (var c in cameraClips) AssetDatabase.AddObjectToAsset(c, timeline);

            AddClip(camTrack, cut1, 0.0, 4.0);
            AddClip(camTrack, cut2, 4.0, 3.5);
            AddClip(camTrack, cut3, 7.5, 4.0);

            // ---- 演出（Activation）----
            var lightTrack = timeline.CreateTrack<ActivationTrack>(null, "DoorLight");
            var lightClip = lightTrack.CreateDefaultClip();
            lightClip.start = 4.2; lightClip.duration = 7.3;   // カット2〜最後まで点灯

            var actorTrack = timeline.CreateTrack<ActivationTrack>(null, "Actor");
            var actorClip = actorTrack.CreateDefaultClip();
            actorClip.start = 7.2; actorClip.duration = 4.3;   // カット3で出現

            // ---- 字幕（カスタムトラック）----
            var subTrack = timeline.CreateTrack<SubtitleTrack>(null, "Subtitles");
            AddSubtitle(subTrack, 0.4, 3.2, "……ここは、どこだ。", "", new Color(0.95f, 0.95f, 0.95f));
            AddSubtitle(subTrack, 4.2, 3.0, "扉の向こうで、何かが軋んだ。", "", new Color(1f, 0.9f, 0.85f));
            AddSubtitle(subTrack, 7.6, 3.6, "——見つけた。", "？？？", new Color(1f, 0.75f, 0.75f));

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private static void AddClip(AnimationTrack track, AnimationClip clip, double start, double duration)
        {
            var tc = track.CreateClip(clip);
            tc.start = start;
            tc.duration = duration;
            tc.displayName = clip.name;
        }

        private static void AddSubtitle(SubtitleTrack track, double start, double duration,
                                        string text, string speaker, Color color)
        {
            var tc = track.CreateClip<SubtitleClip>();
            tc.start = start;
            tc.duration = duration;
            tc.displayName = text.Length > 12 ? text.Substring(0, 12) + "…" : text;
            // 端をわずかにブレンドして自然にフェードさせる
            tc.easeInDuration = 0.3;
            tc.easeOutDuration = 0.3;
            var asset = (SubtitleClip)tc.asset;
            asset.Subtitle.Text = text;
            asset.Subtitle.Speaker = speaker;
            asset.Subtitle.Color = color;
        }

        /// <summary>始点→終点へ移動＋回転するカメラ用AnimationClipを作る</summary>
        private static AnimationClip CameraClip(string name, Vector3 posFrom, Vector3 posTo,
                                                Vector3 rotFrom, Vector3 rotTo, float length)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };

            void Curve(string prop, float a, float b)
            {
                // ゆるやかな加減速（カットシーンらしい動き）
                var curve = new AnimationCurve(
                    new Keyframe(0f, a, 0f, 0f),
                    new Keyframe(length, b, 0f, 0f));
                clip.SetCurve("", typeof(Transform), prop, curve);
            }

            Curve("localPosition.x", posFrom.x, posTo.x);
            Curve("localPosition.y", posFrom.y, posTo.y);
            Curve("localPosition.z", posFrom.z, posTo.z);
            Curve("localEulerAnglesRaw.x", rotFrom.x, rotTo.x);
            Curve("localEulerAnglesRaw.y", rotFrom.y, rotTo.y);
            Curve("localEulerAnglesRaw.z", rotFrom.z, rotTo.z);
            return clip;
        }

        // ============================== Director（再生役）==============================

        private static PlayableDirector BuildDirector(TimelineAsset timeline, GameObject camera,
                                                      GameObject actor, GameObject doorLight,
                                                      GameObject subtitles, List<AnimationClip> camClips)
        {
            var go = new GameObject("CutsceneDirector");
            var director = go.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;          // CutsceneDirectorが制御する
            director.extrapolationMode = DirectorWrapMode.None;

            // 各トラックのバインド先を設定
            foreach (var track in timeline.GetOutputTracks())
            {
                switch (track)
                {
                    case AnimationTrack _: director.SetGenericBinding(track, camera); break;
                    case SubtitleTrack _: director.SetGenericBinding(track, subtitles.GetComponent<CutsceneSubtitleView>()); break;
                    case ActivationTrack _:
                        director.SetGenericBinding(track, track.name == "Actor" ? actor : doorLight);
                        break;
                }
            }

            var ctrl = go.AddComponent<CutsceneDirector>();
            ctrl.Director = director;
            ctrl.PlayOnStart = true;   // テストシーンなので即再生
            return director;
        }

        // ============================== 共通ヘルパー ==============================

        private static Material GetMat(string name, Color color, float smoothness)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private const float UvPerMeter = 0.5f;
        private static readonly Dictionary<Vector3, Mesh> MeshCache = new Dictionary<Vector3, Mesh>();

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMeshWorldUV(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static Mesh BoxMeshWorldUV(Vector3 s)
        {
            if (MeshCache.TryGetValue(s, out var cached) && cached != null) return cached;
            Vector3 h = s * 0.5f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>();
            var uvs = new List<Vector2>(); var tris = new List<int>();
            void Face(Vector3 c, Vector3 right, Vector3 up, Vector3 n, float uS, float vS)
            {
                int i0 = verts.Count;
                verts.Add(c - right - up); verts.Add(c - right + up);
                verts.Add(c + right + up); verts.Add(c + right - up);
                for (int i = 0; i < 4; i++) norms.Add(n);
                float u = uS * UvPerMeter, v = vS * UvPerMeter;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(u, 0));
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            Face(new Vector3(0, 0, -h.z), new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.back, s.x, s.y);
            Face(new Vector3(0, 0, h.z), new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.forward, s.x, s.y);
            Face(new Vector3(-h.x, 0, 0), new Vector3(0, 0, -h.z), new Vector3(0, h.y, 0), Vector3.left, s.z, s.y);
            Face(new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), Vector3.right, s.z, s.y);
            Face(new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), Vector3.up, s.x, s.z);
            Face(new Vector3(0, -h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, -h.z), Vector3.down, s.x, s.z);
            var mesh = new Mesh { name = $"BoxUV_{s.x:0.##}x{s.y:0.##}x{s.z:0.##}" };
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            MeshCache[s] = mesh;
            return mesh;
        }
    }
}
