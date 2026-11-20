using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yvsim.DeviceFiles
{
    internal sealed class FileTypeFilter
    {
        internal FileTypeFilter(List<string> extensions, List<string> mimeTypes)
        {
            Extensions = extensions;
            MimeTypes = mimeTypes;
        }

        internal IReadOnlyList<string> Extensions { get; }
        internal IReadOnlyList<string> MimeTypes { get; }
    }

    internal static class DeviceFilesUtility
    {
        private static readonly Dictionary<string, string> ExtensionToMime =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".png", "image/png" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".gif", "image/gif" },
                { ".bmp", "image/bmp" },
                { ".webp", "image/webp" },
                { ".txt", "text/plain" },
                { ".log", "text/plain" },
                { ".md", "text/markdown" },
                { ".json", "application/json" },
                { ".xml", "application/xml" },
                { ".csv", "text/csv" },
                { ".pdf", "application/pdf" },
                { ".zip", "application/zip" },
                { ".mp3", "audio/mpeg" },
                { ".wav", "audio/wav" },
                { ".ogg", "audio/ogg" },
                { ".aif", "audio/aiff" },
                { ".aiff", "audio/aiff" },
                { ".m4a", "audio/mp4" },
                { ".mp4", "video/mp4" },
                { ".webm", "video/webm" },
                { ".mov", "video/quicktime" },
                { ".m4v", "video/x-m4v" },
                { ".ogv", "video/ogg" }
            };

        private static readonly Dictionary<string, string[]> MimeToExtensions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "image/png", new[] { ".png" } },
                { "image/jpeg", new[] { ".jpg", ".jpeg" } },
                { "image/gif", new[] { ".gif" } },
                { "image/bmp", new[] { ".bmp" } },
                { "image/webp", new[] { ".webp" } },
                { "image/*", new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" } },
                { "text/plain", new[] { ".txt", ".log" } },
                { "text/markdown", new[] { ".md" } },
                { "text/csv", new[] { ".csv" } },
                { "text/*", new[] { ".txt", ".log", ".md", ".csv", ".json", ".xml" } },
                { "application/json", new[] { ".json" } },
                { "application/xml", new[] { ".xml" } },
                { "application/pdf", new[] { ".pdf" } },
                { "application/zip", new[] { ".zip" } },
                { "audio/mpeg", new[] { ".mp3" } },
                { "audio/wav", new[] { ".wav" } },
                { "audio/x-wav", new[] { ".wav" } },
                { "audio/ogg", new[] { ".ogg" } },
                { "audio/aiff", new[] { ".aif", ".aiff" } },
                { "audio/mp4", new[] { ".m4a" } },
                { "audio/*", new[] { ".mp3", ".wav", ".ogg", ".aif", ".aiff", ".m4a" } },
                { "video/mp4", new[] { ".mp4" } },
                { "video/webm", new[] { ".webm" } },
                { "video/quicktime", new[] { ".mov" } },
                { "video/x-m4v", new[] { ".m4v" } },
                { "video/ogg", new[] { ".ogv" } },
                { "video/*", new[] { ".mp4", ".webm", ".mov", ".m4v", ".ogv" } }
            };

        internal static FileTypeFilter ParseAccept(string accept)
        {
            List<string> extensions = new List<string>();
            List<string> mimeTypes = new List<string>();
            HashSet<string> extensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> mimeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(accept))
            {
                return new FileTypeFilter(extensions, mimeTypes);
            }

            string[] tokens = accept.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim().ToLowerInvariant();
                if (token.Length == 0)
                {
                    continue;
                }

                if (token[0] == '.')
                {
                    if (!IsValidExtension(token))
                    {
                        continue;
                    }

                    AddUnique(extensions, extensionSet, token);
                    if (ExtensionToMime.TryGetValue(token, out string mappedMime))
                    {
                        AddUnique(mimeTypes, mimeSet, mappedMime);
                    }

                    continue;
                }

                if (!IsValidMimeType(token))
                {
                    continue;
                }

                AddUnique(mimeTypes, mimeSet, token);
                if (!MimeToExtensions.TryGetValue(token, out string[] mappedExtensions))
                {
                    continue;
                }

                for (int extensionIndex = 0; extensionIndex < mappedExtensions.Length; extensionIndex++)
                {
                    AddUnique(extensions, extensionSet, mappedExtensions[extensionIndex]);
                }
            }

            return new FileTypeFilter(extensions, mimeTypes);
        }

        internal static string BuildWindowsFilter(string accept)
        {
            IReadOnlyList<string> extensions = ParseAccept(accept).Extensions;
            if (extensions.Count == 0)
            {
                return "All files\0*.*\0\0";
            }

            StringBuilder patterns = new StringBuilder();
            for (int i = 0; i < extensions.Count; i++)
            {
                if (i > 0)
                {
                    patterns.Append(';');
                }

                patterns.Append('*').Append(extensions[i]);
            }

            return "Accepted files\0" + patterns + "\0All files\0*.*\0\0";
        }

        internal static string BuildSaveWindowsFilter(string fileName, string mimeType)
        {
            string extension = Path.GetExtension(fileName);
            string accept = IsValidExtension(extension)
                ? extension
                : (mimeType ?? string.Empty);
            return BuildWindowsFilter(accept);
        }

        internal static string BuildAndroidMimeTypesJson(string accept)
        {
            IReadOnlyList<string> mimeTypes = ParseAccept(accept).MimeTypes;
            StringBuilder result = new StringBuilder("[");
            for (int i = 0; i < mimeTypes.Count; i++)
            {
                if (i > 0)
                {
                    result.Append(',');
                }

                result.Append('"').Append(EscapeJson(mimeTypes[i])).Append('"');
            }

            return result.Append(']').ToString();
        }

        internal static SelectedFile ReadSelectedFile(
            string path,
            int maxFileBytes,
            string name = null,
            string mimeType = null,
            bool isManagedTemporaryFile = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The selected file path must not be empty.", nameof(path));
            }

            FileInfo fileInfo = new FileInfo(path);
            byte[] data = ReadFileBytes(path, maxFileBytes);

            return new SelectedFile(
                string.IsNullOrEmpty(name) ? fileInfo.Name : name,
                string.IsNullOrEmpty(mimeType) ? GetMimeType(path) : mimeType,
                new Uri(fileInfo.FullName).AbsoluteUri,
                data,
                isManagedTemporaryFile ? fileInfo.FullName : null);
        }

        internal static byte[] ReadFileBytes(string path, int maxFileBytes)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The source file path must not be empty.", nameof(path));
            }

            if (maxFileBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            }

            FileInfo fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("The source file no longer exists.", path);
            }

            if (fileInfo.Length > int.MaxValue)
            {
                throw new InvalidOperationException("The source file exceeds the supported 32-bit byte array length.");
            }

            if (maxFileBytes > 0 && fileInfo.Length > maxFileBytes)
            {
                throw new InvalidOperationException(
                    "The source file is too large. Maximum: " + maxFileBytes +
                    " bytes; actual: " + fileInfo.Length + " bytes.");
            }

            byte[] data = File.ReadAllBytes(fileInfo.FullName);
            if (maxFileBytes > 0 && data.Length > maxFileBytes)
            {
                throw new InvalidOperationException(
                    "The source file grew beyond the configured size limit while it was being read.");
            }

            return data;
        }

        internal static string GetMimeType(string path)
        {
            string extension = Path.GetExtension(path);
            return ExtensionToMime.TryGetValue(extension, out string mimeType)
                ? mimeType
                : string.Empty;
        }

        internal static string GetMimeTypeFromContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return string.Empty;
            }

            int parameterIndex = contentType.IndexOf(';');
            string mimeType = (parameterIndex >= 0
                    ? contentType.Substring(0, parameterIndex)
                    : contentType)
                .Trim()
                .ToLowerInvariant();
            return IsValidMimeType(mimeType) ? mimeType : string.Empty;
        }

        internal static string GetFileNameFromUrl(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            string escapedName = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrEmpty(escapedName))
            {
                return "download";
            }

            try
            {
                return GetSafeFileName(Uri.UnescapeDataString(escapedName));
            }
            catch (UriFormatException)
            {
                return GetSafeFileName(escapedName);
            }
        }

        internal static Uri GetHttpUri(string url)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("The source URL must be an absolute HTTP or HTTPS URL.", nameof(url));
            }

            return uri;
        }

        internal static string GetSafeFileName(string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeName) || safeName == "." || safeName == "..")
            {
                throw new ArgumentException("The file name must contain a valid file name.", nameof(fileName));
            }

            return safeName;
        }

        internal static bool TryDeleteManagedTemporaryFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            try
            {
                string selectedRoot = Path.GetFullPath(
                    Path.Combine(UnityEngine.Application.temporaryCachePath, "YvsimDeviceFiles", "Selected"));
                string fullPath = Path.GetFullPath(path);
                if (!IsPathUnderRoot(fullPath, selectedRoot))
                {
                    return false;
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                    Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension) || extension.Length < 2 || extension[0] != '.' || extension.Length > 32)
            {
                return false;
            }

            for (int i = 1; i < extension.Length; i++)
            {
                char character = extension[i];
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '+')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidMimeType(string mimeType)
        {
            int slashIndex = mimeType.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == mimeType.Length - 1 || mimeType.IndexOf('/', slashIndex + 1) >= 0)
            {
                return false;
            }

            string type = mimeType.Substring(0, slashIndex);
            string subtype = mimeType.Substring(slashIndex + 1);
            if (type.IndexOf('*') >= 0 && type != "*")
            {
                return false;
            }

            if (type == "*" && subtype != "*")
            {
                return false;
            }

            if (subtype.IndexOf('*') >= 0 && subtype != "*")
            {
                return false;
            }

            for (int i = 0; i < mimeType.Length; i++)
            {
                char character = mimeType[i];
                if (char.IsLetterOrDigit(character) || character == '/' || character == '-' ||
                    character == '+' || character == '.' || character == '*')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void AddUnique(List<string> values, HashSet<string> set, string value)
        {
            if (set.Add(value))
            {
                values.Add(value);
            }
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
