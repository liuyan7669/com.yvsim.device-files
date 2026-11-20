using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Yvsim.DeviceFiles
{
    internal static class DeviceFilesSourceLoader
    {
        private const int WebGLReadChunkBytes = 256 * 1024;

        internal static bool LoadPath(
            string path,
            int maxFileBytes,
            Action<byte[]> completed,
            Action<string> failed)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return DeviceFilesDispatcher.StartRoutine(
                ReadWebGLPath(path, maxFileBytes, completed, failed));
#else
            try
            {
                Task.Run(() => DeviceFilesUtility.ReadFileBytes(path, maxFileBytes))
                    .ContinueWith(task =>
                    {
                        bool scheduled = DeviceFilesDispatcher.Schedule(() =>
                        {
                            if (task.IsFaulted)
                            {
                                failed(GetTaskError(task, "The source file could not be read."));
                                return;
                            }

                            if (task.IsCanceled)
                            {
                                failed("The source file read was canceled.");
                                return;
                            }

                            completed(task.Result);
                        });
                        if (!scheduled)
                        {
                            Debug.LogError(
                                "Device Files could not return the source-file result to the Unity main thread.");
                        }
                    }, TaskScheduler.Default);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
#endif
        }

        internal static bool LoadUrl(
            Uri uri,
            int maxFileBytes,
            int timeoutSeconds,
            Action<byte[], string> completed,
            Action<string> failed)
        {
            return DeviceFilesDispatcher.StartRoutine(
                DownloadUrl(uri, maxFileBytes, timeoutSeconds, completed, failed));
        }

        private static IEnumerator DownloadUrl(
            Uri uri,
            int maxFileBytes,
            int timeoutSeconds,
            Action<byte[], string> completed,
            Action<string> failed)
        {
            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(uri.AbsoluteUri);
                request.timeout = timeoutSeconds;
            }
            catch (Exception exception)
            {
                failed(exception.Message);
                yield break;
            }

            using (request)
            {
                UnityWebRequestAsyncOperation operation;
                try
                {
                    operation = request.SendWebRequest();
                }
                catch (Exception exception)
                {
                    failed(exception.Message);
                    yield break;
                }

                while (!operation.isDone)
                {
                    if (maxFileBytes > 0 && request.downloadedBytes > (ulong)maxFileBytes)
                    {
                        request.Abort();
                        failed("The URL response exceeded the configured maximum of " +
                               maxFileBytes + " bytes.");
                        yield break;
                    }

                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed(string.IsNullOrEmpty(request.error)
                        ? "The URL request failed."
                        : request.error);
                    yield break;
                }

                byte[] data = request.downloadHandler == null
                    ? null
                    : request.downloadHandler.data;
                if (data == null)
                {
                    failed("The URL request returned no file bytes.");
                    yield break;
                }

                if (maxFileBytes > 0 && data.Length > maxFileBytes)
                {
                    failed("The URL response exceeded the configured maximum of " +
                           maxFileBytes + " bytes.");
                    yield break;
                }

                completed(data, request.GetResponseHeader("Content-Type"));
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static IEnumerator ReadWebGLPath(
            string path,
            int maxFileBytes,
            Action<byte[]> completed,
            Action<string> failed)
        {
            FileStream stream = null;
            byte[] data = null;
            Exception error = null;
            try
            {
                FileInfo fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException("The source file no longer exists.", path);
                }

                if (fileInfo.Length > int.MaxValue)
                {
                    throw new InvalidOperationException(
                        "The source file exceeds the supported 32-bit byte array length.");
                }

                if (maxFileBytes > 0 && fileInfo.Length > maxFileBytes)
                {
                    throw new InvalidOperationException(
                        "The source file is too large. Maximum: " + maxFileBytes +
                        " bytes; actual: " + fileInfo.Length + " bytes.");
                }

                data = new byte[(int)fileInfo.Length];
                stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            int offset = 0;
            while (error == null && offset < data.Length)
            {
                try
                {
                    int read = stream.Read(
                        data,
                        offset,
                        Math.Min(WebGLReadChunkBytes, data.Length - offset));
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            "The source file ended before all declared bytes were read.");
                    }

                    offset += read;
                }
                catch (Exception exception)
                {
                    error = exception;
                }

                if (error == null && offset < data.Length)
                {
                    yield return null;
                }
            }

            if (error == null)
            {
                try
                {
                    if (stream.ReadByte() >= 0)
                    {
                        throw new InvalidOperationException(
                            "The source file grew while it was being read.");
                    }
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            }

            stream?.Dispose();
            if (error != null)
            {
                failed(error.Message);
                yield break;
            }

            completed(data);
        }
#endif

        private static string GetTaskError(Task task, string fallback)
        {
            return task.Exception == null
                ? fallback
                : task.Exception.GetBaseException().Message;
        }
    }
}
