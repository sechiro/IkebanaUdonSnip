using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Snip Undo Reset Controller")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaSnipUndoResetController : UdonSharpBehaviour
    {
        public Transform scissorTransform;
        public Transform resetReferenceTransform;
        public GameObject undoButtonObject;
        public GameObject resetButtonObject;
        public IkebanaSnipContactPicker contactPicker;
        public IkebanaUdonSnip resetRootCutter;
        public float undoOffsetMeters = 0.1f;
        public float resetOffsetXMeters = -0.3f;
        public float resetOffsetYMeters = 0.5f;
        public bool hideUndoButtonAfterUndo = true;
        public GameObject scissorResetButtonObject;
        public float scissorResetOffsetYMeters = 0.5f;
        public bool enableDebugLog;

        private const float HoldSeconds = 1f;
        private bool _resetReferenceCaptured;
        private Vector3 _resetReferenceInitialPosition;
        private Quaternion _resetReferenceInitialRotation;
        private Vector3 _resetReferenceInitialLocalScale;
        private bool _scissorInitialCaptured;
        private Vector3 _scissorInitialPosition;
        private Quaternion _scissorInitialRotation;

        public void Start()
        {
            CaptureResetReferenceState();
            PlaceResetButton();
            CaptureScissorInitialState();
            PlaceScissorResetButton();
            ApplyHoldSeconds();

            if (undoButtonObject != null)
            {
                undoButtonObject.SetActive(false);
            }
        }

        public void ShowUndoButtonBelowScissor()
        {
            RequestShowUndoButtonGlobal();
        }

        public void RequestShowUndoButtonGlobal()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ApplyShowUndoButtonGlobal));
        }

        public void RequestHideUndoButtonGlobal()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ApplyHideUndoButtonGlobal));
        }

        [NetworkCallable]
        public void ApplyShowUndoButtonGlobal()
        {
            ShowUndoButtonBelowScissorLocal();
        }

        [NetworkCallable]
        public void ApplyHideUndoButtonGlobal()
        {
            if (undoButtonObject != null)
            {
                undoButtonObject.SetActive(false);
            }
        }

        private void ShowUndoButtonBelowScissorLocal()
        {
            if (undoButtonObject == null || scissorTransform == null)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipUndoResetController] Missing undoButtonObject or scissorTransform.", this);
                }
                return;
            }

            undoButtonObject.transform.position = scissorTransform.position + (Vector3.down * undoOffsetMeters);
            undoButtonObject.transform.rotation = Quaternion.identity;
            undoButtonObject.SetActive(true);
        }

        public void ExecuteUndo()
        {
            if (contactPicker == null)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipUndoResetController] contactPicker is not assigned.", this);
                }
                return;
            }

            bool undone = contactPicker.UndoLastCutTarget();
            if (hideUndoButtonAfterUndo && (!undone || !contactPicker.HasUndoHistory()))
            {
                RequestHideUndoButtonGlobal();
            }
        }

        public void ExecuteReset()
        {
            if (resetRootCutter != null)
            {
                resetRootCutter.ResetBranchToInitial();
            }
            else if (contactPicker != null)
            {
                contactPicker.ResetAllCuts();
            }
            else
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipUndoResetController] resetRootCutter and contactPicker are not assigned.", this);
                }
                return;
            }

            if (contactPicker != null)
            {
                contactPicker.RefreshManagedCutterScope();
            }

            RestoreResetReferenceTransform();
            PlaceResetButton();
            RequestHideUndoButtonGlobal();
        }

        private void CaptureResetReferenceState()
        {
            if (_resetReferenceCaptured)
            {
                return;
            }

            if (resetReferenceTransform == null && resetRootCutter != null && resetRootCutter.sourceMeshFilter != null)
            {
                resetReferenceTransform = resetRootCutter.sourceMeshFilter.transform;
            }

            if (resetReferenceTransform == null && contactPicker != null && contactPicker.rootCutter != null && contactPicker.rootCutter.sourceMeshFilter != null)
            {
                resetReferenceTransform = contactPicker.rootCutter.sourceMeshFilter.transform;
            }

            if (resetReferenceTransform == null)
            {
                return;
            }

            _resetReferenceInitialPosition = resetReferenceTransform.position;
            _resetReferenceInitialRotation = resetReferenceTransform.rotation;
            _resetReferenceInitialLocalScale = resetReferenceTransform.localScale;
            _resetReferenceCaptured = true;
        }

        private void RestoreResetReferenceTransform()
        {
            TryRespawnResetReferenceWithManualSync();

            if (!_resetReferenceCaptured || resetReferenceTransform == null)
            {
                return;
            }

            resetReferenceTransform.position = _resetReferenceInitialPosition;
            resetReferenceTransform.rotation = _resetReferenceInitialRotation;
            resetReferenceTransform.localScale = _resetReferenceInitialLocalScale;
        }

        private void TryRespawnResetReferenceWithManualSync()
        {
            if (resetReferenceTransform == null)
            {
                return;
            }

            Transform current = resetReferenceTransform;
            int depth = 0;
            while (current != null && depth < 16)
            {
                UdonBehaviour syncBehaviour = current.GetComponent<UdonBehaviour>();
                if (syncBehaviour != null)
                {
                    VRCPlayerApi localPlayer = Networking.LocalPlayer;
                    if (!Utilities.IsValid(localPlayer))
                    {
                        return;
                    }

                    GameObject host = syncBehaviour.gameObject;
                    if (!Networking.IsOwner(host))
                    {
                        Networking.SetOwner(localPlayer, host);
                        if (!Networking.IsOwner(host))
                        {
                            if (enableDebugLog)
                            {
                                Debug.Log("[IkebanaSnipUndoResetController] Could not acquire owner for reset reference.", this);
                            }
                            current = current.parent;
                            depth++;
                            continue;
                        }
                    }

                    syncBehaviour.SendCustomEvent("Respawn");
                    return;
                }

                current = current.parent;
                depth++;
            }
        }

        private void PlaceResetButton()
        {
            CaptureResetReferenceState();
            if (resetButtonObject == null || !_resetReferenceCaptured)
            {
                return;
            }

            Vector3 offsetDirection = _resetReferenceInitialRotation * Vector3.right;
            resetButtonObject.transform.position = _resetReferenceInitialPosition + (offsetDirection * resetOffsetXMeters) + (Vector3.up * resetOffsetYMeters);
            resetButtonObject.transform.rotation = Quaternion.identity;
        }

        public void ExecuteScissorReset()
        {
            TryRespawnScissorWithManualSync();
        }

        private void CaptureScissorInitialState()
        {
            if (_scissorInitialCaptured || scissorTransform == null)
            {
                return;
            }

            _scissorInitialPosition = scissorTransform.position;
            _scissorInitialRotation = scissorTransform.rotation;
            _scissorInitialCaptured = true;
        }

        private void PlaceScissorResetButton()
        {
            CaptureScissorInitialState();
            if (scissorResetButtonObject == null || !_scissorInitialCaptured)
            {
                return;
            }

            scissorResetButtonObject.transform.position = _scissorInitialPosition + Vector3.up * scissorResetOffsetYMeters;
            scissorResetButtonObject.transform.rotation = Quaternion.identity;
        }

        private void TryRespawnScissorWithManualSync()
        {
            if (scissorTransform == null)
            {
                return;
            }

            Transform current = scissorTransform;
            int depth = 0;
            while (current != null && depth < 16)
            {
                UdonBehaviour syncBehaviour = current.GetComponent<UdonBehaviour>();
                if (syncBehaviour != null)
                {
                    VRCPlayerApi localPlayer = Networking.LocalPlayer;
                    if (!Utilities.IsValid(localPlayer))
                    {
                        return;
                    }

                    GameObject host = syncBehaviour.gameObject;
                    if (!Networking.IsOwner(host))
                    {
                        Networking.SetOwner(localPlayer, host);
                        if (!Networking.IsOwner(host))
                        {
                            if (enableDebugLog)
                            {
                                Debug.Log("[IkebanaSnipUndoResetController] Could not acquire owner for scissor reset.", this);
                            }

                            current = current.parent;
                            depth++;
                            continue;
                        }
                    }

                    syncBehaviour.SendCustomEvent("Respawn");
                    return;
                }

                current = current.parent;
                depth++;
            }

            if (!_scissorInitialCaptured)
            {
                return;
            }

            scissorTransform.position = _scissorInitialPosition;
            scissorTransform.rotation = _scissorInitialRotation;
        }

        private void ApplyHoldSeconds()
        {
            ApplyHoldSecondsToButton(undoButtonObject);
            ApplyHoldSecondsToButton(resetButtonObject);
            ApplyHoldSecondsToButton(scissorResetButtonObject);
        }

        private void ApplyHoldSecondsToButton(GameObject buttonObject)
        {
            if (buttonObject == null)
            {
                return;
            }

            IkebanaSnipHoldUseButton holdButton = buttonObject.GetComponent<IkebanaSnipHoldUseButton>();
            if (holdButton == null)
            {
                return;
            }

            holdButton.requiredHoldSeconds = HoldSeconds;
        }
    }
}
