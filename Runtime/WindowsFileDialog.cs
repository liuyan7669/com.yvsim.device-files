#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Yvsim.DeviceFiles
{
    internal enum NativeDialogStatus
    {
        Completed,
        Canceled,
        Failed
    }
    internal sealed class OpenDialogResult
    {
        internal OpenDialogResult(NativeDialogStatus status, IReadOnlyList<string> paths, string error)
        {
            Status = status;
            Paths = paths;
            Error = error;
        }

        internal NativeDialogStatus Status { get; }
        internal IReadOnlyList<string> Paths { get; }
        internal string Error { get; }
    }
    internal sealed class SaveDialogResult
    {
        internal SaveDialogResult(NativeDialogStatus status, string path, string error)
        {
            Status = status;
            Path = path;
            Error = error;
        }

        internal NativeDialogStatus Status { get; }
        internal string Path { get; }
        internal string Error { get; }
    }
    internal static class WindowsFileDialog
    {
        private const int OpenBufferCharacters = 1_048_576;
        internal const int SaveBufferCharacters = 32_768;
        private const uint OfnExplorer = 0x00080000;
        private const uint OfnFileMustExist = 0x00001000;
        private const uint OfnPathMustExist = 0x00000800;
        private const uint OfnHideReadOnly = 0x00000004;
        private const uint OfnAllowMultiSelect = 0x00000200;
        private const uint OfnOverwritePrompt = 0x00000002;
        private const uint OfnNoChangeDirectory = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileNameW
        {
            internal uint StructureSize;
            internal IntPtr Owner;
            internal IntPtr Instance;
            internal IntPtr Filter;
            internal IntPtr CustomFilter;
            internal uint MaximumCustomFilter;
            internal uint FilterIndex;
            internal IntPtr FileBuffer;
            internal uint MaximumFile;
            internal IntPtr FileTitle;
            internal uint MaximumFileTitle;
            internal IntPtr InitialDirectory;
            internal IntPtr Title;
            internal uint Flags;
            internal ushort FileOffset;
            internal ushort FileExtension;
            internal IntPtr DefaultExtension;
            internal IntPtr CustomData;
            internal IntPtr Hook;
            internal IntPtr TemplateName;
            internal IntPtr Reserved;
            internal uint ReservedValue;
            internal uint ExtendedFlags;
        }

        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref OpenFileNameW value);

        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileNameW(ref OpenFileNameW value);

        [DllImport("Comdlg32.dll", ExactSpelling = true)]
        private static extern uint CommDlgExtendedError();

        [DllImport("User32.dll", ExactSpelling = true)]
        private static extern IntPtr GetActiveWindow();

        [DllImport("User32.dll", ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("User32.dll", ExactSpelling = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        internal static OpenDialogResult Open(string accept, bool allowMultiple)
        {
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr filterBuffer = IntPtr.Zero;
            try
            {
                fileBuffer = AllocateZeroedUnicodeBuffer(OpenBufferCharacters);
                filterBuffer = Marshal.StringToHGlobalUni(DeviceFilesUtility.BuildWindowsFilter(accept));
                OpenFileNameW value = CreateDialogValue(fileBuffer, OpenBufferCharacters, filterBuffer);
                value.Title = Marshal.StringToHGlobalUni("Select file");
                value.Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist |
                              OfnHideReadOnly | OfnNoChangeDirectory;
                if (allowMultiple)
                {
                    value.Flags |= OfnAllowMultiSelect;
                }

                try
                {
                    if (!GetOpenFileNameW(ref value))
                    {
                        uint error = CommDlgExtendedError();
                        return error == 0
                            ? new OpenDialogResult(NativeDialogStatus.Canceled, Array.Empty<string>(), string.Empty)
                            : new OpenDialogResult(
                                NativeDialogStatus.Failed,
                                Array.Empty<string>(),
                                "Windows file dialog failed with error 0x" + error.ToString("X8") + ".");
                    }

                    IReadOnlyList<string> parts = ReadNullSeparatedStrings(fileBuffer, OpenBufferCharacters);
                    IReadOnlyList<string> paths = ParseSelectedPaths(parts);
                    return paths.Count == 0
                        ? new OpenDialogResult(NativeDialogStatus.Failed, paths, "Windows returned no selected file path.")
                        : new OpenDialogResult(NativeDialogStatus.Completed, paths, string.Empty);
                }
                finally
                {
                    if (value.Title != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(value.Title);
                    }
                }
            }
            catch (Exception exception)
            {
                return new OpenDialogResult(NativeDialogStatus.Failed, Array.Empty<string>(), exception.Message);
            }
            finally
            {
                if (filterBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(filterBuffer);
                }

                if (fileBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(fileBuffer);
                }
            }
        }

        internal static SaveDialogResult Save(string fileName, string mimeType)
        {
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr filterBuffer = IntPtr.Zero;
            try
            {
                string safeFileName = DeviceFilesUtility.GetSafeFileName(fileName);
                if (safeFileName.Length >= SaveBufferCharacters)
                {
                    return new SaveDialogResult(
                        NativeDialogStatus.Failed,
                        string.Empty,
                        "The save file name is too long for the Windows file dialog.");
                }

                fileBuffer = AllocateZeroedUnicodeBuffer(SaveBufferCharacters);
                char[] initialValue = (safeFileName + '\0').ToCharArray();
                Marshal.Copy(initialValue, 0, fileBuffer, initialValue.Length);

                filterBuffer = Marshal.StringToHGlobalUni(
                    DeviceFilesUtility.BuildSaveWindowsFilter(safeFileName, mimeType));
                OpenFileNameW value = CreateDialogValue(fileBuffer, SaveBufferCharacters, filterBuffer);
                value.Title = Marshal.StringToHGlobalUni("Save file");
                value.Flags = OfnExplorer | OfnPathMustExist | OfnOverwritePrompt |
                              OfnHideReadOnly | OfnNoChangeDirectory;

                try
                {
                    if (!GetSaveFileNameW(ref value))
                    {
                        uint error = CommDlgExtendedError();
                        return error == 0
                            ? new SaveDialogResult(NativeDialogStatus.Canceled, string.Empty, string.Empty)
                            : new SaveDialogResult(
                                NativeDialogStatus.Failed,
                                string.Empty,
                                "Windows save dialog failed with error 0x" + error.ToString("X8") + ".");
                    }

                    string path = Marshal.PtrToStringUni(fileBuffer) ?? string.Empty;
                    return string.IsNullOrEmpty(path)
                        ? new SaveDialogResult(NativeDialogStatus.Failed, string.Empty, "Windows returned no save path.")
                        : new SaveDialogResult(NativeDialogStatus.Completed, path, string.Empty);
                }
                finally
                {
                    if (value.Title != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(value.Title);
                    }
                }
            }
            catch (Exception exception)
            {
                return new SaveDialogResult(NativeDialogStatus.Failed, string.Empty, exception.Message);
            }
            finally
            {
                if (filterBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(filterBuffer);
                }

                if (fileBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(fileBuffer);
                }
            }
        }

        internal static IReadOnlyList<string> ParseSelectedPaths(IReadOnlyList<string> parts)
        {
            if (parts == null || parts.Count == 0 || string.IsNullOrEmpty(parts[0]))
            {
                return Array.Empty<string>();
            }

            if (parts.Count == 1)
            {
                return new[] { parts[0] };
            }

            List<string> paths = new List<string>(parts.Count - 1);
            for (int i = 1; i < parts.Count; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    paths.Add(Path.Combine(parts[0], parts[i]));
                }
            }

            return paths;
        }

        private static OpenFileNameW CreateDialogValue(
            IntPtr fileBuffer,
            int maximumFileCharacters,
            IntPtr filterBuffer)
        {
            return new OpenFileNameW
            {
                StructureSize = (uint)Marshal.SizeOf(typeof(OpenFileNameW)),
                Owner = GetSafeOwnerWindow(),
                Filter = filterBuffer,
                FilterIndex = 1,
                FileBuffer = fileBuffer,
                MaximumFile = (uint)maximumFileCharacters
            };
        }

        private static IntPtr AllocateZeroedUnicodeBuffer(int characterCount)
        {
            int byteCount = checked(characterCount * sizeof(char));
            IntPtr buffer = Marshal.AllocHGlobal(byteCount);
            Marshal.Copy(new byte[byteCount], 0, buffer, byteCount);
            return buffer;
        }

        internal static IReadOnlyList<string> ReadNullSeparatedStrings(IntPtr buffer, int maximumCharacters)
        {
            List<string> values = new List<string>();
            List<char> current = new List<char>();

            for (int index = 0; index < maximumCharacters; index++)
            {
                char character = (char)Marshal.ReadInt16(buffer, index * sizeof(char));
                if (character != '\0')
                {
                    current.Add(character);
                    continue;
                }

                if (current.Count == 0)
                {
                    break;
                }

                values.Add(new string(current.ToArray()));
                current.Clear();
            }

            return values;
        }

        private static IntPtr GetSafeOwnerWindow()
        {
            IntPtr activeWindow = GetActiveWindow();
            if (activeWindow != IntPtr.Zero)
            {
                return activeWindow;
            }

            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            GetWindowThreadProcessId(foregroundWindow, out uint processId);
            return processId == (uint)Process.GetCurrentProcess().Id
                ? foregroundWindow
                : IntPtr.Zero;
        }
    }
}
#endif
