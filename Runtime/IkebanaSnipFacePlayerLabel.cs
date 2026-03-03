using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Snip Face Player Label")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaSnipFacePlayerLabel : UdonSharpBehaviour
    {
        public Transform labelTransform;
        public Vector3 worldUp = Vector3.up;
        public bool enableDebugLog;

        private const float MinDirectionSqrMagnitude = 0.000001f;

        public void Start()
        {
            if (labelTransform == null)
            {
                labelTransform = transform;
            }
        }

        public void Update()
        {
            Transform target = labelTransform != null ? labelTransform : transform;
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
            {
                return;
            }

            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 forward = headPosition - target.position;
            if (forward.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                return;
            }

            Vector3 up = worldUp;
            if (up.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                up = Vector3.up;
            }

            Vector3 toPlayerNormalized = forward.normalized;
            Vector3 faceDirection = -toPlayerNormalized;
            Vector3 upNormalized = up.normalized;
            if (Mathf.Abs(Vector3.Dot(faceDirection, upNormalized)) > 0.98f)
            {
                up = Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(faceDirection, up)) > 0.98f)
                {
                    up = Vector3.right;
                }
            }

            target.rotation = Quaternion.LookRotation(faceDirection, up);
        }
    }
}
