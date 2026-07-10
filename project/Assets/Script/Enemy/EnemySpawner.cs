using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 来訪フェーズ開始で探索者を生成する（プレハブ不要）
    /// ロング黒髪・和装をプリミティブで表現
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("配置（シーンビルダーが自動設定）")]
        public Transform EntryPoint;
        public Transform ExitPoint;
        public Transform[] PatrolPoints;

        private SearcherController _current;

        private void OnEnable()
        {
            GameEvents.OnVisitStart += Spawn;
            GameEvents.OnPlayerCaught += DespawnImmediate;
            GameEvents.OnSpecialDeath += HandleSpecialDeath;
            GameEvents.OnGameOver += DespawnImmediate;
            GameEvents.OnGameClear += DespawnImmediate;
        }

        private void OnDisable()
        {
            GameEvents.OnVisitStart -= Spawn;
            GameEvents.OnPlayerCaught -= DespawnImmediate;
            GameEvents.OnSpecialDeath -= HandleSpecialDeath;
            GameEvents.OnGameOver -= DespawnImmediate;
            GameEvents.OnGameClear -= DespawnImmediate;
        }

        private void HandleSpecialDeath(string id) => DespawnImmediate();

        private void Spawn(SearcherType type)
        {
            DespawnImmediate();
            Vector3 pos = EntryPoint != null ? EntryPoint.position : transform.position;

            // 不具合対策：BuildVisual内でプリミティブ生成やコンポーネント追加（Light等）が
            // 行われる間、ルートのtransform.positionが未設定（＝ワールド原点）のままだと
            // その一瞬だけ原点に敵の見た目が出現しうる。
            // → ルートGameObjectを先に作って位置を確定させてから、
            //   子オブジェクト（見た目）とSearcherControllerを組み立てる。
            var root = new GameObject($"Searcher_{type}");
            root.transform.position = pos;

            BuildVisualInto(root, type);

            var ctrl = root.AddComponent<SearcherController>();
            ctrl.Type = type;
            ctrl.PatrolPoints = PatrolPoints;
            ctrl.ExitPoint = (ExitPoint != null ? ExitPoint : EntryPoint != null ? EntryPoint : transform).position;
            _current = ctrl;
        }

        private void DespawnImmediate()
        {
            if (_current != null) { Destroy(_current.gameObject); _current = null; }
        }

        /// <summary>ロング黒髪・和装の探索者をプリミティブで構築（新規ルートを作って返す）。
        /// 呼び出し側で位置を未設定のまま使う場合、生成の一瞬だけ原点(0,0,0)に
        /// 出現しうる点に注意（ResidentSearcherのように既に位置確定済みの親へ
        /// SetParentする用途を想定）。位置確定済みのルートに組み込みたい場合は
        /// BuildVisualInto を使うこと。</summary>
        public static GameObject BuildVisual(SearcherType type)
        {
            var root = new GameObject($"Searcher_{type}");
            BuildVisualInto(root, type);
            return root;
        }

        /// <summary>既存のGameObject（位置設定済み）に見た目パーツを組み込む</summary>
        public static void BuildVisualInto(GameObject root, SearcherType type)
        {
            // 和服の胴（細い円柱）
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            body.transform.localScale = new Vector3(0.55f, 0.95f, 0.4f);
            Object.Destroy(body.GetComponent<Collider>());
            SetColor(body, new Color(0.06f, 0.05f, 0.08f));   // 黒い和服

            // 頭
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            head.transform.localScale = Vector3.one * 0.34f;
            Object.Destroy(head.GetComponent<Collider>());
            SetColor(head, new Color(0.82f, 0.78f, 0.72f));   // 蒼白い肌

            // ロング黒髪（前後に垂らす）
            var hairBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hairBack.name = "HairBack";
            hairBack.transform.SetParent(root.transform, false);
            hairBack.transform.localPosition = new Vector3(0f, 1.35f, -0.16f);
            hairBack.transform.localScale = new Vector3(0.42f, 1.3f, 0.12f);
            Object.Destroy(hairBack.GetComponent<Collider>());
            SetColor(hairBack, new Color(0.02f, 0.02f, 0.03f));

            var hairTop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hairTop.name = "HairTop";
            hairTop.transform.SetParent(root.transform, false);
            hairTop.transform.localPosition = new Vector3(0f, 2.02f, -0.02f);
            hairTop.transform.localScale = new Vector3(0.38f, 0.34f, 0.4f);
            Object.Destroy(hairTop.GetComponent<Collider>());
            SetColor(hairTop, new Color(0.02f, 0.02f, 0.03f));

            // 単眼の眼光（種類色）
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.97f, 0.16f);
            eye.transform.localScale = Vector3.one * 0.1f;
            Object.Destroy(eye.GetComponent<Collider>());
            Color c = SearcherController.GetTypeColor(type);
            var em = SetColor(eye, c);
            em.EnableKeyword("_EMISSION");
            em.SetColor("_EmissionColor", c * 2.5f);

            var lightGo = new GameObject("EyeLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.97f, 0.3f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c; light.range = 6f; light.intensity = 1.8f;
        }

        private static Material SetColor(GameObject go, Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = c };
            go.GetComponent<Renderer>().material = mat;
            return mat;
        }
    }
}
