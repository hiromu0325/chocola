using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EscapeProto
{
    /// <summary>
    /// InteractOutline の HDRP 版の描画パス（HDRP にはパスを差し込む仕組みが無いので、カスタムパスで描く）。
    /// ・Mask（ポストエフェクトの前）：対象の形を下書きに 1 で塗る。TAA で画面が揺らされている間に描くので、
    ///   場面の深度と同じ揺れになり、見えている所だけを正しく塗れる
    /// ・Composite（ポストエフェクトの後）：下書きの縁だけを画面に重ねる（色調補正でくすまず、UI と同じ真鍮色のまま）
    /// 下書きは2つのパスで共有する
    /// </summary>
    [System.Serializable]
    public class InteractOutlineHdrpPass : CustomPass
    {
        public enum Step { Mask, Composite }

        public Step Kind;
        public InteractOutline Owner;

        private static RTHandle _maskRT;
        private static int _users;
        private static bool _maskDrawn;

        private static readonly int MaskId = Shader.PropertyToID("_OutlineMask");
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int HaloWidthId = Shader.PropertyToID("_HaloWidth");
        private static readonly int HaloAlphaId = Shader.PropertyToID("_HaloAlpha");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (_users++ == 0)
                _maskRT = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                    colorFormat: GraphicsFormat.R8_UNorm, useDynamicScale: true, name: "_InteractOutlineMask");
        }

        protected override void Cleanup()
        {
            if (--_users > 0) return;
            _maskRT?.Release();
            _maskRT = null;
        }

        protected override void Execute(CustomPassContext ctx)
        {
            var o = Owner;
            if (o == null || _maskRT == null || ctx.hdCamera.camera.cameraType != CameraType.Game) return;
            if (Kind == Step.Mask) DrawMask(ctx, o);
            else DrawComposite(ctx, o);
        }

        private static void DrawMask(CustomPassContext ctx, InteractOutline o)
        {
            _maskDrawn = o.HasShapes;
            if (!_maskDrawn) return;
            CoreUtils.SetRenderTarget(ctx.cmd, _maskRT, ctx.cameraDepthBuffer, ClearFlag.Color, Color.clear);
            foreach (var (r, subs) in o.Renderers)
            {
                if (r == null) continue;
                for (int s = 0; s < subs; s++) ctx.cmd.DrawRenderer(r, o.MaskMaterial, s, 0);
            }
            // 見た目の無い物の箱は深度なしで（箱が見た目の内側にあっても形が出るように）
            foreach (var (mesh, t, local) in o.Shapes)
            {
                if (mesh == null || t == null) continue;
                var m = t.localToWorldMatrix * local;
                for (int s = 0; s < mesh.subMeshCount; s++) ctx.cmd.DrawMesh(mesh, m, o.MaskMaterial, s, 1);
            }
        }

        private static void DrawComposite(CustomPassContext ctx, InteractOutline o)
        {
            if (!_maskDrawn || !o.HasShapes) return;
            float k = Mathf.Max(0.75f, ctx.hdCamera.actualHeight / 1080f);
            var mat = o.CompositeMaterial;
            mat.SetColor(ColorId, o.Color);
            mat.SetFloat(WidthId, o.Width * k);
            mat.SetFloat(HaloWidthId, o.HaloWidth * k);
            mat.SetFloat(HaloAlphaId, o.HaloAlpha);
            ctx.propertyBlock.SetTexture(MaskId, _maskRT);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, mat, ctx.propertyBlock, shaderPassId: 0);
        }
    }
}
