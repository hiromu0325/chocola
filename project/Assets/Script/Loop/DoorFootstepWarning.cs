using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋の中に居るとき、回廊側で異形がこの部屋の扉（入口／出口）に近づいていたら、
    /// 対応する室内の扉から足音を鳴らす。「出た瞬間に襲われる」を音で避けられるようにする。
    /// </summary>
    public class DoorFootstepWarning : MonoBehaviour
    {
        [Tooltip("この距離（m）以内に異形が居る扉から足音が聞こえる")]
        public float HearRange = 8f;

        private readonly Dictionary<LoopRoomDoor, AudioSource> _sources = new Dictionary<LoopRoomDoor, AudioSource>();
        private readonly Dictionary<LoopRoomDoor, float> _nextStep = new Dictionary<LoopRoomDoor, float>();
        private LoopRoomDoor[] _doorsCache;
        private string _doorsRoom;

        private void Update()
        {
            string cur = LoopRooms.CurrentRoomId;
            var room = LoopRooms.Get(cur);
            if (room == null) return;
            var searchers = FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None);
            if (searchers.Length == 0) return;

            if (_doorsRoom != cur || _doorsCache == null)
            {
                _doorsCache = room.GetComponentsInChildren<LoopRoomDoor>(true);
                _doorsRoom = cur;
            }

            foreach (var door in _doorsCache)
            {
                if (door == null) continue;
                // この室内扉に対応する回廊側の扉前位置
                int side = door.IsExitDoor ? (room.Side + 2) % 4 : room.Side;
                Vector3 front = LoopCorridorLayout.DoorFrontPosition(side, room.Slot);
                float nearest = float.MaxValue;
                foreach (var s in searchers)
                {
                    if (s == null || !s.InCorridor || s.IsRetreating) continue;
                    float d = Vector3.Distance(s.transform.position, front);
                    if (d < nearest) nearest = d;
                }
                if (nearest > HearRange) continue;

                float t = 1f - Mathf.Clamp01(nearest / HearRange);   // 0=遠い 1=扉のすぐ外
                _nextStep.TryGetValue(door, out float next);
                if (Time.time < next) continue;
                _nextStep[door] = Time.time + Mathf.Lerp(0.95f, 0.45f, t);

                if (!_sources.TryGetValue(door, out var src) || src == null)
                {
                    var go = new GameObject("DoorFootsteps");
                    go.transform.SetParent(door.transform, false);
                    go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                    src = go.AddComponent<AudioSource>();
                    src.spatialBlend = 1f; src.minDistance = 1.5f; src.maxDistance = 14f;
                    src.rolloffMode = AudioRolloffMode.Linear;
                    _sources[door] = src;
                }
                src.pitch = Random.Range(0.85f, 1.05f);
                src.PlayOneShot(ProceduralAudio.Footstep(), Mathf.Lerp(0.25f, 0.9f, t));
            }
        }
    }
}
