using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// このオブジェクト（子を含む）のコライダーをNavMeshベイクから除外するマーカー。
    /// 小物や装飾がNavMeshを分断して敵が通れなくなる場合に付ける。
    /// 事前生成（NavMeshPrebaker）と実行時ベイク（NavMeshBootstrap）の両方が参照する。
    /// </summary>
    public class NavMeshBakeExclude : MonoBehaviour { }
}
