# Device Files

`com.yvsim.device-files` 为 Unity 2021.3 LTS 与 Unity 2022.3 LTS 提供统一的本地文件选择、完整字节读取和本地保存能力。保存源可以是完整 `byte[]`、`SelectedFile`、最近一次选择结果、应用可访问的 `filePath` 或 HTTP(S) URL。

- Package Manager 显示名：`Device Files`
- 包名：`com.yvsim.device-files`
- 程序集：`Yvsim.DeviceFiles`
- 命名空间：`Yvsim.DeviceFiles`
- 许可：MIT

这个包负责把文件数据搬进或搬出 Unity：打开系统选择窗口、把用户选择的文件完整读取为 `byte[]`、提供临时预览 URL、异步读取应用可访问路径、异步取得 HTTP(S) 响应，并把最终字节交给系统保存或浏览器下载。它不包含 HTTP 上传、对象存储写入、业务服务器接口、Base64 协议或媒体解码器。

## Unity 版本支持

- 支持 Unity 2021.3 LTS 与 Unity 2022.3 LTS。
- `package.json` 中的 `"unity": "2021.3"` 表示最低支持 Unity 版本，而不是只能使用 Unity 2021。Unity 2022.3 在此最低版本之上，属于支持范围。

## 支持平台

| 平台 | 选择文件 | 多选 | `PreviewUrl` | 保存结果 |
|---|---|---:|---|---|
| Windows Editor | Windows 原生文件窗口 | 支持 | 原文件的 `file:` URI | 实际绝对路径 |
| macOS / Linux Editor | `EditorUtility` 文件窗口 | 不支持，自动退化为单选 | 原文件的 `file:` URI | 实际绝对路径 |
| Windows Standalone | Windows 原生文件窗口 | 支持 | 原文件的 `file:` URI | 实际绝对路径 |
| Android 5.1 / API 22+ | Storage Access Framework | 支持 | 应用私有缓存的 `file:` URI | `content:` URI |
| WebGL | 浏览器文件选择器 | 支持 | 临时 `blob:` URL | 浏览器建议下载文件名 |

当前不支持 macOS / Linux Standalone、iOS、tvOS 和主机平台；这些 Player 中 `DeviceFiles.IsAvailable` 返回 `false`。

Android API 22 是当前最低支持基线，包不会替项目修改 Min API Level。Android 通过系统 Storage Access Framework 访问用户明确选择的位置，不会主动申请 `READ_EXTERNAL_STORAGE`、`WRITE_EXTERNAL_STORAGE` 或整盘存储权限。

WebGL 的文件选择和 `SaveFile(byte[])` 本身不依赖业务 HTTP 接口，但正式运行 Unity WebGL 构建时仍建议通过 HTTP / HTTPS Web 服务器加载。直接以 `file://` 打开构建页面不属于保证支持的部署方式。选择和保存应由按钮点击等用户手势直接触发。`SaveFileFromUrl` 还受 CORS、HTTPS 混合内容和浏览器自动下载策略限制。

## 安装

当前源码版本为 `1.0.0`。准备本包源码，使用本地 UPM 安装：

1. 在 `Window > Package Manager > + > Add package from disk...` 选择本包根目录的 `package.json`。
2. 等待 Unity 解析清单中声明的 Unity 依赖并完成编译。

也可以将完整包目录放入项目的 `Packages/com.yvsim.device-files`，作为 Embedded Package 使用。

本说明不代表 Git Tag 或 OpenUPM 的 `1.0.0` 已发布；发布完成前请使用上述本地源码安装方式。

## 导入 Basic Usage 示例

1. 打开 **Window > Package Manager**。
2. 选中 **Device Files**。
3. 展开 **Samples**，点击 **Basic Usage** 右侧的 **Import**。
4. 打开导入后的 `Basic Usage.unity`，进入 Play Mode。

Unity 2021.3 默认会将示例导入到：

```text
Assets\Samples\Device Files\1.0.0\Basic Usage
```

示例使用 Canvas、Button、InputField 和 Text 搭建完整 UGUI 界面，不使用 `OnGUI`。直接调用本包的公开 API。

## 最短使用流程

`DeviceFiles` 是静态类，不需要把组件、Prefab 或管理器挂到场景中。使用顺序是：

```text
先订阅静态事件
→ 从按钮点击调用 DeviceFiles.OpenFilePicker(...)
→ 用户在系统窗口选择文件并确定
→ 每个成功读取的文件触发一次 DeviceFiles.FileSelected(file)
→ 从 file.Data 取得完整原始 byte[]
→ 本次全部成功后触发 DeviceFiles.SelectionCompleted(count)
```

选择文件功能只有一个入口：

```csharp
DeviceFiles.OpenFilePicker(...)
```

所有公开入口都集中在静态门面 `DeviceFiles` 中；不需要在场景中挂组件。保存使用 `SaveFile...` 方法族，释放临时预览资源使用 `DeviceFiles.ReleasePreview(...)`。

## 完整入门代码

```csharp
using Yvsim.DeviceFiles;
using UnityEngine;

public sealed class DeviceFilesExample : MonoBehaviour
{
    private const int MaxFileBytes = 20 * 1024 * 1024;
    private SelectedFile lastFile;

    private void OnEnable()
    {
        // 选择文件的四个事件。
        DeviceFiles.FileSelected += HandleFileSelected;
        DeviceFiles.SelectionCompleted += HandleSelectionCompleted;
        DeviceFiles.SelectionCanceled += HandleSelectionCanceled;
        DeviceFiles.SelectionFailed += HandleSelectionFailed;

        // 保存文件的三个事件。
        DeviceFiles.SaveCompleted += HandleSaveCompleted;
        DeviceFiles.SaveCanceled += HandleSaveCanceled;
        DeviceFiles.SaveFailed += HandleSaveFailed;
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

    // 可直接绑定到 Unity Button.onClick。
    public void SelectAnyFile()
    {
        bool requestAccepted = DeviceFiles.OpenFilePicker(
            accept: "",
            allowMultiple: false,
            maxFileBytes: MaxFileBytes);

        if (!requestAccepted)
        {
            Debug.LogWarning("选择请求没有启动，不要继续等待成功回调。");
        }
    }

    private void HandleFileSelected(SelectedFile file)
    {
        // 本示例只发起单选。重复选择前先释放上一个临时预览资源。
        if (lastFile != null)
        {
            DeviceFiles.ReleasePreview(lastFile);
        }

        lastFile = file;

        byte[] originalBytes = file.Data;
        Debug.Log("文件名：" + file.Name);
        Debug.Log("MIME：" + file.MimeType);
        Debug.Log("字节数：" + file.Size);
        Debug.Log("原始 byte[] 长度：" + originalBytes.Length);
    }

    private void HandleSelectionCompleted(int count)
    {
        Debug.Log("本次选择全部完成，成功文件数：" + count);
    }

    private void HandleSelectionCanceled()
    {
        Debug.Log("用户取消了文件选择。");
    }

    private void HandleSelectionFailed(string message)
    {
        // 包本身已经记录错误日志；这里处理自己的 UI 或业务状态。
        Debug.Log("业务收到选择失败：" + message);
    }

    public void SaveLastFile()
    {
        if (lastFile == null)
        {
            return;
        }

        bool requestAccepted = DeviceFiles.SaveFile(lastFile);
        if (!requestAccepted)
        {
            Debug.LogWarning("保存请求没有启动。");
        }
    }

    private void HandleSaveCompleted(string destination)
    {
        Debug.Log("保存完成：" + destination);
    }

    private void HandleSaveCanceled()
    {
        Debug.Log("用户取消了保存。");
    }

    private void HandleSaveFailed(string message)
    {
        Debug.Log("业务收到保存失败：" + message);
    }

    public void ReleaseLastPreview()
    {
        if (lastFile == null)
        {
            return;
        }

        DeviceFiles.ReleasePreview(lastFile);
        lastFile = null;
    }

    private void OnDestroy()
    {
        ReleaseLastPreview();
    }
}
```

`HandleFileSelected` 等方法名由使用者自己决定；`FileSelected`、`SelectionCompleted` 等才是包提供的静态事件。必须先订阅再发起请求，并在 `OnDisable` 中对称退订，否则对象反复启用后可能收到重复回调。

## 选择文件入口

```csharp
bool DeviceFiles.OpenFilePicker(
    string accept = "",
    bool allowMultiple = false,
    int maxFileBytes = 0);
```

### 参数

| 参数 | 默认值 | 功能 |
|---|---|---|
| `accept` | `""` | HTML 风格的文件类型过滤提示。支持 MIME、通配 MIME、扩展名及逗号组合；空字符串表示任意文件。 |
| `allowMultiple` | `false` | 是否允许多选。Windows、Android、WebGL 支持；非 Windows Editor 会退化为单选。 |
| `maxFileBytes` | `0` | 每个文件各自的最大字节数。`0` 表示没有业务上限，但仍受 `int.MaxValue`、托管数组长度和可用内存限制。 |

常用过滤器：

```csharp
DeviceFiles.OpenFilePicker("", false, 20 * 1024 * 1024);                   // 任意文件，单选
DeviceFiles.OpenFilePicker("", true, 20 * 1024 * 1024);                    // 任意文件，多选
DeviceFiles.OpenFilePicker("image/*,.png,.jpg,.jpeg", false, 10 * 1024 * 1024);
DeviceFiles.OpenFilePicker("audio/*,.mp3,.wav,.ogg", false, 50 * 1024 * 1024);
DeviceFiles.OpenFilePicker("application/json,.json", false, 2 * 1024 * 1024);
```

`accept` 只是系统窗口的筛选提示，不是安全验证。收到文件后仍应按业务要求检查文件名、实际内容、大小和格式，不能信任扩展名或 MIME。

当前内置的常用扩展名映射包括：

- 图片：`.png`、`.jpg`、`.jpeg`、`.gif`、`.bmp`、`.webp`
- 文本与数据：`.txt`、`.log`、`.md`、`.json`、`.xml`、`.csv`、`.pdf`、`.zip`
- 音频：`.mp3`、`.wav`、`.ogg`、`.aif`、`.aiff`、`.m4a`
- 视频：`.mp4`、`.webm`、`.mov`、`.m4v`、`.ogv`

平台对过滤器的处理不同：

- WebGL 将有效字符串交给浏览器的 `<input type="file" accept="...">`。
- Windows 将可识别的 MIME 和扩展名转换为原生扩展名过滤器，同时保留 **All files**。
- Android 将可识别的扩展名转换为 MIME；部分系统或文件提供器可能忽略过滤提示。
- 非 Windows Editor 当前忽略 `accept`，使用 Unity 的单文件选择窗口。

### 返回值

- `true`：平台后端接受了启动请求。它不表示用户已经选中文件，也不表示文件已经读取成功；继续等待事件。
- `false`：请求没有保持为已接受状态，例如平台不支持、已有同类请求或后端无法启动。此时不能假设之后还会有终态回调。

某些启动错误可能在方法返回前同步触发失败事件，所以始终要先订阅事件，再调用 `OpenFilePicker`。

## 选择文件的四个回调

这四个成员都是全局静态事件：

| 事件及类型 | 用户回调形参 | 何时触发 |
|---|---|---|
| `Action<SelectedFile> FileSelected` | `SelectedFile file` | 每成功读取一个文件触发一次。`file` 包含文件信息、完整原始字节和临时预览 URL。 |
| `Action<int> SelectionCompleted` | `int count` | 本次所有文件全部成功读取后触发一次；`count` 是本次成功读取数。 |
| `Action SelectionCanceled` | 无 | 用户取消文件选择时触发。 |
| `Action<string> SelectionFailed` | `string message` | 窗口启动、文件超限、缓存或读取失败时触发；`message` 是错误说明。 |

所有结果事件都会在 Unity 主线程交付。某个订阅者抛出异常时，包会记录异常并继续通知其他订阅者。

### 事件顺序

单选成功：

```text
FileSelected(file)
SelectionCompleted(1)
```

多选成功：

```text
FileSelected(file1)
FileSelected(file2)
...
SelectionCompleted(count)
```

用户取消：

```text
SelectionCanceled()
```

多选中途失败时允许出现部分成功：

```text
FileSelected(file1)
FileSelected(file2)
SelectionFailed(message)
```

部分失败后不会再触发 `SelectionCompleted`。如果业务要求“所有文件都成功后才统一处理”，应先在 `FileSelected` 中临时收集，只有收到 `SelectionCompleted` 后才提交；收到 `SelectionFailed` 时自行丢弃或保留部分结果。

多选回调采用平台返回的顺序，不应把它当作用户点击文件的先后顺序。业务需要固定顺序时，请根据文件名或自己的规则排序。

## `SelectedFile` 回调参数

`FileSelected` 返回的是一个 `SelectedFile` 对象，不是路径字符串，也不是 Base64。

| 属性 | 类型 | 功能 |
|---|---|---|
| `Name` | `string` | 文件名。它不是可信的本地绝对路径。 |
| `MimeType` | `string` | MIME 类型；平台无法识别时可能为空，也不能作为安全判断依据。 |
| `Size` | `int` | 文件字节数，等于 `Data.Length`。 |
| `Data` | `byte[]` | 文件完整原始二进制内容；空文件对应长度为 0 的数组。 |
| `PreviewUrl` | `string` | 平台提供的临时预览资源 URL。它不是永久路径。 |

最重要的结果是：

```csharp
private void HandleFileSelected(SelectedFile file)
{
    byte[] completeRawBytes = file.Data;
}
```

`byte[]` 是 .NET 的原始字节数组。包不会自动把它转成文字、图片、音频、视频或 Base64。使用方式取决于业务：

```csharp
// 明确知道文件是 UTF-8 文本时：
string text = System.Text.Encoding.UTF8.GetString(file.Data);

// 只有接口明确要求 Base64 时才转换：
string base64 = System.Convert.ToBase64String(file.Data);

// 需要原始字节上传时，直接把 file.Data 交给自己的网络层。
```

包不是流式文件读取器。每个文件会完整复制到托管内存；如果业务在多选回调中收集全部 `SelectedFile`，就会同时持有多个完整 `byte[]`。移动设备和 WebGL 必须设置合理的 `maxFileBytes`，并及时释放业务不再需要的引用。

## 其他公开状态属性

| 属性 | 类型 | 功能 |
|---|---|---|
| `LastSelectedFile` | `SelectedFile` | 最近一次成功交付的文件；尚未成功选择时为 `null`。多选结束后只指向最后一个成功文件。 |
| `IsSelectionInProgress` | `bool` | 当前是否仍在等待选择窗口或读取文件。 |
| `IsSaveInProgress` | `bool` | 当前是否仍在读取路径、下载 URL、等待保存位置或写入文件。 |
| `IsAvailable` | `bool` | 当前运行平台是否有选择和保存后端。 |

开始新请求、取消、失败或调用 `ReleasePreview` 都不会自动清空 `LastSelectedFile`。它会一直静态持有最近一次成功文件的 `Data`，直到下一次成功选择将它替换，或 Unity 重置该运行时子系统。当前没有单独清空这个属性的公开 API，因此不要把它当作长期文件集合；多文件请只在自己的回调和集合中管理。

包会统一拒绝并发的选择与保存请求，保证一次最多只有一个系统文件窗口。调用前仍可用两个 `Is...InProgress` 状态控制按钮交互。

`FileSelected` 触发时选择流程通常仍在进行；`SelectionCompleted`、`SelectionCanceled` 或 `SelectionFailed` 触发前，`IsSelectionInProgress` 已恢复为 `false`。三个保存终态事件触发前，`IsSaveInProgress` 也已恢复为 `false`。

URL 超时常量：`MinimumUrlTimeoutSeconds = 1`、`MaximumUrlTimeoutSeconds = 3600`、`DefaultUrlTimeoutSeconds = 60`。

## 保存文件

保存方法都在同一个 `DeviceFiles` 静态门面中：

```csharp
bool DeviceFiles.SaveFile(
    byte[] data,
    string fileName,
    string mimeType = null);

bool DeviceFiles.SaveFile(SelectedFile file);

bool DeviceFiles.SaveLastSelectedFile();

bool DeviceFiles.SaveFileFromPath(
    string filePath,
    string fileName = null,
    string mimeType = null,
    int maxFileBytes = 0);

bool DeviceFiles.SaveFileFromUrl(
    string url,
    string fileName = null,
    string mimeType = null,
    int maxFileBytes = 0,
    int timeoutSeconds = 60);
```

参数含义：

| 参数 | 功能 |
|---|---|
| `data` | 要保存的完整原始字节；支持任意文件类型，不限于音频。 |
| `fileName` | 建议文件名，不是目标路径；目录部分会被移除。 |
| `mimeType` | 文件 MIME；为空时先按扩展名推断，仍无法识别时使用 `application/octet-stream`。URL 还会读取响应 `Content-Type`。 |
| `file` | 直接使用该 `SelectedFile` 的 `Data`、`Name` 和 `MimeType`。 |
| `filePath` | 应用当前有权读取的文件路径。它不是 Android `content:` URI；WebGL 中只能是 Unity 虚拟文件系统路径。 |
| `url` | 绝对 HTTP 或 HTTPS URL。包会先异步下载完整响应，再进入系统保存流程；Unity Editor 中须在 Play Mode 调用。 |
| `maxFileBytes` | 路径文件或 URL 响应的最大字节数；`0` 表示不设置业务上限。 |
| `timeoutSeconds` | URL 请求超时秒数，范围 1～3600，默认 60；超时走 `SaveFailed`。 |

保存新生成的数据：

```csharp
byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello");
bool requestAccepted = DeviceFiles.SaveFile(bytes, "hello.txt", "text/plain");
```

原样保存已选择文件：

```csharp
bool requestAccepted = DeviceFiles.SaveFile(selectedFile);
```

保存最近一次选择结果：

```csharp
bool requestAccepted = DeviceFiles.SaveLastSelectedFile();
```

异步读取应用可访问路径后保存：

```csharp
bool requestAccepted = DeviceFiles.SaveFileFromPath(
    Application.persistentDataPath + "/report.zip",
    "report.zip",
    "application/zip",
    50 * 1024 * 1024);
```

异步取得 URL 后保存：

```csharp
bool requestAccepted = DeviceFiles.SaveFileFromUrl(
    "https://example.com/files/voice.wav",
    "voice.wav",
    "audio/wav",
    20 * 1024 * 1024,
    60);
```

`SaveFileFromPath` 和 `SaveFileFromUrl` 一旦返回 `true`，`IsSaveInProgress` 会覆盖“取得源字节 → 打开系统保存界面 → 写入目标”的完整流程；读取失败、HTTP 失败、超限、用户取消和最终写入失败都仍从同一组三个保存终态事件报告，而且每个被接受的请求最多只发布一个终态。

Unity Editor 未进入 Play Mode 时，`SaveFileFromUrl` 会返回 `false` 且不会开始请求；导入的 Basic Usage 示例本来就需要先进入 Play Mode。`SaveFileFromPath`、`SaveFile(byte[])` 和文件选择仍可按各自 Editor 后端使用。

返回 `true` 只表示请求已被包接受，不代表用户最终保存成功。返回 `false` 时不能继续假设一定会收到终态回调。直接传入的 `byte[]` 在保存终态到达前由调用方保持不变；路径和 URL 入口得到的是包自己持有的完整数组。

`Stream`、`TextAsset`、数据库 Blob、录音结果或第三方 SDK 数据不需要新的平台后端：调用方先取得完整 `byte[]`，再统一调用 `SaveFile(bytes, fileName, mimeType)`。Device Files 不把实时分块自动拼成具体媒体格式；媒体包应先生成合法完整文件。

### 各平台保存位置

- Windows Editor / Standalone：打开系统“另存为”窗口，由用户选择目录；成功后返回绝对路径。本包不会静默写入一个未知默认目录，也不会自动打开资源管理器。
- macOS / Linux Editor：打开 Unity Editor 保存窗口，由用户选择目录。
- Android：使用 Storage Access Framework 的 `ACTION_CREATE_DOCUMENT`，由用户在系统文件提供器中选择位置；成功后返回 `content:` URI，不申请传统整盘存储权限。
- WebGL：创建浏览器 Blob 下载，实际目录由浏览器的下载设置决定；包只能返回建议文件名，不能获知最终磁盘路径。

### 与 Audio Recorder 组合保存 WAV

两个包互不依赖。Audio Recorder 单独负责在录音结束时交付完整 PCM16 WAV；Device Files 单独负责把任意完整字节保存到设备。两个包都安装后，在真实“下载录音”按钮中组合：

```csharp
using Yvsim.AudioRecorder;
using Yvsim.DeviceFiles;

public void DownloadLastRecording()
{
    RecordedAudio recording = AudioRecorder.LastRecording;
    if (recording == null)
    {
        return;
    }

    DeviceFiles.SaveFile(recording.Data, recording.Name, recording.MimeType);
}
```

即使录音时开启实时 PCM 输出，`RecordingCompleted(RecordedAudio recording)` 最终仍会单独交付完整 WAV `recording.Data`。`AudioStreamChunk.Data` 是实时 PCM16 分块，不是独立 WAV 文件，不能逐块改名为 `.wav` 保存。WebGL 应在完成事件中缓存结果并启用下载按钮，再由用户真实点击按钮调用 `SaveFile`；不要在异步录音完成回调里自动触发浏览器下载。

## 保存文件的三个回调

| 事件及类型 | 用户回调形参 | 何时触发 |
|---|---|---|
| `Action<string> SaveCompleted` | `string destination` | 保存完成。形参含义随平台变化，见下表。 |
| `Action SaveCanceled` | 无 | 用户取消保存。 |
| `Action<string> SaveFailed` | `string message` | 保存窗口无法打开或写入失败；`message` 是错误说明。 |

`SelectionFailed` 和 `SaveFailed` 触发前，包自身已经写入错误日志。业务回调可更新 UI、提示用户或上报状态；如果再次调用 `Debug.LogError`，控制台会出现重复错误日志。

`SaveCompleted(destination)` 的形参含义：

| 平台 | `destination` |
|---|---|
| Windows Editor / Standalone | 实际绝对保存路径 |
| macOS / Linux Editor | 实际绝对保存路径 |
| Android | Storage Access Framework 返回的 `content:` URI |
| WebGL | 建议下载文件名，不是用户磁盘上的最终路径 |

WebGL 无法知道用户最终保存到哪里，只能确认浏览器下载动作已经发起。WebGL 成功时 `SaveCompleted` 可能在 `SaveFile` 返回之前同步触发；启动失败时 `SaveFailed` 也可能在返回 `false` 前同步触发。保存事件必须先订阅，且不要在 `SaveFile` 返回后无条件覆盖回调已经设置的 UI 状态。

## 临时预览 URL 与释放

`SelectedFile.PreviewUrl` 可交给相应的 Unity 或浏览器媒体加载流程：

- WebGL：临时 `blob:` URL。
- Android：包复制到应用私有缓存后的 `file:` URI。
- Windows / Editor：用户原文件的 `file:` URI。

`PreviewUrl` 只提供资源地址，不保证任意图片、音频或视频格式都能解码播放。实际播放能力取决于 Unity 组件、浏览器、操作系统、媒体容器与编解码器；预览失败不代表 `file.Data` 读取失败。

预览结束后调用：

```csharp
bool released = DeviceFiles.ReleasePreview(file);
```

平台行为：

- WebGL：撤销包创建并跟踪的 Blob URL。
- Android：删除本包创建的私有选择缓存文件。
- Windows / Editor：原文件 URI 无需包释放；方法不会删除或修改用户原文件，正常非空 URI 返回 `true`。

返回 `true` 表示平台资源已成功释放或无需额外释放；未知、为空、已撤销或无法删除的资源可能返回 `false`。释放后不要再使用 `PreviewUrl`，但 `file.Data` 的内容仍然存在且可以继续使用。

`ReleasePreview` 不会清空 `LastSelectedFile`，也不会替使用者移除集合中的 `SelectedFile`。应及时移除自己的对象引用；但最近一次成功文件仍会由静态 `LastSelectedFile` 保留到下一次成功选择或运行时子系统重置。

## 主线程、用户手势与生命周期

- `OpenFilePicker`、全部 `SaveFile...` 方法和 `ReleasePreview` 必须从 Unity 主线程调用；从后台线程调用会抛出 `InvalidOperationException`。
- 所有事件结果在 Unity 主线程交付。
- 事件是全局静态事件，应在 `OnEnable` 中订阅，在 `OnDisable` 中对称退订。
- WebGL 的选择和直接 `SaveFile(byte[])` 下载应从按钮点击等用户手势直接发起。`SaveFileFromUrl` / `SaveFileFromPath` 需要先异步取得字节，部分浏览器可能把随后自动触发的下载拦截；要求最稳时，应先由业务异步准备 `byte[]`，启用下载按钮，再在下一次真实点击中调用 `SaveFile`。
- 不需要在场景中挂载 Device Files 组件；包会自动维护内部调度对象。

## 参数异常与资源限制

| 情况 | 结果 |
|---|---|
| `maxFileBytes < 0` | `ArgumentOutOfRangeException` |
| URL `timeoutSeconds` 不在 1～3600 | `ArgumentOutOfRangeException` |
| `SaveFile` 的 `data == null` | `ArgumentNullException` |
| `fileName == null` | `ArgumentNullException` |
| `fileName == ""`，或移除目录后为空、`.`、`..` | `ArgumentException` |
| `SaveFile((SelectedFile)null)` | `ArgumentNullException` |
| `SaveFileFromPath(null)` 或 `SaveFileFromUrl(null)` | `ArgumentNullException` |
| 空路径、非 HTTP(S) 绝对 URL、无效建议文件名 | `ArgumentException` |
| `ReleasePreview(null)` | `ArgumentNullException` |
| 从非 Unity 主线程调用启动或释放方法 | `InvalidOperationException` |
| 输入字符串包含无效 UTF-16 | `ArgumentException` |

选择结果、URL 响应和路径源最终都是 `byte[]`，因此单文件绝对上限不能超过 `int.MaxValue`，实际可用上限通常更低并取决于内存。`maxFileBytes` 限制单个选择文件、单个路径源或单次 URL 响应，不是一次多选的总和，也不是严格的峰值内存上限；读取器或 `UnityWebRequest` 仍可能在检查终态长度前持有中间缓冲。WebGL、Android 和多选场景可能存在浏览器、缓存和托管内存中的中间副本，应按设备能力设置远低于理论上限的业务值。

本包不是流式文件管道：保存前始终需要一份完整 `byte[]`。原生平台的路径读取、目标写入和 Android 保存缓存写入已放到后台线程；URL 使用 UnityWebRequest 异步获取；WebGL 虚拟路径按帧分块读取。系统文件窗口和 WebGL 的托管字节到 JavaScript / Blob 复制仍发生在主线程，超大文件仍可能造成峰值内存和卡顿。直接传入的数组在保存终态前不得修改。

当前没有主动取消正在进行的路径读取、URL 下载或后台目标写入的公开方法；URL 由 `timeoutSeconds` 限时，系统保存窗口可由用户取消并触发 `SaveCanceled`。业务应在请求期间禁用重复按钮，并等待对应终态后再开始下一次操作。

文件名、扩展名、MIME 和文件内容都属于外部输入；包只负责搬运数据，不替业务进行病毒扫描、格式真实性验证或安全解析。

## 测试与平台验证

`Tests/Editor` 覆盖过滤器、文件名与大小校验、字节读取、临时资源释放、主线程约束、回调隔离及平台桥接数据。测试程序集需通过项目 `manifest.json` 的 `testables` 包含 `com.yvsim.device-files` 后在 Test Runner 中运行。

EditMode 测试不替代真实 Windows 文件窗口、Android Storage Access Framework 或目标浏览器的设备验证。设备验收应覆盖取消、文件大小限制、用户手势、CORS、下载策略以及预览 URL 的释放。

## 包目录结构

```text
com.yvsim.device-files\
├─ .gitattributes                     统一仓库文本换行为 LF
├─ Runtime\                           运行时代码与 Yvsim.DeviceFiles.asmdef
│  ├─ DeviceFilesSourceLoader.cs       路径与 HTTP(S) URL 异步来源
│  └─ Plugins\
│     ├─ Android\                     Android Java / SAF 后端
│     └─ WebGL\                       WebGL .jslib 后端
├─ Tests\Editor\                     EditMode 测试与测试 asmdef
├─ Samples~\Basic Usage\             可从 Package Manager 导入的入门场景和脚本
├─ package.json                       UPM 清单、依赖与 Samples 注册
├─ README.md                          完整安装、API 与平台说明
├─ CHANGELOG.md                       版本变更记录
└─ LICENSE.md                         MIT 许可证
```

本包没有独立的 Editor 工具，因此不创建空的 `Editor` 目录。Editor 文件窗口由 Runtime 程序集中的条件编译分支实现。`Samples~` 使用 Unity UPM 的波浪号目录约定，不会在导入 Sample 前参与普通工程资源导入。

## 依赖

`package.json` 显式声明以下 Unity 内置模块：

- `com.unity.modules.androidjni`：Android Java 桥接。
- `com.unity.modules.jsonserialize`：Android 原生事件 JSON 解析。
- `com.unity.modules.unitywebrequest`：HTTP(S) URL 异步来源。
- `com.unity.ugui`：`Basic Usage` 示例的 Canvas、Button、InputField 与 Text 界面。

不依赖第三方文件选择插件、AndroidX、外部 AAR 或网络上传 SDK。

## 依赖说明

本包不依赖 Inspector Groups 或其他 Yvsim 包；Samples 中的可选组合需按示例说明准备依赖。

## 第三方字体许可

Basic Usage 示例自带完整的 Noto Sans CJK SC Regular 字体，已绑定所有 uGUI 文本，供 WebGL 等平台显示中文界面和中文文件名。字体资源独立包含在 Sample 中，不要求安装 Noto TMP 包。

本包代码使用 MIT 许可；`Samples~/Basic Usage/Fonts/NotoSansCJKsc-Regular.otf` 使用 SIL Open Font License 1.1，详见同目录 `OFL-Noto-CJK.txt`，并保留原字体内嵌版权信息。

## 从 GitHub 安装

仓库：[com.yvsim.device-files](https://github.com/liuyan7669/com.yvsim.device-files)。仓库名与 UPM 包名一致。

在 Unity Package Manager 选择 Add package from git URL，输入：

```text
https://github.com/liuyan7669/com.yvsim.device-files.git#main
```

此地址指向 main 分支，当前包版本为 1.0.0。本次上传不创建版本 tag 或 GitHub Release，也不代表已在 OpenUPM 登记；Unity 官方依赖仍按 package.json 解析。
