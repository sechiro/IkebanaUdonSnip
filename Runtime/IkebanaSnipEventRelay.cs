using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Snip Event Relay")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaSnipEventRelay : UdonSharpBehaviour
    {
        public UdonBehaviour targetBehaviour;
        public string eventName = "CutNow";
        public bool invokeOnPickup = true;
        public UdonBehaviour pickupTargetBehaviour;
        public string pickupEventName = "RequestHideUndoButtonGlobal";
        public bool invokeOnPickupUseDown = true;
        public bool invokeOnDrop;
        public UdonBehaviour dropTargetBehaviour;
        public string dropEventName = "RequestShowUndoButtonGlobal";
        public float minInvokeIntervalSeconds = 0.15f;
        public bool enableDebugLog;
        private float _nextInvokeAllowedTime;
        private const string PickupTrackedTargetsClearEvent = "OnScissorPickedUp";

        // Interact経由の発火は使用しないため、メソッド全体を無効化
        /*
        public override void Interact()
        {
            Run();
        }
        */

        public override void OnPickupUseDown()
        {
            if (invokeOnPickupUseDown)
            {
                Run();
            }
        }

        public override void OnPickup()
        {
            if (!invokeOnPickup)
            {
                return;
            }

            UdonBehaviour resolvedPickupTarget = pickupTargetBehaviour != null ? pickupTargetBehaviour : dropTargetBehaviour;
            string resolvedPickupEvent = pickupEventName;
            if (resolvedPickupEvent == null || resolvedPickupEvent.Length == 0)
            {
                resolvedPickupEvent = "RequestHideUndoButtonGlobal";
            }

            if (resolvedPickupTarget == null)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipEventRelay] Missing pickup target or pickup event name.", this);
                }
                return;
            }

            resolvedPickupTarget.SendCustomEvent(resolvedPickupEvent);

            if (targetBehaviour != null && eventName == "CutOneTouchedTarget")
            {
                targetBehaviour.SendCustomEvent(PickupTrackedTargetsClearEvent);
            }
        }

        public override void OnDrop()
        {
            if (!invokeOnDrop)
            {
                return;
            }

            if (dropTargetBehaviour == null || dropEventName == null || dropEventName.Length == 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipEventRelay] Missing drop target or drop event name.", this);
                }
                return;
            }

            dropTargetBehaviour.SendCustomEvent(dropEventName);
        }

        public void Run()
        {
            if (Time.time < _nextInvokeAllowedTime)
            {
                return;
            }

            if (targetBehaviour == null || eventName == null || eventName.Length == 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipEventRelay] Missing targetBehaviour or eventName.", this);
                }
                return;
            }

            _nextInvokeAllowedTime = Time.time + minInvokeIntervalSeconds;
            targetBehaviour.SendCustomEvent(eventName);
        }
    }
}
