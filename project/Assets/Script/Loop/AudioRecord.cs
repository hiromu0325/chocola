using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 音声記録（カセットテープ・ボイスレコーダー・端末の再生メッセージ）の音声。
    /// Clips は DocCatalog.Tape の台詞と同じ並び（物音の行は null）。調べる画面で再生する。
    /// ※シーンに保存されるのでクラス名とファイル名を一致させてある
    /// </summary>
    public class AudioRecord : MonoBehaviour
    {
        public AudioClip[] Clips;
    }
}
