using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// マーカーの線（リボン）を描く。なぞった点の並びを滑らかにして、太さのある帯でつなぐ（両端は丸く、縁は少しぼかす）。
    /// 座標は本文の基準点からの相対（MarkerText と同じ）。※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class MarkerRibbon : MaskableGraphic
    {
        public class Line
        {
            public List<Vector2> Points = new List<Vector2>();
            public float Width;
            public Color Color;
        }

        public readonly List<Line> Lines = new List<Line>();

        private const float Feather = 1.2f;   // 縁のぼかし幅
        private const int CapSteps = 10;      // 丸い端の刻み

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var l in Lines) AddLine(vh, l);
        }

        private static void AddLine(VertexHelper vh, Line l)
        {
            var pts = Smooth(l.Points);
            if (pts.Count == 0) return;
            float r = Mathf.Max(0.5f, l.Width * 0.5f - Feather * 0.5f);
            var c = l.Color;
            var c0 = new Color(c.r, c.g, c.b, 0f);
            if (pts.Count == 1)
            {
                Cap(vh, pts[0], Vector2.up, Vector2.right, r, c, c0, Mathf.PI * 2f);
                return;
            }

            int n = pts.Count;
            int start = vh.currentVertCount;
            Vector2 firstN = Vector2.zero, lastN = Vector2.zero, firstT = Vector2.zero, lastT = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                var tPrev = (pts[Mathf.Max(i, 1)] - pts[Mathf.Max(i, 1) - 1]).normalized;
                var tNext = (pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Min(i + 1, n - 1) - 1]).normalized;
                var t = tPrev + tNext;
                t = t.sqrMagnitude < 1e-6f ? tNext : t.normalized;
                var nrm = new Vector2(-t.y, t.x);
                // 曲がり角で帯が細らないよう、角の二等分線の向きに少し伸ばす
                float d = Mathf.Abs(Vector2.Dot(nrm, new Vector2(-tPrev.y, tPrev.x)));
                float k = 1f / Mathf.Max(0.6f, d);
                var p = pts[i];
                Vert(vh, p + nrm * k * (r + Feather), c0);
                Vert(vh, p + nrm * k * r, c);
                Vert(vh, p - nrm * k * r, c);
                Vert(vh, p - nrm * k * (r + Feather), c0);
                if (i == 0) { firstN = nrm; firstT = t; }
                if (i == n - 1) { lastN = nrm; lastT = t; }
            }
            for (int i = 0; i + 1 < n; i++)
            {
                int a = start + i * 4, b = a + 4;
                for (int s = 0; s < 3; s++)
                {
                    vh.AddTriangle(a + s, a + s + 1, b + s + 1);
                    vh.AddTriangle(a + s, b + s + 1, b + s);
                }
            }
            // 両端を丸く（始点は後ろ向き、終点は前向きに半円）
            Cap(vh, pts[0], firstN, -firstT, r, c, c0, Mathf.PI);
            Cap(vh, pts[n - 1], lastN, lastT, r, c, c0, Mathf.PI);
        }

        /// <summary>中心 p の扇（nrm から始まり、out 側を回って sweep だけ回る）</summary>
        private static void Cap(VertexHelper vh, Vector2 p, Vector2 nrm, Vector2 outDir, float r, Color c, Color c0, float sweep)
        {
            int center = vh.currentVertCount;
            Vert(vh, p, c);
            int first = vh.currentVertCount;
            for (int s = 0; s <= CapSteps; s++)
            {
                float th = sweep * s / CapSteps;
                var dir = nrm * Mathf.Cos(th) + outDir * Mathf.Sin(th);
                Vert(vh, p + dir * r, c);
                Vert(vh, p + dir * (r + Feather), c0);
            }
            for (int s = 0; s < CapSteps; s++)
            {
                int i0 = first + s * 2, i1 = i0 + 2;
                vh.AddTriangle(center, i0, i1);
                vh.AddTriangle(i0, i0 + 1, i1 + 1);
                vh.AddTriangle(i0, i1 + 1, i1);
            }
        }

        private static void Vert(VertexHelper vh, Vector2 p, Color c)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        /// <summary>近すぎる点を間引いて、角を2回削る（Chaikin）＝手で引いた線の揺れをならす</summary>
        private static List<Vector2> Smooth(List<Vector2> src)
        {
            var pts = new List<Vector2>(src.Count);
            foreach (var p in src)
                if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude > 0.25f) pts.Add(p);
            for (int it = 0; it < 2 && pts.Count > 2; it++)
            {
                var next = new List<Vector2>(pts.Count * 2) { pts[0] };
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[i + 1];
                    if (i > 0) next.Add(Vector2.Lerp(a, b, 0.25f));
                    if (i + 2 < pts.Count) next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                next.Add(pts[pts.Count - 1]);
                pts = next;
            }
            return pts;
        }
    }
}
