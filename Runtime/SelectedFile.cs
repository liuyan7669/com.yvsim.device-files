using System;

namespace Yvsim.DeviceFiles
{
    /// <summary>
    /// 用户从本机选择并复制到 Unity 托管内存中的文件。
    /// </summary>
    public sealed class SelectedFile
    {
        internal SelectedFile(
            string name,
            string mimeType,
            string previewUrl,
            byte[] data,
            string managedTemporaryPath = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MimeType = mimeType ?? string.Empty;
            PreviewUrl = previewUrl ?? string.Empty;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ManagedTemporaryPath = managedTemporaryPath ?? string.Empty;
        }

        /// <summary>
        /// 文件名，不包含可信的本地绝对路径。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 文件 MIME 类型。平台无法识别时可能为空字符串。
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// 用于图片、音频或视频预览的 URL。
        /// WebGL 中为临时 <c>blob:</c> URL；其他平台通常为 <c>file:</c> URI。
        /// 使用完毕后应调用 <see cref="DeviceFiles.ReleasePreview(SelectedFile)"/>。
        /// </summary>
        public string PreviewUrl { get; }

        /// <summary>
        /// 文件的完整二进制内容。空文件对应长度为 0 的数组。
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// 文件字节数。
        /// </summary>
        public int Size => Data.Length;

        internal string ManagedTemporaryPath { get; }
    }
}
