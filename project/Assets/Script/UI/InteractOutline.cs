using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EscapeProto
{
    /// <summary>
    /// E で何かできる物のアウトライン。レティクルが合って「[E] ○○」の案内が出ている間だけ、その物の輪郭を
    /// 真鍮色の線で縁取る（HUDManager が SetTarget で渡す）。常に全部には出さない（隠してある物が分かってしまうので）。
    /// 描き方：対象の形を別の画面に 1 で塗り（見えている所だけ。カメラの深度と比べる。ポストエフェクトの前）、
    /// その縁だけを画面に重ねる（ポストエフェクトの後＝色調補正でくすまず、UI と同じ真鍮色のまま）
    /// → 物の形や面の向きに関係なく、画面上で一定の太さの線になる。明るい所でも暗い所でも読めるよう、
    /// 真鍮の線の外側に薄い黒の縁を付ける。描画の設定アセットは触らず、ゲームのカメラが描く直前に描画パスを差し込む。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class InteractOutline : MonoBehaviour
    {
        public static InteractOutline Instance { get; private set; }

        /// <summary>線の色（UI の主アクセント＝真鍮）</summary>
        public Color Color = new Color(0.78f, 0.6f, 0.32f, 0.95f);
        /// <summary>線の太さ（1080p での画素。画面の高さに合わせて伸縮）</summary>
        public float Width = 2.4f;
        /// <summary>外側の黒い縁の太さ（画素）と濃さ</summary>
        public float HaloWidth = 1.6f, HaloAlpha = 0.45f;

        private Component _target;
        private readonly List<(Renderer r, int subs)> _renderers = new List<(Renderer, int)>();
        // 見た目の無い物（部屋のモデルに作り込まれた端末や扉など）は、隠した箱か当たり判定の箱の形で（深度なしで塗る）
        private readonly List<(Mesh mesh, Transform t, Matrix4x4 local)> _shapes = new List<(Mesh, Transform, Matrix4x4)>();
        private MaskPass _maskPass;
        private CompositePass _compositePass;
        private Material _mask, _composite;
        private static Mesh _cube;

        private void Awake()
        {
            Instance = this;
            var maskShader = Resources.Load<Shader>("Shaders/OutlineMask");
            var compShader = Resources.Load<Shader>("Shaders/OutlineComposite");
            if (maskShader == null || compShader == null) { enabled = false; return; }
            _mask = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
            _composite = new Material(compShader) { hideFlags = HideFlags.HideAndDontSave };
            _maskPass = new MaskPass(this) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            _compositePass = new CompositePass(this) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_mask != null) Destroy(_mask);
            if (_composite != null) Destroy(_composite);
        }

        /// <summary>縁取る物（IInteractable を持つコンポーネント）。null で消す</summary>
        public void SetTarget(Component target)
        {
            if (target == _target) return;
            _target = target;
            Collect();
        }

        /// <summary>
        /// 縁取る形を集める。① 対象とその子の見えている形のうち、当たり判定の近くにあるもの（天井へ伸びる配管などは除く）
        /// ② 無ければ隠した箱の形 ③ それも無ければ当たり判定の箱の形
        /// </summary>
        private void Collect()
        {
            _renderers.Clear();
            _shapes.Clear();
            if (_target == null) return;

            bool hasArea = false;
            var area = new Bounds();
            foreach (var c in _target.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                if (!hasArea) { area = c.bounds; hasArea = true; } else area.Encapsulate(c.bounds);
            }
            if (hasArea) area.Expand(0.7f);

            var all = new List<(Renderer, int)>();
            foreach (var r in _target.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                int subs = r is SkinnedMeshRenderer smr ? (smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 0)
                         : r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0;
                if (subs == 0) continue;
                all.Add((r, subs));
                if (!hasArea || area.Contains(r.bounds.center)) _renderers.Add((r, subs));
            }
            if (_renderers.Count == 0) _renderers.AddRange(all);
            if (_renderers.Count > 0) return;

            foreach (var mf in _target.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.gameObject.activeInHierarchy) _shapes.Add((mf.sharedMesh, mf.transform, Matrix4x4.identity));
            if (_shapes.Count > 0) return;

            if (_cube == null) _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (var bc in _target.GetComponentsInChildren<BoxCollider>())
                if (bc.enabled) _shapes.Add((_cube, bc.transform, Matrix4x4.TRS(bc.center, Quaternion.identity, bc.size)));
        }

        private bool HasShapes => _target != null && (_renderers.Count > 0 || _shapes.Count > 0);

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!HasShapes || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(_maskPass);
            data.scriptableRenderer.EnqueuePass(_compositePass);
        }

        // ============================== 描画パス ==============================

        /// <summary>下書き（形を塗った画面）を、後の線を重ねるパスへ渡す</summary>
        private class MaskFrame : ContextItem
        {
            public TextureHandle mask;
            public Vector2 size;
            public override void Reset() { mask = TextureHandle.nullHandle; size = Vector2.zero; }
        }

        /// <summary>1) 対象の形を 1 で塗る（見えている所だけ。ポストエフェクトの前＝場面の深度がある間に）</summary>
        private class MaskPass : ScriptableRenderPass
        {
            private readonly InteractOutline _o;
            public MaskPass(InteractOutline o) { _o = o; }

            private class Data
            {
                public List<(Renderer r, int subs)> renderers;
                public List<(Mesh mesh, Transform t, Matrix4x4 local)> shapes;
                public Material material;
                public int pass;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!_o.HasShapes) return;
                var res = frameData.Get<UniversalResourceData>();
                var cam = frameData.Get<UniversalCameraData>();
                bool depth = !res.isActiveTargetBackBuffer && res.activeDepthTexture.IsValid();

                var desc = cam.cameraTargetDescriptor;
                desc.graphicsFormat = GraphicsFormat.R8_UNorm;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.depthBufferBits = 0;
                if (!depth) desc.msaaSamples = 1;
                var mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_InteractOutlineMask", true, FilterMode.Bilinear);
                var mf = frameData.Contains<MaskFrame>() ? frameData.Get<MaskFrame>() : frameData.Create<MaskFrame>();
                mf.mask = mask;
                mf.size = new Vector2(desc.width, desc.height);

                using (var builder = renderGraph.AddRasterRenderPass<Data>("Interact Outline Mask", out var d))
                {
                    d.renderers = _o._renderers;
                    d.shapes = _o._shapes;
                    d.material = _o._mask;
                    d.pass = depth ? 0 : 1;
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    if (depth) builder.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((Data data, RasterGraphContext ctx) =>
                    {
                        foreach (var (r, subs) in data.renderers)
                        {
                            if (r == null) continue;
                            for (int s = 0; s < subs; s++) ctx.cmd.DrawRenderer(r, data.material, s, data.pass);
                        }
                        // 見た目の無い物の箱は深度なしで（箱が見た目の内側にあっても形が出るように）
                        foreach (var (mesh, t, local) in data.shapes)
                        {
                            if (mesh == null || t == null) continue;
                            var m = t.localToWorldMatrix * local;
                            for (int s = 0; s < mesh.subMeshCount; s++) ctx.cmd.DrawMesh(mesh, m, data.material, s, 1);
                        }
                    });
                }
            }
        }

        /// <summary>2) 下書きの縁だけを画面に重ねる（ポストエフェクトの後）</summary>
        private class CompositePass : ScriptableRenderPass
        {
            private readonly InteractOutline _o;
            public CompositePass(InteractOutline o) { _o = o; }

            private class Data
            {
                public TextureHandle mask;
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!frameData.Contains<MaskFrame>()) return;
                var mf = frameData.Get<MaskFrame>();
                if (!mf.mask.IsValid()) return;
                var res = frameData.Get<UniversalResourceData>();

                float k = Mathf.Max(0.75f, mf.size.y / 1080f);
                var mat = _o._composite;
                mat.SetColor("_OutlineColor", _o.Color);
                mat.SetFloat("_OutlineWidth", _o.Width * k);
                mat.SetFloat("_HaloWidth", _o.HaloWidth * k);
                mat.SetFloat("_HaloAlpha", _o.HaloAlpha);
                mat.SetVector("_MaskTexel", new Vector4(1f / mf.size.x, 1f / mf.size.y, mf.size.x, mf.size.y));
                using (var builder = renderGraph.AddRasterRenderPass<Data>("Interact Outline Composite", out var d))
                {
                    d.mask = mf.mask;
                    d.material = mat;
                    builder.UseTexture(mf.mask, AccessFlags.Read);
                    builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((Data data, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, data.mask, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }
            }
        }
    }
}
