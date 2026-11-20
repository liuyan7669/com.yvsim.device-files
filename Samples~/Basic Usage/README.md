# Device Files - Basic Usage

这个示例用于回答第一次使用 Device Files 时最常见的问题：入口在哪里、选择成功后触发什么回调、回调实际交付什么数据，以及怎样保存 `byte[]`、最近文件、`filePath` 和 HTTP(S) URL。

## 如何运行

1. 在 Unity Package Manager 中选中 **Device Files**。
2. 展开 **Samples**，导入 **Basic Usage**。
3. 打开导入目录中的 `Basic Usage.unity`。
4. 进入 Play Mode，通过场景中已经搭建好的 UGUI 按钮、输入框和状态区域操作。

示例场景使用标准 UGUI `Canvas`、`Button`、`InputField` 和 `Text`，不使用 `OnGUI`，所有界面引用已经在 Inspector 中绑定。示例不使用音频，相机不附带 `AudioListener`，无需额外启用 Audio 模块。

## 中文字体与许可

示例的所有 uGUI Text（包括按钮、状态区域和 InputField 子文本）已绑定 `Fonts/NotoSansCJKsc-Regular.otf`，在 WebGL 中无需依赖操作系统的 Arial 中文回退。随附完整 Noto Sans CJK SC Regular 字体，没有按静态按钮文案裁剪，因此中文文件名不受示例文案子集限制；字体本身未覆盖的 Unicode 字符仍需按项目需求补充回退字体。

此字体独立随 Sample 导入，不需要安装 Noto TMP 或其他 Yvsim 包。字体采用 SIL Open Font License 1.1，详见同目录 `Fonts/OFL-Noto-CJK.txt`，保留原字体内嵌版权与许可证信息。Sample 代码的 MIT 许可不替代字体许可。

示例直接使用包的公开 API：

```csharp
using Yvsim.DeviceFiles;
```

## 最重要的调用顺序

```text
DeviceFiles.OpenFilePicker(...)
→ 用户在系统窗口中选择并确定
→ DeviceFiles.FileSelected 触发
→ 回调参数 SelectedFile.Data 提供完整原始 byte[]
→ DeviceFiles.SelectionCompleted 说明本次所有文件均已成功读取
```

选择文件的统一入口只有：

```csharp
DeviceFiles.OpenFilePicker(
    accept: "",            // 空字符串表示不过滤文件类型
    allowMultiple: false,  // false 单选，true 多选
    maxFileBytes: 20 * 1024 * 1024);
```

成功事件的注册方式：

```csharp
private void OnEnable()
{
    DeviceFiles.FileSelected += HandleFileSelected;
}

private void OnDisable()
{
    DeviceFiles.FileSelected -= HandleFileSelected;
}

private void HandleFileSelected(SelectedFile file)
{
    byte[] originalBytes = file.Data;

    Debug.Log(file.Name);
    Debug.Log(file.MimeType);
    Debug.Log(file.Size);
    Debug.Log(originalBytes.Length);
}
```

`HandleFileSelected` 是示例自己起的方法名，可以修改；`FileSelected` 才是包提供的事件。

## 回调得到的是什么

`FileSelected` 交付一个 `SelectedFile`：

| 属性 | 含义 |
|---|---|
| `Name` | 文件名，不是可信的绝对路径 |
| `MimeType` | MIME 类型，平台无法判断时可能为空 |
| `Size` | 文件字节数 |
| `Data` | 文件完整原始二进制内容，类型为 `byte[]` |
| `PreviewUrl` | 用于媒体预览的临时 URL，不是永久文件路径 |

`Data` 不是 Base64。如果业务接口明确要求 Base64，再自行转换：

```csharp
string base64 = System.Convert.ToBase64String(file.Data);
```

## 全部事件

选择事件：

- `FileSelected(SelectedFile file)`：每成功读取一个文件触发一次。
- `SelectionCompleted(int count)`：本次所有文件均成功读取完成。
- `SelectionCanceled()`：用户取消选择。
- `SelectionFailed(string message)`：窗口、大小限制或文件读取失败。

保存事件：

- `SaveCompleted(string destination)`：保存完成；WebGL 中表示浏览器下载已发起。
- `SaveCanceled()`：用户取消保存。
- `SaveFailed(string message)`：保存窗口或写入失败。

## 保存来源

示例界面的四种保存按钮分别调用：

```csharp
DeviceFiles.SaveFile(bytes, "example.txt", "text/plain");
DeviceFiles.SaveLastSelectedFile();
DeviceFiles.SaveFileFromPath(filePath, maxFileBytes: 20 * 1024 * 1024);
DeviceFiles.SaveFileFromUrl(url, maxFileBytes: 20 * 1024 * 1024);
```

输入框中的 `https://example.com/file.bin` 仅为占位示例，运行 URL 保存前需要填写可访问的真实文件 URL。

路径和 URL 会先异步取得完整字节，再打开平台保存界面；整个流程统一使用 `IsSaveInProgress` 和三个保存终态事件。Unity Editor 中 URL 入口须在 Play Mode 调用。WebGL 的路径只表示 Unity 虚拟文件系统路径，URL 还受 CORS、HTTPS 和浏览器自动下载策略限制。

## 需要注意

- `OpenFilePicker()` 返回 `true` 只表示平台后端接受了请求，不表示用户已经选择成功。
- 多选时会先触发多次 `FileSelected`，最后触发一次 `SelectionCompleted`。
- 示例界面中的“累计保留文件数”可以跨多次选择累加；`SelectionCompleted(count)` 的 `count` 只表示当前这一次请求成功读取的数量。
- 多选中途失败时，可能已经收到若干 `FileSelected`，随后触发 `SelectionFailed`，并且不会再触发 `SelectionCompleted`。
- `accept` 是系统窗口的过滤提示，收到文件后仍需根据业务规则校验。
- 包会把文件完整读取到内存，移动设备和 WebGL 应设置合理的 `maxFileBytes`。
- WebGL 中应从按钮点击直接调用 `OpenFilePicker()` 和 `SaveFile()`。路径或 URL 的一键异步保存可能被浏览器下载策略拦截，生产界面最好先准备字节，再让用户点击独立下载按钮。
- `PreviewUrl` 使用结束后调用 `DeviceFiles.ReleasePreview(file)`；这不会修改 `file.Data`。示例的“释放所有临时预览资源”按钮随后还会清空示例自己的列表和对象引用。
