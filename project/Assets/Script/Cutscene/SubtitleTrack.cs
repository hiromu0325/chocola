using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EscapeProto
{
    /// <summary>
    /// 字幕クリップの中身。Timelineのクリップとして並べ、表示時間＝クリップ長になる。
    /// </summary>
    [Serializable]
    public class SubtitleBehaviour : PlayableBehaviour
    {
        [TextArea(2, 5)] public string Text = "";
        public Color Color = Color.white;
        [Tooltip("話者名（空なら表示しない）")]
        public string Speaker = "";
    }

    /// <summary>
    /// 字幕クリップ（PlayableAsset）。Timelineウィンドウでクリップを選ぶと文面を編集できる。
    /// </summary>
    [Serializable]
    public class SubtitleClip : PlayableAsset, ITimelineClipAsset
    {
        public SubtitleBehaviour Subtitle = new SubtitleBehaviour();

        // フェードイン/アウト（クリップの端をドラッグ）に対応させる
        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            ScriptPlayable<SubtitleBehaviour>.Create(graph, Subtitle);
    }

    /// <summary>
    /// 字幕トラック。CutsceneSubtitleView にバインドして使う。
    /// </summary>
    [TrackClipType(typeof(SubtitleClip))]
    [TrackBindingType(typeof(CutsceneSubtitleView))]
    [TrackColor(0.9f, 0.8f, 0.3f)]
    public class SubtitleTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<SubtitleMixer>.Create(graph, inputCount);
    }

    /// <summary>
    /// 同時に複数クリップが重なった場合は、重みが最大のものを表示する。
    /// クリップが無い区間は自動的に非表示になる。
    /// </summary>
    public class SubtitleMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var view = playerData as CutsceneSubtitleView;
            if (view == null) return;

            string text = null, speaker = null;
            Color color = Color.white;
            float best = 0f;

            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                float w = playable.GetInputWeight(i);
                if (w <= 0.0001f || w < best) continue;
                var input = (ScriptPlayable<SubtitleBehaviour>)playable.GetInput(i);
                var b = input.GetBehaviour();
                if (b == null) continue;
                best = w;
                text = b.Text;
                speaker = b.Speaker;
                color = b.Color;
            }

            view.Render(text, speaker, color, best);
        }

        /// <summary>再生停止時は字幕を消す（スキップ時の消し忘れ防止）</summary>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            // playerDataはここでは取れないため、Viewの側でも停止時にクリアする
        }
    }
}
