using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>LoopPrototypeの部屋をプレイモードなしで撮影する検証用ツール</summary>
    public static class AdShotVerify
    {
        [MenuItem("Tools/EscapePrototype/Debug/中吊り広告を撮影")]
        public static void ShootAd()
        {
            var ad = ShowOnly("train", "Room_train/HangingAd/Find_ad");
            if (ad == null) return;
            Shoot(ad.transform.position + new Vector3(0f, -0.15f, -2.2f), Quaternion.Euler(6f, 0f, 0f), 45f,
                  "Assets/Screenshots/ad_applied.png", 800, 800);
            Done();
        }

        [MenuItem("Tools/EscapePrototype/Debug/電車車内を撮影")]
        public static void ShootTrain()
        {
            var room = ShowOnly("train", "Rooms/Room_train");
            if (room == null) return;
            var o = room.transform.position;
            // 入口から奥を見る全景（一点透視）
            Shoot(o + new Vector3(0f, 1.6f, -7.6f), Quaternion.Euler(3f, 0f, 0f), 62f, "Assets/Screenshots/train_01_overview.png", 1200, 675);
            // シートと窓の寄り
            Shoot(o + new Vector3(-0.3f, 1.4f, -1.0f), Quaternion.Euler(6f, 62f, 0f), 60f, "Assets/Screenshots/train_02_seats.png", 1200, 675);
            // 車端の赤い扉
            Shoot(o + new Vector3(0f, 1.5f, 5.2f), Quaternion.Euler(0f, 0f, 0f), 55f, "Assets/Screenshots/train_03_door.png", 1200, 675);
            // 天井・吊革・中吊り
            Shoot(o + new Vector3(0f, 1.3f, -3.6f), Quaternion.Euler(-28f, 0f, 0f), 65f, "Assets/Screenshots/train_04_ceiling.png", 1200, 675);
            Done();
        }

        [MenuItem("Tools/EscapePrototype/Debug/研究所オフィスを撮影")]
        public static void ShootLab()
        {
            var room = ShowOnly("lab", "Rooms/Room_lab");
            if (room == null) return;
            var o = room.transform.position;
            // 入口から全景
            Shoot(o + new Vector3(-1.5f, 1.6f, -5.2f), Quaternion.Euler(4f, 20f, 0f), 70f, "Assets/Screenshots/lab_01_entry.png", 1200, 675);
            // バーカウンター側から共有デスクを見る
            Shoot(o + new Vector3(-3.0f, 1.55f, 3.5f), Quaternion.Euler(6f, 130f, 0f), 68f, "Assets/Screenshots/lab_02_bar.png", 1200, 675);
            // 奥（木目壁・掲示板）
            Shoot(o + new Vector3(0.5f, 1.6f, 0.5f), Quaternion.Euler(2f, -25f, 0f), 60f, "Assets/Screenshots/lab_03_backwall.png", 1200, 675);
            // ガラス会議室
            Shoot(o + new Vector3(2.0f, 1.6f, 0.0f), Quaternion.Euler(3f, 40f, 0f), 60f, "Assets/Screenshots/lab_04_glassroom.png", 1200, 675);
            Done();
        }

        /// <summary>シーン再生成→v2部屋の撮影を一度のバッチ起動で行う</summary>
        public static void BuildAndShootV2()
        {
            LoopPrototypeBuilder.BuildScene();
            ShootScenarioV2();
        }

        /// <summary>シナリオv2で追加した部屋を一括撮影（バッチモード実行可）</summary>
        [MenuItem("Tools/EscapePrototype/Debug/シナリオv2の部屋を撮影")]
        public static void ShootScenarioV2()
        {
            // (部屋Id, 奥行き, 追い撮り位置/回転（nullなら無し）)
            var rooms = new (string id, float d)[]
            {
                ("analysis", 9f), ("saeki_home", 8f), ("ward", 12f),
                ("core_ante", 8f), ("mizuno_apart", 9f), ("data_room", 10f),
                ("system_room", 10f), ("kuroda_home", 8f), ("core_main", 12f), ("son_room", 5f),
            };
            foreach (var r in rooms)
            {
                var room = ShowOnly(r.id, "Rooms/Room_" + r.id);
                if (room == null) continue;
                var o = room.transform.position;
                Shoot(o + new Vector3(0f, 1.6f, -r.d * 0.5f + 1.1f), Quaternion.Euler(2f, 0f, 0f), 72f,
                      $"Assets/Screenshots/v2_{r.id}.png", 1200, 675);
            }
            // 残響の代表カット: 口論の視点差ペア
            var saeki = ShowOnly("saeki_home", "Rooms/Room_saeki_home");
            if (saeki != null)
                Shoot(saeki.transform.position + new Vector3(1.6f, 1.55f, -0.6f), Quaternion.Euler(4f, -90f, 0f), 60f,
                      "Assets/Screenshots/v2_echo_saeki_view.png", 1200, 675);
            var data = ShowOnly("data_room", "Rooms/Room_data_room");
            if (data != null)
                Shoot(data.transform.position + new Vector3(0.6f, 1.55f, -2.2f), Quaternion.Euler(0f, -12f, 0f), 60f,
                      "Assets/Screenshots/v2_echo_kuroda_view.png", 1200, 675);
            Done();
        }

        private static GameObject ShowOnly(string roomId, string findPath)
        {
            EditorSceneManager.OpenScene("Assets/EscapePrototype/LoopPrototype.unity");
            var corridor = GameObject.Find("Corridor");
            if (corridor != null) corridor.SetActive(false);
            foreach (var r in Object.FindObjectsByType<LoopRoomRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                r.gameObject.SetActive(r.Id == roomId);
            var go = GameObject.Find(findPath);
            if (go == null) Debug.LogError($"{findPath} が見つかりません");
            return go;
        }

        private static void Shoot(Vector3 pos, Quaternion rot, float fov, string path, int w, int h)
        {
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            camGo.transform.SetPositionAndRotation(pos, rot);

            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Shot] " + path);
        }

        private static void Done()
        {
            AssetDatabase.Refresh();
            Debug.Log("[Shot] 撮影完了");
        }
    }
}
