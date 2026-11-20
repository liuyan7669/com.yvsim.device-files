using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yvsim.DeviceFiles
{
    [Preserve]
    internal sealed class DeviceFilesDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> PendingActions = new Queue<Action>();
        private static readonly object QueueGate = new object();
        private static DeviceFilesDispatcher instance;
        private static int mainThreadId;
        private static SynchronizationContext unitySynchronizationContext;
        private static volatile bool runtimeQueueReady;

        internal static bool IsMainThread =>
            mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId;

        internal static void EnsureMainThread()
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException("Device Files public APIs must be called on the Unity main thread.");
            }
        }

        internal static bool Schedule(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            try
            {
                if (!IsMainThread)
                {
                    SynchronizationContext context = unitySynchronizationContext;
                    if (context != null)
                    {
                        try
                        {
                            context.Post(_ => ExecuteScheduledAction(action), null);
                            return true;
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }

                    lock (QueueGate)
                    {
                        if (!runtimeQueueReady)
                        {
                            return false;
                        }

                        PendingActions.Enqueue(action);
                    }

                    return true;
                }

                return EnqueueOnMainThread(action);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        internal static GameObject EnsureHost(string objectName)
        {
            EnsureMainThread();
            EnsureInstance();
            if (instance.gameObject.name != objectName)
            {
                instance.gameObject.name = objectName;
            }

            return instance.gameObject;
        }

        internal static void PrepareForAsyncWork()
        {
            EnsureMainThread();
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                return;
            }
#endif
            EnsureInstance();
        }

        internal static bool StartRoutine(IEnumerator routine)
        {
            if (routine == null)
            {
                throw new ArgumentNullException(nameof(routine));
            }

            try
            {
                EnsureMainThread();
                EnsureInstance();
                instance.StartCoroutine(routine);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            CaptureMainThread();
            lock (QueueGate)
            {
                PendingActions.Clear();
            }

            instance = null;
            runtimeQueueReady = false;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditorState()
        {
            CaptureMainThread();
        }
#endif

        private static void CaptureMainThread()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            unitySynchronizationContext = SynchronizationContext.Current;
        }

        private static bool EnqueueOnMainThread(Action action)
        {
            try
            {
                EnsureInstance();
                lock (QueueGate)
                {
                    PendingActions.Enqueue(action);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static void ExecuteScheduledAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            EnsureMainThread();
            GameObject host = new GameObject("[Yvsim.DeviceFiles]");
            instance = host.AddComponent<DeviceFilesDispatcher>();
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            runtimeQueueReady = true;
        }

        private void Update()
        {
            Action[] actions;
            lock (QueueGate)
            {
                actions = PendingActions.ToArray();
                PendingActions.Clear();
            }

            for (int i = 0; i < actions.Length; i++)
            {
                try
                {
                    actions[i]();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
