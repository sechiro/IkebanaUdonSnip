using UdonSharp;
using UnityEngine;

namespace Hatago.IkebanaUdonSnip
{
    [AddComponentMenu("Hatago/Ikebana/Snip Contact Picker")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class IkebanaSnipContactPicker : UdonSharpBehaviour
    {
        public int maxTrackedCutters = 64;
        public int maxUndoHistory = 64;
        public int maxRegisteredRootCutters = 16;
        public float minCutIntervalSeconds = 0.15f;
        public IkebanaUdonSnip rootCutter;
        public IkebanaUdonSnip[] registeredRootCutters;
        public int registeredRootCutterCount;
        public bool restrictToRootCutterBranch = true;
        public bool enableDebugLog;

        private IkebanaUdonSnip[] _trackedCutters;
        private int _trackedCount;
        private float _nextCutAllowedTime;
        private IkebanaUdonSnip[] _undoHistory;
        private int _undoHistoryCount;
        private IkebanaUdonSnip[] _managedCutters;
        private int _managedCount;
        private IkebanaUdonSnip[] _managedQueue;

        public void Start()
        {
            if (maxTrackedCutters < 1)
            {
                maxTrackedCutters = 1;
            }
            if (maxUndoHistory < 1)
            {
                maxUndoHistory = 1;
            }
            if (maxRegisteredRootCutters < 1)
            {
                maxRegisteredRootCutters = 1;
            }

            _trackedCutters = new IkebanaUdonSnip[maxTrackedCutters];
            _trackedCount = 0;
            _undoHistory = new IkebanaUdonSnip[maxUndoHistory];
            _undoHistoryCount = 0;
            if (registeredRootCutters == null || registeredRootCutters.Length != maxRegisteredRootCutters)
            {
                IkebanaUdonSnip[] resizedRoots = new IkebanaUdonSnip[maxRegisteredRootCutters];
                int copyCount = 0;
                if (registeredRootCutters != null)
                {
                    copyCount = registeredRootCutters.Length;
                    if (copyCount > resizedRoots.Length)
                    {
                        copyCount = resizedRoots.Length;
                    }
                }

                for (int i = 0; i < copyCount; i++)
                {
                    resizedRoots[i] = registeredRootCutters[i];
                }

                registeredRootCutters = resizedRoots;
            }
            if (registeredRootCutterCount < 0)
            {
                registeredRootCutterCount = 0;
            }
            if (registeredRootCutterCount > registeredRootCutters.Length)
            {
                registeredRootCutterCount = registeredRootCutters.Length;
            }
            if (registeredRootCutterCount <= 0 && rootCutter != null)
            {
                registeredRootCutters[0] = rootCutter;
                registeredRootCutterCount = 1;
            }
            _managedCutters = new IkebanaUdonSnip[maxTrackedCutters];
            _managedQueue = new IkebanaUdonSnip[maxTrackedCutters];
            _managedCount = 0;
            RefreshManagedCutterScope();
        }

        public void OnTriggerEnter(Collider other)
        {
            if (other == null)
            {
                return;
            }

            IkebanaUdonSnip cutter = ResolveCutter(other);
            if (cutter == null)
            {
                return;
            }
            if (!IsCutterInScope(cutter))
            {
                return;
            }

            if (Contains(cutter))
            {
                return;
            }

            if (_trackedCount >= _trackedCutters.Length)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] Cutter list is full.", this);
                }
                return;
            }

            _trackedCutters[_trackedCount] = cutter;
            _trackedCount++;
        }

        public void OnTriggerExit(Collider other)
        {
            if (other == null)
            {
                return;
            }

            IkebanaUdonSnip cutter = ResolveCutter(other);
            if (cutter == null)
            {
                return;
            }

            Remove(cutter);
        }

        public void CutOneTouchedTarget()
        {
            if (Time.time < _nextCutAllowedTime)
            {
                return;
            }

            Compact();
            while (_trackedCount > 0 && !IsCutterInScope(_trackedCutters[0]))
            {
                RemoveAt(0);
            }
            if (_trackedCount <= 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] No touched cutter.", this);
                }
                return;
            }

            IkebanaUdonSnip target = _trackedCutters[0];
            RemoveAt(0);

            if (target != null)
            {
                int beforeCutVersion = target.GetCutVersion();
                target.CutNow();
                if (target.GetCutVersion() > beforeCutVersion)
                {
                    _nextCutAllowedTime = Time.time + minCutIntervalSeconds;
                    PushUndoHistory(target);
                }
            }
        }

        public bool UndoLastCutTarget()
        {
            bool undone = false;
            while (_undoHistoryCount > 0)
            {
                IkebanaUdonSnip undoTarget = PopUndoHistory();
                if (undoTarget == null)
                {
                    continue;
                }
                if (!IsCutterInScope(undoTarget))
                {
                    continue;
                }

                undone = undoTarget.UndoLastCut();
                if (undone)
                {
                    break;
                }
            }

            if (!undone)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] Undo target history is empty or all targets had no cut state.", this);
                }
                return false;
            }

            _nextCutAllowedTime = Time.time + minCutIntervalSeconds;
            return true;
        }

        public bool HasUndoHistory()
        {
            return _undoHistoryCount > 0;
        }

        public void ResetAllCuts()
        {
            if (rootCutter == null)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] rootCutter is not assigned.", this);
                }
                return;
            }

            rootCutter.ResetBranchToInitial();
            RefreshManagedCutterScope();
            ClearTrackedCutters();
            ClearUndoHistory();
            _nextCutAllowedTime = Time.time + minCutIntervalSeconds;
        }

        public void RefreshManagedCutterScope()
        {
            ClearManagedCutters();
            if (!restrictToRootCutterBranch)
            {
                return;
            }

            if (_managedCutters == null || _managedQueue == null || _managedQueue.Length <= 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] Managed buffers are not initialized.", this);
                }
                return;
            }

            int rootCount = 0;
            if (registeredRootCutters != null)
            {
                rootCount = registeredRootCutterCount;
                if (rootCount > registeredRootCutters.Length)
                {
                    rootCount = registeredRootCutters.Length;
                }
            }

            int queueHead = 0;
            int queueTail = 0;
            for (int i = 0; i < rootCount && queueTail < _managedQueue.Length; i++)
            {
                IkebanaUdonSnip registeredRoot = registeredRootCutters[i];
                if (registeredRoot == null)
                {
                    continue;
                }

                _managedQueue[queueTail] = registeredRoot;
                queueTail++;
            }

            if (queueTail <= 0 && rootCutter != null)
            {
                _managedQueue[queueTail] = rootCutter;
                queueTail++;
            }

            if (queueTail <= 0)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] No registered root cutter for scoped tracking.", this);
                }
                return;
            }

            while (queueHead < queueTail)
            {
                IkebanaUdonSnip current = _managedQueue[queueHead];
                queueHead++;
                if (current == null)
                {
                    continue;
                }

                if (ContainsManaged(current))
                {
                    continue;
                }

                if (_managedCount >= _managedCutters.Length)
                {
                    if (enableDebugLog)
                    {
                        Debug.Log("[IkebanaSnipContactPicker] Managed cutter scope is full.", this);
                    }
                    break;
                }

                _managedCutters[_managedCount] = current;
                _managedCount++;

                EnqueueChildCutter(current.positiveOutputMeshFilter, ref queueTail);
                EnqueueChildCutter(current.negativeOutputMeshFilter, ref queueTail);
            }
        }

        public void RegisterRootCutter(IkebanaUdonSnip cutter)
        {
            if (cutter == null)
            {
                return;
            }

            if (maxRegisteredRootCutters < 1)
            {
                maxRegisteredRootCutters = 1;
            }

            if (registeredRootCutters == null || registeredRootCutters.Length != maxRegisteredRootCutters)
            {
                registeredRootCutters = new IkebanaUdonSnip[maxRegisteredRootCutters];
                registeredRootCutterCount = 0;
            }

            int count = registeredRootCutterCount;
            if (count < 0)
            {
                count = 0;
            }
            if (count > registeredRootCutters.Length)
            {
                count = registeredRootCutters.Length;
            }

            for (int i = 0; i < count; i++)
            {
                if (registeredRootCutters[i] == cutter)
                {
                    if (rootCutter == null)
                    {
                        rootCutter = cutter;
                    }
                    registeredRootCutterCount = count;
                    RefreshManagedCutterScope();
                    return;
                }
            }

            if (count >= registeredRootCutters.Length)
            {
                for (int i = 1; i < count; i++)
                {
                    registeredRootCutters[i - 1] = registeredRootCutters[i];
                }
                count = registeredRootCutters.Length - 1;
            }

            registeredRootCutters[count] = cutter;
            count++;
            registeredRootCutterCount = count;

            if (rootCutter == null)
            {
                rootCutter = cutter;
            }

            RefreshManagedCutterScope();
        }

        private void PushUndoHistory(IkebanaUdonSnip cutter)
        {
            if (cutter == null || _undoHistory == null || _undoHistory.Length <= 0)
            {
                return;
            }

            if (_undoHistoryCount >= _undoHistory.Length)
            {
                for (int i = 1; i < _undoHistoryCount; i++)
                {
                    _undoHistory[i - 1] = _undoHistory[i];
                }
                _undoHistoryCount = _undoHistory.Length - 1;
            }

            _undoHistory[_undoHistoryCount] = cutter;
            _undoHistoryCount++;
        }

        private IkebanaUdonSnip PopUndoHistory()
        {
            if (_undoHistoryCount <= 0)
            {
                return null;
            }

            int index = _undoHistoryCount - 1;
            IkebanaUdonSnip cutter = _undoHistory[index];
            _undoHistory[index] = null;
            _undoHistoryCount--;
            return cutter;
        }

        private void ClearUndoHistory()
        {
            for (int i = 0; i < _undoHistoryCount; i++)
            {
                _undoHistory[i] = null;
            }
            _undoHistoryCount = 0;
        }

        private void ClearTrackedCutters()
        {
            for (int i = 0; i < _trackedCount; i++)
            {
                _trackedCutters[i] = null;
            }
            _trackedCount = 0;
        }

        private void ClearManagedCutters()
        {
            if (_managedCutters != null)
            {
                for (int i = 0; i < _managedCutters.Length; i++)
                {
                    _managedCutters[i] = null;
                }
            }

            if (_managedQueue != null)
            {
                for (int i = 0; i < _managedQueue.Length; i++)
                {
                    _managedQueue[i] = null;
                }
            }

            _managedCount = 0;
        }

        private bool ContainsManaged(IkebanaUdonSnip cutter)
        {
            for (int i = 0; i < _managedCount; i++)
            {
                if (_managedCutters[i] == cutter)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCutterInScope(IkebanaUdonSnip cutter)
        {
            if (cutter == null)
            {
                return false;
            }

            if (!restrictToRootCutterBranch)
            {
                return true;
            }

            if (_managedCount <= 0)
            {
                RefreshManagedCutterScope();
            }

            return ContainsManaged(cutter);
        }

        private void EnqueueChildCutter(MeshFilter outputMeshFilter, ref int queueTail)
        {
            if (outputMeshFilter == null || _managedQueue == null)
            {
                return;
            }

            IkebanaUdonSnip child = outputMeshFilter.GetComponent<IkebanaUdonSnip>();
            if (child == null || ContainsManaged(child))
            {
                return;
            }

            if (queueTail >= _managedQueue.Length)
            {
                if (enableDebugLog)
                {
                    Debug.Log("[IkebanaSnipContactPicker] Managed queue overflowed.", this);
                }
                return;
            }

            _managedQueue[queueTail] = child;
            queueTail++;
        }

        private bool Contains(IkebanaUdonSnip cutter)
        {
            for (int i = 0; i < _trackedCount; i++)
            {
                if (_trackedCutters[i] == cutter)
                {
                    return true;
                }
            }

            return false;
        }

        private void Remove(IkebanaUdonSnip cutter)
        {
            for (int i = 0; i < _trackedCount; i++)
            {
                if (_trackedCutters[i] == cutter)
                {
                    RemoveAt(i);
                    return;
                }
            }
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < _trackedCount - 1; i++)
            {
                _trackedCutters[i] = _trackedCutters[i + 1];
            }

            if (_trackedCount > 0)
            {
                _trackedCutters[_trackedCount - 1] = null;
                _trackedCount--;
            }
        }

        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < _trackedCount; read++)
            {
                IkebanaUdonSnip cutter = _trackedCutters[read];
                if (cutter != null)
                {
                    _trackedCutters[write] = cutter;
                    write++;
                }
            }

            for (int i = write; i < _trackedCount; i++)
            {
                _trackedCutters[i] = null;
            }

            _trackedCount = write;
        }

        private IkebanaUdonSnip ResolveCutter(Collider other)
        {
            if (other == null)
            {
                return null;
            }

            IkebanaUdonSnip cutter = other.GetComponent<IkebanaUdonSnip>();
            if (cutter != null)
            {
                return cutter;
            }

            if (other.attachedRigidbody != null)
            {
                cutter = other.attachedRigidbody.GetComponent<IkebanaUdonSnip>();
                if (cutter != null)
                {
                    return cutter;
                }
            }

            Transform current = other.transform;
            int depth = 0;
            while (current != null && depth < 16)
            {
                cutter = current.GetComponent<IkebanaUdonSnip>();
                if (cutter != null)
                {
                    return cutter;
                }

                current = current.parent;
                depth++;
            }

            return null;
        }
    }
}



