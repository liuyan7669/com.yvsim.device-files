using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AOT;
using UnityEngine;
using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yvsim.DeviceFiles
{
    /// <summary>
    /// 在 Unity Editor、Windows、Android 和 WebGL 中选择本地文件并保存 Unity 字节。
    /// 选择后的文件会完整复制到 <see cref="SelectedFile.Data"/>；此包不包含上传到远程服务器的网络协议。
    /// </summary>
    [Preserve]
    public static class DeviceFiles
    {
        public const int MinimumUrlTimeoutSeconds = 1;
        public const int MaximumUrlTimeoutSeconds = 3600;
        public const int DefaultUrlTimeoutSeconds = 60;

        private const int SelectionCompletedState = 0;
        private const int SelectionCanceledState = 1;
        private const int SelectionFailedState = 2;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static int nextSaveRequestId;
        private static int activeSaveRequestId;

#if UNITY_WEBGL && !UNITY_EDITOR
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FileReceivedCallback(
            IntPtr dataBytes,
            int dataLength,
            IntPtr nameBytes,
            int nameLength,
            IntPtr mimeBytes,
            int mimeLength,
            IntPtr previewUrlBytes,
            int previewUrlLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SelectionStateCallback(
            int state,
            int fileCount,
            IntPtr messageBytes,
            int messageLength);

        private static readonly FileReceivedCallback BrowserFileCallback = OnBrowserFileReceived;
        private static readonly SelectionStateCallback BrowserSelectionStateCallback = OnBrowserSelectionState;
        private static bool callbacksRegistered;

        [DllImport("__Internal")]
        private static extern void YvsimWebGLFile_RegisterCallbacks(
            FileReceivedCallback fileCallback,
            SelectionStateCallback selectionStateCallback);

        [DllImport("__Internal")]
        private static extern void YvsimWebGLFile_UnregisterCallbacks();

        [DllImport("__Internal")]
        private static extern int YvsimWebGLFile_OpenPicker(
            byte[] acceptBytes,
            int acceptLength,
            int allowMultiple,
            int maxFileBytes);

        [DllImport("__Internal")]
        private static extern int YvsimWebGLFile_DownloadBytes(
            byte[] dataBytes,
            int dataLength,
            byte[] nameBytes,
            int nameLength,
            byte[] mimeBytes,
            int mimeLength);

        [DllImport("__Internal")]
        private static extern int YvsimWebGLFile_ReleaseObjectUrl(byte[] previewUrlBytes, int previewUrlLength);
#endif

        /// <summary>每成功读取一个文件时触发。</summary>
        public static event Action<SelectedFile> FileSelected;

        /// <summary>一次文件选择中的所有文件读取完成时触发，参数为成功读取数。</summary>
        public static event Action<int> SelectionCompleted;

        /// <summary>用户取消文件选择时触发。</summary>
        public static event Action SelectionCanceled;

        /// <summary>文件选择窗口无法打开、文件超限或读取失败时触发。</summary>
        public static event Action<string> SelectionFailed;

        /// <summary>文件实际保存完成时触发。参数为可获取时的路径、URI 或浏览器文件名。</summary>
        public static event Action<string> SaveCompleted;

        /// <summary>用户取消保存时触发。</summary>
        public static event Action SaveCanceled;

        /// <summary>保存窗口无法打开或写入失败时触发。</summary>
        public static event Action<string> SaveFailed;

        /// <summary>最近一次成功收到的文件；尚未收到时为 <c>null</c>。</summary>
        public static SelectedFile LastSelectedFile { get; private set; }

        /// <summary>文件选择是否仍在等待或读取。</summary>
        public static bool IsSelectionInProgress { get; private set; }

        /// <summary>文件保存是否仍在等待用户选择位置或写入。</summary>
        public static bool IsSaveInProgress { get; private set; }

        /// <summary>当前平台是否实现了文件选择和保存后端。</summary>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_ANDROID || (UNITY_WEBGL && !UNITY_EDITOR)
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 打开系统文件选择窗口，并把选择结果完整复制到 Unity。
        /// Windows、Android 与 WebGL 支持多选；非 Windows Editor 只支持单选。
        /// </summary>
        /// <param name="accept">HTML 风格过滤器，例如 <c>image/*,text/*,.json</c>；空字符串表示不过滤。</param>
        /// <param name="allowMultiple">是否允许一次选择多个文件。</param>
        /// <param name="maxFileBytes">单文件最大字节数；0 表示仅受 32 位数组长度和可用内存限制。</param>
        /// <returns>请求被平台后端接受时返回 <c>true</c>；不支持、已有请求或无法启动时返回 <c>false</c>。</returns>
        public static bool OpenFilePicker(string accept = "", bool allowMultiple = false, int maxFileBytes = 0)
        {
            if (maxFileBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            }

            accept = accept ?? string.Empty;
            byte[] acceptBytes = EncodeUtf8(accept, nameof(accept));
            DeviceFilesDispatcher.EnsureMainThread();
            if (IsSelectionInProgress || IsSaveInProgress)
            {
                return false;
            }

            IsSelectionInProgress = true;

#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureWebGLInitialized();
            int opened = YvsimWebGLFile_OpenPicker(
                acceptBytes,
                acceptBytes.Length,
                allowMultiple ? 1 : 0,
                maxFileBytes);
            if (opened == 0)
            {
                IsSelectionInProgress = false;
                return false;
            }

            return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidDeviceFiles.OpenFilePicker(accept, allowMultiple, maxFileBytes))
            {
                return true;
            }

            IsSelectionInProgress = false;
            return false;
#elif UNITY_EDITOR_WIN
            EditorApplication.delayCall += () => OpenWindowsFilePicker(accept, allowMultiple, maxFileBytes);
            return true;
#elif UNITY_STANDALONE_WIN
            if (DeviceFilesDispatcher.Schedule(() => OpenWindowsFilePicker(accept, allowMultiple, maxFileBytes)))
            {
                return true;
            }

            IsSelectionInProgress = false;
            return false;
#elif UNITY_EDITOR
            if (allowMultiple)
            {
                Debug.LogWarning("Device Files uses single-file selection in this Unity Editor platform.");
            }

            EditorApplication.delayCall += () => OpenEditorFilePicker(maxFileBytes);
            return true;
#else
            IsSelectionInProgress = false;
            Debug.LogWarning("Device Files is not available on the current platform.");
            return false;
#endif
        }

        /// <summary>
        /// 打开系统保存窗口并写入字节。返回值只表示请求是否被接受；异步结果通过保存事件报告。
        /// WebGL 只能确认浏览器下载已发起，无法取得最终磁盘路径。
        /// </summary>
        [Preserve]
        public static bool SaveFile(
            byte[] data,
            string fileName,
            string mimeType = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (fileName.Length == 0)
            {
                throw new ArgumentException("The save file name must not be empty.", nameof(fileName));
            }

            string safeFileName = DeviceFilesUtility.GetSafeFileName(fileName);
            mimeType = GetSaveMimeType(mimeType, safeFileName);
            EncodeUtf8(safeFileName, nameof(fileName));
            EncodeUtf8(mimeType, nameof(mimeType));
            DeviceFilesDispatcher.EnsureMainThread();
            if (!TryBeginSave(out int requestId))
            {
                return false;
            }

            return StartPreparedSave(data, safeFileName, mimeType, requestId);
        }

        /// <summary>把已选择文件的字节保存到本地。</summary>
        public static bool SaveFile(SelectedFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            return SaveFile(file.Data, file.Name, file.MimeType);
        }

        /// <summary>保存最近一次成功选择的文件；尚无文件时返回 <c>false</c>。</summary>
        public static bool SaveLastSelectedFile()
        {
            DeviceFilesDispatcher.EnsureMainThread();
            SelectedFile file = LastSelectedFile;
            return file != null && SaveFile(file);
        }

        /// <summary>
        /// 异步读取应用可访问的文件路径，再进入与 <see cref="SaveFile(byte[], string, string)"/>
        /// 相同的平台保存流程。
        /// </summary>
        public static bool SaveFileFromPath(
            string filePath,
            string fileName = null,
            string mimeType = null,
            int maxFileBytes = 0)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (filePath.Length == 0)
            {
                throw new ArgumentException("The source file path must not be empty.", nameof(filePath));
            }

            if (maxFileBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            }

            string fullPath = Path.GetFullPath(filePath);
            string safeFileName = DeviceFilesUtility.GetSafeFileName(
                string.IsNullOrEmpty(fileName) ? Path.GetFileName(fullPath) : fileName);
            string safeMimeType = GetSaveMimeType(mimeType, safeFileName);
            EncodeUtf8(safeFileName, nameof(fileName));
            EncodeUtf8(safeMimeType, nameof(mimeType));
            DeviceFilesDispatcher.EnsureMainThread();
            if (!IsAvailable)
            {
                Debug.LogWarning("Device Files is not available on the current platform.");
                return false;
            }

            if (!TryBeginSave(out int requestId))
            {
                return false;
            }

            DeviceFilesDispatcher.PrepareForAsyncWork();
            bool started = DeviceFilesSourceLoader.LoadPath(
                fullPath,
                maxFileBytes,
                data => CompletePreparedSource(requestId, data, safeFileName, safeMimeType),
                message => NotifySaveFailed(requestId, message));
            if (!started)
            {
                EndSaveWithoutNotification(requestId);
            }

            return started;
        }

        /// <summary>
        /// 异步下载完整 HTTP(S) 响应字节，再进入与
        /// <see cref="SaveFile(byte[], string, string)"/> 相同的平台保存流程。
        /// Unity Editor 中须在 Play Mode 调用。
        /// </summary>
        public static bool SaveFileFromUrl(
            string url,
            string fileName = null,
            string mimeType = null,
            int maxFileBytes = 0,
            int timeoutSeconds = DefaultUrlTimeoutSeconds)
        {
            if (maxFileBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            }

            if (timeoutSeconds < MinimumUrlTimeoutSeconds ||
                timeoutSeconds > MaximumUrlTimeoutSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            Uri uri = DeviceFilesUtility.GetHttpUri(url);
            string safeFileName = DeviceFilesUtility.GetSafeFileName(
                string.IsNullOrEmpty(fileName) ? DeviceFilesUtility.GetFileNameFromUrl(uri) : fileName);
            string explicitMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType.Trim();
            EncodeUtf8(safeFileName, nameof(fileName));
            if (explicitMimeType != null)
            {
                EncodeUtf8(explicitMimeType, nameof(mimeType));
            }

            DeviceFilesDispatcher.EnsureMainThread();
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Device Files URL saving is available in Unity Editor Play Mode only.");
                return false;
            }
#endif
            if (!IsAvailable)
            {
                Debug.LogWarning("Device Files is not available on the current platform.");
                return false;
            }

            if (!TryBeginSave(out int requestId))
            {
                return false;
            }

            DeviceFilesDispatcher.PrepareForAsyncWork();
            bool started = DeviceFilesSourceLoader.LoadUrl(
                uri,
                maxFileBytes,
                timeoutSeconds,
                (data, responseContentType) =>
                {
                    string resolvedMimeType = explicitMimeType;
                    if (string.IsNullOrEmpty(resolvedMimeType))
                    {
                        resolvedMimeType = DeviceFilesUtility.GetMimeTypeFromContentType(responseContentType);
                    }

                    CompletePreparedSource(
                        requestId,
                        data,
                        safeFileName,
                        GetSaveMimeType(resolvedMimeType, safeFileName));
                },
                message => NotifySaveFailed(requestId, message));
            if (!started)
            {
                EndSaveWithoutNotification(requestId);
            }

            return started;
        }

        private static bool StartPreparedSave(
            byte[] data,
            string safeFileName,
            string mimeType,
            int requestId)
        {
            if (!IsActiveSave(requestId))
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureWebGLInitialized();
            byte[] nameBytes = EncodeUtf8(safeFileName, nameof(safeFileName));
            byte[] mimeBytes = EncodeUtf8(mimeType, nameof(mimeType));
            bool started = YvsimWebGLFile_DownloadBytes(
                data,
                data.Length,
                nameBytes,
                nameBytes.Length,
                mimeBytes,
                mimeBytes.Length) != 0;
            if (started)
            {
                NotifySaveCompleted(requestId, safeFileName);
            }
            else
            {
                NotifySaveFailed(requestId, "The browser rejected the download request.");
            }

            return started;
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidDeviceFiles.SaveFile(data, safeFileName, mimeType))
            {
                return true;
            }

            NotifySaveFailed(requestId, "Android could not start the system save request.");
            return false;
#elif UNITY_EDITOR_WIN
            EditorApplication.delayCall += () => SaveWindowsFile(data, safeFileName, mimeType, requestId);
            return true;
#elif UNITY_STANDALONE_WIN
            if (DeviceFilesDispatcher.Schedule(
                    () => SaveWindowsFile(data, safeFileName, mimeType, requestId)))
            {
                return true;
            }

            NotifySaveFailed(requestId, "Windows could not schedule the system save request.");
            return false;
#elif UNITY_EDITOR
            EditorApplication.delayCall += () => SaveEditorFile(data, safeFileName, requestId);
            return true;
#else
            EndSaveWithoutNotification(requestId);
            Debug.LogWarning("Device Files is not available on the current platform.");
            return false;
#endif
        }

        /// <summary>
        /// 释放平台为预览创建的临时资源。释放后 <see cref="SelectedFile.Data"/> 仍可使用。
        /// </summary>
        public static bool ReleasePreview(SelectedFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            DeviceFilesDispatcher.EnsureMainThread();
            if (!string.IsNullOrEmpty(file.ManagedTemporaryPath))
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return AndroidDeviceFiles.TryDeleteManagedSelectedFile(file.ManagedTemporaryPath);
#else
                return DeviceFilesUtility.TryDeleteManagedTemporaryFile(file.ManagedTemporaryPath);
#endif
            }

            if (string.IsNullOrEmpty(file.PreviewUrl))
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureWebGLInitialized();
            byte[] previewUrlBytes = EncodeUtf8(file.PreviewUrl, nameof(file));
            return YvsimWebGLFile_ReleaseObjectUrl(previewUrlBytes, previewUrlBytes.Length) != 0;
#else
            return true;
#endif
        }

        internal static void NotifyFileSelected(SelectedFile file)
        {
            LastSelectedFile = file;
            InvokeSafely(FileSelected, file);
        }

        internal static void NotifySelectionCompleted(int count)
        {
            IsSelectionInProgress = false;
            InvokeSafely(SelectionCompleted, count);
        }

        internal static void NotifySelectionCanceled()
        {
            IsSelectionInProgress = false;
            InvokeSafely(SelectionCanceled);
        }

        internal static void NotifySelectionFailed(string message)
        {
            IsSelectionInProgress = false;
            string safeMessage = string.IsNullOrEmpty(message) ? "File selection failed." : message;
            Debug.LogError(safeMessage);
            InvokeSafely(SelectionFailed, safeMessage);
        }

        internal static void NotifySaveCompleted(string destination)
        {
            NotifySaveCompleted(activeSaveRequestId, destination);
        }

        internal static void NotifySaveCanceled()
        {
            NotifySaveCanceled(activeSaveRequestId);
        }

        internal static void NotifySaveFailed(string message)
        {
            NotifySaveFailed(activeSaveRequestId, message);
        }

        private static void NotifySaveCompleted(int requestId, string destination)
        {
            if (!TryEndSave(requestId))
            {
                return;
            }

            InvokeSafely(SaveCompleted, destination ?? string.Empty);
        }

        private static void NotifySaveCanceled(int requestId)
        {
            if (!TryEndSave(requestId))
            {
                return;
            }

            InvokeSafely(SaveCanceled);
        }

        private static void NotifySaveFailed(int requestId, string message)
        {
            if (!TryEndSave(requestId))
            {
                return;
            }

            string safeMessage = string.IsNullOrEmpty(message) ? "File save failed." : message;
            Debug.LogError(safeMessage);
            InvokeSafely(SaveFailed, safeMessage);
        }

        private static void CompletePreparedSource(
            int requestId,
            byte[] data,
            string fileName,
            string mimeType)
        {
            if (!IsActiveSave(requestId))
            {
                return;
            }

            bool started = StartPreparedSave(data, fileName, mimeType, requestId);
            if (!started && IsActiveSave(requestId))
            {
                NotifySaveFailed(requestId, "The platform save backend rejected the prepared file.");
            }
        }

        private static bool TryBeginSave(out int requestId)
        {
            if (IsSelectionInProgress || IsSaveInProgress)
            {
                requestId = 0;
                return false;
            }

            nextSaveRequestId = nextSaveRequestId == int.MaxValue ? 1 : nextSaveRequestId + 1;
            requestId = nextSaveRequestId;
            activeSaveRequestId = requestId;
            IsSaveInProgress = true;
            return true;
        }

        private static bool IsActiveSave(int requestId)
        {
            return requestId != 0 && requestId == activeSaveRequestId && IsSaveInProgress;
        }

        private static bool TryEndSave(int requestId)
        {
            if (!IsActiveSave(requestId))
            {
                return false;
            }

            activeSaveRequestId = 0;
            IsSaveInProgress = false;
            return true;
        }

        private static void EndSaveWithoutNotification(int requestId)
        {
            TryEndSave(requestId);
        }

        private static string GetSaveMimeType(string mimeType, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                return mimeType.Trim();
            }

            string inferredMimeType = DeviceFilesUtility.GetMimeType(fileName);
            return string.IsNullOrEmpty(inferredMimeType)
                ? "application/octet-stream"
                : inferredMimeType;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        [Preserve]
        private static void ResetStaticState()
        {
            FileSelected = null;
            SelectionCompleted = null;
            SelectionCanceled = null;
            SelectionFailed = null;
            SaveCompleted = null;
            SaveCanceled = null;
            SaveFailed = null;
            LastSelectedFile = null;
            IsSelectionInProgress = false;
            IsSaveInProgress = false;
            activeSaveRequestId = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            callbacksRegistered = false;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        [Preserve]
        private static void Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureWebGLInitialized();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static void EnsureWebGLInitialized()
        {
            if (callbacksRegistered)
            {
                return;
            }

            YvsimWebGLFile_RegisterCallbacks(BrowserFileCallback, BrowserSelectionStateCallback);
            callbacksRegistered = true;
            Application.quitting -= ShutdownWebGL;
            Application.quitting += ShutdownWebGL;
        }

        private static void ShutdownWebGL()
        {
            if (!callbacksRegistered)
            {
                return;
            }

            YvsimWebGLFile_UnregisterCallbacks();
            callbacksRegistered = false;
            IsSelectionInProgress = false;
            Application.quitting -= ShutdownWebGL;
        }

        [MonoPInvokeCallback(typeof(FileReceivedCallback))]
        [Preserve]
        private static void OnBrowserFileReceived(
            IntPtr dataBytes,
            int dataLength,
            IntPtr nameBytes,
            int nameLength,
            IntPtr mimeBytes,
            int mimeLength,
            IntPtr previewUrlBytes,
            int previewUrlLength)
        {
            try
            {
                NotifyFileSelected(new SelectedFile(
                    DecodeUtf8(nameBytes, nameLength),
                    DecodeUtf8(mimeBytes, mimeLength),
                    DecodeUtf8(previewUrlBytes, previewUrlLength),
                    CopyBytes(dataBytes, dataLength)));
            }
            catch (Exception exception)
            {
                NotifySelectionFailed(exception.Message);
            }
        }

        [MonoPInvokeCallback(typeof(SelectionStateCallback))]
        [Preserve]
        private static void OnBrowserSelectionState(
            int state,
            int fileCount,
            IntPtr messageBytes,
            int messageLength)
        {
            string message;
            try
            {
                message = DecodeUtf8(messageBytes, messageLength);
            }
            catch (Exception exception)
            {
                message = exception.Message;
                state = SelectionFailedState;
            }

            switch (state)
            {
                case SelectionCompletedState:
                    NotifySelectionCompleted(fileCount);
                    break;
                case SelectionCanceledState:
                    NotifySelectionCanceled();
                    break;
                case SelectionFailedState:
                    NotifySelectionFailed(message);
                    break;
                default:
                    NotifySelectionFailed("Unknown browser file selection state: " + state);
                    break;
            }
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static void OpenWindowsFilePicker(string accept, bool allowMultiple, int maxFileBytes)
        {
            try
            {
                OpenDialogResult result = WindowsFileDialog.Open(accept, allowMultiple);
                if (result.Status == NativeDialogStatus.Canceled)
                {
                    NotifySelectionCanceled();
                    return;
                }

                if (result.Status == NativeDialogStatus.Failed)
                {
                    NotifySelectionFailed(result.Error);
                    return;
                }

                int completedCount = 0;
                for (int i = 0; i < result.Paths.Count; i++)
                {
                    NotifyFileSelected(DeviceFilesUtility.ReadSelectedFile(result.Paths[i], maxFileBytes));
                    completedCount++;
                }

                NotifySelectionCompleted(completedCount);
            }
            catch (Exception exception)
            {
                NotifySelectionFailed(exception.Message);
            }
        }

        private static void SaveWindowsFile(
            byte[] data,
            string fileName,
            string mimeType,
            int requestId)
        {
            if (!IsActiveSave(requestId))
            {
                return;
            }

            try
            {
                SaveDialogResult result = WindowsFileDialog.Save(fileName, mimeType);
                if (result.Status == NativeDialogStatus.Canceled)
                {
                    NotifySaveCanceled(requestId);
                    return;
                }

                if (result.Status == NativeDialogStatus.Failed)
                {
                    NotifySaveFailed(requestId, result.Error);
                    return;
                }

                WriteBytesInBackground(data, result.Path, requestId);
            }
            catch (Exception exception)
            {
                NotifySaveFailed(requestId, exception.Message);
            }
        }
#endif

#if UNITY_EDITOR && !UNITY_EDITOR_WIN
        private static void OpenEditorFilePicker(int maxFileBytes)
        {
            try
            {
                string path = EditorUtility.OpenFilePanel("Select file", string.Empty, string.Empty);
                if (string.IsNullOrEmpty(path))
                {
                    NotifySelectionCanceled();
                    return;
                }

                NotifyFileSelected(DeviceFilesUtility.ReadSelectedFile(path, maxFileBytes));
                NotifySelectionCompleted(1);
            }
            catch (Exception exception)
            {
                NotifySelectionFailed(exception.Message);
            }
        }

        private static void SaveEditorFile(byte[] data, string fileName, int requestId)
        {
            if (!IsActiveSave(requestId))
            {
                return;
            }

            try
            {
                string extension = Path.GetExtension(fileName).TrimStart('.');
                string defaultName = Path.GetFileNameWithoutExtension(fileName);
                string path = EditorUtility.SaveFilePanel("Save file", string.Empty, defaultName, extension);
                if (string.IsNullOrEmpty(path))
                {
                    NotifySaveCanceled(requestId);
                    return;
                }

                WriteBytesInBackground(data, path, requestId);
            }
            catch (Exception exception)
            {
                NotifySaveFailed(requestId, exception.Message);
            }
        }
#endif

        private static void WriteBytesInBackground(byte[] data, string path, int requestId)
        {
            try
            {
                DeviceFilesDispatcher.PrepareForAsyncWork();
                Task.Run(() => File.WriteAllBytes(path, data))
                    .ContinueWith(task =>
                    {
                        bool scheduled = DeviceFilesDispatcher.Schedule(() =>
                        {
                            if (!IsActiveSave(requestId))
                            {
                                return;
                            }

                            if (task.IsFaulted)
                            {
                                string message = task.Exception == null
                                    ? "The destination file could not be written."
                                    : task.Exception.GetBaseException().Message;
                                NotifySaveFailed(requestId, message);
                                return;
                            }

                            if (task.IsCanceled)
                            {
                                NotifySaveFailed(requestId, "The destination file write was canceled.");
                                return;
                            }

                            NotifySaveCompleted(requestId, path);
                        });
                        if (!scheduled)
                        {
                            Debug.LogError(
                                "Device Files could not return the destination-write result to the Unity main thread.");
                        }
                    }, TaskScheduler.Default);
            }
            catch (Exception exception)
            {
                NotifySaveFailed(requestId, exception.Message);
            }
        }

        private static byte[] EncodeUtf8(string value, string parameterName)
        {
            try
            {
                return StrictUtf8.GetBytes(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("The value contains invalid UTF-16.", parameterName, exception);
            }
        }

        private static string DecodeUtf8(IntPtr pointer, int byteLength)
        {
            if (byteLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteLength));
            }

            if (byteLength == 0)
            {
                return string.Empty;
            }

            if (pointer == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            byte[] bytes = new byte[byteLength];
            Marshal.Copy(pointer, bytes, 0, byteLength);
            return StrictUtf8.GetString(bytes);
        }

        private static byte[] CopyBytes(IntPtr pointer, int byteLength)
        {
            if (byteLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteLength));
            }

            byte[] data = new byte[byteLength];
            if (byteLength == 0)
            {
                return data;
            }

            if (pointer == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            Marshal.Copy(pointer, data, 0, byteLength);
            return data;
        }

        internal static void InvokeSafely(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            Delegate[] callbacks = callback.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((Action)callbacks[i])();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        internal static void InvokeSafely<T>(Action<T> callback, T value)
        {
            if (callback == null)
            {
                return;
            }

            Delegate[] callbacks = callback.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((Action<T>)callbacks[i])(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
