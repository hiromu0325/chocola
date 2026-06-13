using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 来訪フェーズ開始イベントを受けて敵を生成する
    /// プレハブ不要：プリミティブから敵を組み立てる（プロトタイプ用）
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("配置（シーンビルダーが自動設定）")]
        public Transform EntryPoint;      // 入口（スポーン位置）
        public Transform ExitPoint;       // 出口（撤収先。未設定なら入口を流用）
        public Transform[] PatrolPoints;  // 巡回ポイント

        private EnemyController _current;

        private void OnEnable()
        {
            GameEvents.OnVisitStart += Spawn;
            GameEvents.OnPlayerCaught += DespawnImmediate;
            GameEvents.OnGameOver += DespawnImmediate;
            GameEvents.OnGameClear += DespawnImmediate;
        }

        private void OnDisable()
        {
            GameEvents.OnVisitStart -= Spawn;
            GameEvents.OnPlayerCaught -= DespawnImmediate;
            GameEvents.OnGameOver -= DespawnImmediate;
            GameEvents.OnGameClear -= DespawnImmediate;
        }

        private void Spawn(EnemyType type)
        {
            DespawnImmediate();

            Vector3 pos = EntryPoint != null ? EntryPoint.position : transform.position;
            var go = BuildEnemyVisual(type);
            go.transform.position = pos;

            var ctrl = go.AddComponent<EnemyController>();
            ctrl.Type = type;
            ctrl.PatrolPoints = PatrolPoints;
            ctrl.ExitPoint = (ExitPoint != null ? ExitPoint : EntryPoint != null ? EntryPoint : transform).position;

            _current = ctrl;
        }

        private void DespawnImmediate()
        {
            if (_current != null)
            {
                Destroy(_current.gameObject);
                _current = null;
            }
        }

        /// <summary>プリミティブで敵の見た目を構築</summary>
        public static GameObject BuildEnemyVisual(EnemyType type)
        {
            var root = new GameObject($"Enemy_{type}");

            // 胴体
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.Destroy(body.GetComponent<Collider>()); // 当たりはCharacterController側
            SetColor(body, new Color(0.08f, 0.06f, 0.08f));

            // 単眼（タイプ色で発光）
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.55f, 0.32f);
            eye.transform.localScale = Vector3.one * 0.28f;
            Object.Destroy(eye.GetComponent<Collider>());
            Color c = EnemyController.GetTypeColor(type);
            var mat = SetColor(eye, c);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", c * 2.5f);

            // 眼光（不気味さ＆視認性）
            var lightGo = new GameObject("EyeLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.55f, 0.45f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = 7f;
            light.intensity = 2.2f;

            return root;
        }

        private static Material SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = c };
            r.material = mat;
            return mat;
        }
    }
}
