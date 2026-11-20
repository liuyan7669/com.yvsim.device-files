# Changelog

## [1.0.0]

首次发布。

- 统一 Windows、Android、WebGL 和 Unity Editor 的文件选择、完整字节读取及设备保存。
- 支持字节、选择结果、本地路径和 HTTP(S) 保存来源，提供状态、终态事件及临时预览释放。
- 附带可导入的 Basic Usage UGUI 场景与 EditMode 测试。
- 移除 Basic Usage 相机上无用的 AudioListener，示例在最小依赖 WebGL 构建中不再要求 Audio 模块。
- Basic Usage 自带完整 Noto CJK 字体及 OFL 许可，全部 uGUI 文本使用自包含中文字体，修复 WebGL 中文按钮和状态文字缺失。
- 根据实际使用移除 Inspector Groups 依赖，支持不安装其他 Yvsim 包的独立使用。
