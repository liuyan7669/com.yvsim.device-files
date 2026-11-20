using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace Yvsim.DeviceFiles
{
    [Serializable]
    internal sealed class AndroidNativeEvent
    {
        public string kind;
        public string requestId;
        public string name;
        public string mimeType;
        public string path;
        public string uri;
        public string message;
        public long size;
        public int count;

        internal static AndroidNativeEvent FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return UnityEngine.JsonUtility.FromJson<AndroidNativeEvent>(json);
        }
    }
}

#if UNITY_ANDROID && !UNITY_EDITOR
namespace Yvsim.DeviceFiles
{
    internal static class AndroidDeviceFiles
    {
        private const string JavaClassName = "com.yvsim.devicefiles.YvsimDeviceFilesAndroid";
        private const string CallbackObjectName = "[Yvsim.DeviceFiles.AndroidCallback]";
        private static AndroidJavaClass pluginClass;
        private static AndroidJavaObject activity;
        private static string activeSelectionRequestId;
        private static string activeSaveRequestId;
        private static string managedCacheRoot;
        private static bool selectedFileReadInProgress;

        internal static bool OpenFilePicker(string accept, bool allowMultiple, int maxFileBytes)
        {
            try
            {
                EnsureInitialized();
                string requestId = Guid.NewGuid().ToString("N");
                activeSelectionRequestId = requestId;
                pluginClass.CallStatic(
                    "openFilePicker",
                    activity,
                    CallbackObjectName,
                    requestId,
                    DeviceFilesUtility.BuildAndroidMimeTypesJson(accept),
                    allowMultiple,
                    (long)maxFileBytes);
                return true;
            }
            catch (Exception exception)
            {
                activeSelectionRequestId = null;
                Debug.LogException(exception);
                return false;
            }
        }

        internal static bool SaveFile(byte[] data, string fileName, string mimeType)
        {
            string temporaryPath = null;
            try
            {
                EnsureInitialized();
                string requestId = Guid.NewGuid().ToString("N");
                activeSaveRequestId = requestId;
                string saveRoot = Path.Combine(managedCacheRoot, "Save");
                Directory.CreateDirectory(saveRoot);
                temporaryPath = Path.Combine(saveRoot, Guid.NewGuid().ToString("N") + ".tmp");
                string capturedPath = temporaryPath;
                string safeFileName = DeviceFilesUtility.GetSafeFileName(fileName);
                string safeMimeType = string.IsNullOrEmpty(mimeType)
                    ? "application/octet-stream"
                    : mimeType;
                Task.Run(() => File.WriteAllBytes(capturedPath, data))
                    .ContinueWith(task =>
                    {
                        bool scheduled = DeviceFilesDispatcher.Schedule(() =>
                        {
                            if (requestId != activeSaveRequestId)
                            {
                                TryDeleteManagedSaveFile(capturedPath);
                                return;
                            }

                            if (task.IsFaulted || task.IsCanceled)
                            {
                                activeSaveRequestId = null;
                                TryDeleteManagedSaveFile(capturedPath);
                                string message = task.Exception == null
                                    ? "The Android save cache file could not be written."
                                    : task.Exception.GetBaseException().Message;
                                DeviceFiles.NotifySaveFailed(message);
                                return;
                            }

                            try
                            {
                                pluginClass.CallStatic(
                                    "saveFile",
                                    activity,
                                    CallbackObjectName,
                                    requestId,
                                    capturedPath,
                                    safeFileName,
                                    safeMimeType);
                            }
                            catch (Exception exception)
                            {
                                activeSaveRequestId = null;
                                TryDeleteManagedSaveFile(capturedPath);
                                DeviceFiles.NotifySaveFailed(exception.Message);
                            }
                        });
                        if (!scheduled)
                        {
                            try
                            {
                                File.Delete(capturedPath);
                            }
                            catch
                            {
                                // The managed stale-file cleanup will retry later.
                            }

                            Debug.LogError(
                                "Device Files could not return the Android cache result to the Unity main thread.");
                        }
                    }, TaskScheduler.Default);
                return true;
            }
            catch (Exception exception)
            {
                activeSaveRequestId = null;
                TryDeleteManagedSaveFile(temporaryPath);

                Debug.LogException(exception);
                return false;
            }
        }

        internal static void DrainNativeEvents()
        {
            EnsureInitialized();
            while (!selectedFileReadInProgress)
            {
                string json = pluginClass.CallStatic<string>("pollEvent");
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                ProcessNativeEvent(AndroidNativeEvent.FromJson(json));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            pluginClass = null;
            activity = null;
            activeSelectionRequestId = null;
            activeSaveRequestId = null;
            managedCacheRoot = null;
            selectedFileReadInProgress = false;
        }

        private static void EnsureInitialized()
        {
            if (pluginClass == null)
            {
                pluginClass = new AndroidJavaClass(JavaClassName);
            }

            if (activity == null)
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }
            }

            if (string.IsNullOrEmpty(managedCacheRoot))
            {
                managedCacheRoot = pluginClass.CallStatic<string>("initialize", activity);
                if (string.IsNullOrEmpty(managedCacheRoot))
                {
                    throw new InvalidOperationException("Android Device Files returned no managed cache root.");
                }
            }

            GameObject host = DeviceFilesDispatcher.EnsureHost(CallbackObjectName);
            if (host.GetComponent<AndroidDeviceFilesCallback>() == null)
            {
                host.AddComponent<AndroidDeviceFilesCallback>();
            }
        }

        private static void ProcessNativeEvent(AndroidNativeEvent nativeEvent)
        {
            if (nativeEvent == null || string.IsNullOrEmpty(nativeEvent.kind))
            {
                return;
            }

            switch (nativeEvent.kind)
            {
                case "fileSelected":
                    if (nativeEvent.requestId != activeSelectionRequestId)
                    {
                        TryDeleteManagedSelectedFile(nativeEvent.path);
                        return;
                    }

                    ProcessSelectedFile(nativeEvent);
                    break;
                case "selectionCompleted":
                    if (nativeEvent.requestId == activeSelectionRequestId)
                    {
                        activeSelectionRequestId = null;
                        DeviceFiles.NotifySelectionCompleted(nativeEvent.count);
                    }
                    break;
                case "selectionCanceled":
                    if (nativeEvent.requestId == activeSelectionRequestId)
                    {
                        activeSelectionRequestId = null;
                        DeviceFiles.NotifySelectionCanceled();
                    }
                    break;
                case "selectionFailed":
                    if (nativeEvent.requestId == activeSelectionRequestId)
                    {
                        activeSelectionRequestId = null;
                        DeviceFiles.NotifySelectionFailed(nativeEvent.message);
                    }
                    break;
                case "saveCompleted":
                    if (nativeEvent.requestId == activeSaveRequestId)
                    {
                        activeSaveRequestId = null;
                        DeviceFiles.NotifySaveCompleted(nativeEvent.uri);
                    }
                    break;
                case "saveCanceled":
                    if (nativeEvent.requestId == activeSaveRequestId)
                    {
                        activeSaveRequestId = null;
                        DeviceFiles.NotifySaveCanceled();
                    }
                    break;
                case "saveFailed":
                    if (nativeEvent.requestId == activeSaveRequestId)
                    {
                        activeSaveRequestId = null;
                        DeviceFiles.NotifySaveFailed(nativeEvent.message);
                    }
                    break;
                default:
                    Debug.LogWarning("Device Files received an unknown Android event: " + nativeEvent.kind);
                    break;
            }
        }

        private static void ProcessSelectedFile(AndroidNativeEvent nativeEvent)
        {
            selectedFileReadInProgress = true;
            string requestId = nativeEvent.requestId;
            Task.Run(() => DeviceFilesUtility.ReadSelectedFile(
                    nativeEvent.path,
                    0,
                    nativeEvent.name,
                    nativeEvent.mimeType,
                    true))
                .ContinueWith(task => DeviceFilesDispatcher.Schedule(() =>
                {
                    selectedFileReadInProgress = false;
                    if (requestId != activeSelectionRequestId)
                    {
                        TryDeleteManagedSelectedFile(nativeEvent.path);
                        DrainNativeEvents();
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        activeSelectionRequestId = null;
                        TryDeleteManagedSelectedFile(nativeEvent.path);
                        Exception exception = task.Exception == null
                            ? new InvalidOperationException("Android cached file read failed.")
                            : task.Exception.GetBaseException();
                        DeviceFiles.NotifySelectionFailed(exception.Message);
                        DrainNativeEvents();
                        return;
                    }

                    SelectedFile file = task.Result;
                    if (nativeEvent.size >= 0 && file.Size != nativeEvent.size)
                    {
                        activeSelectionRequestId = null;
                        TryDeleteManagedSelectedFile(nativeEvent.path);
                        DeviceFiles.NotifySelectionFailed(
                            "The Android cached file size changed before Unity could read it.");
                        DrainNativeEvents();
                        return;
                    }

                    DeviceFiles.NotifyFileSelected(file);
                    DrainNativeEvents();
                }), TaskScheduler.Default);
        }

        internal static bool TryDeleteManagedSelectedFile(string path)
        {
            try
            {
                EnsureInitialized();
                return pluginClass.CallStatic<bool>("deleteManagedSelectedFile", activity, path ?? string.Empty);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDeleteManagedSaveFile(string path)
        {
            try
            {
                EnsureInitialized();
                return pluginClass.CallStatic<bool>("deleteManagedSaveFile", activity, path ?? string.Empty);
            }
            catch
            {
                return false;
            }
        }
    }

    [Preserve]
    internal sealed class AndroidDeviceFilesCallback : MonoBehaviour
    {
        [Preserve]
        public void OnNativeEventsAvailable(string ignored)
        {
            try
            {
                AndroidDeviceFiles.DrainNativeEvents();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
#endif
