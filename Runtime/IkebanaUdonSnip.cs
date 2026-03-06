using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Udon Snip")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaUdonSnip : UdonSharpBehaviour
    {
        [Header("Source")]
        public MeshFilter sourceMeshFilter;
        public MeshRenderer sourceMeshRenderer;
        public MeshCollider sourceMeshCollider;
        public Mesh sourceMeshAsset;

        [Header("Outputs")]
        public MeshFilter positiveOutputMeshFilter;
        public MeshRenderer positiveOutputMeshRenderer;
        public MeshCollider positiveOutputMeshCollider;
        public MeshFilter negativeOutputMeshFilter;
        public MeshRenderer negativeOutputMeshRenderer;
        public MeshCollider negativeOutputMeshCollider;

        [Header("Cut Plane")]
        public Transform cutPlaneTransform;
        public bool useCutPlaneUpAxis = false;
        public Vector3 fallbackPlaneNormalLocal = Vector3.up;

        [Header("Limits")]
        public int maxGeneratedVerticesPerSide = 65535;
        public int maxGeneratedIndicesPerSide = 65535;
        public int maxCutIntersections = 32768;
        public float sideEpsilon = 0.00001f;
        public float mergeDistance = 0.000001f;

        [Header("Behavior")]
        public bool disableSourceAfterCut = true;
        public bool updateOutputColliders = true;
        public bool updateSourceColliderWhenNotDisabling = true;
        public bool allowMultipleCuts = false;
        public bool generateCutCaps = false;
        public bool separatePiecesAfterCut = true;
        public float separationDistanceMeters = 0.01f;
        public bool enableDebugLog = false;

        [Header("Performance")]
        public bool spreadCutOverMultipleFrames = true;
        public int cutTrianglesPerFrame = 128;

        [Header("Collision Cut")]
        public bool enableCutOnTriggerEnter = true;
        public string requiredCutterTriggerName = "CutTrigger";
        public float triggerCutCooldownSeconds = 0.2f;
        public float contactPointInset = 0.01f;
        public bool requireTriggerExitBeforeNextCut = true;
        public float activateGuardSeconds = 0.2f;

        [Header("Use Test Cut")]
        public bool autoCreateFallbackMeshForTest = true;

        [Header("Global Sync")]
        public bool enableGlobalSync = true;
        public bool autoTakeOwnershipOnCut = true;
        public bool applySyncedCutOnDeserialize = true;
        public int syncSlotCount = 1;
        public int syncSlotId;

        private const int MaxMeshVertexCount = 65535;
        private const int SyncSchemaVersionCurrent = 2;
        private const int MaxSyncSlots = 32;
        private const int MaxSyncOps = 32;
        private const int MaxOpsPerSerialization = 8;
        private const int MaxCutStage = 5;
        private const int SyncChunkMaxRetryCount = 3;
        private const float SyncChunkRetryDelaySeconds = 0.25f;
        private const int QPos = 1000;
        private const int QPosMin = -2000000;
        private const int QPosMax = 2000000;
        private const int QNorm = 10000;

        [UdonSynced] private int _syncSchemaVersion = SyncSchemaVersionCurrent;
        [UdonSynced] private int _syncRevision;
        [UdonSynced] private int _syncSlotCount = 1;
        [UdonSynced] private int _syncStageBits0;
        [UdonSynced] private int _syncStageBits1;
        [UdonSynced] private int _syncStageBits2;
        [UdonSynced] private int _syncOpCount;
        [UdonSynced] private int[] _syncOpSlotId = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpStage = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlanePointQx = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlanePointQy = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlanePointQz = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlaneNormalQx = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlaneNormalQy = new int[MaxSyncOps];
        [UdonSynced] private int[] _syncOpPlaneNormalQz = new int[MaxSyncOps];
        [UdonSynced] private int _syncLastOpType;
        [UdonSynced] private int _syncLastOpSlot = -1;

        [UdonSynced] private int _syncCutStage;
        [UdonSynced] private int _syncPlanePointQx;
        [UdonSynced] private int _syncPlanePointQy;
        [UdonSynced] private int _syncPlanePointQz;
        [UdonSynced] private int _syncPlaneNormalQx;
        [UdonSynced] private int _syncPlaneNormalQy;
        [UdonSynced] private int _syncPlaneNormalQz;

        private bool _hasCut;
        private bool _overflowed;
        private float _nextTriggerAllowedTime;
        private bool _triggerArmed = true;
        private bool _useOverridePlanePoint;
        private Vector3 _overridePlanePointWorld;
        private bool _useOverridePlaneNormal;
        private Vector3 _overridePlaneNormalWorld;
        private Mesh _cachedSourceMesh;
        private int _appliedSyncRevision = -1;
        private int _appliedSyncOpCount;
        private bool _isApplyingSyncedCut;
        private int[] _stageScratch = new int[MaxSyncSlots];
        private int _syncLocalOpCount;
        private int _syncPublishTargetOpCount;
        private bool _isSyncChunkSending;
        private int _syncChunkRetryCount;
        private int _cutVersion;
        private bool _sourceTransformCaptured;
        private Vector3 _sourceInitialLocalPosition;
        private Quaternion _sourceInitialLocalRotation;
        private Vector3 _sourceInitialLocalScale;
        private bool _positiveTransformCaptured;
        private Vector3 _positiveInitialLocalPosition;
        private Quaternion _positiveInitialLocalRotation;
        private Vector3 _positiveInitialLocalScale;
        private bool _negativeTransformCaptured;
        private Vector3 _negativeInitialLocalPosition;
        private Quaternion _negativeInitialLocalRotation;
        private Vector3 _negativeInitialLocalScale;

        private Vector3[] _triInV;
        private Vector3[] _triInN;
        private Vector2[] _triInUv;
        private float[] _triInD;
        private Vector3[] _triPosPolyV;
        private Vector3[] _triPosPolyN;
        private Vector2[] _triPosPolyUv;
        private float[] _triPosPolyD;
        private Vector3[] _triNegPolyV;
        private Vector3[] _triNegPolyN;
        private Vector2[] _triNegPolyUv;
        private float[] _triNegPolyD;
        private Vector3[] _triCutPoints;
        private bool _isCutJobRunning;
        private Mesh _cutJobSourceMesh;
        private Vector3[] _cutJobSourceVertices;
        private int[] _cutJobSourceTriangles;
        private Vector3[] _cutJobSourceNormals;
        private Vector2[] _cutJobSourceUv;
        private bool _cutJobHasNormals;
        private bool _cutJobHasUv;
        private Vector3 _cutJobPlanePointLocal;
        private Vector3 _cutJobPlaneNormalLocal;
        private Vector3 _cutJobPlaneNormalWorld;
        private bool _cutJobCollectCapSegments;
        private int _cutJobTriangleCount;
        private int _cutJobTriangleCursor;
        private int _cutJobPosVertexCount;
        private int _cutJobPosIndexCount;
        private int _cutJobNegVertexCount;
        private int _cutJobNegIndexCount;
        private int _cutJobCapSegmentCount;
        private Vector3[] _cutJobPosVertices;
        private Vector3[] _cutJobPosNormals;
        private Vector2[] _cutJobPosUv;
        private int[] _cutJobPosIndices;
        private Vector3[] _cutJobNegVertices;
        private Vector3[] _cutJobNegNormals;
        private Vector2[] _cutJobNegUv;
        private int[] _cutJobNegIndices;
        private Vector3[] _cutJobCapSegmentStart;
        private Vector3[] _cutJobCapSegmentEnd;

        public void Start()
        {
            _cachedSourceMesh = ResolveSourceMesh();
            EnsureClipBuffers();
            EnsureSyncArrays();
            NormalizeSyncConfig();
            CaptureInitialTransformState(false);

            if (_syncSchemaVersion <= 0)
            {
                _syncSchemaVersion = SyncSchemaVersionCurrent;
            }

            if (_syncSlotCount <= 0)
            {
                _syncSlotCount = GetConfiguredSlotCount();
            }

            int syncedOpCount = ClampSyncedOpCount();
            _syncLocalOpCount = syncedOpCount;
            _syncPublishTargetOpCount = syncedOpCount;
            _syncChunkRetryCount = 0;
            _isSyncChunkSending = false;

            // 参加直後の同期値適用を試みる（未到着時はOnDeserialization側で適用）
            SendCustomEventDelayedFrames(nameof(ApplySyncedStateIfAvailable), 1);
        }

        public void ApplySyncedStateIfAvailable()
        {
            if (!enableGlobalSync || !applySyncedCutOnDeserialize)
            {
                return;
            }

            int syncedOpCount = ClampSyncedOpCount();
            if (_syncRevision < _appliedSyncRevision)
            {
                return;
            }

            if (_syncRevision == _appliedSyncRevision && syncedOpCount <= _appliedSyncOpCount)
            {
                return;
            }

            ApplySyncedCutFromState();
        }

        public override void OnDeserialization()
        {
            if (!enableGlobalSync || !applySyncedCutOnDeserialize)
            {
                return;
            }

            ApplySyncedCutFromState();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!_isSyncChunkSending)
            {
                return;
            }

            if (!result.success)
            {
                _syncChunkRetryCount++;
                if (_syncChunkRetryCount > SyncChunkMaxRetryCount)
                {
                    _isSyncChunkSending = false;
                    Log("Chunk serialization failed and retries were exhausted.");
                    return;
                }

                SendCustomEventDelayedSeconds(nameof(RetryChunkSerialization), SyncChunkRetryDelaySeconds);
                return;
            }

            _syncChunkRetryCount = 0;
            if (_syncOpCount < _syncPublishTargetOpCount)
            {
                SendCustomEventDelayedFrames(nameof(ContinueChunkSerialization), 1);
                return;
            }

            _isSyncChunkSending = false;
        }

        public void ContinueChunkSerialization()
        {
            PublishNextSyncChunk();
        }

        public void RetryChunkSerialization()
        {
            PublishNextSyncChunk();
        }

        public int GetCutVersion()
        {
            return _cutVersion;
        }

        public bool IsCutInProgress()
        {
            return _isCutJobRunning;
        }

        public bool UndoLastCut()
        {
            if (_isCutJobRunning)
            {
                Log("Undo suppressed because cut is still in progress.");
                return false;
            }

            if (!ValidateBindings())
            {
                return false;
            }

            int opCount = ClampLocalOpCount();
            if (!_hasCut && opCount <= 0)
            {
                return false;
            }

            TrimLocalLastSyncOperation();
            RebuildVisualStateFromLocalOperations();

            if (enableGlobalSync && !_isApplyingSyncedCut)
            {
                TrySyncUndoState();
            }

            return true;
        }

        private void TrimLocalLastSyncOperation()
        {
            int opCount = ClampLocalOpCount();
            if (opCount > 0)
            {
                opCount--;
                ClearSyncOperationRange(opCount, 1);
            }

            _syncLocalOpCount = opCount;
            if (_syncPublishTargetOpCount > opCount)
            {
                _syncPublishTargetOpCount = opCount;
            }
            if (_syncOpCount > opCount)
            {
                _syncOpCount = opCount;
            }
        }

        private void RebuildVisualStateFromLocalOperations()
        {
            int slotCount = GetConfiguredSlotCount();
            int slotId = GetConfiguredSlotId(slotCount);
            int opCount = ClampLocalOpCount();

            ResetChildBranchesToInitial();
            RestoreUncutVisualState();

            for (int i = 0; i < opCount; i++)
            {
                if (_syncOpSlotId[i] != slotId)
                {
                    continue;
                }

                int opStage = _syncOpStage[i];
                if (opStage <= 0)
                {
                    continue;
                }

                Vector3 localPoint;
                Vector3 localNormal;
                DecodeSyncOperationPlane(i, out localPoint, out localNormal);

                _isApplyingSyncedCut = true;
                CutAtLocalPlane(localPoint, localNormal);
                _isApplyingSyncedCut = false;
            }

            ApplyPublishedStageState(slotCount, opCount, slotId);
        }

        private void DecodeSyncOperationPlane(int opIndex, out Vector3 localPoint, out Vector3 localNormal)
        {
            localPoint = new Vector3(
                DequantizePointComponent(_syncOpPlanePointQx[opIndex]),
                DequantizePointComponent(_syncOpPlanePointQy[opIndex]),
                DequantizePointComponent(_syncOpPlanePointQz[opIndex]));
            localNormal = new Vector3(
                DequantizeNormalComponent(_syncOpPlaneNormalQx[opIndex]),
                DequantizeNormalComponent(_syncOpPlaneNormalQy[opIndex]),
                DequantizeNormalComponent(_syncOpPlaneNormalQz[opIndex]));

            if (localNormal.sqrMagnitude < 0.000001f)
            {
                localNormal = fallbackPlaneNormalLocal;
            }
            localNormal.Normalize();
        }

        public void ResetBranchToInitial()
        {
            if (_isCutJobRunning)
            {
                ResetCutJobState();
            }

            if (!ValidateBindings())
            {
                return;
            }

            ResetChildBranchesToInitial();
            RestoreUncutVisualState();

            if (enableGlobalSync && !_isApplyingSyncedCut)
            {
                TrySyncResetState();
            }
        }

        /*
        public override void Interact()
        {
            // Use判定を出さないため無効化
        }
        */

        public void CutAtWorldPoint(Vector3 worldPoint)
        {
            _useOverridePlanePoint = true;
            _overridePlanePointWorld = worldPoint;
            CutNow();
        }

        public void CutAtWorldPlane(Vector3 worldPoint, Vector3 worldNormal)
        {
            _useOverridePlanePoint = true;
            _overridePlanePointWorld = worldPoint;

            if (worldNormal.sqrMagnitude > 0.000001f)
            {
                _useOverridePlaneNormal = true;
                _overridePlaneNormalWorld = worldNormal.normalized;
            }
            else
            {
                _useOverridePlaneNormal = false;
            }

            CutNow();
        }

        public void CutAtLocalPlane(Vector3 localPoint, Vector3 localNormal)
        {
            Transform sourceTransform = sourceMeshFilter != null ? sourceMeshFilter.transform : transform;
            Vector3 worldPoint = sourceTransform.TransformPoint(localPoint);
            Vector3 worldNormal = sourceTransform.TransformDirection(localNormal);
            CutAtWorldPlane(worldPoint, worldNormal);
        }

        public void OnTriggerEnter(Collider other)
        {
            if (!enableCutOnTriggerEnter || other == null)
            {
                return;
            }

            if (_hasCut && !allowMultipleCuts)
            {
                return;
            }

            if (Time.time < _nextTriggerAllowedTime)
            {
                return;
            }

            if (requireTriggerExitBeforeNextCut && !_triggerArmed)
            {
                return;
            }

            if (!IsValidCutterTrigger(other))
            {
                return;
            }

            Vector3 hitPoint = EstimateHitPoint(other);
            _nextTriggerAllowedTime = Time.time + triggerCutCooldownSeconds;
            if (requireTriggerExitBeforeNextCut)
            {
                _triggerArmed = false;
            }
            CutAtWorldPoint(hitPoint);
        }

        public void OnTriggerExit(Collider other)
        {
            if (!enableCutOnTriggerEnter || other == null)
            {
                return;
            }

            if (!IsValidCutterTrigger(other))
            {
                return;
            }

            _triggerArmed = true;
        }

        public void NotifyActivatedFromParent()
        {
            _nextTriggerAllowedTime = Time.time + activateGuardSeconds;
            _triggerArmed = false;
            CaptureInitialTransformState(true);
        }

        public void CutNow()
        {
            bool useOverridePlanePoint = _useOverridePlanePoint;
            Vector3 overridePlanePointWorld = _overridePlanePointWorld;
            _useOverridePlanePoint = false;
            bool useOverridePlaneNormal = _useOverridePlaneNormal;
            Vector3 overridePlaneNormalWorld = _overridePlaneNormalWorld;
            _useOverridePlaneNormal = false;

            if (_isCutJobRunning)
            {
                Log("Cut suppressed because a previous cut is still in progress.");
                return;
            }

            if (_hasCut && !allowMultipleCuts)
            {
                Log("Cut suppressed because allowMultipleCuts is false.");
                return;
            }

            if (!ValidateBindings())
            {
                return;
            }
            if (!_hasCut)
            {
                CaptureInitialTransformState(true);
            }

            if (maxGeneratedVerticesPerSide < 3 || maxGeneratedIndicesPerSide < 3 || maxCutIntersections < 1)
            {
                Log("Invalid limits. Ensure maxGeneratedVerticesPerSide/maxGeneratedIndicesPerSide/maxCutIntersections are positive.");
                return;
            }

            if (maxGeneratedVerticesPerSide > MaxMeshVertexCount || maxGeneratedIndicesPerSide > MaxMeshVertexCount)
            {
                Log("Invalid limits. maxGeneratedVerticesPerSide/maxGeneratedIndicesPerSide must be <= 65535.");
                return;
            }

            if (!TryPrepareCutJob(useOverridePlanePoint, overridePlanePointWorld, useOverridePlaneNormal, overridePlaneNormalWorld))
            {
                return;
            }

            bool useBatchedCut = spreadCutOverMultipleFrames && !_isApplyingSyncedCut;
            if (!useBatchedCut)
            {
                ProcessCutTrianglesBatch(_cutJobTriangleCount);
                FinalizeCutJob();
                return;
            }

            int trianglesPerBatch = cutTrianglesPerFrame;
            if (trianglesPerBatch < 1)
            {
                trianglesPerBatch = 1;
            }

            _isCutJobRunning = true;
            ProcessCutTrianglesBatch(trianglesPerBatch);
            if (_cutJobTriangleCursor >= _cutJobTriangleCount || _overflowed)
            {
                _isCutJobRunning = false;
                FinalizeCutJob();
                return;
            }

            SendCustomEventDelayedFrames(nameof(ContinueCutNow), 1);
        }

        public void ContinueCutNow()
        {
            if (!_isCutJobRunning)
            {
                return;
            }

            int trianglesPerBatch = cutTrianglesPerFrame;
            if (trianglesPerBatch < 1)
            {
                trianglesPerBatch = 1;
            }

            ProcessCutTrianglesBatch(trianglesPerBatch);
            if (_cutJobTriangleCursor < _cutJobTriangleCount && !_overflowed)
            {
                SendCustomEventDelayedFrames(nameof(ContinueCutNow), 1);
                return;
            }

            _isCutJobRunning = false;
            FinalizeCutJob();
        }

        private bool TryPrepareCutJob(
            bool useOverridePlanePoint,
            Vector3 overridePlanePointWorld,
            bool useOverridePlaneNormal,
            Vector3 overridePlaneNormalWorld)
        {
            ResetCutJobState();
            EnsureClipBuffers();

            Mesh sourceMesh = GetSourceMesh();
            if (sourceMesh == null)
            {
                Log("Source mesh is null. Set sourceMeshFilter/sharedMesh (or sourceMeshCollider.sharedMesh).");
                return false;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            int[] sourceTriangles = sourceMesh.triangles;
            if (sourceVertices == null || sourceTriangles == null || sourceTriangles.Length < 3)
            {
                Log("Source mesh has no triangles.");
                return false;
            }

            Vector3[] sourceNormals = sourceMesh.normals;
            Vector2[] sourceUv = sourceMesh.uv;
            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasUv = sourceUv != null && sourceUv.Length == sourceVertices.Length;

            Vector3 planePointWorld = useOverridePlanePoint ? overridePlanePointWorld : cutPlaneTransform.position;
            Vector3 planeNormalWorld = useOverridePlaneNormal
                ? overridePlaneNormalWorld
                : (useCutPlaneUpAxis ? cutPlaneTransform.up : cutPlaneTransform.forward);

            if (planeNormalWorld.sqrMagnitude < 0.000001f)
            {
                planeNormalWorld = useCutPlaneUpAxis ? cutPlaneTransform.up : cutPlaneTransform.forward;
            }
            if (planeNormalWorld.sqrMagnitude < 0.000001f)
            {
                planeNormalWorld = transform.up;
            }
            planeNormalWorld.Normalize();

            Vector3 planeNormalLocal = sourceMeshFilter.transform.InverseTransformDirection(planeNormalWorld);
            if (planeNormalLocal.sqrMagnitude < 0.000001f)
            {
                planeNormalLocal = fallbackPlaneNormalLocal;
            }
            planeNormalLocal.Normalize();

            bool collectCapSegments = generateCutCaps;
            EnsureCutWorkBuffers(collectCapSegments);

            _cutJobSourceMesh = sourceMesh;
            _cutJobSourceVertices = sourceVertices;
            _cutJobSourceTriangles = sourceTriangles;
            _cutJobSourceNormals = sourceNormals;
            _cutJobSourceUv = sourceUv;
            _cutJobHasNormals = hasNormals;
            _cutJobHasUv = hasUv;
            _cutJobPlanePointLocal = sourceMeshFilter.transform.InverseTransformPoint(planePointWorld);
            _cutJobPlaneNormalLocal = planeNormalLocal;
            _cutJobPlaneNormalWorld = planeNormalWorld;
            _cutJobCollectCapSegments = collectCapSegments;
            _cutJobTriangleCount = sourceTriangles.Length / 3;
            _cutJobTriangleCursor = 0;
            _cutJobPosVertexCount = 0;
            _cutJobPosIndexCount = 0;
            _cutJobNegVertexCount = 0;
            _cutJobNegIndexCount = 0;
            _cutJobCapSegmentCount = 0;
            _overflowed = false;
            return true;
        }

        private void ProcessCutTrianglesBatch(int maxTriangleCount)
        {
            if (maxTriangleCount < 1)
            {
                maxTriangleCount = 1;
            }

            int processed = 0;
            while (_cutJobTriangleCursor < _cutJobTriangleCount && processed < maxTriangleCount)
            {
                int baseIndex = _cutJobTriangleCursor * 3;
                _cutJobTriangleCursor++;
                processed++;

                int i0 = _cutJobSourceTriangles[baseIndex];
                int i1 = _cutJobSourceTriangles[baseIndex + 1];
                int i2 = _cutJobSourceTriangles[baseIndex + 2];
                if (!IsVertexIndexValid(i0, _cutJobSourceVertices.Length) || !IsVertexIndexValid(i1, _cutJobSourceVertices.Length) || !IsVertexIndexValid(i2, _cutJobSourceVertices.Length))
                {
                    continue;
                }

                Vector3 v0 = _cutJobSourceVertices[i0];
                Vector3 v1 = _cutJobSourceVertices[i1];
                Vector3 v2 = _cutJobSourceVertices[i2];

                Vector3 referenceNormal = ComputeFaceNormal(v0, v1, v2);
                Vector3 n0 = _cutJobHasNormals ? _cutJobSourceNormals[i0] : referenceNormal;
                Vector3 n1 = _cutJobHasNormals ? _cutJobSourceNormals[i1] : n0;
                Vector3 n2 = _cutJobHasNormals ? _cutJobSourceNormals[i2] : n0;

                Vector2 uv0 = _cutJobHasUv ? _cutJobSourceUv[i0] : Vector2.zero;
                Vector2 uv1 = _cutJobHasUv ? _cutJobSourceUv[i1] : Vector2.zero;
                Vector2 uv2 = _cutJobHasUv ? _cutJobSourceUv[i2] : Vector2.zero;

                float d0 = Vector3.Dot(v0 - _cutJobPlanePointLocal, _cutJobPlaneNormalLocal);
                float d1 = Vector3.Dot(v1 - _cutJobPlanePointLocal, _cutJobPlaneNormalLocal);
                float d2 = Vector3.Dot(v2 - _cutJobPlanePointLocal, _cutJobPlaneNormalLocal);

                ClipAndEmitTriangle(
                    v0, v1, v2,
                    n0, n1, n2,
                    uv0, uv1, uv2,
                    d0, d1, d2,
                    referenceNormal,
                    _cutJobCollectCapSegments,
                    _cutJobPosVertices, _cutJobPosNormals, _cutJobPosUv, _cutJobPosIndices,
                    ref _cutJobPosVertexCount, ref _cutJobPosIndexCount,
                    _cutJobNegVertices, _cutJobNegNormals, _cutJobNegUv, _cutJobNegIndices,
                    ref _cutJobNegVertexCount, ref _cutJobNegIndexCount,
                    _cutJobCapSegmentStart, _cutJobCapSegmentEnd, ref _cutJobCapSegmentCount);

                if (_overflowed)
                {
                    return;
                }
            }
        }

        private void FinalizeCutJob()
        {
            if (_overflowed)
            {
                Log("Cut aborted because generated mesh exceeded configured capacities.");
                ResetCutJobState();
                return;
            }

            if (_cutJobPosIndexCount < 3 || _cutJobNegIndexCount < 3)
            {
                Log("No valid cut result. Plane may not intersect mesh.");
                ResetCutJobState();
                return;
            }

            if (_cutJobCollectCapSegments)
            {
                AppendCapFaces(
                    _cutJobCapSegmentStart,
                    _cutJobCapSegmentEnd,
                    _cutJobCapSegmentCount,
                    _cutJobPlanePointLocal,
                    _cutJobPlaneNormalLocal,
                    _cutJobPosVertices, _cutJobPosNormals, _cutJobPosUv, _cutJobPosIndices,
                    ref _cutJobPosVertexCount, ref _cutJobPosIndexCount,
                    _cutJobNegVertices, _cutJobNegNormals, _cutJobNegUv, _cutJobNegIndices,
                    ref _cutJobNegVertexCount, ref _cutJobNegIndexCount);

                if (_overflowed)
                {
                    Log("Cut aborted while generating cap faces due to capacity limits.");
                    ResetCutJobState();
                    return;
                }
            }

            if (_cutJobPosVertexCount > MaxMeshVertexCount || _cutJobNegVertexCount > MaxMeshVertexCount)
            {
                Log("Cut aborted because generated mesh vertex count exceeded 65535.");
                ResetCutJobState();
                return;
            }

            Mesh posMesh = BuildMesh(_cutJobPosVertices, _cutJobPosNormals, _cutJobPosUv, _cutJobPosIndices, _cutJobPosVertexCount, _cutJobPosIndexCount);
            Mesh negMesh = BuildMesh(_cutJobNegVertices, _cutJobNegNormals, _cutJobNegUv, _cutJobNegIndices, _cutJobNegVertexCount, _cutJobNegIndexCount);
            if (posMesh == null || negMesh == null)
            {
                Log("Cut aborted because output mesh build failed.");
                ResetCutJobState();
                return;
            }

            Vector3 separationNormalWorld = _cutJobPlaneNormalWorld;
            if (separationNormalWorld.sqrMagnitude < 0.000001f)
            {
                separationNormalWorld = cutPlaneTransform != null ? cutPlaneTransform.up : Vector3.up;
            }
            separationNormalWorld.Normalize();

            ApplyOutputMesh(positiveOutputMeshFilter, positiveOutputMeshRenderer, positiveOutputMeshCollider, posMesh, true, separationNormalWorld);
            ApplyOutputMesh(negativeOutputMeshFilter, negativeOutputMeshRenderer, negativeOutputMeshCollider, negMesh, false, separationNormalWorld);

            if (disableSourceAfterCut)
            {
                if (sourceMeshRenderer != null)
                {
                    sourceMeshRenderer.enabled = false;
                }
                if (sourceMeshCollider != null)
                {
                    sourceMeshCollider.enabled = false;
                }
            }
            else if (updateSourceColliderWhenNotDisabling && sourceMeshCollider != null)
            {
                sourceMeshCollider.sharedMesh = null;
                sourceMeshCollider.sharedMesh = _cutJobSourceMesh;
            }

            _hasCut = true;
            _cutVersion++;
            if (enableGlobalSync && !_isApplyingSyncedCut)
            {
                TrySyncCutState(_cutJobPlanePointLocal, _cutJobPlaneNormalLocal);
            }
            Log("Cut complete.");
            ResetCutJobState();
        }

        private void ResetCutJobState()
        {
            _isCutJobRunning = false;
            _cutJobSourceMesh = null;
            _cutJobSourceVertices = null;
            _cutJobSourceTriangles = null;
            _cutJobSourceNormals = null;
            _cutJobSourceUv = null;
            _cutJobHasNormals = false;
            _cutJobHasUv = false;
            _cutJobCollectCapSegments = false;
            _cutJobTriangleCount = 0;
            _cutJobTriangleCursor = 0;
            _cutJobPosVertexCount = 0;
            _cutJobPosIndexCount = 0;
            _cutJobNegVertexCount = 0;
            _cutJobNegIndexCount = 0;
            _cutJobCapSegmentCount = 0;
        }

        private bool ValidateBindings()
        {
            if (sourceMeshFilter == null)
            {
                sourceMeshFilter = (MeshFilter)GetComponent(typeof(MeshFilter));
            }
            if (sourceMeshRenderer == null)
            {
                sourceMeshRenderer = (MeshRenderer)GetComponent(typeof(MeshRenderer));
            }
            if (sourceMeshCollider == null)
            {
                sourceMeshCollider = (MeshCollider)GetComponent(typeof(MeshCollider));
            }

            if (sourceMeshFilter == null || cutPlaneTransform == null)
            {
                Log("Required binding missing: sourceMeshFilter or cutPlaneTransform.");
                return false;
            }

            if (positiveOutputMeshFilter == null || negativeOutputMeshFilter == null)
            {
                Log("Required binding missing: output MeshFilter.");
                return false;
            }

            return true;
        }

        private void CaptureInitialTransformState(bool forceUpdate)
        {
            if (sourceMeshFilter == null)
            {
                sourceMeshFilter = (MeshFilter)GetComponent(typeof(MeshFilter));
            }

            if ((forceUpdate || !_sourceTransformCaptured) && sourceMeshFilter != null)
            {
                Transform sourceTransform = sourceMeshFilter.transform;
                _sourceInitialLocalPosition = sourceTransform.localPosition;
                _sourceInitialLocalRotation = sourceTransform.localRotation;
                _sourceInitialLocalScale = sourceTransform.localScale;
                _sourceTransformCaptured = true;
            }

            if ((forceUpdate || !_positiveTransformCaptured) && positiveOutputMeshFilter != null)
            {
                Transform positiveTransform = positiveOutputMeshFilter.transform;
                _positiveInitialLocalPosition = positiveTransform.localPosition;
                _positiveInitialLocalRotation = positiveTransform.localRotation;
                _positiveInitialLocalScale = positiveTransform.localScale;
                _positiveTransformCaptured = true;
            }

            if ((forceUpdate || !_negativeTransformCaptured) && negativeOutputMeshFilter != null)
            {
                Transform negativeTransform = negativeOutputMeshFilter.transform;
                _negativeInitialLocalPosition = negativeTransform.localPosition;
                _negativeInitialLocalRotation = negativeTransform.localRotation;
                _negativeInitialLocalScale = negativeTransform.localScale;
                _negativeTransformCaptured = true;
            }
        }

        private void ResetChildBranchesToInitial()
        {
            IkebanaUdonSnip positiveChild = positiveOutputMeshFilter != null ? positiveOutputMeshFilter.GetComponent<IkebanaUdonSnip>() : null;
            if (positiveChild != null)
            {
                positiveChild.ResetBranchToInitial();
            }

            IkebanaUdonSnip negativeChild = negativeOutputMeshFilter != null ? negativeOutputMeshFilter.GetComponent<IkebanaUdonSnip>() : null;
            if (negativeChild != null)
            {
                negativeChild.ResetBranchToInitial();
            }
        }

        private void RestoreUncutVisualState()
        {
            if (_isCutJobRunning)
            {
                ResetCutJobState();
            }

            CaptureInitialTransformState(false);
            RestoreTrackedTransform(
                sourceMeshFilter != null ? sourceMeshFilter.transform : null,
                _sourceTransformCaptured,
                _sourceInitialLocalPosition,
                _sourceInitialLocalRotation,
                _sourceInitialLocalScale);
            RestoreTrackedTransform(
                positiveOutputMeshFilter != null ? positiveOutputMeshFilter.transform : null,
                _positiveTransformCaptured,
                _positiveInitialLocalPosition,
                _positiveInitialLocalRotation,
                _positiveInitialLocalScale);
            RestoreTrackedTransform(
                negativeOutputMeshFilter != null ? negativeOutputMeshFilter.transform : null,
                _negativeTransformCaptured,
                _negativeInitialLocalPosition,
                _negativeInitialLocalRotation,
                _negativeInitialLocalScale);

            Mesh sourceMesh = GetSourceMesh();
            if (sourceMeshFilter != null && sourceMesh != null)
            {
                sourceMeshFilter.sharedMesh = sourceMesh;
            }

            if (sourceMeshRenderer != null)
            {
                sourceMeshRenderer.enabled = true;
            }

            if (sourceMeshCollider != null)
            {
                sourceMeshCollider.sharedMesh = null;
                sourceMeshCollider.sharedMesh = sourceMesh;
                sourceMeshCollider.enabled = true;
            }
            RefreshPickupHostStateAfterReset(sourceMesh);

            if (positiveOutputMeshFilter != null)
            {
                positiveOutputMeshFilter.sharedMesh = null;
            }
            if (positiveOutputMeshRenderer != null)
            {
                positiveOutputMeshRenderer.enabled = false;
            }
            if (positiveOutputMeshCollider != null)
            {
                positiveOutputMeshCollider.sharedMesh = null;
                positiveOutputMeshCollider.enabled = false;
            }

            if (negativeOutputMeshFilter != null)
            {
                negativeOutputMeshFilter.sharedMesh = null;
            }
            if (negativeOutputMeshRenderer != null)
            {
                negativeOutputMeshRenderer.enabled = false;
            }
            if (negativeOutputMeshCollider != null)
            {
                negativeOutputMeshCollider.sharedMesh = null;
                negativeOutputMeshCollider.enabled = false;
            }

            _hasCut = false;
            _overflowed = false;
            _useOverridePlanePoint = false;
            _useOverridePlaneNormal = false;
        }

        private void RefreshPickupHostStateAfterReset(Mesh sourceMesh)
        {
            Transform current = sourceMeshFilter != null ? sourceMeshFilter.transform : transform;
            int depth = 0;
            while (current != null && depth < 16)
            {
                VRCPickup pickup = current.GetComponent<VRCPickup>();
                if (pickup != null)
                {
                    Collider[] colliders = current.GetComponents<Collider>();
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        Collider collider = colliders[i];
                        if (collider == null)
                        {
                            continue;
                        }

                        collider.enabled = true;
                        MeshCollider meshCollider = (MeshCollider)collider.GetComponent(typeof(MeshCollider));
                        if (meshCollider != null)
                        {
                            if (meshCollider.sharedMesh == null)
                            {
                                if (sourceMesh != null)
                                {
                                    meshCollider.sharedMesh = sourceMesh;
                                }
                            }
                        }
                    }

                    Rigidbody rb = current.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.useGravity = false;
                        rb.isKinematic = true;
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    pickup.pickupable = true;
                    pickup.enabled = false;
                    pickup.enabled = true;
                    return;
                }

                current = current.parent;
                depth++;
            }
        }

        private void RestoreTrackedTransform(
            Transform targetTransform,
            bool hasCaptured,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            if (!hasCaptured || targetTransform == null)
            {
                return;
            }

            targetTransform.localPosition = localPosition;
            targetTransform.localRotation = localRotation;
            targetTransform.localScale = localScale;
        }

        private void EnsureClipBuffers()
        {
            if (_triInV == null)
            {
                _triInV = new Vector3[3];
                _triInN = new Vector3[3];
                _triInUv = new Vector2[3];
                _triInD = new float[3];
                _triPosPolyV = new Vector3[6];
                _triPosPolyN = new Vector3[6];
                _triPosPolyUv = new Vector2[6];
                _triPosPolyD = new float[6];
                _triNegPolyV = new Vector3[6];
                _triNegPolyN = new Vector3[6];
                _triNegPolyUv = new Vector2[6];
                _triNegPolyD = new float[6];
                _triCutPoints = new Vector3[3];
            }
        }

        private void EnsureCutWorkBuffers(bool collectCapSegments)
        {
            if (_cutJobPosVertices == null || _cutJobPosVertices.Length != maxGeneratedVerticesPerSide)
            {
                _cutJobPosVertices = new Vector3[maxGeneratedVerticesPerSide];
                _cutJobPosNormals = new Vector3[maxGeneratedVerticesPerSide];
                _cutJobPosUv = new Vector2[maxGeneratedVerticesPerSide];
                _cutJobNegVertices = new Vector3[maxGeneratedVerticesPerSide];
                _cutJobNegNormals = new Vector3[maxGeneratedVerticesPerSide];
                _cutJobNegUv = new Vector2[maxGeneratedVerticesPerSide];
            }

            if (_cutJobPosIndices == null || _cutJobPosIndices.Length != maxGeneratedIndicesPerSide)
            {
                _cutJobPosIndices = new int[maxGeneratedIndicesPerSide];
                _cutJobNegIndices = new int[maxGeneratedIndicesPerSide];
            }

            if (collectCapSegments)
            {
                if (_cutJobCapSegmentStart == null || _cutJobCapSegmentStart.Length != maxCutIntersections)
                {
                    _cutJobCapSegmentStart = new Vector3[maxCutIntersections];
                    _cutJobCapSegmentEnd = new Vector3[maxCutIntersections];
                }
            }
        }

        private Mesh GetSourceMesh()
        {
            Mesh resolved = ResolveSourceMesh();
            if (resolved != null)
            {
                _cachedSourceMesh = resolved;
                return resolved;
            }

            return _cachedSourceMesh;
        }

        private Mesh ResolveSourceMesh()
        {
            if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
            {
                return sourceMeshFilter.sharedMesh;
            }

            if (sourceMeshFilter != null && sourceMeshFilter.mesh != null)
            {
                return sourceMeshFilter.mesh;
            }

            if (sourceMeshCollider != null && sourceMeshCollider.sharedMesh != null)
            {
                return sourceMeshCollider.sharedMesh;
            }

            if (sourceMeshAsset != null)
            {
                return sourceMeshAsset;
            }

            if (autoCreateFallbackMeshForTest && sourceMeshFilter != null)
            {
                Mesh fallback = CreateFallbackBoxMesh();
                sourceMeshFilter.sharedMesh = fallback;
                if (sourceMeshCollider != null)
                {
                    sourceMeshCollider.sharedMesh = fallback;
                }
                sourceMeshAsset = fallback;
                Log("Source mesh was missing. Generated fallback box mesh for test.");
                return fallback;
            }

            return null;
        }

        private Mesh CreateFallbackBoxMesh()
        {
            Mesh mesh = new Mesh();

            Vector3[] v = new Vector3[8];
            v[0] = new Vector3(-0.1f, 0.0f, -0.1f);
            v[1] = new Vector3( 0.1f, 0.0f, -0.1f);
            v[2] = new Vector3( 0.1f, 0.0f,  0.1f);
            v[3] = new Vector3(-0.1f, 0.0f,  0.1f);
            v[4] = new Vector3(-0.1f, 1.0f, -0.1f);
            v[5] = new Vector3( 0.1f, 1.0f, -0.1f);
            v[6] = new Vector3( 0.1f, 1.0f,  0.1f);
            v[7] = new Vector3(-0.1f, 1.0f,  0.1f);

            int[] t = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            };

            Vector2[] uv = new Vector2[8];
            uv[0] = new Vector2(0f, 0f);
            uv[1] = new Vector2(1f, 0f);
            uv[2] = new Vector2(1f, 1f);
            uv[3] = new Vector2(0f, 1f);
            uv[4] = new Vector2(0f, 0f);
            uv[5] = new Vector2(1f, 0f);
            uv[6] = new Vector2(1f, 1f);
            uv[7] = new Vector2(0f, 1f);

            mesh.vertices = v;
            mesh.triangles = t;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool IsValidCutterTrigger(Collider other)
        {
            if (requiredCutterTriggerName == null || requiredCutterTriggerName.Length == 0)
            {
                return true;
            }

            return other.gameObject != null && other.gameObject.name == requiredCutterTriggerName;
        }

        private Vector3 EstimateHitPoint(Collider other)
        {
            Vector3 probePoint = cutPlaneTransform != null ? cutPlaneTransform.position : other.transform.position;
            Vector3 onOther = other.bounds.ClosestPoint(probePoint);
            Vector3 basePoint = onOther;

            if (sourceMeshCollider != null)
            {
                basePoint = sourceMeshCollider.bounds.ClosestPoint(onOther);
            }

            if (contactPointInset > 0f)
            {
                Vector3 normalWorld = cutPlaneTransform != null
                    ? (useCutPlaneUpAxis ? cutPlaneTransform.up : cutPlaneTransform.forward)
                    : Vector3.up;

                if (normalWorld.sqrMagnitude > 0.000001f)
                {
                    normalWorld.Normalize();

                    Vector3 centerWorld = transform.position;
                    if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
                    {
                        centerWorld = sourceMeshFilter.transform.TransformPoint(sourceMeshFilter.sharedMesh.bounds.center);
                    }

                    float sign = Vector3.Dot(centerWorld - basePoint, normalWorld) >= 0f ? 1f : -1f;
                    basePoint += normalWorld * (contactPointInset * sign);
                }
            }

            return basePoint;
        }

        private bool IsVertexIndexValid(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 0.000001f)
            {
                return Vector3.up;
            }
            return normal.normalized;
        }

        private void ClipAndEmitTriangle(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 n0,
            Vector3 n1,
            Vector3 n2,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            float d0,
            float d1,
            float d2,
            Vector3 referenceNormal,
            bool collectCapSegments,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount,
            Vector3[] capSegmentStart,
            Vector3[] capSegmentEnd,
            ref int capSegmentCount)
        {
            _triInV[0] = v0;
            _triInV[1] = v1;
            _triInV[2] = v2;
            _triInN[0] = n0;
            _triInN[1] = n1;
            _triInN[2] = n2;
            _triInUv[0] = uv0;
            _triInUv[1] = uv1;
            _triInUv[2] = uv2;
            _triInD[0] = d0;
            _triInD[1] = d1;
            _triInD[2] = d2;

            int posCount = ClipPolygonHalfSpace(_triInV, _triInN, _triInUv, _triInD, 3, true, _triPosPolyV, _triPosPolyN, _triPosPolyUv, _triPosPolyD);
            EmitClippedPolygon(
                _triPosPolyV, _triPosPolyN, _triPosPolyUv, posCount, referenceNormal,
                posVertices, posNormals, posUv, posIndices, ref posVertexCount, ref posIndexCount);

            int negCount = ClipPolygonHalfSpace(_triInV, _triInN, _triInUv, _triInD, 3, false, _triNegPolyV, _triNegPolyN, _triNegPolyUv, _triNegPolyD);
            EmitClippedPolygon(
                _triNegPolyV, _triNegPolyN, _triNegPolyUv, negCount, referenceNormal,
                negVertices, negNormals, negUv, negIndices, ref negVertexCount, ref negIndexCount);

            if (!collectCapSegments || capSegmentStart == null || capSegmentEnd == null)
            {
                return;
            }

            AddTriangleCutSegment(
                v0, v1, v2,
                n0, n1, n2,
                uv0, uv1, uv2,
                d0, d1, d2,
                capSegmentStart, capSegmentEnd, ref capSegmentCount);
        }

        private int ClipPolygonHalfSpace(
            Vector3[] inV,
            Vector3[] inN,
            Vector2[] inUv,
            float[] inD,
            int inCount,
            bool keepPositive,
            Vector3[] outV,
            Vector3[] outN,
            Vector2[] outUv,
            float[] outD)
        {
            int outCount = 0;
            if (inCount <= 0)
            {
                return 0;
            }

            for (int i = 0; i < inCount; i++)
            {
                int j = (i + 1) % inCount;
                Vector3 aV = inV[i];
                Vector3 bV = inV[j];
                Vector3 aN = inN[i];
                Vector3 bN = inN[j];
                Vector2 aUv = inUv[i];
                Vector2 bUv = inUv[j];
                float aD = inD[i];
                float bD = inD[j];
                if (Mathf.Abs(aD) <= sideEpsilon)
                {
                    aD = 0f;
                }
                if (Mathf.Abs(bD) <= sideEpsilon)
                {
                    bD = 0f;
                }

                bool aInside = keepPositive ? (aD >= 0f) : (aD <= 0f);
                bool bInside = keepPositive ? (bD >= 0f) : (bD <= 0f);

                if (aInside && bInside)
                {
                    AppendClipVertex(outV, outN, outUv, outD, ref outCount, bV, bN, bUv, bD);
                }
                else if (aInside && !bInside)
                {
                    Vector3 iV;
                    Vector3 iN;
                    Vector2 iUv;
                    InterpolateVertex(aV, aN, aUv, aD, bV, bN, bUv, bD, out iV, out iN, out iUv);
                    AppendClipVertex(outV, outN, outUv, outD, ref outCount, iV, iN, iUv, 0f);
                }
                else if (!aInside && bInside)
                {
                    Vector3 iV;
                    Vector3 iN;
                    Vector2 iUv;
                    InterpolateVertex(aV, aN, aUv, aD, bV, bN, bUv, bD, out iV, out iN, out iUv);
                    AppendClipVertex(outV, outN, outUv, outD, ref outCount, iV, iN, iUv, 0f);
                    AppendClipVertex(outV, outN, outUv, outD, ref outCount, bV, bN, bUv, bD);
                }

                if (_overflowed)
                {
                    return 0;
                }
            }

            return outCount;
        }

        private void AppendClipVertex(
            Vector3[] outV,
            Vector3[] outN,
            Vector2[] outUv,
            float[] outD,
            ref int outCount,
            Vector3 v,
            Vector3 n,
            Vector2 uv,
            float d)
        {
            if (outCount >= outV.Length)
            {
                _overflowed = true;
                return;
            }

            outV[outCount] = v;
            outN[outCount] = n;
            outUv[outCount] = uv;
            outD[outCount] = d;
            outCount++;
        }

        private void EmitClippedPolygon(
            Vector3[] polyV,
            Vector3[] polyN,
            Vector2[] polyUv,
            int polyCount,
            Vector3 referenceNormal,
            Vector3[] dstVertices,
            Vector3[] dstNormals,
            Vector2[] dstUv,
            int[] dstIndices,
            ref int dstVertexCount,
            ref int dstIndexCount)
        {
            polyCount = CompactPolygonVertices(polyV, polyN, polyUv, polyCount);
            if (polyCount < 3 || _overflowed)
            {
                return;
            }

            for (int i = 1; i < polyCount - 1; i++)
            {
                AddTriangleAutoWinding(
                    polyV[0], polyV[i], polyV[i + 1],
                    polyN[0], polyN[i], polyN[i + 1],
                    polyUv[0], polyUv[i], polyUv[i + 1],
                    referenceNormal,
                    dstVertices, dstNormals, dstUv, dstIndices,
                    ref dstVertexCount, ref dstIndexCount);

                if (_overflowed)
                {
                    return;
                }
            }
        }

        private int CompactPolygonVertices(
            Vector3[] polyV,
            Vector3[] polyN,
            Vector2[] polyUv,
            int polyCount)
        {
            if (polyCount <= 1)
            {
                return polyCount;
            }

            float dedupeDistance = 0.0000001f;
            float mergeDistanceSq = dedupeDistance * dedupeDistance;
            int write = 0;
            for (int i = 0; i < polyCount; i++)
            {
                Vector3 current = polyV[i];
                bool duplicate = false;
                if (write > 0)
                {
                    Vector3 prev = polyV[write - 1];
                    if ((prev - current).sqrMagnitude <= mergeDistanceSq)
                    {
                        duplicate = true;
                    }
                }

                if (!duplicate)
                {
                    polyV[write] = polyV[i];
                    polyN[write] = polyN[i];
                    polyUv[write] = polyUv[i];
                    write++;
                }
            }

            if (write >= 2)
            {
                Vector3 first = polyV[0];
                Vector3 last = polyV[write - 1];
                if ((first - last).sqrMagnitude <= mergeDistanceSq)
                {
                    write--;
                }
            }

            return write;
        }

        private void AddTriangleCutSegment(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 n0,
            Vector3 n1,
            Vector3 n2,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            float d0,
            float d1,
            float d2,
            Vector3[] capSegmentStart,
            Vector3[] capSegmentEnd,
            ref int capSegmentCount)
        {
            int pointCount = 0;

            TryAddCrossPoint(v0, n0, uv0, d0, v1, n1, uv1, d1, _triCutPoints, ref pointCount);
            TryAddCrossPoint(v1, n1, uv1, d1, v2, n2, uv2, d2, _triCutPoints, ref pointCount);
            TryAddCrossPoint(v2, n2, uv2, d2, v0, n0, uv0, d0, _triCutPoints, ref pointCount);

            if (pointCount >= 2)
            {
                AddCutSegment(capSegmentStart, capSegmentEnd, ref capSegmentCount, _triCutPoints[0], _triCutPoints[1]);
            }
        }

        private void TryAddCrossPoint(
            Vector3 aV,
            Vector3 aN,
            Vector2 aUv,
            float aD,
            Vector3 bV,
            Vector3 bN,
            Vector2 bUv,
            float bD,
            Vector3[] points,
            ref int pointCount)
        {
            if (Mathf.Abs(aD) <= sideEpsilon)
            {
                AddUniquePoint(points, ref pointCount, aV);
            }

            if (Mathf.Abs(bD) <= sideEpsilon)
            {
                AddUniquePoint(points, ref pointCount, bV);
            }

            bool aOnPos = aD > sideEpsilon;
            bool aOnNeg = aD < -sideEpsilon;
            bool bOnPos = bD > sideEpsilon;
            bool bOnNeg = bD < -sideEpsilon;

            if ((aOnPos && bOnNeg) || (aOnNeg && bOnPos))
            {
                Vector3 iV;
                Vector3 iN;
                Vector2 iUv;
                InterpolateVertex(aV, aN, aUv, aD, bV, bN, bUv, bD, out iV, out iN, out iUv);
                AddUniquePoint(points, ref pointCount, iV);
            }
        }

        private void AddUniquePoint(Vector3[] points, ref int count, Vector3 point)
        {
            float capMergeDistance = GetCapMergeDistance();
            float mergeDistanceSq = capMergeDistance * capMergeDistance;
            for (int i = 0; i < count; i++)
            {
                if ((points[i] - point).sqrMagnitude <= mergeDistanceSq)
                {
                    return;
                }
            }

            if (count >= points.Length)
            {
                return;
            }

            points[count] = point;
            count++;
        }

        private void AddCutSegment(
            Vector3[] segmentStart,
            Vector3[] segmentEnd,
            ref int segmentCount,
            Vector3 start,
            Vector3 end)
        {
            if (segmentCount >= segmentStart.Length)
            {
                _overflowed = true;
                return;
            }

            segmentStart[segmentCount] = start;
            segmentEnd[segmentCount] = end;
            segmentCount++;
        }

        private void InterpolateVertex(
            Vector3 a,
            Vector3 an,
            Vector2 auv,
            float da,
            Vector3 b,
            Vector3 bn,
            Vector2 buv,
            float db,
            out Vector3 outVertex,
            out Vector3 outNormal,
            out Vector2 outUv)
        {
            float denom = da - db;
            float t = 0.5f;
            if (Mathf.Abs(denom) > 0.0000001f)
            {
                t = da / denom;
            }

            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            outVertex = a + ((b - a) * t);
            outNormal = an + ((bn - an) * t);
            if (outNormal.sqrMagnitude > 0.000001f)
            {
                outNormal.Normalize();
            }
            else
            {
                outNormal = Vector3.up;
            }

            outUv = auv + ((buv - auv) * t);
        }

        private void AddTriangleAutoWinding(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 n0,
            Vector3 n1,
            Vector3 n2,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            Vector3 referenceNormal,
            Vector3[] dstVertices,
            Vector3[] dstNormals,
            Vector2[] dstUv,
            int[] dstIndices,
            ref int dstVertexCount,
            ref int dstIndexCount)
        {
            if (_overflowed)
            {
                return;
            }

            if (dstVertexCount + 3 > dstVertices.Length || dstIndexCount + 3 > dstIndices.Length)
            {
                _overflowed = true;
                return;
            }

            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v0;
            Vector3 triNormal = Vector3.Cross(e0, e1);
            bool flip = Vector3.Dot(triNormal, referenceNormal) < 0f;

            int baseVertex = dstVertexCount;
            if (!flip)
            {
                dstVertices[baseVertex] = v0;
                dstNormals[baseVertex] = n0;
                dstUv[baseVertex] = uv0;

                dstVertices[baseVertex + 1] = v1;
                dstNormals[baseVertex + 1] = n1;
                dstUv[baseVertex + 1] = uv1;

                dstVertices[baseVertex + 2] = v2;
                dstNormals[baseVertex + 2] = n2;
                dstUv[baseVertex + 2] = uv2;
            }
            else
            {
                dstVertices[baseVertex] = v0;
                dstNormals[baseVertex] = n0;
                dstUv[baseVertex] = uv0;

                dstVertices[baseVertex + 1] = v2;
                dstNormals[baseVertex + 1] = n2;
                dstUv[baseVertex + 1] = uv2;

                dstVertices[baseVertex + 2] = v1;
                dstNormals[baseVertex + 2] = n1;
                dstUv[baseVertex + 2] = uv1;
            }

            dstIndices[dstIndexCount] = baseVertex;
            dstIndices[dstIndexCount + 1] = baseVertex + 1;
            dstIndices[dstIndexCount + 2] = baseVertex + 2;

            dstVertexCount += 3;
            dstIndexCount += 3;
        }

        private void AppendCapFaces(
            Vector3[] capSegmentStart,
            Vector3[] capSegmentEnd,
            int capSegmentCount,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount)
        {
            if (capSegmentCount < 1)
            {
                return;
            }

            Vector3[] uniquePoints = new Vector3[capSegmentCount * 2];
            int uniqueCount = 0;
            int[] edgeA = new int[capSegmentCount];
            int[] edgeB = new int[capSegmentCount];

            for (int i = 0; i < capSegmentCount; i++)
            {
                edgeA[i] = FindOrAddUniquePoint(uniquePoints, ref uniqueCount, capSegmentStart[i]);
                edgeB[i] = FindOrAddUniquePoint(uniquePoints, ref uniqueCount, capSegmentEnd[i]);
            }

            if (uniqueCount < 3)
            {
                return;
            }

            Vector3 axisSeed = Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            Vector3 axisX = Vector3.Cross(planeNormal, axisSeed);
            if (axisX.sqrMagnitude < 0.000001f)
            {
                axisX = Vector3.right;
            }
            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(planeNormal, axisX).normalized;

            bool[] usedEdge = new bool[capSegmentCount];
            int[] loopIndices = new int[uniqueCount + 1];

            for (int startEdge = 0; startEdge < capSegmentCount; startEdge++)
            {
                if (usedEdge[startEdge])
                {
                    continue;
                }

                int loopCount = 0;
                int startVertex = edgeA[startEdge];
                int currentVertex = edgeB[startEdge];
                int previousVertex = startVertex;
                usedEdge[startEdge] = true;

                loopIndices[loopCount] = startVertex;
                loopCount++;
                loopIndices[loopCount] = currentVertex;
                loopCount++;

                bool closed = false;
                while (loopCount <= uniqueCount)
                {
                    if (currentVertex == startVertex)
                    {
                        closed = true;
                        break;
                    }

                    int nextEdge = FindNextUnusedEdge(
                        usedEdge,
                        edgeA,
                        edgeB,
                        capSegmentCount,
                        currentVertex,
                        previousVertex,
                        uniquePoints,
                        axisX,
                        axisY);
                    if (nextEdge < 0)
                    {
                        break;
                    }

                    usedEdge[nextEdge] = true;
                    int nextVertex = edgeA[nextEdge] == currentVertex ? edgeB[nextEdge] : edgeA[nextEdge];
                    loopIndices[loopCount] = nextVertex;
                    loopCount++;
                    previousVertex = currentVertex;
                    currentVertex = nextVertex;
                }

                if (!closed || loopCount < 4)
                {
                    EmitCapPatchFromOpenEdges(
                        startEdge,
                        usedEdge,
                        edgeA,
                        edgeB,
                        capSegmentCount,
                        uniquePoints,
                        planePoint,
                        planeNormal,
                        axisX,
                        axisY,
                        posVertices, posNormals, posUv, posIndices,
                        ref posVertexCount, ref posIndexCount,
                        negVertices, negNormals, negUv, negIndices,
                        ref negVertexCount, ref negIndexCount);

                    if (_overflowed)
                    {
                        return;
                    }
                    continue;
                }

                int polygonVertexCount = loopCount - 1; // last vertex equals start vertex
                EmitCapLoop(
                    uniquePoints,
                    loopIndices,
                    polygonVertexCount,
                    planePoint,
                    planeNormal,
                    axisX,
                    axisY,
                    posVertices, posNormals, posUv, posIndices,
                    ref posVertexCount, ref posIndexCount,
                    negVertices, negNormals, negUv, negIndices,
                    ref negVertexCount, ref negIndexCount);

                if (_overflowed)
                {
                    return;
                }
            }
        }

        private void EmitCapPatchFromOpenEdges(
            int seedEdge,
            bool[] usedEdge,
            int[] edgeA,
            int[] edgeB,
            int edgeCount,
            Vector3[] uniquePoints,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3 axisX,
            Vector3 axisY,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount)
        {
            int[] queue = new int[edgeCount];
            int[] componentEdges = new int[edgeCount];
            int queueHead = 0;
            int queueTail = 0;
            int componentEdgeCount = 0;

            queue[queueTail] = seedEdge;
            queueTail++;
            componentEdges[componentEdgeCount] = seedEdge;
            componentEdgeCount++;

            while (queueHead < queueTail)
            {
                int e = queue[queueHead];
                queueHead++;

                int a = edgeA[e];
                int b = edgeB[e];

                for (int i = 0; i < edgeCount; i++)
                {
                    if (usedEdge[i])
                    {
                        continue;
                    }

                    if (edgeA[i] == a || edgeB[i] == a || edgeA[i] == b || edgeB[i] == b)
                    {
                        usedEdge[i] = true;
                        queue[queueTail] = i;
                        queueTail++;
                        componentEdges[componentEdgeCount] = i;
                        componentEdgeCount++;
                    }
                }
            }

            int[] polygon = new int[edgeCount * 2];
            int polygonCount = 0;
            for (int i = 0; i < componentEdgeCount; i++)
            {
                int e = componentEdges[i];
                AddUniqueVertexIndex(polygon, ref polygonCount, edgeA[e]);
                AddUniqueVertexIndex(polygon, ref polygonCount, edgeB[e]);
            }

            if (polygonCount < 3)
            {
                return;
            }

            EmitCapFanFromIndices(
                uniquePoints,
                polygon,
                polygonCount,
                planePoint,
                planeNormal,
                axisX,
                axisY,
                posVertices, posNormals, posUv, posIndices,
                ref posVertexCount, ref posIndexCount,
                negVertices, negNormals, negUv, negIndices,
                ref negVertexCount, ref negIndexCount);
        }

        private void AddUniqueVertexIndex(int[] indices, ref int count, int value)
        {
            for (int i = 0; i < count; i++)
            {
                if (indices[i] == value)
                {
                    return;
                }
            }

            if (count >= indices.Length)
            {
                _overflowed = true;
                return;
            }

            indices[count] = value;
            count++;
        }

        private int FindOrAddUniquePoint(Vector3[] uniquePoints, ref int uniqueCount, Vector3 point)
        {
            float capMergeDistance = GetCapMergeDistance();
            float mergeDistanceSq = capMergeDistance * capMergeDistance;
            for (int i = 0; i < uniqueCount; i++)
            {
                if ((uniquePoints[i] - point).sqrMagnitude <= mergeDistanceSq)
                {
                    return i;
                }
            }

            if (uniqueCount >= uniquePoints.Length)
            {
                _overflowed = true;
                return 0;
            }

            uniquePoints[uniqueCount] = point;
            uniqueCount++;
            return uniqueCount - 1;
        }

        private int FindNextUnusedEdge(
            bool[] usedEdge,
            int[] edgeA,
            int[] edgeB,
            int edgeCount,
            int currentVertex,
            int previousVertex,
            Vector3[] uniquePoints,
            Vector3 axisX,
            Vector3 axisY)
        {
            int bestEdge = -1;
            float bestScore = -1000f;
            bool hasForwardCandidate = false;

            float prevDx = 0f;
            float prevDy = 0f;
            bool hasPrevDir = false;
            if (previousVertex >= 0 && previousVertex < uniquePoints.Length)
            {
                Vector3 prevP = uniquePoints[previousVertex];
                Vector3 currP = uniquePoints[currentVertex];
                Vector3 prevDelta = currP - prevP;
                prevDx = Vector3.Dot(prevDelta, axisX);
                prevDy = Vector3.Dot(prevDelta, axisY);
                float prevLenSq = (prevDx * prevDx) + (prevDy * prevDy);
                if (prevLenSq > 0.0000000001f)
                {
                    float invLen = 1f / Mathf.Sqrt(prevLenSq);
                    prevDx *= invLen;
                    prevDy *= invLen;
                    hasPrevDir = true;
                }
            }

            for (int i = 0; i < edgeCount; i++)
            {
                if (usedEdge[i])
                {
                    continue;
                }

                if (edgeA[i] != currentVertex && edgeB[i] != currentVertex)
                {
                    continue;
                }

                int nextVertex = edgeA[i] == currentVertex ? edgeB[i] : edgeA[i];
                bool isBacktrack = nextVertex == previousVertex;
                if (!isBacktrack)
                {
                    hasForwardCandidate = true;
                }

                float score = 0f;
                if (hasPrevDir)
                {
                    Vector3 currP = uniquePoints[currentVertex];
                    Vector3 nextP = uniquePoints[nextVertex];
                    Vector3 nextDelta = nextP - currP;
                    float nextDx = Vector3.Dot(nextDelta, axisX);
                    float nextDy = Vector3.Dot(nextDelta, axisY);
                    float nextLenSq = (nextDx * nextDx) + (nextDy * nextDy);
                    if (nextLenSq > 0.0000000001f)
                    {
                        float invLen = 1f / Mathf.Sqrt(nextLenSq);
                        nextDx *= invLen;
                        nextDy *= invLen;
                        score = (prevDx * nextDx) + (prevDy * nextDy);
                    }
                }

                if (isBacktrack)
                {
                    score -= 2f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestEdge = i;
                }
            }

            if (bestEdge >= 0)
            {
                return bestEdge;
            }

            if (!hasForwardCandidate)
            {
                for (int i = 0; i < edgeCount; i++)
                {
                    if (usedEdge[i])
                    {
                        continue;
                    }

                    if (edgeA[i] == currentVertex || edgeB[i] == currentVertex)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private float GetCapMergeDistance()
        {
            float maxMerge = sideEpsilon * 0.25f;
            if (maxMerge < 0.000001f)
            {
                maxMerge = 0.000001f;
            }

            float candidate = mergeDistance;
            if (candidate > maxMerge)
            {
                candidate = maxMerge;
            }
            if (candidate < 0.000001f)
            {
                candidate = 0.000001f;
            }

            return candidate;
        }

        private void EmitCapLoop(
            Vector3[] uniquePoints,
            int[] loopIndices,
            int loopCount,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3 axisX,
            Vector3 axisY,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount)
        {
            if (loopCount < 3)
            {
                return;
            }

            int[] polygon = new int[loopCount];
            for (int i = 0; i < loopCount; i++)
            {
                polygon[i] = loopIndices[i];
            }

            float[] px = new float[loopCount];
            float[] py = new float[loopCount];
            for (int i = 0; i < loopCount; i++)
            {
                Vector3 p = uniquePoints[polygon[i]];
                Vector3 rel = p - planePoint;
                px[i] = Vector3.Dot(rel, axisX);
                py[i] = Vector3.Dot(rel, axisY);
            }

            bool ccw = SignedArea2D(px, py, loopCount) >= 0f;
            Vector3 posCapNormal = -planeNormal;
            Vector3 negCapNormal = planeNormal;

            int guard = 0;
            int vertexCount = loopCount;
            while (vertexCount > 2 && guard < 1024)
            {
                guard++;
                bool earFound = false;

                for (int i = 0; i < vertexCount; i++)
                {
                    int prev = (i + vertexCount - 1) % vertexCount;
                    int next = (i + 1) % vertexCount;
                    if (!IsConvex2D(px[prev], py[prev], px[i], py[i], px[next], py[next], ccw))
                    {
                        continue;
                    }

                    bool containsPoint = false;
                    for (int p = 0; p < vertexCount; p++)
                    {
                        if (p == prev || p == i || p == next)
                        {
                            continue;
                        }

                        if (PointInTriangle2D(
                            px[p], py[p],
                            px[prev], py[prev],
                            px[i], py[i],
                            px[next], py[next]))
                        {
                            containsPoint = true;
                            break;
                        }
                    }

                    if (containsPoint)
                    {
                        continue;
                    }

                    Vector3 a = uniquePoints[polygon[prev]];
                    Vector3 b = uniquePoints[polygon[i]];
                    Vector3 c = uniquePoints[polygon[next]];
                    Vector2 uvA = PlanarCapUv(a, planePoint, axisX, axisY);
                    Vector2 uvB = PlanarCapUv(b, planePoint, axisX, axisY);
                    Vector2 uvC = PlanarCapUv(c, planePoint, axisX, axisY);

                    AddTriangleAutoWinding(
                        a, b, c,
                        posCapNormal, posCapNormal, posCapNormal,
                        uvA, uvB, uvC,
                        posCapNormal,
                        posVertices, posNormals, posUv, posIndices,
                        ref posVertexCount, ref posIndexCount);

                    AddTriangleAutoWinding(
                        a, c, b,
                        negCapNormal, negCapNormal, negCapNormal,
                        uvA, uvC, uvB,
                        negCapNormal,
                        negVertices, negNormals, negUv, negIndices,
                        ref negVertexCount, ref negIndexCount);

                    if (_overflowed)
                    {
                        return;
                    }

                    for (int shift = i; shift < vertexCount - 1; shift++)
                    {
                        polygon[shift] = polygon[shift + 1];
                        px[shift] = px[shift + 1];
                        py[shift] = py[shift + 1];
                    }

                    vertexCount--;
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    EmitCapFanFromCurrentPolygon(
                        uniquePoints,
                        polygon,
                        vertexCount,
                        planePoint,
                        planeNormal,
                        axisX,
                        axisY,
                        posVertices, posNormals, posUv, posIndices,
                        ref posVertexCount, ref posIndexCount,
                        negVertices, negNormals, negUv, negIndices,
                        ref negVertexCount, ref negIndexCount);
                    break;
                }
            }
        }

        private void EmitCapFanFromCurrentPolygon(
            Vector3[] uniquePoints,
            int[] polygon,
            int vertexCount,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3 axisX,
            Vector3 axisY,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount)
        {
            if (vertexCount < 3)
            {
                return;
            }

            int[] indices = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                indices[i] = polygon[i];
            }

            EmitCapFanFromIndices(
                uniquePoints,
                indices,
                vertexCount,
                planePoint,
                planeNormal,
                axisX,
                axisY,
                posVertices, posNormals, posUv, posIndices,
                ref posVertexCount, ref posIndexCount,
                negVertices, negNormals, negUv, negIndices,
                ref negVertexCount, ref negIndexCount);
        }

        private void EmitCapFanFromIndices(
            Vector3[] uniquePoints,
            int[] polygonIndices,
            int polygonCount,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3 axisX,
            Vector3 axisY,
            Vector3[] posVertices,
            Vector3[] posNormals,
            Vector2[] posUv,
            int[] posIndices,
            ref int posVertexCount,
            ref int posIndexCount,
            Vector3[] negVertices,
            Vector3[] negNormals,
            Vector2[] negUv,
            int[] negIndices,
            ref int negVertexCount,
            ref int negIndexCount)
        {
            if (polygonCount < 3 || _overflowed)
            {
                return;
            }

            float centerX = 0f;
            float centerY = 0f;
            float[] angles = new float[polygonCount];
            int[] sorted = new int[polygonCount];

            for (int i = 0; i < polygonCount; i++)
            {
                Vector3 p = uniquePoints[polygonIndices[i]];
                Vector3 rel = p - planePoint;
                float x = Vector3.Dot(rel, axisX);
                float y = Vector3.Dot(rel, axisY);
                centerX += x;
                centerY += y;
                sorted[i] = polygonIndices[i];
                angles[i] = 0f;
            }

            centerX /= polygonCount;
            centerY /= polygonCount;

            for (int i = 0; i < polygonCount; i++)
            {
                Vector3 p = uniquePoints[sorted[i]];
                Vector3 rel = p - planePoint;
                float x = Vector3.Dot(rel, axisX);
                float y = Vector3.Dot(rel, axisY);
                angles[i] = Mathf.Atan2(y - centerY, x - centerX);
            }

            for (int i = 0; i < polygonCount - 1; i++)
            {
                for (int j = i + 1; j < polygonCount; j++)
                {
                    if (angles[i] > angles[j])
                    {
                        float ta = angles[i];
                        angles[i] = angles[j];
                        angles[j] = ta;

                        int tv = sorted[i];
                        sorted[i] = sorted[j];
                        sorted[j] = tv;
                    }
                }
            }

            Vector3 posCapNormal = -planeNormal;
            Vector3 negCapNormal = planeNormal;
            Vector3 anchor = uniquePoints[sorted[0]];
            Vector2 anchorUv = PlanarCapUv(anchor, planePoint, axisX, axisY);

            for (int i = 1; i < polygonCount - 1; i++)
            {
                Vector3 b = uniquePoints[sorted[i]];
                Vector3 c = uniquePoints[sorted[i + 1]];
                Vector2 uvB = PlanarCapUv(b, planePoint, axisX, axisY);
                Vector2 uvC = PlanarCapUv(c, planePoint, axisX, axisY);

                AddTriangleAutoWinding(
                    anchor, b, c,
                    posCapNormal, posCapNormal, posCapNormal,
                    anchorUv, uvB, uvC,
                    posCapNormal,
                    posVertices, posNormals, posUv, posIndices,
                    ref posVertexCount, ref posIndexCount);

                AddTriangleAutoWinding(
                    anchor, c, b,
                    negCapNormal, negCapNormal, negCapNormal,
                    anchorUv, uvC, uvB,
                    negCapNormal,
                    negVertices, negNormals, negUv, negIndices,
                    ref negVertexCount, ref negIndexCount);

                if (_overflowed)
                {
                    return;
                }
            }
        }

        private float SignedArea2D(float[] px, float[] py, int count)
        {
            float area = 0f;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                area += (px[i] * py[j]) - (px[j] * py[i]);
            }
            return area * 0.5f;
        }

        private bool IsConvex2D(float ax, float ay, float bx, float by, float cx, float cy, bool ccw)
        {
            float cross = ((bx - ax) * (cy - by)) - ((by - ay) * (cx - bx));
            return ccw ? (cross > 0.000001f) : (cross < -0.000001f);
        }

        private bool PointInTriangle2D(
            float px,
            float py,
            float ax,
            float ay,
            float bx,
            float by,
            float cx,
            float cy)
        {
            float v0x = cx - ax;
            float v0y = cy - ay;
            float v1x = bx - ax;
            float v1y = by - ay;
            float v2x = px - ax;
            float v2y = py - ay;

            float dot00 = (v0x * v0x) + (v0y * v0y);
            float dot01 = (v0x * v1x) + (v0y * v1y);
            float dot02 = (v0x * v2x) + (v0y * v2y);
            float dot11 = (v1x * v1x) + (v1y * v1y);
            float dot12 = (v1x * v2x) + (v1y * v2y);

            float denom = (dot00 * dot11) - (dot01 * dot01);
            if (Mathf.Abs(denom) < 0.0000001f)
            {
                return false;
            }

            float invDenom = 1f / denom;
            float u = ((dot11 * dot02) - (dot01 * dot12)) * invDenom;
            float v = ((dot00 * dot12) - (dot01 * dot02)) * invDenom;
            return u >= 0f && v >= 0f && (u + v) <= 1f;
        }

        private Vector2 PlanarCapUv(Vector3 point, Vector3 origin, Vector3 axisX, Vector3 axisY)
        {
            Vector3 relative = point - origin;
            return new Vector2(Vector3.Dot(relative, axisX), Vector3.Dot(relative, axisY));
        }

        private Mesh BuildMesh(
            Vector3[] srcVertices,
            Vector3[] srcNormals,
            Vector2[] srcUv,
            int[] srcIndices,
            int vertexCount,
            int indexCount)
        {
            if (srcVertices == null || srcNormals == null || srcUv == null || srcIndices == null)
            {
                return null;
            }

            if (vertexCount < 3 || indexCount < 3)
            {
                return null;
            }

            if (vertexCount > MaxMeshVertexCount)
            {
                Log("BuildMesh aborted because vertex count exceeded 65535.");
                return null;
            }

            Vector3[] outVertices = new Vector3[vertexCount];
            Vector3[] outNormals = new Vector3[vertexCount];
            Vector2[] outUv = new Vector2[vertexCount];
            int[] outIndices = new int[indexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                outVertices[i] = srcVertices[i];
                outNormals[i] = srcNormals[i];
                outUv[i] = srcUv[i];
            }

            for (int i = 0; i < indexCount; i++)
            {
                outIndices[i] = srcIndices[i];
            }

            Mesh mesh = new Mesh();
            mesh.vertices = outVertices;
            mesh.normals = outNormals;
            mesh.uv = outUv;
            mesh.triangles = outIndices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ApplyOutputMesh(
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            MeshCollider meshCollider,
            Mesh mesh,
            bool isPositiveSide,
            Vector3 separationNormalWorld)
        {
            if (meshFilter != null)
            {
                if (sourceMeshFilter != null)
                {
                    Transform src = sourceMeshFilter.transform;
                    Transform dst = meshFilter.transform;

                    if (dst.parent == src.parent)
                    {
                        dst.localPosition = src.localPosition;
                        dst.localRotation = src.localRotation;
                        dst.localScale = src.localScale;
                    }
                    else
                    {
                        dst.position = src.position;
                        dst.rotation = src.rotation;
                        dst.localScale = src.lossyScale;
                    }
                }

                meshFilter.sharedMesh = mesh;

                if (separatePiecesAfterCut && separationDistanceMeters > 0f)
                {
                    float sign = isPositiveSide ? 1f : -1f;
                    meshFilter.transform.position += separationNormalWorld * (separationDistanceMeters * sign);
                }

                IkebanaUdonSnip nextCutter = meshFilter.GetComponent<IkebanaUdonSnip>();
                if (nextCutter != null)
                {
                    nextCutter.NotifyActivatedFromParent();
                }
            }

            if (meshRenderer != null)
            {
                if (sourceMeshRenderer != null)
                {
                    meshRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
                }
                meshRenderer.enabled = true;
            }

            if (updateOutputColliders && meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.enabled = true;
            }
        }

        private void ApplySyncedCutFromState()
        {
            EnsureSyncArrays();
            int incomingOpCount = ClampSyncedOpCount();
            if (_syncRevision < _appliedSyncRevision)
            {
                return;
            }

            if (_syncRevision == _appliedSyncRevision && incomingOpCount <= _appliedSyncOpCount)
            {
                return;
            }

            if (_syncSchemaVersion != SyncSchemaVersionCurrent)
            {
                Log("Sync schema mismatch. Ignored remote sync state.");
                _appliedSyncRevision = _syncRevision;
                return;
            }

            int slotCount = Mathf.Clamp(_syncSlotCount, 1, MaxSyncSlots);
            int opCount = incomingOpCount;
            if (opCount > _syncLocalOpCount)
            {
                _syncLocalOpCount = opCount;
            }
            if (opCount > _syncPublishTargetOpCount)
            {
                _syncPublishTargetOpCount = opCount;
            }
            if (!Networking.IsOwner(gameObject))
            {
                _syncLocalOpCount = opCount;
                _syncPublishTargetOpCount = opCount;
                _isSyncChunkSending = false;
                _syncChunkRetryCount = 0;
            }
            bool sameCountCutRevision = _syncLastOpType == 1 && _syncRevision > _appliedSyncRevision && opCount == _appliedSyncOpCount;
            bool requiresFullReplay = _appliedSyncOpCount > opCount || _syncLastOpType == 2 || _syncLastOpType == 3 || sameCountCutRevision;
            if (requiresFullReplay)
            {
                RestoreUncutVisualState();
                _appliedSyncOpCount = 0;
            }

            if (opCount <= 0)
            {
                if (_syncCutStage > 0 && _syncLastOpType == 0)
                {
                    ApplyLegacySyncedCutIfNeeded();
                }
                else
                {
                    RestoreUncutVisualState();
                }
                _appliedSyncOpCount = 0;
                _appliedSyncRevision = _syncRevision;
                return;
            }

            for (int i = _appliedSyncOpCount; i < opCount; i++)
            {
                ApplySyncedOperationAtIndex(i, slotCount);
            }

            _appliedSyncOpCount = opCount;

            int localSlotId = GetConfiguredSlotId(slotCount);
            _syncCutStage = GetStageFromBits(_syncStageBits0, _syncStageBits1, _syncStageBits2, localSlotId);
            ValidateAndRepairStageBits(slotCount, opCount);
            _appliedSyncRevision = _syncRevision;
        }

        private void ApplyLegacySyncedCutIfNeeded()
        {
            if (_hasCut && !allowMultipleCuts)
            {
                return;
            }

            Vector3 localPoint = new Vector3(
                DequantizePointComponent(_syncPlanePointQx),
                DequantizePointComponent(_syncPlanePointQy),
                DequantizePointComponent(_syncPlanePointQz));
            Vector3 localNormal = new Vector3(
                DequantizeNormalComponent(_syncPlaneNormalQx),
                DequantizeNormalComponent(_syncPlaneNormalQy),
                DequantizeNormalComponent(_syncPlaneNormalQz));

            if (localNormal.sqrMagnitude < 0.000001f)
            {
                localNormal = fallbackPlaneNormalLocal;
            }
            localNormal.Normalize();

            _isApplyingSyncedCut = true;
            CutAtLocalPlane(localPoint, localNormal);
            _isApplyingSyncedCut = false;
        }

        private void ApplySyncedOperationAtIndex(int opIndex, int slotCount)
        {
            if (opIndex < 0 || opIndex >= MaxSyncOps)
            {
                return;
            }

            int localSlotId = GetConfiguredSlotId(slotCount);
            int opSlot = _syncOpSlotId[opIndex];
            if (opSlot < 0 || opSlot >= slotCount || opSlot != localSlotId)
            {
                return;
            }

            int opStage = _syncOpStage[opIndex];
            if (opStage <= 0)
            {
                return;
            }

            if (_hasCut && !allowMultipleCuts)
            {
                return;
            }

            Vector3 localPoint = new Vector3(
                DequantizePointComponent(_syncOpPlanePointQx[opIndex]),
                DequantizePointComponent(_syncOpPlanePointQy[opIndex]),
                DequantizePointComponent(_syncOpPlanePointQz[opIndex]));
            Vector3 localNormal = new Vector3(
                DequantizeNormalComponent(_syncOpPlaneNormalQx[opIndex]),
                DequantizeNormalComponent(_syncOpPlaneNormalQy[opIndex]),
                DequantizeNormalComponent(_syncOpPlaneNormalQz[opIndex]));
            if (localNormal.sqrMagnitude < 0.000001f)
            {
                localNormal = fallbackPlaneNormalLocal;
            }
            localNormal.Normalize();

            _isApplyingSyncedCut = true;
            CutAtLocalPlane(localPoint, localNormal);
            _isApplyingSyncedCut = false;
        }

        private void TrySyncCutState(Vector3 planePointLocal, Vector3 planeNormalLocal)
        {
            if (!EnsureOwnershipForSync())
            {
                Log("Sync skipped because owner could not be acquired.");
                return;
            }

            EnsureSyncArrays();
            NormalizeSyncConfig();

            if (planeNormalLocal.sqrMagnitude < 0.000001f)
            {
                planeNormalLocal = fallbackPlaneNormalLocal;
            }
            planeNormalLocal.Normalize();

            int slotCount = GetConfiguredSlotCount();
            int slotId = GetConfiguredSlotId(slotCount);
            int currentStage = GetLatestStageFromLocalOps(slotId, slotCount);
            int nextStage = currentStage + 1;
            if (nextStage > MaxCutStage)
            {
                nextStage = MaxCutStage;
            }

            _syncSchemaVersion = SyncSchemaVersionCurrent;
            _syncSlotCount = slotCount;
            _syncPlanePointQx = QuantizePointComponent(planePointLocal.x);
            _syncPlanePointQy = QuantizePointComponent(planePointLocal.y);
            _syncPlanePointQz = QuantizePointComponent(planePointLocal.z);
            _syncPlaneNormalQx = QuantizeNormalComponent(planeNormalLocal.x);
            _syncPlaneNormalQy = QuantizeNormalComponent(planeNormalLocal.y);
            _syncPlaneNormalQz = QuantizeNormalComponent(planeNormalLocal.z);
            _syncLastOpType = 1;
            _syncLastOpSlot = slotId;

            bool replacedOldestOperation = AppendAndTrimSyncOperation(slotId, nextStage, planePointLocal, planeNormalLocal);
            int opCount = ClampLocalOpCount();
            _syncPublishTargetOpCount = opCount;
            _syncCutStage = nextStage;
            _appliedSyncOpCount = opCount;

            // 履歴上限到達時はopCountが同じまま中身だけ更新されるため、1回は必ず再送する。
            if (replacedOldestOperation && _syncOpCount >= _syncPublishTargetOpCount)
            {
                Log("Sync op buffer was shifted at max capacity. Forcing full snapshot serialization.");
                int publishSlotCount = Mathf.Clamp(_syncSlotCount, 1, MaxSyncSlots);
                int publishSlotId = GetConfiguredSlotId(publishSlotCount);
                ApplyPublishedStageState(publishSlotCount, opCount, publishSlotId);
                _syncRevision = _syncRevision + 1;
                _appliedSyncRevision = _syncRevision;
                _isSyncChunkSending = false;
                _syncChunkRetryCount = 0;
                RequestSerialization();
                return;
            }

            BeginChunkSerialization();
        }

        private void TrySyncUndoState()
        {
            if (!EnsureOwnershipForSync())
            {
                Log("Undo sync skipped because owner could not be acquired.");
                return;
            }

            EnsureSyncArrays();
            NormalizeSyncConfig();

            int slotCount = GetConfiguredSlotCount();
            int slotId = GetConfiguredSlotId(slotCount);
            int opCount = ClampLocalOpCount();

            _syncSchemaVersion = SyncSchemaVersionCurrent;
            _syncSlotCount = slotCount;
            _syncLocalOpCount = opCount;
            _syncPublishTargetOpCount = opCount;
            _syncOpCount = opCount;
            _syncLastOpType = 2;
            _syncLastOpSlot = slotId;
            _syncPlanePointQx = 0;
            _syncPlanePointQy = 0;
            _syncPlanePointQz = 0;
            _syncPlaneNormalQx = 0;
            _syncPlaneNormalQy = 0;
            _syncPlaneNormalQz = 0;
            ApplyPublishedStageState(slotCount, opCount, slotId);

            _syncRevision = _syncRevision + 1;
            _appliedSyncRevision = _syncRevision;
            _appliedSyncOpCount = opCount;
            _isSyncChunkSending = false;
            _syncChunkRetryCount = 0;
            RequestSerialization();
        }

        private void TrySyncResetState()
        {
            if (!EnsureOwnershipForSync())
            {
                Log("Reset sync skipped because owner could not be acquired.");
                return;
            }

            EnsureSyncArrays();
            NormalizeSyncConfig();

            int slotCount = GetConfiguredSlotCount();
            int slotId = GetConfiguredSlotId(slotCount);
            ClearSyncOperationRange(0, MaxSyncOps);

            _syncSchemaVersion = SyncSchemaVersionCurrent;
            _syncSlotCount = slotCount;
            _syncLocalOpCount = 0;
            _syncPublishTargetOpCount = 0;
            _syncOpCount = 0;
            _syncStageBits0 = 0;
            _syncStageBits1 = 0;
            _syncStageBits2 = 0;
            _syncCutStage = 0;
            _syncLastOpType = 3;
            _syncLastOpSlot = slotId;
            _syncPlanePointQx = 0;
            _syncPlanePointQy = 0;
            _syncPlanePointQz = 0;
            _syncPlaneNormalQx = 0;
            _syncPlaneNormalQy = 0;
            _syncPlaneNormalQz = 0;

            _syncRevision = _syncRevision + 1;
            _appliedSyncRevision = _syncRevision;
            _appliedSyncOpCount = 0;
            _isSyncChunkSending = false;
            _syncChunkRetryCount = 0;
            RequestSerialization();
        }

        private bool AppendAndTrimSyncOperation(int slotId, int stage, Vector3 planePointLocal, Vector3 planeNormalLocal)
        {
            int opCount = ClampLocalOpCount();
            bool replacedOldestOperation = false;
            if (opCount >= MaxSyncOps)
            {
                ShiftSyncOperationsLeft(opCount - MaxSyncOps + 1);
                opCount = ClampLocalOpCount();
                replacedOldestOperation = true;
            }

            int writeIndex = opCount;
            _syncOpSlotId[writeIndex] = slotId;
            _syncOpStage[writeIndex] = Mathf.Clamp(stage, 0, 7);
            _syncOpPlanePointQx[writeIndex] = QuantizePointComponent(planePointLocal.x);
            _syncOpPlanePointQy[writeIndex] = QuantizePointComponent(planePointLocal.y);
            _syncOpPlanePointQz[writeIndex] = QuantizePointComponent(planePointLocal.z);
            _syncOpPlaneNormalQx[writeIndex] = QuantizeNormalComponent(planeNormalLocal.x);
            _syncOpPlaneNormalQy[writeIndex] = QuantizeNormalComponent(planeNormalLocal.y);
            _syncOpPlaneNormalQz[writeIndex] = QuantizeNormalComponent(planeNormalLocal.z);
            _syncLocalOpCount = writeIndex + 1;
            if (_syncPublishTargetOpCount < _syncLocalOpCount)
            {
                _syncPublishTargetOpCount = _syncLocalOpCount;
            }
            if (_syncOpCount > _syncLocalOpCount)
            {
                _syncOpCount = _syncLocalOpCount;
            }

            return replacedOldestOperation;
        }

        private void ShiftSyncOperationsLeft(int removeCount)
        {
            int opCount = ClampLocalOpCount();
            if (removeCount <= 0 || opCount <= 0)
            {
                return;
            }

            if (removeCount >= opCount)
            {
                ClearSyncOperationRange(0, opCount);
                _syncLocalOpCount = 0;
                _syncPublishTargetOpCount = 0;
                _syncOpCount = 0;
                _appliedSyncOpCount = 0;
                return;
            }

            int write = 0;
            for (int read = removeCount; read < opCount; read++)
            {
                _syncOpSlotId[write] = _syncOpSlotId[read];
                _syncOpStage[write] = _syncOpStage[read];
                _syncOpPlanePointQx[write] = _syncOpPlanePointQx[read];
                _syncOpPlanePointQy[write] = _syncOpPlanePointQy[read];
                _syncOpPlanePointQz[write] = _syncOpPlanePointQz[read];
                _syncOpPlaneNormalQx[write] = _syncOpPlaneNormalQx[read];
                _syncOpPlaneNormalQy[write] = _syncOpPlaneNormalQy[read];
                _syncOpPlaneNormalQz[write] = _syncOpPlaneNormalQz[read];
                write++;
            }

            ClearSyncOperationRange(write, opCount - write);
            _syncLocalOpCount = write;
            if (_syncPublishTargetOpCount > _syncLocalOpCount)
            {
                _syncPublishTargetOpCount = _syncLocalOpCount;
            }
            if (_syncOpCount > _syncLocalOpCount)
            {
                _syncOpCount = _syncLocalOpCount;
            }
            if (_appliedSyncOpCount > _syncOpCount)
            {
                _appliedSyncOpCount = _syncOpCount;
            }
        }

        private void ClearSyncOperationRange(int startIndex, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int index = startIndex + i;
                if (index < 0 || index >= MaxSyncOps)
                {
                    continue;
                }

                _syncOpSlotId[index] = 0;
                _syncOpStage[index] = 0;
                _syncOpPlanePointQx[index] = 0;
                _syncOpPlanePointQy[index] = 0;
                _syncOpPlanePointQz[index] = 0;
                _syncOpPlaneNormalQx[index] = 0;
                _syncOpPlaneNormalQy[index] = 0;
                _syncOpPlaneNormalQz[index] = 0;
            }
        }

        private void RebuildSyncStageBitsFromOps(int slotCount, int opCount, out int stageBits0, out int stageBits1, out int stageBits2)
        {
            slotCount = Mathf.Clamp(slotCount, 1, MaxSyncSlots);
            opCount = Mathf.Clamp(opCount, 0, MaxSyncOps);

            for (int i = 0; i < slotCount; i++)
            {
                _stageScratch[i] = 0;
            }

            for (int i = 0; i < opCount; i++)
            {
                int slot = _syncOpSlotId[i];
                if (slot < 0 || slot >= slotCount)
                {
                    continue;
                }

                int stage = _syncOpStage[i];
                if (stage < 0)
                {
                    stage = 0;
                }
                else if (stage > 7)
                {
                    stage = 7;
                }

                _stageScratch[slot] = stage;
            }

            stageBits0 = 0;
            stageBits1 = 0;
            stageBits2 = 0;

            for (int i = 0; i < slotCount; i++)
            {
                int stage = _stageScratch[i];
                int bit = 1 << i;
                if ((stage & 1) != 0)
                {
                    stageBits0 |= bit;
                }
                if ((stage & 2) != 0)
                {
                    stageBits1 |= bit;
                }
                if ((stage & 4) != 0)
                {
                    stageBits2 |= bit;
                }
            }
        }

        private int GetStageFromBits(int stageBits0, int stageBits1, int stageBits2, int slotId)
        {
            if (slotId < 0 || slotId >= MaxSyncSlots)
            {
                return 0;
            }

            int bit = 1 << slotId;
            int stage = 0;
            if ((stageBits0 & bit) != 0)
            {
                stage |= 1;
            }
            if ((stageBits1 & bit) != 0)
            {
                stage |= 2;
            }
            if ((stageBits2 & bit) != 0)
            {
                stage |= 4;
            }

            return stage;
        }

        private void ValidateAndRepairStageBits(int slotCount, int opCount)
        {
            int rebuiltStageBits0;
            int rebuiltStageBits1;
            int rebuiltStageBits2;
            RebuildSyncStageBitsFromOps(slotCount, opCount, out rebuiltStageBits0, out rebuiltStageBits1, out rebuiltStageBits2);

            if (rebuiltStageBits0 == _syncStageBits0 && rebuiltStageBits1 == _syncStageBits1 && rebuiltStageBits2 == _syncStageBits2)
            {
                return;
            }

            if (!Networking.IsOwner(gameObject))
            {
                return;
            }

            _syncStageBits0 = rebuiltStageBits0;
            _syncStageBits1 = rebuiltStageBits1;
            _syncStageBits2 = rebuiltStageBits2;

            int localSlotId = GetConfiguredSlotId(slotCount);
            _syncCutStage = GetStageFromBits(_syncStageBits0, _syncStageBits1, _syncStageBits2, localSlotId);

            _syncRevision = _syncRevision + 1;
            _appliedSyncRevision = _syncRevision;
            RequestSerialization();
        }

        private void BeginChunkSerialization()
        {
            if (!Networking.IsOwner(gameObject))
            {
                _isSyncChunkSending = false;
                return;
            }

            int localOpCount = ClampLocalOpCount();
            if (_syncPublishTargetOpCount < 0)
            {
                _syncPublishTargetOpCount = 0;
            }
            if (_syncPublishTargetOpCount > localOpCount)
            {
                _syncPublishTargetOpCount = localOpCount;
            }
            if (_syncOpCount > _syncPublishTargetOpCount)
            {
                _syncOpCount = _syncPublishTargetOpCount;
            }

            if (_syncOpCount >= _syncPublishTargetOpCount)
            {
                _isSyncChunkSending = false;
                return;
            }

            if (_isSyncChunkSending)
            {
                return;
            }

            _isSyncChunkSending = true;
            _syncChunkRetryCount = 0;
            PublishNextSyncChunk();
        }

        private void PublishNextSyncChunk()
        {
            if (!_isSyncChunkSending || !Networking.IsOwner(gameObject))
            {
                _isSyncChunkSending = false;
                return;
            }

            int localOpCount = ClampLocalOpCount();
            if (_syncPublishTargetOpCount > localOpCount)
            {
                _syncPublishTargetOpCount = localOpCount;
            }

            int publishedCount = ClampSyncedOpCount();
            int targetCount = _syncPublishTargetOpCount;
            if (publishedCount >= targetCount)
            {
                _isSyncChunkSending = false;
                return;
            }

            int nextCount = publishedCount + MaxOpsPerSerialization;
            if (nextCount > targetCount)
            {
                nextCount = targetCount;
            }
            _syncOpCount = nextCount;
            int slotCount = Mathf.Clamp(_syncSlotCount, 1, MaxSyncSlots);
            int slotId = GetConfiguredSlotId(slotCount);
            ApplyPublishedStageState(slotCount, _syncOpCount, slotId);
            _syncRevision = _syncRevision + 1;
            _appliedSyncRevision = _syncRevision;
            RequestSerialization();
        }

        private void ApplyPublishedStageState(int slotCount, int publishedOpCount, int localSlotId)
        {
            int rebuiltStageBits0;
            int rebuiltStageBits1;
            int rebuiltStageBits2;
            RebuildSyncStageBitsFromOps(slotCount, publishedOpCount, out rebuiltStageBits0, out rebuiltStageBits1, out rebuiltStageBits2);
            _syncStageBits0 = rebuiltStageBits0;
            _syncStageBits1 = rebuiltStageBits1;
            _syncStageBits2 = rebuiltStageBits2;
            _syncCutStage = GetStageFromBits(_syncStageBits0, _syncStageBits1, _syncStageBits2, localSlotId);
        }

        private int GetLatestStageFromLocalOps(int slotId, int slotCount)
        {
            if (slotId < 0 || slotId >= slotCount)
            {
                return 0;
            }

            int opCount = ClampLocalOpCount();
            int stage = 0;
            for (int i = 0; i < opCount; i++)
            {
                if (_syncOpSlotId[i] != slotId)
                {
                    continue;
                }

                int opStage = _syncOpStage[i];
                if (opStage < 0)
                {
                    opStage = 0;
                }
                else if (opStage > 7)
                {
                    opStage = 7;
                }

                stage = opStage;
            }

            return stage;
        }

        private int ClampLocalOpCount()
        {
            if (_syncLocalOpCount < 0)
            {
                _syncLocalOpCount = 0;
            }
            if (_syncLocalOpCount > MaxSyncOps)
            {
                _syncLocalOpCount = MaxSyncOps;
            }

            return _syncLocalOpCount;
        }

        private int ClampSyncedOpCount()
        {
            if (_syncOpCount < 0)
            {
                _syncOpCount = 0;
            }
            if (_syncOpCount > MaxSyncOps)
            {
                _syncOpCount = MaxSyncOps;
            }

            return _syncOpCount;
        }

        private void EnsureSyncArrays()
        {
            if (_syncOpSlotId == null || _syncOpSlotId.Length != MaxSyncOps)
            {
                _syncOpSlotId = new int[MaxSyncOps];
            }
            if (_syncOpStage == null || _syncOpStage.Length != MaxSyncOps)
            {
                _syncOpStage = new int[MaxSyncOps];
            }
            if (_syncOpPlanePointQx == null || _syncOpPlanePointQx.Length != MaxSyncOps)
            {
                _syncOpPlanePointQx = new int[MaxSyncOps];
            }
            if (_syncOpPlanePointQy == null || _syncOpPlanePointQy.Length != MaxSyncOps)
            {
                _syncOpPlanePointQy = new int[MaxSyncOps];
            }
            if (_syncOpPlanePointQz == null || _syncOpPlanePointQz.Length != MaxSyncOps)
            {
                _syncOpPlanePointQz = new int[MaxSyncOps];
            }
            if (_syncOpPlaneNormalQx == null || _syncOpPlaneNormalQx.Length != MaxSyncOps)
            {
                _syncOpPlaneNormalQx = new int[MaxSyncOps];
            }
            if (_syncOpPlaneNormalQy == null || _syncOpPlaneNormalQy.Length != MaxSyncOps)
            {
                _syncOpPlaneNormalQy = new int[MaxSyncOps];
            }
            if (_syncOpPlaneNormalQz == null || _syncOpPlaneNormalQz.Length != MaxSyncOps)
            {
                _syncOpPlaneNormalQz = new int[MaxSyncOps];
            }
            if (_stageScratch == null || _stageScratch.Length != MaxSyncSlots)
            {
                _stageScratch = new int[MaxSyncSlots];
            }
        }

        private void NormalizeSyncConfig()
        {
            if (syncSlotCount < 1)
            {
                syncSlotCount = 1;
            }
            if (syncSlotCount > MaxSyncSlots)
            {
                syncSlotCount = MaxSyncSlots;
            }

            if (syncSlotId < 0)
            {
                syncSlotId = 0;
            }
            if (syncSlotId >= syncSlotCount)
            {
                syncSlotId = syncSlotCount - 1;
            }
        }

        private int GetConfiguredSlotCount()
        {
            NormalizeSyncConfig();
            return syncSlotCount;
        }

        private int GetConfiguredSlotId(int slotCount)
        {
            NormalizeSyncConfig();
            if (slotCount < 1)
            {
                return 0;
            }

            if (syncSlotId >= slotCount)
            {
                return slotCount - 1;
            }

            return syncSlotId;
        }

        private bool EnsureOwnershipForSync()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
            {
                return false;
            }

            if (Networking.IsOwner(gameObject))
            {
                return true;
            }

            if (!autoTakeOwnershipOnCut)
            {
                return false;
            }

            Networking.SetOwner(localPlayer, gameObject);
            return Networking.IsOwner(gameObject);
        }

        private int QuantizePointComponent(float value)
        {
            int q = Mathf.RoundToInt(value * QPos);
            return Mathf.Clamp(q, QPosMin, QPosMax);
        }

        private int QuantizeNormalComponent(float value)
        {
            int q = Mathf.RoundToInt(value * QNorm);
            return Mathf.Clamp(q, -QNorm, QNorm);
        }

        private float DequantizePointComponent(int quantizedValue)
        {
            return (float)quantizedValue / (float)QPos;
        }

        private float DequantizeNormalComponent(int quantizedValue)
        {
            return (float)quantizedValue / (float)QNorm;
        }

        private void Log(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log("[IkebanaUdonSnip] " + message, this);
            }
        }
    }
}








