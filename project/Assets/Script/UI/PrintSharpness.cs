using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 調べる画面の実物に載せた文字（World Space のキャンバス）の細かさを、画面に映る大きさに合わせる。
    /// 文字の画像は「キャンバスの1単位を何画素で描くか（CanvasScaler.dynamicPixelsPerUnit）」で作られ、縮小用の段階（ミップマップ）が無い。
    /// 細かさが固定だと、遠くで小さく映る時は縮めすぎてギザギザになり、寄るとぼやける（画面の解像度が変わっても同じ）。
    /// そこで毎コマ、キャンバスの1単位が画面で何画素に映るかを測り、その1.7倍の細かさで描く
    /// （等倍だと細い線がにじむ。2倍を超えて縮めると、縮小の時に飛ばされる画素が出て再びギザギザになる）。
    /// 変えると文字の画像の描き直しになるので、細かさは 2^(1/4) 刻みにし、今の段から1段以上ずれた時だけ変える。
    /// ※AddComponent で付けるのでファイル名と一致させてある
    /// </summary>
    public class PrintSharpness : MonoBehaviour
    {
        private const float Oversample = 1.7f;
        private const float Step = 1.189207f;      // 2^(1/4)
        private const float MinPpu = 0.5f;
        private const float MaxGlyphPx = 160f;     // いちばん大きい文字でもこの画素数まで（文字の画像置き場があふれないように）

        private CanvasScaler _scaler;
        private RectTransform _rt;
        private float _maxFont = 1f;
        private int _level = int.MinValue;
        private readonly Vector3[] _corners = new Vector3[4];

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _rt = (RectTransform)transform;
        }

        private void Start()
        {
            foreach (var t in GetComponentsInChildren<Text>(true)) _maxFont = Mathf.Max(_maxFont, t.fontSize);
        }

        private void LateUpdate()
        {
            var cam = Camera.main;   // 調べる画面のカメラは主カメラの子で、向きと画角が同じ
            if (cam == null || _scaler == null) return;
            var size = _rt.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            _rt.GetWorldCorners(_corners);   // 0=左下 1=左上 3=右下
            Vector2 s0 = cam.WorldToScreenPoint(_corners[0]);
            Vector2 s1 = cam.WorldToScreenPoint(_corners[1]);
            Vector2 s3 = cam.WorldToScreenPoint(_corners[3]);
            float ppu = Mathf.Max((s3 - s0).magnitude / size.x, (s1 - s0).magnitude / size.y) * Oversample;
            ppu = Mathf.Clamp(ppu, MinPpu, Mathf.Max(MinPpu, MaxGlyphPx / _maxFont));
            float exact = Mathf.Log(ppu, Step);
            if (_level != int.MinValue && Mathf.Abs(exact - _level) < 1f) return;   // 行ったり来たりで描き直し続けないように
            _level = Mathf.RoundToInt(exact);
            _scaler.dynamicPixelsPerUnit = Mathf.Pow(Step, _level);
        }
    }
}
