using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Yvsim.DeviceFiles.Tests
{
    public sealed class DeviceFilesTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Application.temporaryCachePath,
                "YvsimDeviceFilesTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }

        [Test]
        public void ParseAccept_MapsExtensionsMimeAndWildcardsInInputOrder()
        {
            FileTypeFilter filter = DeviceFilesUtility.ParseAccept(
                ".json,image/png,audio/*,.JSON,application/unknown");

            CollectionAssert.AreEqual(
                new[] { ".json", ".png", ".mp3", ".wav", ".ogg", ".aif", ".aiff", ".m4a" },
                filter.Extensions);
            CollectionAssert.AreEqual(
                new[] { "application/json", "image/png", "audio/*", "application/unknown" },
                filter.MimeTypes);
        }

        [Test]
        public void ParseAccept_IgnoresMalformedOrInjectableTokens()
        {
            FileTypeFilter filter = DeviceFilesUtility.ParseAccept(
                ".png;*.exe,.bad/path,text/plain\0application/json,not-a-mime,im*age/png,image/pn*g,*/png");

            CollectionAssert.AreEqual(Array.Empty<string>(), filter.Extensions);
            CollectionAssert.AreEqual(Array.Empty<string>(), filter.MimeTypes);
        }

        [Test]
        public void BuildWindowsFilter_IsDoubleNullTerminatedAndDeduplicated()
        {
            string filter = DeviceFilesUtility.BuildWindowsFilter("image/jpeg,.jpg,.png");

            Assert.That(filter, Is.EqualTo(
                "Accepted files\0*.jpg;*.jpeg;*.png\0All files\0*.*\0\0"));
            Assert.That(filter.EndsWith("\0\0", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void BuildWindowsFilter_UsesAllFilesForUnknownMime()
        {
            Assert.That(
                DeviceFilesUtility.BuildWindowsFilter("application/unknown"),
                Is.EqualTo("All files\0*.*\0\0"));
        }

        [Test]
        public void BuildAndroidMimeTypesJson_MapsKnownExtensionAndPreservesMime()
        {
            Assert.That(
                DeviceFilesUtility.BuildAndroidMimeTypesJson(".png,text/plain,.unknown"),
                Is.EqualTo("[\"image/png\",\"text/plain\"]"));
        }

        [TestCase("picture.PNG", "image/png")]
        [TestCase("recording.wav", "audio/wav")]
        [TestCase("movie.webm", "video/webm")]
        [TestCase("unknown.data", "")]
        public void GetMimeType_UsesCaseInsensitiveExtension(string fileName, string expected)
        {
            Assert.That(DeviceFilesUtility.GetMimeType(fileName), Is.EqualTo(expected));
        }

        [Test]
        public void ReadSelectedFile_PreservesUnicodeNameEmptyBytesAndFileUri()
        {
            string path = Path.Combine(temporaryDirectory, "中文 空文件.txt");
            File.WriteAllBytes(path, Array.Empty<byte>());

            SelectedFile file = DeviceFilesUtility.ReadSelectedFile(path, 0);

            Assert.That(file.Name, Is.EqualTo("中文 空文件.txt"));
            Assert.That(file.MimeType, Is.EqualTo("text/plain"));
            Assert.That(file.Size, Is.Zero);
            Assert.That(file.PreviewUrl, Does.StartWith("file:"));
        }

        [Test]
        public void ReadSelectedFile_AcceptsExactLimitAndRejectsAboveLimit()
        {
            string path = Path.Combine(temporaryDirectory, "limit.bin");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            Assert.That(DeviceFilesUtility.ReadSelectedFile(path, 4).Size, Is.EqualTo(4));
            Assert.Throws<InvalidOperationException>(() => DeviceFilesUtility.ReadSelectedFile(path, 3));
        }

        [Test]
        public void ReadFileBytes_ReturnsExactBytesAndEnforcesLimit()
        {
            string path = Path.Combine(temporaryDirectory, "source.bin");
            byte[] expected = { 1, 3, 5, 7 };
            File.WriteAllBytes(path, expected);

            CollectionAssert.AreEqual(expected, DeviceFilesUtility.ReadFileBytes(path, expected.Length));
            Assert.Throws<InvalidOperationException>(
                () => DeviceFilesUtility.ReadFileBytes(path, expected.Length - 1));
        }

        [TestCase("https://example.com/audio/voice%20sample.wav?token=1", "voice sample.wav")]
        [TestCase("https://example.com/", "download")]
        public void UrlMetadata_UsesSafeDecodedPathFileName(string url, string expected)
        {
            Uri uri = DeviceFilesUtility.GetHttpUri(url);

            Assert.That(DeviceFilesUtility.GetFileNameFromUrl(uri), Is.EqualTo(expected));
        }

        [TestCase("audio/wav; charset=binary", "audio/wav")]
        [TestCase("Application/JSON", "application/json")]
        [TestCase("invalid", "")]
        public void ContentTypeMetadata_NormalizesValidMimeType(string value, string expected)
        {
            Assert.That(DeviceFilesUtility.GetMimeTypeFromContentType(value), Is.EqualTo(expected));
        }

        [Test]
        public void ReleasePreview_DeletesOnlyManagedSelectedCacheFiles()
        {
            string managedRoot = Path.Combine(
                Application.temporaryCachePath,
                "YvsimDeviceFiles",
                "Selected");
            Directory.CreateDirectory(managedRoot);
            string managedPath = Path.Combine(managedRoot, Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(managedPath, new byte[] { 7 });
            SelectedFile managedFile = new SelectedFile(
                "managed.tmp",
                string.Empty,
                new Uri(managedPath).AbsoluteUri,
                new byte[] { 7 },
                managedPath);

            Assert.That(DeviceFiles.ReleasePreview(managedFile), Is.True);
            Assert.That(File.Exists(managedPath), Is.False);

            string outsidePath = Path.Combine(temporaryDirectory, "outside.tmp");
            File.WriteAllBytes(outsidePath, new byte[] { 9 });
            SelectedFile outsideFile = new SelectedFile(
                "outside.tmp",
                string.Empty,
                new Uri(outsidePath).AbsoluteUri,
                new byte[] { 9 },
                outsidePath);

            Assert.That(DeviceFiles.ReleasePreview(outsideFile), Is.False);
            Assert.That(File.Exists(outsidePath), Is.True);
        }

        [Test]
        public void PublicEntryPoints_RejectInvalidArgumentsBeforeOpeningNativeUi()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DeviceFiles.OpenFilePicker(maxFileBytes: -1));
            Assert.Throws<ArgumentNullException>(() => DeviceFiles.SaveFile(null, "file.bin"));
            Assert.Throws<ArgumentNullException>(() => DeviceFiles.SaveFile(Array.Empty<byte>(), null));
            Assert.Throws<ArgumentException>(() => DeviceFiles.SaveFile(Array.Empty<byte>(), string.Empty));
            Assert.Throws<ArgumentNullException>(() => DeviceFiles.SaveFileFromPath(null));
            Assert.Throws<ArgumentException>(() => DeviceFiles.SaveFileFromPath(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceFiles.SaveFileFromPath("source.bin", maxFileBytes: -1));
            Assert.Throws<ArgumentNullException>(() => DeviceFiles.SaveFileFromUrl(null));
            Assert.Throws<ArgumentException>(() => DeviceFiles.SaveFileFromUrl("not-a-url"));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceFiles.SaveFileFromUrl("https://example.com/file.bin", maxFileBytes: -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceFiles.SaveFileFromUrl("https://example.com/file.bin", timeoutSeconds: 0));
        }

        [Test]
        public void SaveFileFromUrl_EditModeRejectsWithoutStartingARequest()
        {
            LogAssert.Expect(
                LogType.Warning,
                "Device Files URL saving is available in Unity Editor Play Mode only.");

            bool accepted = DeviceFiles.SaveFileFromUrl("https://example.com/file.bin");

            Assert.That(accepted, Is.False);
            Assert.That(DeviceFiles.IsSaveInProgress, Is.False);
        }

        [Test]
        public void ByteArraySaveEntry_IsPreservedForOptionalReflectionConsumers()
        {
            System.Reflection.MethodInfo method = typeof(DeviceFiles).GetMethod(
                nameof(DeviceFiles.SaveFile),
                new[] { typeof(byte[]), typeof(string), typeof(string) });

            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.IsDefined(typeof(UnityEngine.Scripting.PreserveAttribute), false),
                Is.True);
        }

        [Test]
        public void PublicEntryPoints_RejectBackgroundThreadCalls()
        {
            Exception capturedException = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    DeviceFiles.SaveFile(Array.Empty<byte>(), "background.bin");
                }
                catch (Exception exception)
                {
                    capturedException = exception;
                }
            });

            thread.Start();
            Assert.That(thread.Join(5000), Is.True, "The background validation thread did not finish.");
            Assert.That(capturedException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void InvokeSafely_ContinuesAfterSubscriberThrows()
        {
            int callCount = 0;
            Action callback = () => throw new InvalidOperationException("expected-test-error");
            callback += () => callCount++;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: expected-test-error");

            DeviceFiles.InvokeSafely(callback);

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void AndroidNativeEvent_ParsesUnicodeAndLongSize()
        {
            AndroidNativeEvent nativeEvent = AndroidNativeEvent.FromJson(
                "{\"kind\":\"fileSelected\",\"requestId\":\"request-1\",\"name\":\"语音.wav\",\"size\":2147483647}");

            Assert.That(nativeEvent.kind, Is.EqualTo("fileSelected"));
            Assert.That(nativeEvent.requestId, Is.EqualTo("request-1"));
            Assert.That(nativeEvent.name, Is.EqualTo("语音.wav"));
            Assert.That(nativeEvent.size, Is.EqualTo(2147483647L));
        }

#if UNITY_EDITOR_WIN
        [Test]
        public void WindowsPathParser_HandlesSingleAndMultiSelect()
        {
            CollectionAssert.AreEqual(
                new[] { @"C:\文件\one.txt" },
                WindowsFileDialog.ParseSelectedPaths(new[] { @"C:\文件\one.txt" }));
            CollectionAssert.AreEqual(
                new[] { @"C:\文件\one.txt", @"C:\文件\two.txt" },
                WindowsFileDialog.ParseSelectedPaths(new[] { @"C:\文件", "one.txt", "two.txt" }));
        }

        [Test]
        public void WindowsNativeBufferParser_StopsAtDoubleNull()
        {
            char[] characters = (@"C:\文件" + '\0' + "one.txt" + '\0' + "two.txt" + "\0\0ignored").ToCharArray();
            IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(characters.Length * sizeof(char));
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(characters, 0, buffer, characters.Length);

                CollectionAssert.AreEqual(
                    new[] { @"C:\文件", "one.txt", "two.txt" },
                    WindowsFileDialog.ReadNullSeparatedStrings(buffer, characters.Length));
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }

        [Test]
        public void WindowsSave_RejectsFileNameThatCannotFitNativeBuffer()
        {
            SaveDialogResult result = WindowsFileDialog.Save(
                new string('a', WindowsFileDialog.SaveBufferCharacters),
                "application/octet-stream");

            Assert.That(result.Status, Is.EqualTo(NativeDialogStatus.Failed));
            Assert.That(result.Error, Does.Contain("too long"));
        }
#endif
    }
}
