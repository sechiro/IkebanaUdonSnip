#if UNITY_EDITOR
using System;
using System.Reflection;
using Hatago.IkebanaUdonSnip;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.Udon;

namespace Hatago.IkebanaUdonSnip.Editor
{
    public static class IkebanaSnipSceneBuilder
    {
        private const int DefaultRecursiveCutDepth = 5;
        private const int MinRecursiveCutDepth = 1;
        private const int MaxRecursiveCutDepth = 5;
        private const int MaxSyncSlots = 32;
        private const int ProgressRingSegmentCount = 48;
        private const float ProgressRingRadiusMeters = 0.03f;
        private const float ProgressRingThicknessMeters = 0.002f;
        private const float ControlLabelHeightMeters = 0.9f;
        private const float ControlLabelScale = 0.1f;
        private const float ControlLabelFontSize = 32f;
        private const string SetupRootNamePrefix = "IkebanaUdonSnip_";
        private const string ManualObjectSyncTypeName = "MimyLab.FukuroUdon.ManualObjectSync";
        private const string ManualObjectSyncAssemblyQualifiedName = "MimyLab.FukuroUdon.ManualObjectSync, FukuroUdon";
        private const string PickupPlatformOverrideTypeName = "MimyLab.FukuroUdon.PickupPlatformOverride";
        private const string PickupPlatformOverrideAssemblyQualifiedName = "MimyLab.FukuroUdon.PickupPlatformOverride, FukuroUdon";

        [MenuItem("Hatago/Ikebana/Open Cutter Setup Tool", false, 100)]
        public static void OpenCutterSetupTool()
        {
            IkebanaSnipSetupWindow.OpenWindow();
        }

        [MenuItem("Hatago/Ikebana/Create Cutter Setup (Quick Default Target)", false, 110)]
        public static void CreateConfiguredCutterSetup()
        {
            GameObject root = CreateSetupRoot(null, BuildSetupRootName(null));
            GameObject cutTarget = CreateCutTargetObject(root.transform);
            ConfigureSetup(root, cutTarget, DefaultRecursiveCutDepth, false, null, null);
        }

        [MenuItem("Hatago/Ikebana/Create Cutter Setup From Selected (Quick)", false, 111)]
        public static void CreateConfiguredCutterSetupFromSelected()
        {
            CreateConfiguredCutterSetupFromSelectedWithOptions(DefaultRecursiveCutDepth, null, false, null, null);
        }

        [MenuItem("Hatago/Ikebana/Open Cutter Setup Tool (Legacy Advanced)", false, 112)]
        public static void OpenCutterSetupToolLegacy()
        {
            IkebanaSnipSetupWindow.OpenWindow();
        }

        internal static void CreateConfiguredCutterSetupFromSelectedWithOptions(
            int recursiveCutDepth,
            GameObject cutTargetSource,
            bool reuseExistingScissor,
            GameObject existingScissorObject,
            GameObject scissorPrefab)
        {
            GameObject selectedCutTarget = cutTargetSource != null ? cutTargetSource : Selection.activeGameObject;
            if (selectedCutTarget == null)
            {
                Debug.LogWarning("[IkebanaSnipSceneBuilder] Select a CutTarget source object or assign CutTarget in setup tool.");
                return;
            }

            GameObject root = CreateSetupRoot(selectedCutTarget.transform, BuildSetupRootName(selectedCutTarget));
            GameObject cutTarget = InstantiateCutTargetSource(root.transform, selectedCutTarget);
            if (cutTarget == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                Debug.LogWarning("[IkebanaSnipSceneBuilder] Failed to create CutTarget from specified source object.");
                return;
            }

            int normalizedDepth = Mathf.Clamp(recursiveCutDepth, MinRecursiveCutDepth, MaxRecursiveCutDepth);
            ConfigureSetup(root, cutTarget, normalizedDepth, reuseExistingScissor, existingScissorObject, scissorPrefab);
        }

        private static string BuildSetupRootName(GameObject cutTargetSource)
        {
            string suffix = "CutTargetObject";
            if (cutTargetSource != null)
            {
                string sourceName = cutTargetSource.name;
                if (!string.IsNullOrEmpty(sourceName))
                {
                    suffix = sourceName;
                }
            }

            return SetupRootNamePrefix + suffix;
        }

        private static GameObject CreateSetupRoot(Transform parentHint, string rootName)
        {
            string resolvedRootName = string.IsNullOrEmpty(rootName) ? (SetupRootNamePrefix + "CutTargetObject") : rootName;
            GameObject root = new GameObject(resolvedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Ikebana Cutter Setup");

            if (parentHint != null && !PrefabUtility.IsPartOfPrefabAsset(parentHint))
            {
                root.transform.SetParent(parentHint, false);
            }
            else if (Selection.activeTransform != null && !PrefabUtility.IsPartOfPrefabAsset(Selection.activeTransform))
            {
                root.transform.SetParent(Selection.activeTransform, false);
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject InstantiateCutTargetSource(Transform parent, GameObject cutTargetSource)
        {
            if (cutTargetSource == null)
            {
                return null;
            }

            GameObject instance;

            if (PrefabUtility.IsPartOfPrefabAsset(cutTargetSource))
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(cutTargetSource);
            }
            else
            {
                instance = UnityEngine.Object.Instantiate(cutTargetSource);
            }

            if (instance == null)
            {
                return null;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create Selected Cut Target");
            instance.name = "CutTargetObject";
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = new Vector3(0f, 0f, 0f);

            EnsureMeshColliderOnSource(instance);
            EnsureSyncedPickup(instance);
            return instance;
        }

        private static void ConfigureSetup(GameObject root, GameObject cutTarget, int recursiveCutDepth, bool reuseExistingScissor, GameObject existingScissorObject, GameObject scissorPrefab)
        {
            GameObject scissorObject = ResolveScissorObject(
                root.transform,
                reuseExistingScissor,
                existingScissorObject,
                scissorPrefab,
                out Transform cutPlaneTransform,
                out GameObject cutTrigger);
            IkebanaSnipContactPicker contactPicker = cutTrigger.GetComponent<IkebanaSnipContactPicker>();
            if (contactPicker == null)
            {
                contactPicker = UdonSharpUndo.AddComponent<IkebanaSnipContactPicker>(cutTrigger);
            }
            GameObject prebuiltPool = new GameObject("PrebuiltCutTargets");
            Undo.RegisterCreatedObjectUndo(prebuiltPool, "Create Prebuilt Cut Targets");
            prebuiltPool.transform.SetParent(root.transform, false);

            Mesh capacitySourceMesh = ResolveCapacitySourceMesh(cutTarget);
            ResolveCapacityPreset(capacitySourceMesh, out int sharedLimitVertices, out int sharedLimitIndices, out int sharedLimitIntersections);

            int syncSlotCount = Mathf.Clamp(CalculateBranchNodeCount(recursiveCutDepth), 1, MaxSyncSlots);
            int syncSlotCursor = 0;
            IkebanaUdonSnip cutter = BuildCutterBranch(
                cutTarget,
                prebuiltPool.transform,
                cutPlaneTransform,
                "CutTargetObject",
                recursiveCutDepth,
                sharedLimitVertices,
                sharedLimitIndices,
                sharedLimitIntersections,
                syncSlotCount,
                ref syncSlotCursor);
            contactPicker.RegisterRootCutter(cutter);
            IkebanaSnipEventRelay relay = scissorObject.GetComponent<IkebanaSnipEventRelay>();
            if (relay == null)
            {
                relay = UdonSharpUndo.AddComponent<IkebanaSnipEventRelay>(scissorObject);
            }
            IkebanaSnipUndoResetController sharedUndoController = GetOrCreateSharedUndoController(scissorObject, contactPicker);
            IkebanaSnipUndoResetController setupResetController = CreateSetupResetController(
                root.transform,
                scissorObject,
                cutTarget,
                cutter,
                contactPicker,
                sharedUndoController);

            UdonBehaviour contactPickerUdonBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(contactPicker);
            relay.targetBehaviour = contactPickerUdonBehaviour;
            relay.eventName = "CutOneTouchedTarget";
            relay.invokeOnPickup = true;
            relay.pickupTargetBehaviour = sharedUndoController != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(sharedUndoController) : null;
            relay.pickupEventName = "RequestHideUndoButtonGlobal";
            relay.invokeOnPickupUseDown = true;
            relay.invokeOnDrop = true;
            relay.dropTargetBehaviour = sharedUndoController != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(sharedUndoController) : null;
            relay.dropEventName = "RequestShowUndoButtonGlobal";
            relay.minInvokeIntervalSeconds = 0.15f;
            relay.enableDebugLog = false;

            EditorUtility.SetDirty(cutter);
            EditorUtility.SetDirty(contactPicker);
            EditorUtility.SetDirty(relay);
            if (sharedUndoController != null)
            {
                EditorUtility.SetDirty(sharedUndoController);
            }
            if (setupResetController != null)
            {
                EditorUtility.SetDirty(setupResetController);
            }
            EnforceManualObjectSyncInHierarchy(root);
            EnsureManualObjectSync(scissorObject);
            Selection.activeGameObject = root;

            EditorApplication.delayCall += () =>
            {
                if (root != null)
                {
                    EnforceManualObjectSyncInHierarchy(root);
                }
            };
        }

        private static IkebanaUdonSnip BuildCutterBranch(
            GameObject sourceObject,
            Transform outputsParent,
            Transform cutPlaneTransform,
            string branchName,
            int remainingDepth,
            int sharedLimitVertices,
            int sharedLimitIndices,
            int sharedLimitIntersections,
            int syncSlotCount,
            ref int syncSlotCursor)
        {
            EnsureMeshColliderOnSource(sourceObject);
            EnsureSyncedPickup(sourceObject);

            MeshFilter sourceMeshFilter = sourceObject.GetComponentInChildren<MeshFilter>();
            MeshRenderer sourceMeshRenderer = sourceObject.GetComponentInChildren<MeshRenderer>();
            MeshCollider sourceMeshCollider = sourceObject.GetComponentInChildren<MeshCollider>();
            GameObject cutterHost = sourceMeshCollider != null ? sourceMeshCollider.gameObject : sourceObject;

            GameObject positive = CreateOutput(outputsParent, branchName + "_CutResult_Positive_D" + remainingDepth);
            GameObject negative = CreateOutput(outputsParent, branchName + "_CutResult_Negative_D" + remainingDepth);

            IkebanaUdonSnip cutter = UdonSharpUndo.AddComponent<IkebanaUdonSnip>(cutterHost);
            cutter.sourceMeshFilter = sourceMeshFilter;
            cutter.sourceMeshRenderer = sourceMeshRenderer;
            cutter.sourceMeshCollider = sourceMeshCollider;
            if (sourceMeshFilter != null)
            {
                cutter.sourceMeshAsset = sourceMeshFilter.sharedMesh;
            }
            ApplyCapacityPreset(cutter, sharedLimitVertices, sharedLimitIndices, sharedLimitIntersections);

            cutter.positiveOutputMeshFilter = positive.GetComponent<MeshFilter>();
            cutter.positiveOutputMeshRenderer = positive.GetComponent<MeshRenderer>();
            cutter.positiveOutputMeshCollider = positive.GetComponent<MeshCollider>();
            cutter.negativeOutputMeshFilter = negative.GetComponent<MeshFilter>();
            cutter.negativeOutputMeshRenderer = negative.GetComponent<MeshRenderer>();
            cutter.negativeOutputMeshCollider = negative.GetComponent<MeshCollider>();
            cutter.cutPlaneTransform = cutPlaneTransform;
            cutter.useCutPlaneUpAxis = false;
            cutter.allowMultipleCuts = true;
            cutter.enableCutOnTriggerEnter = false;
            cutter.requiredCutterTriggerName = "CutTrigger";
            cutter.generateCutCaps = false;
            cutter.autoCreateFallbackMeshForTest = false;
            cutter.enableDebugLog = false;
            cutter.syncSlotCount = syncSlotCount;
            if (syncSlotCursor < 0)
            {
                syncSlotCursor = 0;
            }
            if (syncSlotCursor >= syncSlotCount)
            {
                syncSlotCursor = syncSlotCount - 1;
            }
            cutter.syncSlotId = syncSlotCursor;
            syncSlotCursor++;

            if (remainingDepth > 1)
            {
                BuildCutterBranch(
                    positive,
                    outputsParent,
                    cutPlaneTransform,
                    positive.name,
                    remainingDepth - 1,
                    sharedLimitVertices,
                    sharedLimitIndices,
                    sharedLimitIntersections,
                    syncSlotCount,
                    ref syncSlotCursor);
                BuildCutterBranch(
                    negative,
                    outputsParent,
                    cutPlaneTransform,
                    negative.name,
                    remainingDepth - 1,
                    sharedLimitVertices,
                    sharedLimitIndices,
                    sharedLimitIntersections,
                    syncSlotCount,
                    ref syncSlotCursor);
            }

            return cutter;
        }

        private static int CalculateBranchNodeCount(int depth)
        {
            if (depth < 1)
            {
                return 1;
            }

            int total = 0;
            int levelNodes = 1;
            for (int i = 0; i < depth; i++)
            {
                total += levelNodes;
                levelNodes *= 2;
            }

            return total;
        }

        private static Mesh ResolveCapacitySourceMesh(GameObject sourceObject)
        {
            if (sourceObject == null)
            {
                return null;
            }

            MeshFilter sourceMeshFilter = sourceObject.GetComponentInChildren<MeshFilter>();
            if (sourceMeshFilter == null)
            {
                return null;
            }

            Mesh sourceMesh = sourceMeshFilter.sharedMesh;
            if (sourceMesh == null)
            {
                sourceMesh = sourceMeshFilter.mesh;
            }

            return sourceMesh;
        }

        private static void ResolveCapacityPreset(Mesh sourceMesh, out int recommendedVertices, out int recommendedIndices, out int recommendedIntersections)
        {
            if (sourceMesh == null)
            {
                recommendedVertices = 65535;
                recommendedIndices = 65535;
                recommendedIntersections = 32768;
                return;
            }

            int sourceVertexCount = sourceMesh.vertexCount;
            recommendedVertices = Mathf.Clamp(sourceVertexCount * 8, 4096, 65535);
            recommendedIndices = recommendedVertices;
            recommendedIntersections = Mathf.Clamp(sourceVertexCount * 2, 4096, 32768);
        }

        private static void ApplyCapacityPreset(IkebanaUdonSnip cutter, int recommendedVertices, int recommendedIndices, int recommendedIntersections)
        {
            if (cutter == null)
            {
                return;
            }

            cutter.maxGeneratedVerticesPerSide = Mathf.Clamp(recommendedVertices, 3, 65535);
            cutter.maxGeneratedIndicesPerSide = Mathf.Clamp(recommendedIndices, 3, 65535);
            cutter.maxCutIntersections = Mathf.Clamp(recommendedIntersections, 1, 32768);
        }

        private static GameObject CreateCutTargetObject(Transform parent)
        {
            GameObject cutTarget = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Undo.RegisterCreatedObjectUndo(cutTarget, "Create Cut Target");
            cutTarget.name = "CutTargetObject";
            cutTarget.transform.SetParent(parent, false);
            cutTarget.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            cutTarget.transform.localRotation = Quaternion.identity;
            cutTarget.transform.localScale = new Vector3(0.15f, 0.5f, 0.15f);

            EnsureMeshColliderOnSource(cutTarget);
            EnsureSyncedPickup(cutTarget);

            return cutTarget;
        }

        private static void EnsureMeshColliderOnSource(GameObject cutTarget)
        {
            MeshFilter sourceMeshFilter = cutTarget.GetComponentInChildren<MeshFilter>();
            if (sourceMeshFilter == null)
            {
                return;
            }

            MeshCollider sourceCollider = sourceMeshFilter.GetComponent<MeshCollider>();
            if (sourceCollider == null)
            {
                sourceCollider = sourceMeshFilter.gameObject.AddComponent<MeshCollider>();
            }

            Mesh sourceMesh = sourceMeshFilter.sharedMesh;
            if (sourceMesh == null)
            {
                sourceMesh = sourceMeshFilter.mesh;
            }

            if (sourceMesh != null)
            {
                sourceMeshFilter.sharedMesh = sourceMesh;
                sourceCollider.sharedMesh = sourceMesh;
            }
            else
            {
                sourceCollider.sharedMesh = sourceMeshFilter.sharedMesh;
            }

            sourceCollider.convex = false;
        }

        private static GameObject ResolveScissorObject(
            Transform parent,
            bool reuseExistingScissor,
            GameObject existingScissorObject,
            GameObject scissorPrefab,
            out Transform cutPlaneTransform,
            out GameObject cutTrigger)
        {
            if (TryInstantiateScissorPrefab(scissorPrefab, parent, out GameObject prefabScissor, out cutPlaneTransform, out cutTrigger))
            {
                return prefabScissor;
            }

            if (reuseExistingScissor && TrySetupExistingScissor(existingScissorObject, out GameObject reusedScissor, out cutPlaneTransform, out cutTrigger))
            {
                return reusedScissor;
            }

            return CreateScissorObject(parent, out cutPlaneTransform, out cutTrigger);
        }

        private static bool TryInstantiateScissorPrefab(
            GameObject scissorPrefab,
            Transform parent,
            out GameObject scissorObject,
            out Transform cutPlaneTransform,
            out GameObject cutTrigger)
        {
            scissorObject = null;
            cutPlaneTransform = null;
            cutTrigger = null;

            if (scissorPrefab == null)
            {
                return false;
            }

            GameObject instance;
            if (PrefabUtility.IsPartOfPrefabAsset(scissorPrefab))
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(scissorPrefab);
            }
            else
            {
                instance = UnityEngine.Object.Instantiate(scissorPrefab);
            }

            if (instance == null)
            {
                Debug.LogWarning("[IkebanaSnipSceneBuilder] Failed to instantiate Scissor prefab. Fallback to default ScissorObject.");
                return false;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create Scissor Object From Prefab");
            instance.name = "ScissorObject";
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = new Vector3(0.3f, 0.55f, 0f);
            instance.transform.localRotation = Quaternion.identity;

            bool setupOk = TrySetupExistingScissor(instance, out scissorObject, out cutPlaneTransform, out cutTrigger);
            if (!setupOk)
            {
                Undo.DestroyObjectImmediate(instance);
                Debug.LogWarning("[IkebanaSnipSceneBuilder] Failed to setup Scissor prefab. Fallback to default ScissorObject.");
                scissorObject = null;
                cutPlaneTransform = null;
                cutTrigger = null;
                return false;
            }

            return true;
        }

        private static bool TrySetupExistingScissor(
            GameObject existingScissorObject,
            out GameObject scissorObject,
            out Transform cutPlaneTransform,
            out GameObject cutTrigger)
        {
            scissorObject = existingScissorObject;
            cutPlaneTransform = null;
            cutTrigger = null;
            if (scissorObject == null)
            {
                Debug.LogWarning("[IkebanaSnipSceneBuilder] Existing ScissorObject was not assigned. A new ScissorObject will be created.");
                return false;
            }

            Transform scissorTransform = scissorObject.transform;
            cutPlaneTransform = FindTransformInChildren(scissorTransform, "CutPlane");
            if (cutPlaneTransform == null)
            {
                GameObject cutPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Undo.RegisterCreatedObjectUndo(cutPlane, "Create Cut Plane");
                cutPlane.name = "CutPlane";
                cutPlane.transform.SetParent(scissorTransform, false);
                cutPlane.transform.localPosition = new Vector3(-0.1f, 0f, 0f);
                cutPlane.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                cutPlane.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

                MeshRenderer cutPlaneRenderer = cutPlane.GetComponent<MeshRenderer>();
                if (cutPlaneRenderer != null)
                {
                    cutPlaneRenderer.enabled = false;
                }

                Collider cutPlaneCollider = cutPlane.GetComponent<Collider>();
                if (cutPlaneCollider != null)
                {
                    UnityEngine.Object.DestroyImmediate(cutPlaneCollider);
                }

                cutPlaneTransform = cutPlane.transform;
            }

            cutTrigger = EnsureCutTriggerObject(scissorTransform, cutPlaneTransform);
            EnsureScissorRuntimeComponents(scissorObject);
            SetAllCollidersTrigger(scissorObject, true);
            EnsureManualObjectSync(scissorObject);
            return cutTrigger != null;
        }

        private static Transform FindTransformInChildren(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current != null && current.name == targetName)
                {
                    return current;
                }
            }

            return null;
        }

        private static GameObject EnsureCutTriggerObject(Transform scissorTransform, Transform cutPlaneTransform)
        {
            if (scissorTransform == null)
            {
                return null;
            }

            Transform triggerTransform = FindTransformInChildren(scissorTransform, "CutTrigger");
            GameObject cutTrigger = triggerTransform != null ? triggerTransform.gameObject : null;
            if (cutTrigger == null)
            {
                cutTrigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(cutTrigger, "Create Cut Trigger");
                cutTrigger.name = "CutTrigger";
                cutTrigger.transform.SetParent(scissorTransform, false);
                cutTrigger.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            }

            cutTrigger.transform.localPosition = cutPlaneTransform != null ? cutPlaneTransform.localPosition : new Vector3(-0.1f, 0f, 0f);
            cutTrigger.transform.localRotation = Quaternion.identity;

            Collider[] colliders = cutTrigger.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                if (!(collider is BoxCollider))
                {
                    Undo.DestroyObjectImmediate(collider);
                }
            }

            BoxCollider boxCollider = cutTrigger.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = cutTrigger.AddComponent<BoxCollider>();
            }
            boxCollider.isTrigger = true;

            return cutTrigger;
        }

        private static IkebanaSnipUndoResetController GetOrCreateSharedUndoController(
            GameObject scissorObject,
            IkebanaSnipContactPicker contactPicker)
        {
            if (scissorObject == null)
            {
                return null;
            }

            IkebanaSnipUndoResetController controller = FindSharedUndoController(scissorObject.transform);
            GameObject controlsRoot = controller != null ? controller.gameObject : null;
            if (controlsRoot == null)
            {
                controlsRoot = new GameObject("SharedUndoControls");
                Undo.RegisterCreatedObjectUndo(controlsRoot, "Create Shared Undo Controls");
                controlsRoot.transform.SetParent(scissorObject.transform, false);
                controlsRoot.transform.localPosition = Vector3.zero;
                controlsRoot.transform.localRotation = Quaternion.identity;
                controlsRoot.transform.localScale = Vector3.one;
            }

            if (controller == null)
            {
                controller = controlsRoot.GetComponent<IkebanaSnipUndoResetController>();
            }
            if (controller == null)
            {
                controller = UdonSharpUndo.AddComponent<IkebanaSnipUndoResetController>(controlsRoot);
            }

            GameObject undoButton = controller.undoButtonObject;
            if (undoButton == null)
            {
                undoButton = FindChildObject(controlsRoot.transform, "UndoButtonCube");
                if (undoButton == null)
                {
                    undoButton = CreateControlButtonCube(controlsRoot.transform, "UndoButtonCube", new Vector3(0f, 0.2f, 0f));
                }
            }
            GameObject undoProgress = FindChildObject(controlsRoot.transform, "UndoButtonProgressCircle");
            if (undoProgress == null)
            {
                undoProgress = CreateControlProgressCircle(controlsRoot.transform, "UndoButtonProgressCircle");
            }
            EnsureControlLabelExists(undoButton.transform, "UndoButtonLabel", "Undo");
            IkebanaSnipHoldUseButton undoHold = undoButton.GetComponent<IkebanaSnipHoldUseButton>();
            if (undoHold == null)
            {
                undoHold = UdonSharpUndo.AddComponent<IkebanaSnipHoldUseButton>(undoButton);
            }
            UdonBehaviour controllerUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            UdonBehaviour undoHoldUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(undoHold);

            controller.scissorTransform = scissorObject.transform;
            controller.resetReferenceTransform = null;
            controller.undoButtonObject = undoButton;
            controller.resetButtonObject = null;
            controller.contactPicker = contactPicker;
            controller.resetRootCutter = null;
            controller.undoOffsetMeters = 0.1f;
            controller.resetOffsetXMeters = -0.3f;
            controller.resetOffsetYMeters = 0.5f;
            controller.hideUndoButtonAfterUndo = true;
            controller.enableDebugLog = false;

            undoHold.targetBehaviour = controllerUdon;
            undoHold.holdCompleteEventName = "ExecuteUndo";
            undoHold.requiredHoldSeconds = 1f;
            undoHold.deactivateAfterInvoke = false;
            undoHold.progressVisual = undoProgress != null ? undoProgress.transform : null;
            undoHold.progressOffsetMeters = 0.05f;
            undoHold.progressRadiusMeters = 0.03f;
            undoHold.progressThicknessMeters = 0.002f;
            undoHold.enableDebugLog = false;

            SetInteractionConfig(undoHold, undoHoldUdon, "Undo", 0.1f);

            EditorUtility.SetDirty(undoHold);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static IkebanaSnipUndoResetController CreateSetupResetController(
            Transform parent,
            GameObject scissorObject,
            GameObject cutTarget,
            IkebanaUdonSnip resetRootCutter,
            IkebanaSnipContactPicker contactPicker,
            IkebanaSnipUndoResetController sharedUndoController)
        {
            GameObject controlsRoot = new GameObject("ResetControls");
            Undo.RegisterCreatedObjectUndo(controlsRoot, "Create Reset Controls");
            controlsRoot.transform.SetParent(parent, false);
            controlsRoot.transform.localPosition = Vector3.zero;
            controlsRoot.transform.localRotation = Quaternion.identity;
            controlsRoot.transform.localScale = Vector3.one;

            IkebanaSnipUndoResetController controller = UdonSharpUndo.AddComponent<IkebanaSnipUndoResetController>(controlsRoot);
            GameObject resetButton = CreateControlButtonCube(controlsRoot.transform, "ResetButtonCube", new Vector3(0.2f, 0.2f, 0f));
            GameObject resetProgress = CreateControlProgressCircle(controlsRoot.transform, "ResetButtonProgressCircle");
            EnsureControlLabelExists(resetButton.transform, "ResetButtonLabel", "Reset");
            IkebanaSnipHoldUseButton resetHold = UdonSharpUndo.AddComponent<IkebanaSnipHoldUseButton>(resetButton);
            UdonBehaviour controllerUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            UdonBehaviour resetHoldUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(resetHold);

            controller.scissorTransform = scissorObject != null ? scissorObject.transform : null;
            controller.resetReferenceTransform = cutTarget != null ? cutTarget.transform : null;
            controller.undoButtonObject = sharedUndoController != null ? sharedUndoController.undoButtonObject : null;
            controller.resetButtonObject = resetButton;
            controller.contactPicker = contactPicker;
            controller.resetRootCutter = resetRootCutter;
            controller.undoOffsetMeters = 0.1f;
            controller.resetOffsetXMeters = -0.3f;
            controller.resetOffsetYMeters = 0.5f;
            controller.hideUndoButtonAfterUndo = true;
            controller.enableDebugLog = false;

            resetHold.targetBehaviour = controllerUdon;
            resetHold.holdCompleteEventName = "ExecuteReset";
            resetHold.requiredHoldSeconds = 1f;
            resetHold.deactivateAfterInvoke = false;
            resetHold.progressVisual = resetProgress != null ? resetProgress.transform : null;
            resetHold.progressOffsetMeters = 0.05f;
            resetHold.progressRadiusMeters = 0.03f;
            resetHold.progressThicknessMeters = 0.002f;
            resetHold.enableDebugLog = false;

            SetInteractionConfig(resetHold, resetHoldUdon, "Reset", 0.1f);

            EditorUtility.SetDirty(resetHold);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static IkebanaSnipUndoResetController FindSharedUndoController(Transform scissorTransform)
        {
            if (scissorTransform == null)
            {
                return null;
            }

            IkebanaSnipUndoResetController[] controllers = UnityEngine.Object.FindObjectsOfType<IkebanaSnipUndoResetController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                IkebanaSnipUndoResetController current = controllers[i];
                if (current == null || current.scissorTransform != scissorTransform || current.undoButtonObject == null || current.resetButtonObject != null)
                {
                    continue;
                }

                return current;
            }

            return null;
        }

        private static GameObject FindChildObject(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Transform child = FindTransformInChildren(parent, name);
            return child != null ? child.gameObject : null;
        }

        private static void EnsureControlLabelExists(Transform parentButton, string name, string text)
        {
            if (parentButton == null)
            {
                return;
            }

            GameObject existing = FindChildObject(parentButton, name);
            if (existing != null)
            {
                TextMeshPro tmp = existing.GetComponent<TextMeshPro>();
                if (tmp != null)
                {
                    tmp.text = text;
                    tmp.fontSize = ControlLabelFontSize;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.enableWordWrapping = false;
                    tmp.raycastTarget = false;
                    tmp.color = Color.white;
                    EditorUtility.SetDirty(tmp);
                }

                IkebanaSnipFacePlayerLabel faceLabel = existing.GetComponent<IkebanaSnipFacePlayerLabel>();
                if (faceLabel == null)
                {
                    faceLabel = UdonSharpUndo.AddComponent<IkebanaSnipFacePlayerLabel>(existing);
                }
                faceLabel.labelTransform = existing.transform;
                faceLabel.worldUp = Vector3.up;
                faceLabel.enableDebugLog = false;
                EditorUtility.SetDirty(faceLabel);
                return;
            }

            CreateControlLabel(parentButton, name, text);
        }

        private static void SetInteractionConfig(Component udonSharpProxy, UdonBehaviour udonBehaviour, string interactionText, float proximity)
        {
            string resolvedText = string.IsNullOrEmpty(interactionText) ? "Use" : interactionText;
            float resolvedProximity = Mathf.Max(0.01f, proximity);

            ApplyInteractionConfigToSerializedObject(udonSharpProxy, resolvedText, resolvedProximity);
            ApplyInteractionConfigToSerializedObject(udonBehaviour, resolvedText, resolvedProximity);
            ApplyInteractionConfigToMembers(udonSharpProxy, resolvedText, resolvedProximity);
            ApplyInteractionConfigToMembers(udonBehaviour, resolvedText, resolvedProximity);
        }

        private static void ApplyInteractionConfigToSerializedObject(UnityEngine.Object target, string interactionText, float proximity)
        {
            if (target == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            bool changed = false;

            string[] textPropertyNames = new[] { "interactionText", "m_InteractionText", "_interactionText", "interactText", "m_InteractText", "_interactText" };
            for (int i = 0; i < textPropertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(textPropertyNames[i]);
                if (property == null || property.propertyType != SerializedPropertyType.String)
                {
                    continue;
                }

                property.stringValue = interactionText;
                changed = true;
                break;
            }

            string[] proximityPropertyNames = new[] { "proximity", "m_Proximity", "_proximity" };
            for (int i = 0; i < proximityPropertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(proximityPropertyNames[i]);
                if (property == null || property.propertyType != SerializedPropertyType.Float)
                {
                    continue;
                }

                property.floatValue = proximity;
                changed = true;
                break;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
            }
        }

        private static void ApplyInteractionConfigToMembers(object target, string interactionText, float proximity)
        {
            if (target == null)
            {
                return;
            }

            Type targetType = target.GetType();
            SetStringMember(target, targetType, "interactionText", interactionText);
            SetStringMember(target, targetType, "interactText", interactionText);
            SetStringMember(target, targetType, "InteractionText", interactionText);
            SetFloatMember(target, targetType, "proximity", proximity);
            SetFloatMember(target, targetType, "Proximity", proximity);
        }

        private static void SetStringMember(object target, Type targetType, string name, string value)
        {
            FieldInfo field = targetType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = targetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
            {
                property.SetValue(target, value, null);
            }
        }

        private static void SetFloatMember(object target, Type targetType, string name, float value)
        {
            FieldInfo field = targetType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = targetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(float) && property.CanWrite)
            {
                property.SetValue(target, value, null);
            }
        }

        private static GameObject CreateControlProgressCircle(Transform parent, string name)
        {
            GameObject circle = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(circle, "Create Control Progress Circle");
            circle.transform.SetParent(parent, false);
            circle.transform.localPosition = Vector3.zero;
            circle.transform.localRotation = Quaternion.identity;
            circle.SetActive(false);

            float segmentLength = ((2f * Mathf.PI * ProgressRingRadiusMeters) / ProgressRingSegmentCount) * 0.9f;
            float segmentStep = 360f / ProgressRingSegmentCount;
            string segmentPrefix = name.Contains("Undo") ? "UndoSeg_" : "ResetSeg_";

            for (int i = 0; i < ProgressRingSegmentCount; i++)
            {
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(segment, "Create Control Progress Segment");
                segment.name = segmentPrefix + i.ToString("00");
                segment.transform.SetParent(circle.transform, false);

                // 下側開始で反時計回りになるよう、角度を減算方向で配置する
                float angleDeg = -90f - ((i + 0.5f) * segmentStep);
                float angleRad = angleDeg * Mathf.Deg2Rad;
                float posX = Mathf.Cos(angleRad) * ProgressRingRadiusMeters;
                float posY = Mathf.Sin(angleRad) * ProgressRingRadiusMeters;
                segment.transform.localPosition = new Vector3(posX, posY, 0f);
                segment.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg - 90f);
                segment.transform.localScale = new Vector3(segmentLength, ProgressRingThicknessMeters, ProgressRingThicknessMeters);

                Collider segmentCollider = segment.GetComponent<Collider>();
                if (segmentCollider != null)
                {
                    Undo.DestroyObjectImmediate(segmentCollider);
                }
            }

            return circle;
        }

        private static GameObject CreateControlLabel(Transform parentButton, string name, string text)
        {
            GameObject label = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(label, "Create Control Label");
            label.transform.SetParent(parentButton, false);
            label.transform.localPosition = new Vector3(0f, ControlLabelHeightMeters, 0f);
            label.transform.localRotation = Quaternion.identity;
            label.transform.localScale = new Vector3(ControlLabelScale, ControlLabelScale, ControlLabelScale);

            TextMeshPro tmp = label.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = ControlLabelFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.color = Color.white;

            IkebanaSnipFacePlayerLabel faceLabel = UdonSharpUndo.AddComponent<IkebanaSnipFacePlayerLabel>(label);
            faceLabel.labelTransform = label.transform;
            faceLabel.worldUp = Vector3.up;
            faceLabel.enableDebugLog = false;

            EditorUtility.SetDirty(tmp);
            EditorUtility.SetDirty(faceLabel);
            return label;
        }

        private static GameObject CreateControlButtonCube(Transform parent, string name, Vector3 localPosition)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "Create Control Button Cube");
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);

            EnsureControlInteractable(cube);
            return cube;
        }

        private static void EnsureControlInteractable(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            BoxCollider boxCollider = target.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = target.AddComponent<BoxCollider>();
            }
            boxCollider.isTrigger = true;
            VRCPickup pickup = target.GetComponent<VRCPickup>();
            if (pickup != null)
            {
                Undo.DestroyObjectImmediate(pickup);
            }

            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Undo.DestroyObjectImmediate(rb);
            }
        }

        private static GameObject CreateScissorObject(Transform parent, out Transform cutPlaneTransform, out GameObject cutTrigger)
        {
            GameObject scissor = new GameObject("ScissorObject");
            Undo.RegisterCreatedObjectUndo(scissor, "Create Scissor Object");
            scissor.transform.SetParent(parent, false);
            scissor.transform.localPosition = new Vector3(0.3f, 0.55f, 0f);
            scissor.transform.localRotation = Quaternion.identity;
            scissor.transform.localScale = Vector3.one;

            Rigidbody rb = scissor.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            BoxCollider pickupCollider = scissor.AddComponent<BoxCollider>();
            pickupCollider.isTrigger = true;
            pickupCollider.center = new Vector3(-0.02f, 0f, 0f);
            pickupCollider.size = new Vector3(0.34f, 0.18f, 0.08f);

            VRCPickup pickup = scissor.AddComponent<VRCPickup>();
            pickup.proximity = 0.5f;
            pickup.version = VRCPickup.Version.Version_1_1;
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Grip;
            EnsurePickupPlatformOverride(scissor);

            EnsureManualObjectSync(scissor);

            GameObject bladeA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(bladeA, "Create Scissor Blade A");
            bladeA.name = "Blade_A";
            bladeA.transform.SetParent(scissor.transform, false);
            bladeA.transform.localPosition = new Vector3(-0.1f, 0f, 0f);
            bladeA.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            bladeA.transform.localScale = new Vector3(0.24f, 0.015f, 0.03f);

            GameObject bladeB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(bladeB, "Create Scissor Blade B");
            bladeB.name = "Blade_B";
            bladeB.transform.SetParent(scissor.transform, false);
            bladeB.transform.localPosition = new Vector3(-0.1f, 0f, 0f);
            bladeB.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            bladeB.transform.localScale = new Vector3(0.24f, 0.015f, 0.03f);

            GameObject handleA = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(handleA, "Create Scissor Handle A");
            handleA.name = "Handle_A";
            handleA.transform.SetParent(scissor.transform, false);
            handleA.transform.localPosition = new Vector3(0.065f, 0.03f, 0f);
            handleA.transform.localRotation = Quaternion.identity;
            handleA.transform.localScale = new Vector3(0.045f, 0.045f, 0.045f);

            GameObject handleB = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(handleB, "Create Scissor Handle B");
            handleB.name = "Handle_B";
            handleB.transform.SetParent(scissor.transform, false);
            handleB.transform.localPosition = new Vector3(0.065f, -0.03f, 0f);
            handleB.transform.localRotation = Quaternion.identity;
            handleB.transform.localScale = new Vector3(0.045f, 0.045f, 0.045f);

            GameObject cutPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(cutPlane, "Create Cut Plane");
            cutPlane.name = "CutPlane";
            cutPlane.transform.SetParent(scissor.transform, false);
            cutPlane.transform.localPosition = new Vector3(-0.1f, 0f, 0f);
            cutPlane.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cutPlane.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

            MeshRenderer cutPlaneRenderer = cutPlane.GetComponent<MeshRenderer>();
            if (cutPlaneRenderer != null)
            {
                cutPlaneRenderer.enabled = false;
            }

            Collider cutPlaneCollider = cutPlane.GetComponent<Collider>();
            if (cutPlaneCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(cutPlaneCollider);
            }

            cutTrigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cutTrigger, "Create Cut Trigger");
            cutTrigger.name = "CutTrigger";
            cutTrigger.transform.SetParent(scissor.transform, false);
            cutTrigger.transform.localPosition = new Vector3(-0.1f, 0f, 0f);
            cutTrigger.transform.localRotation = Quaternion.identity;
            cutTrigger.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);

            BoxCollider cutTriggerCollider = cutTrigger.GetComponent<BoxCollider>();
            if (cutTriggerCollider != null)
            {
                cutTriggerCollider.isTrigger = true;
            }

            SetAllCollidersTrigger(scissor, true);
            cutPlaneTransform = cutPlane.transform;
            return scissor;
        }

        private static void EnsureScissorRuntimeComponents(GameObject scissorObject)
        {
            if (scissorObject == null)
            {
                return;
            }

            Rigidbody rb = scissorObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = scissorObject.AddComponent<Rigidbody>();
            }
            rb.useGravity = false;
            rb.isKinematic = true;

            VRCPickup pickup = scissorObject.GetComponent<VRCPickup>();
            if (pickup == null)
            {
                pickup = scissorObject.AddComponent<VRCPickup>();
            }
            pickup.proximity = 0.5f;
            pickup.version = VRCPickup.Version.Version_1_1;
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Grip;
            EnsurePickupPlatformOverride(scissorObject);

            Collider[] colliders = scissorObject.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
            {
                BoxCollider fallbackCollider = scissorObject.GetComponent<BoxCollider>();
                if (fallbackCollider == null)
                {
                    fallbackCollider = scissorObject.AddComponent<BoxCollider>();
                }
                fallbackCollider.center = new Vector3(-0.02f, 0f, 0f);
                fallbackCollider.size = new Vector3(0.34f, 0.18f, 0.08f);
                fallbackCollider.isTrigger = true;
            }
        }

        private static void SetAllCollidersTrigger(GameObject root, bool isTrigger)
        {
            if (root == null)
            {
                return;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                collider.isTrigger = isTrigger;
                MeshCollider meshCollider = collider as MeshCollider;
                if (meshCollider != null && isTrigger && !meshCollider.convex)
                {
                    meshCollider.convex = true;
                }
            }
        }

        private static GameObject CreateOutput(Transform parent, string objectName)
        {
            GameObject output = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(output, "Create Cut Output");
            output.transform.SetParent(parent, false);
            output.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            output.transform.localRotation = Quaternion.identity;
            output.transform.localScale = Vector3.one;

            MeshFilter meshFilter = output.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = output.AddComponent<MeshRenderer>();
            MeshCollider meshCollider = output.AddComponent<MeshCollider>();

            meshFilter.sharedMesh = null;
            meshRenderer.enabled = false;
            meshCollider.enabled = false;
            meshCollider.convex = false;
            EnsureSyncedPickup(output);

            return output;
        }

        private static void EnsureSyncedPickup(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            MeshCollider meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = target.AddComponent<MeshCollider>();
            }
            meshCollider.convex = true;
            meshCollider.isTrigger = true;

            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = target.AddComponent<Rigidbody>();
            }
            rb.useGravity = false;
            rb.isKinematic = true;

            VRCPickup pickup = target.GetComponent<VRCPickup>();
            if (pickup == null)
            {
                pickup = target.AddComponent<VRCPickup>();
            }
            pickup.proximity = 0.1f;
            pickup.version = VRCPickup.Version.Version_1_1;
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Grip;
            EnsurePickupPlatformOverride(target);

            // VRCPickup追加後に再確認し、VRCObjectSync混在を防止する
            EnsureManualObjectSync(target);
        }

        private static void EnsurePickupPlatformOverride(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            VRCPickup pickup = target.GetComponent<VRCPickup>();
            if (pickup == null)
            {
                return;
            }

            Type pickupPlatformOverrideType = ResolvePickupPlatformOverrideType();
            if (pickupPlatformOverrideType == null)
            {
                return;
            }

            Component pickupPlatformOverride = target.GetComponent(pickupPlatformOverrideType);
            if (pickupPlatformOverride == null)
            {
                pickupPlatformOverride = target.AddUdonSharpComponent(pickupPlatformOverrideType);
            }
            if (pickupPlatformOverride == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(pickupPlatformOverride);
            bool changed = false;

            changed |= SetEnumPropertyByName(serializedObject, "_overridePlatform", "VR");
            changed |= SetEnumPropertyByName(serializedObject, "_autoHold", "Sometimes");

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pickupPlatformOverride);
            }
        }

        private static bool SetEnumPropertyByName(SerializedObject serializedObject, string propertyName, string enumName)
        {
            if (serializedObject == null || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(enumName))
            {
                return false;
            }

            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return false;
            }

            string[] enumNames = property.enumNames;
            if (enumNames == null || enumNames.Length <= 0)
            {
                return false;
            }

            int targetIndex = property.enumValueIndex;
            for (int i = 0; i < enumNames.Length; i++)
            {
                if (string.Equals(enumNames[i], enumName, StringComparison.Ordinal))
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex == property.enumValueIndex)
            {
                return false;
            }

            property.enumValueIndex = targetIndex;
            return true;
        }

        private static void EnsureManualObjectSync(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            Type manualObjectSyncType = ResolveManualObjectSyncType();
            if (manualObjectSyncType == null)
            {
                return;
            }

            Component manualObjectSync = target.GetComponent(manualObjectSyncType);
            if (manualObjectSync == null)
            {
                manualObjectSync = target.AddUdonSharpComponent(manualObjectSyncType);
            }

            VRCObjectSync legacyObjectSync = target.GetComponent<VRCObjectSync>();
            if (legacyObjectSync != null && manualObjectSync != null)
            {
                Undo.DestroyObjectImmediate(legacyObjectSync);
            }

            if (manualObjectSync != null)
            {
                EditorUtility.SetDirty(manualObjectSync);
            }
        }

        private static Type ResolveManualObjectSyncType()
        {
            Type manualObjectSyncType = Type.GetType(ManualObjectSyncAssemblyQualifiedName);
            if (manualObjectSyncType != null)
            {
                return manualObjectSyncType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                manualObjectSyncType = assembly.GetType(ManualObjectSyncTypeName, false);
                if (manualObjectSyncType != null)
                {
                    return manualObjectSyncType;
                }
            }

            Debug.LogError("[IkebanaSnipSceneBuilder] ManualObjectSync type could not be resolved. Check FukuroUdon package import/assembly settings.");
            return null;
        }

        private static Type ResolvePickupPlatformOverrideType()
        {
            Type pickupPlatformOverrideType = Type.GetType(PickupPlatformOverrideAssemblyQualifiedName);
            if (pickupPlatformOverrideType != null)
            {
                return pickupPlatformOverrideType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                pickupPlatformOverrideType = assembly.GetType(PickupPlatformOverrideTypeName, false);
                if (pickupPlatformOverrideType != null)
                {
                    return pickupPlatformOverrideType;
                }
            }

            Debug.LogWarning("[IkebanaSnipSceneBuilder] PickupPlatformOverride type could not be resolved. Check FukuroUdon package import/assembly settings.");
            return null;
        }

        private static void EnforceManualObjectSyncInHierarchy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            if (transforms == null)
            {
                return;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == null)
                {
                    continue;
                }

                GameObject target = current.gameObject;
                if (target == null)
                {
                    continue;
                }

                if (target.GetComponent<VRCObjectSync>() != null)
                {
                    EnsureManualObjectSync(target);
                }
            }
        }
    }

    public class IkebanaSnipSetupWindow : EditorWindow
    {
        private int _recursiveCutDepth = 5;
        private GameObject _cutTargetSource;
        private bool _useScissorPrefab;
        private GameObject _scissorPrefab;
        private bool _reuseExistingScissor;
        private GameObject _existingScissorObject;
        private Vector2 _scrollPosition;

        public static void OpenWindow()
        {
            IkebanaSnipSetupWindow window = GetWindow<IkebanaSnipSetupWindow>("Ikebana Snip Setup Tool");
            window.minSize = new Vector2(460f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            _recursiveCutDepth = Mathf.Clamp(_recursiveCutDepth, 1, 5);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ikebana Snip Setup Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("CutTarget未指定時は現在選択中オブジェクトを使用します。", MessageType.Info);
            EditorGUILayout.Space();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            _recursiveCutDepth = EditorGUILayout.IntSlider("Cut Depth", _recursiveCutDepth, 1, 5);
            _cutTargetSource = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("CutTarget (Optional)", "未指定時は現在選択中オブジェクトを使用"),
                _cutTargetSource,
                typeof(GameObject),
                true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scissor Source", EditorStyles.boldLabel);
            _useScissorPrefab = EditorGUILayout.ToggleLeft("Use Scissor Prefab", _useScissorPrefab);
            using (new EditorGUI.DisabledScope(!_useScissorPrefab))
            {
                _scissorPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Scissor Prefab",
                    _scissorPrefab,
                    typeof(GameObject),
                    false);
            }

            using (new EditorGUI.DisabledScope(_useScissorPrefab))
            {
                _reuseExistingScissor = EditorGUILayout.ToggleLeft("Reuse Existing ScissorObject", _reuseExistingScissor);
                using (new EditorGUI.DisabledScope(!_reuseExistingScissor))
                {
                    _existingScissorObject = (GameObject)EditorGUILayout.ObjectField(
                        "Existing ScissorObject",
                        _existingScissorObject,
                        typeof(GameObject),
                        true);
                }
            }
            EditorGUILayout.EndScrollView();

            string validationMessage;
            bool canCreate = ValidateInput(out validationMessage);
            if (!canCreate)
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("Create Setup", GUILayout.Height(32f)))
                {
                    CreateSetup();
                }
            }
        }

        private bool ValidateInput(out string validationMessage)
        {
            _recursiveCutDepth = Mathf.Clamp(_recursiveCutDepth, 1, 5);
            GameObject selectedCutTarget = _cutTargetSource != null ? _cutTargetSource : Selection.activeGameObject;
            if (selectedCutTarget == null)
            {
                validationMessage = "CutTargetを指定するか、Hierarchy/ProjectでCutTarget候補を選択してください。";
                return false;
            }

            if (_useScissorPrefab && _scissorPrefab == null)
            {
                validationMessage = "Use Scissor Prefab が有効な場合は Scissor Prefab を指定してください。";
                return false;
            }

            if (!_useScissorPrefab && _reuseExistingScissor && _existingScissorObject == null)
            {
                validationMessage = "Reuse Existing ScissorObject が有効な場合は Existing ScissorObject を指定してください。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private void CreateSetup()
        {
            bool usePrefab = _useScissorPrefab && _scissorPrefab != null;
            bool reuseExisting = !usePrefab && _reuseExistingScissor;
            GameObject scissorObject = reuseExisting ? _existingScissorObject : null;
            GameObject selectedScissorPrefab = usePrefab ? _scissorPrefab : null;
            IkebanaSnipSceneBuilder.CreateConfiguredCutterSetupFromSelectedWithOptions(
                _recursiveCutDepth,
                _cutTargetSource,
                reuseExisting,
                scissorObject,
                selectedScissorPrefab);
        }
    }
}
#endif







