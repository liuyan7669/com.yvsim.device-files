using System.Collections.Generic;
using System.Text;
using Yvsim.DeviceFiles;
using UnityEngine;
using UnityEngine.UI;

namespace Yvsim.DeviceFiles.Samples
{
    /// <summary>
    /// Device Files 的 UGUI 入门示例。
    /// 打开 Basic Usage 场景进入 Play Mode，通过界面按钮观察完整调用顺序。
    /// </summary>
    public sealed class DeviceFilesBasicUsage : MonoBehaviour
    {
        private const int MaxFileBytes = 20 * 1024 * 1024;
        private const string ImageFilter = "image/*,.png,.jpg,.jpeg";
        private const string DefaultSourceUrl = "https://example.com/file.bin";

        [Header("选择文件")]
        [Tooltip("打开单文件选择窗口的按钮，不限制文件类型。")]
        [SerializeField] private Button selectAnyButton;
        [Tooltip("打开多文件选择窗口的按钮，不限制文件类型。")]
        [SerializeField] private Button selectMultipleButton;
        [Tooltip("打开图片选择窗口的按钮，向系统提示常见图片类型。")]
        [SerializeField] private Button selectImageButton;

        [Header("保存文件")]
        [Tooltip("将示例生成的字节数据保存到用户所选位置的按钮。")]
        [SerializeField] private Button saveGeneratedButton;
        [Tooltip("保存最近一次保留的所选文件数据的按钮。")]
        [SerializeField] private Button saveLastSelectedButton;
        [Tooltip("输入待保存源文件路径的输入框，配合“保存本地源文件”按钮使用。")]
        [SerializeField] private InputField sourcePathInput;
        [Tooltip("读取源路径输入框指定的文件并发起保存的按钮。")]
        [SerializeField] private Button saveSourcePathButton;
        [Tooltip("输入待下载文件的网络地址，配合“保存网络源文件”按钮使用。")]
        [SerializeField] private InputField sourceUrlInput;
        [Tooltip("从网络地址输入框指定的 URL 下载文件并发起保存的按钮。")]
        [SerializeField] private Button saveSourceUrlButton;
        [Tooltip("释放所选文件的预览资源并清空示例保留列表的按钮。")]
        [SerializeField] private Button releasePreviewsButton;

        [Header("状态显示")]
        [Tooltip("显示最近一次选择、保存或错误提示的文本组件。")]
        [SerializeField] private Text statusText;
        [Tooltip("显示当前平台以及文件选择、保存状态的文本组件。")]
        [SerializeField] private Text runtimeStateText;
        [Tooltip("显示已保留文件的名称、类型和数据大小等信息的文本组件。")]
        [SerializeField] private Text selectedFileText;

        private readonly List<SelectedFile> selectedFiles = new List<SelectedFile>();

        private string status = "点击左侧按钮开始。选择文件的唯一入口是 DeviceFiles.OpenFilePicker(...)。";
        private bool uiReady;

        private SelectedFile LastRetainedFile =>
            selectedFiles.Count == 0 ? null : selectedFiles[selectedFiles.Count - 1];

        private void Awake()
        {
            uiReady = ValidateBindings();
            if (!uiReady)
            {
                enabled = false;
                return;
            }

            selectAnyButton.onClick.AddListener(SelectAnyFile);
            selectMultipleButton.onClick.AddListener(SelectMultipleFiles);
            selectImageButton.onClick.AddListener(SelectImage);
            saveGeneratedButton.onClick.AddListener(SaveGeneratedBytes);
            saveLastSelectedButton.onClick.AddListener(SaveLastSelectedFile);
            saveSourcePathButton.onClick.AddListener(SaveSourcePath);
            saveSourceUrlButton.onClick.AddListener(SaveSourceUrl);
            releasePreviewsButton.onClick.AddListener(ReleaseAllFilesFromUi);
            sourcePathInput.onValueChanged.AddListener(HandleSourceInputChanged);
            sourceUrlInput.onValueChanged.AddListener(HandleSourceInputChanged);

            if (string.IsNullOrEmpty(sourceUrlInput.text))
            {
                sourceUrlInput.text = DefaultSourceUrl;
            }

            RefreshView();
        }

        private void OnEnable()
        {
            DeviceFiles.FileSelected += HandleFileSelected;
            DeviceFiles.SelectionCompleted += HandleSelectionCompleted;
            DeviceFiles.SelectionCanceled += HandleSelectionCanceled;
            DeviceFiles.SelectionFailed += HandleSelectionFailed;

            DeviceFiles.SaveCompleted += HandleSaveCompleted;
            DeviceFiles.SaveCanceled += HandleSaveCanceled;
            DeviceFiles.SaveFailed += HandleSaveFailed;

            RefreshView();
        }

        private void OnDisable()
        {
            DeviceFiles.FileSelected -= HandleFileSelected;
            DeviceFiles.SelectionCompleted -= HandleSelectionCompleted;
            DeviceFiles.SelectionCanceled -= HandleSelectionCanceled;
            DeviceFiles.SelectionFailed -= HandleSelectionFailed;

            DeviceFiles.SaveCompleted -= HandleSaveCompleted;
            DeviceFiles.SaveCanceled -= HandleSaveCanceled;
            DeviceFiles.SaveFailed -= HandleSaveFailed;
        }

        private void OnDestroy()
        {
            if (uiReady)
            {
                selectAnyButton.onClick.RemoveListener(SelectAnyFile);
                selectMultipleButton.onClick.RemoveListener(SelectMultipleFiles);
                selectImageButton.onClick.RemoveListener(SelectImage);
                saveGeneratedButton.onClick.RemoveListener(SaveGeneratedBytes);
                saveLastSelectedButton.onClick.RemoveListener(SaveLastSelectedFile);
                saveSourcePathButton.onClick.RemoveListener(SaveSourcePath);
                saveSourceUrlButton.onClick.RemoveListener(SaveSourceUrl);
                releasePreviewsButton.onClick.RemoveListener(ReleaseAllFilesFromUi);
                sourcePathInput.onValueChanged.RemoveListener(HandleSourceInputChanged);
                sourceUrlInput.onValueChanged.RemoveListener(HandleSourceInputChanged);
            }

            ReleaseAllFiles();
        }

        /// <summary>不限制类型的单文件选择。</summary>
        public void SelectAnyFile()
        {
            OpenPicker(string.Empty, false);
        }

        /// <summary>不限制类型的多文件选择。</summary>
        public void SelectMultipleFiles()
        {
            OpenPicker(string.Empty, true);
        }

        /// <summary>只向系统选择窗口提示图片类型。</summary>
        public void SelectImage()
        {
            OpenPicker(ImageFilter, false);
        }

        private void OpenPicker(string accept, bool allowMultiple)
        {
            const string startingStatus = "正在启动选择请求...";
            status = startingStatus;
            RefreshView();

            bool requestAccepted = DeviceFiles.OpenFilePicker(
                accept,
                allowMultiple,
                MaxFileBytes);

            if (requestAccepted && DeviceFiles.IsSelectionInProgress)
            {
                status = "选择请求已被平台后端接受。true 不表示用户已经选择成功，请继续等待事件回调。";
            }
            else if (!requestAccepted && status == startingStatus)
            {
                status = "选择请求未启动。返回 false 时不能继续等待成功回调。";
            }

            RefreshView();
        }

        private void HandleFileSelected(SelectedFile file)
        {
            selectedFiles.Add(file);

            byte[] originalBytes = file.Data;
            status = "FileSelected 已触发：" + file.Name +
                     "。回调直接交付 SelectedFile，原始 byte[] 长度为 " +
                     originalBytes.Length + "。";

            Debug.Log("FileSelected：" + file.Name +
                      "，原始 byte[] 长度：" + originalBytes.Length);
            RefreshView();
        }

        private void HandleSelectionCompleted(int count)
        {
            status = "SelectionCompleted 已触发：本次成功读取 " + count +
                     " 个文件。多选时 FileSelected 会先对每个文件各触发一次。";
            RefreshView();
        }

        private void HandleSelectionCanceled()
        {
            status = "SelectionCanceled 已触发：用户取消了文件选择。";
            RefreshView();
        }

        private void HandleSelectionFailed(string message)
        {
            status = "SelectionFailed 已触发：" + message;
            RefreshView();
        }

        private void SaveLastSelectedFile()
        {
            if (LastRetainedFile == null)
            {
                status = "当前没有可以保存的文件。";
                RefreshView();
                return;
            }

            const string startingStatus = "正在启动保存请求...";
            status = startingStatus;
            RefreshView();
            bool requestAccepted = DeviceFiles.SaveLastSelectedFile();
            FinishSaveRequestStart(requestAccepted, startingStatus);
        }

        private void SaveGeneratedBytes()
        {
            const string startingStatus = "正在启动保存请求...";
            status = startingStatus;
            RefreshView();
            byte[] bytes = Encoding.UTF8.GetBytes("Device Files byte[] example.");
            bool requestAccepted = DeviceFiles.SaveFile(
                bytes,
                "device-files-example.txt",
                "text/plain");
            FinishSaveRequestStart(requestAccepted, startingStatus);
        }

        private void SaveSourcePath()
        {
            const string startingStatus = "正在启动保存请求...";
            status = startingStatus;
            RefreshView();
            bool requestAccepted = DeviceFiles.SaveFileFromPath(
                sourcePathInput.text,
                maxFileBytes: MaxFileBytes);
            FinishSaveRequestStart(requestAccepted, startingStatus);
        }

        private void SaveSourceUrl()
        {
            const string startingStatus = "正在启动保存请求...";
            status = startingStatus;
            RefreshView();
            bool requestAccepted = DeviceFiles.SaveFileFromUrl(
                sourceUrlInput.text,
                maxFileBytes: MaxFileBytes);
            FinishSaveRequestStart(requestAccepted, startingStatus);
        }

        private void FinishSaveRequestStart(bool requestAccepted, string startingStatus)
        {
            if (!requestAccepted && status == startingStatus)
            {
                status = "保存请求未启动。";
            }
            else if (DeviceFiles.IsSaveInProgress)
            {
                status = "保存请求已启动，最终结果由 SaveCompleted、SaveCanceled 或 SaveFailed 报告。";
            }

            RefreshView();
        }

        private void HandleSaveCompleted(string destination)
        {
            status = "SaveCompleted 已触发：" + destination +
                     "。WebGL 中这只表示浏览器下载已经发起。";
            RefreshView();
        }

        private void HandleSaveCanceled()
        {
            status = "SaveCanceled 已触发：用户取消保存。";
            RefreshView();
        }

        private void HandleSaveFailed(string message)
        {
            status = "SaveFailed 已触发：" + message;
            RefreshView();
        }

        private void ReleaseAllFilesFromUi()
        {
            ReleaseAllFiles();
            status = "预览资源已释放。ReleasePreview 不会修改 byte[]；示例随后清空了自己的文件列表和引用。";
            RefreshView();
        }

        private void ReleaseAllFiles()
        {
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                DeviceFiles.ReleasePreview(selectedFiles[i]);
            }

            selectedFiles.Clear();
        }

        private void HandleSourceInputChanged(string _)
        {
            RefreshView();
        }

        private void RefreshView()
        {
            if (!uiReady)
            {
                return;
            }

            bool requestIdle = DeviceFiles.IsAvailable &&
                               !DeviceFiles.IsSelectionInProgress &&
                               !DeviceFiles.IsSaveInProgress;

            selectAnyButton.interactable = requestIdle;
            selectMultipleButton.interactable = requestIdle;
            selectImageButton.interactable = requestIdle;
            saveGeneratedButton.interactable = requestIdle;
            saveLastSelectedButton.interactable = requestIdle && LastRetainedFile != null;
            saveSourcePathButton.interactable = requestIdle &&
                                                !string.IsNullOrWhiteSpace(sourcePathInput.text);
            saveSourceUrlButton.interactable = requestIdle &&
                                               !string.IsNullOrWhiteSpace(sourceUrlInput.text);
            releasePreviewsButton.interactable = selectedFiles.Count > 0;

            statusText.text = status;
            runtimeStateText.text =
                "IsAvailable：" + DeviceFiles.IsAvailable + "\n" +
                "IsSelectionInProgress：" + DeviceFiles.IsSelectionInProgress + "\n" +
                "IsSaveInProgress：" + DeviceFiles.IsSaveInProgress + "\n" +
                "示例累计保留文件数：" + selectedFiles.Count;

            SelectedFile file = LastRetainedFile;
            if (file == null)
            {
                selectedFileText.text =
                    "尚未收到 FileSelected。\n\n" +
                    "选择成功后，这里会显示 Name、MimeType、Size、Data.Length 和 PreviewUrl。\n" +
                    "其中 Data 是完整原始 byte[]，不是路径，也不是 Base64。";
                return;
            }

            selectedFileText.text =
                "Name：" + file.Name + "\n" +
                "MimeType：" + EmptyAsUnknown(file.MimeType) + "\n" +
                "Size：" + file.Size + " 字节\n" +
                "Data.Length：" + file.Data.Length + " 字节\n" +
                "PreviewUrl：" + EmptyAsUnknown(file.PreviewUrl) + "\n\n" +
                "结论：Data 是文件完整原始二进制 byte[]；它不是路径，也没有转换为 Base64。";
        }

        private bool ValidateBindings()
        {
            bool valid = selectAnyButton != null &&
                         selectMultipleButton != null &&
                         selectImageButton != null &&
                         saveGeneratedButton != null &&
                         saveLastSelectedButton != null &&
                         sourcePathInput != null &&
                         saveSourcePathButton != null &&
                         sourceUrlInput != null &&
                         saveSourceUrlButton != null &&
                         releasePreviewsButton != null &&
                         statusText != null &&
                         runtimeStateText != null &&
                         selectedFileText != null;

            if (!valid)
            {
                Debug.LogError("[DeviceFilesBasicUsage] UGUI 引用不完整，请重新导入 Basic Usage 示例。", this);
            }

            return valid;
        }

        private static string EmptyAsUnknown(string value)
        {
            return string.IsNullOrEmpty(value) ? "（未知或不可用）" : value;
        }
    }
}
