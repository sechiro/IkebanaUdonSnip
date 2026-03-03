using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Snip Hold Use Button")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaSnipHoldUseButton : UdonSharpBehaviour
    {
        public UdonBehaviour targetBehaviour;
        public string holdCompleteEventName = "ExecuteUndo";
        public float requiredHoldSeconds = 1f;
        public bool deactivateAfterInvoke;
        public Transform progressVisual;
        public float progressOffsetMeters = 0.05f;
        public float progressRadiusMeters = 0.03f;
        public float progressThicknessMeters = 0.002f;
        public bool enableDebugLog;

        private const float MinHoldSeconds = 0.1f;
        private const float MinDirectionSqrMagnitude = 0.000001f;
        private const int MaxProgressSegments = 96;
        private bool _isHoldingUse;
        private bool _invoked;
        private float _holdStartTime;
        private GameObject[] _progressSegments = new GameObject[MaxProgressSegments];
        private int _progressSegmentCount;
        private int _lastVisibleSegmentCount = -1;

        public void Start()
        {
            if (requiredHoldSeconds < MinHoldSeconds)
            {
                requiredHoldSeconds = MinHoldSeconds;
            }

            TryResolveProgressVisual();
            CacheProgressSegments();
            SetProgressVisualActive(false);
        }

        public void OnDisable()
        {
            _isHoldingUse = false;
            _invoked = false;
            SetProgressVisualActive(false);
        }

        public override void Interact()
        {
            if (requiredHoldSeconds < MinHoldSeconds)
            {
                requiredHoldSeconds = MinHoldSeconds;
            }

            if (progressVisual == null)
            {
                TryResolveProgressVisual();
                CacheProgressSegments();
            }

            _isHoldingUse = true;
            _invoked = false;
            _holdStartTime = Time.time;
            UpdateProgressVisual(0f);
            SetProgressVisualActive(true);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!value)
            {
                _isHoldingUse = false;
                _invoked = false;
                SetProgressVisualActive(false);
            }
        }

        public void Update()
        {
            if (!_isHoldingUse)
            {
                return;
            }

            float elapsed = Time.time - _holdStartTime;
            float normalized = requiredHoldSeconds <= MinHoldSeconds ? 1f : elapsed / requiredHoldSeconds;
            UpdateProgressVisual(normalized);
            TryInvokeIfReady();
        }

        private void TryInvokeIfReady()
        {
            if (_invoked || !_isHoldingUse)
            {
                return;
            }

            if ((Time.time - _holdStartTime) < requiredHoldSeconds)
            {
                return;
            }

            _invoked = true;
            _isHoldingUse = false;
            SetProgressVisualActive(false);
            if (targetBehaviour == null || holdCompleteEventName == null || holdCompleteEventName.Length == 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipHoldUseButton] Missing targetBehaviour or holdCompleteEventName.", this);
                }
                return;
            }

            targetBehaviour.SendCustomEvent(holdCompleteEventName);

            if (deactivateAfterInvoke)
            {
                gameObject.SetActive(false);
            }
        }

        private void SetProgressVisualActive(bool active)
        {
            if (progressVisual == null)
            {
                return;
            }

            if (_progressSegmentCount > 0)
            {
                if (active)
                {
                    SetProgressSegmentVisibleCount(0);
                }
                else
                {
                    SetProgressSegmentVisibleCount(0);
                    _lastVisibleSegmentCount = -1;
                }
            }

            if (progressVisual.gameObject.activeSelf != active)
            {
                progressVisual.gameObject.SetActive(active);
            }
        }

        private void TryResolveProgressVisual()
        {
            if (progressVisual != null || transform.parent == null)
            {
                return;
            }

            string progressName = null;
            if (gameObject.name == "UndoButtonCube")
            {
                progressName = "UndoButtonProgressCircle";
            }
            else if (gameObject.name == "ResetButtonCube")
            {
                progressName = "ResetButtonProgressCircle";
            }

            if (progressName == null)
            {
                return;
            }

            int siblingCount = transform.parent.childCount;
            for (int i = 0; i < siblingCount; i++)
            {
                Transform sibling = transform.parent.GetChild(i);
                if (sibling == null || sibling.name != progressName)
                {
                    continue;
                }

                progressVisual = sibling;
                return;
            }
        }

        private void CacheProgressSegments()
        {
            _progressSegmentCount = 0;
            _lastVisibleSegmentCount = -1;
            for (int i = 0; i < MaxProgressSegments; i++)
            {
                _progressSegments[i] = null;
            }

            if (progressVisual == null)
            {
                return;
            }

            int childCount = progressVisual.childCount;
            for (int i = 0; i < childCount && _progressSegmentCount < MaxProgressSegments; i++)
            {
                Transform child = progressVisual.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                GameObject segment = child.gameObject;
                _progressSegments[_progressSegmentCount] = segment;
                segment.SetActive(false);
                _progressSegmentCount++;
            }
        }

        private void SetProgressSegmentVisibleCount(int visibleCount)
        {
            if (_progressSegmentCount <= 0)
            {
                return;
            }

            if (visibleCount < 0)
            {
                visibleCount = 0;
            }
            if (visibleCount > _progressSegmentCount)
            {
                visibleCount = _progressSegmentCount;
            }

            if (_lastVisibleSegmentCount == visibleCount)
            {
                return;
            }

            for (int i = 0; i < _progressSegmentCount; i++)
            {
                GameObject segment = _progressSegments[i];
                if (segment == null)
                {
                    continue;
                }

                bool shouldBeActive = i < visibleCount;
                if (segment.activeSelf != shouldBeActive)
                {
                    segment.SetActive(shouldBeActive);
                }
            }

            _lastVisibleSegmentCount = visibleCount;
        }

        private void UpdateProgressVisual(float normalized)
        {
            if (progressVisual == null)
            {
                return;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
            {
                return;
            }

            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 buttonPosition = transform.position;
            Vector3 toHead = headPosition - buttonPosition;
            if (toHead.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                toHead = transform.forward;
            }

            Vector3 viewDirection = toHead.normalized;
            progressVisual.position = buttonPosition + (viewDirection * Mathf.Max(0f, progressOffsetMeters));
            Quaternion headRotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
            Vector3 headUp = headRotation * Vector3.up;
            Vector3 upAxis = Vector3.ProjectOnPlane(headUp, viewDirection);
            if (upAxis.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                upAxis = Vector3.ProjectOnPlane(Vector3.up, viewDirection);
            }
            if (upAxis.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                upAxis = transform.up;
            }
            upAxis.Normalize();
            progressVisual.rotation = Quaternion.LookRotation(viewDirection, upAxis);

            float progress01 = Mathf.Clamp01(normalized);
            if (_progressSegmentCount > 0)
            {
                int visibleCount = Mathf.CeilToInt(progress01 * _progressSegmentCount);
                if (progress01 <= 0f)
                {
                    visibleCount = 0;
                }
                SetProgressSegmentVisibleCount(visibleCount);
            }
        }
    }
}
