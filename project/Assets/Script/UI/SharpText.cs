using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 拡大・縮小される場所（手帳のメモのボード）に置く文字。
    /// Text は文字の画像をキャンバスの倍率だけで作るので、親を localScale で広げるとぼやけ、縮めすぎるとギザギザになる
    /// （文字の画像には縮小用の段階が無い）。ここでは親の拡大率に合わせた細かさで文字の画像を作る。
    /// ・広げた時：拡大率どおりの細かさ（画面と等倍）
    /// ・縮めた時：拡大率の1.7倍（ただし元の細かさまで）。等倍まで落とすと小さな漢字が潰れて太るので、少し細かめに作って縮める
    /// 置き場所・大きさ・改行は Text と同じ（変わるのは描く細かさだけ）。描き直しを減らすため、細かさは 2^(1/8) 刻みに切り上げる。
    /// ※AddComponent で付けるのでファイル名と一致させてある
    /// </summary>
    public class SharpText : Text
    {
        private const float Step = 1.0905077f;        // 2^(1/8)
        private const float ShrinkOversample = 1.7f;

        private readonly TextGenerator _gen = new TextGenerator();
        private readonly UIVertex[] _quad = new UIVertex[4];
        private float _built = 1f;

        /// <summary>文字の画像を作る細かさ（キャンバスの倍率に対する倍率）</summary>
        private float RasterScale
        {
            get
            {
                var c = canvas;
                if (c == null) return 1f;
                float root = c.rootCanvas.transform.lossyScale.x;
                if (root <= 0f) return 1f;
                float k = Mathf.Clamp(rectTransform.lossyScale.x / root, 0.25f, 4f);
                float r = k >= 1f ? k : Mathf.Min(1f, k * ShrinkOversample);
                return r > 0.99f && r < 1.01f ? 1f : Mathf.Pow(Step, Mathf.Ceil(Mathf.Log(r, Step) - 0.01f));
            }
        }

        private void LateUpdate()
        {
            if (!Mathf.Approximately(RasterScale, _built)) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            float r = RasterScale;
            _built = r;
            if (font == null || Mathf.Approximately(r, 1f)) { base.OnPopulateMesh(toFill); return; }

            m_DisableFontTextureRebuiltCallback = true;   // Text と同じ（作っている途中で文字の画像が作り直されても、この結果は有効）
            var size = rectTransform.rect.size;
            var settings = GetGenerationSettings(size);
            settings.scaleFactor = pixelsPerUnit * r;
            // 細かさを変えると字の幅の丸めが少し変わる。1行に収まっている文は、そのせいで折り返さないようにする
            if (settings.horizontalOverflow == HorizontalWrapMode.Wrap && preferredWidth <= size.x + 0.5f)
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            // 文字の画像が作り直されると前の結果の位置（UV）は古くなる。自前の生成器は Text が無効にしてくれないので毎回作る
            _gen.Invalidate();
            _gen.PopulateWithErrors(text, settings, gameObject);

            toFill.Clear();
            var verts = _gen.verts;
            float unitsPerPixel = 1f / settings.scaleFactor;
            for (int i = 0; i < verts.Count; i++)
            {
                int q = i & 3;
                _quad[q] = verts[i];
                _quad[q].position *= unitsPerPixel;
                if (q == 3) toFill.AddUIVertexQuad(_quad);
            }
            m_DisableFontTextureRebuiltCallback = false;
        }
    }
}
