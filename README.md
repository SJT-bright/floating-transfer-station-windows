# 悬浮中转站 · Windows

贴在屏幕右侧的本地文字、图片中转站：复制自动收集，鼠标移入展开、移出收回，整理后拖到其他软件使用。无需账号或服务器。

## 下载与安装

打开 [最新版下载](https://github.com/SJT-bright/floating-transfer-station-windows/releases/latest)，下载 **FloatingTransferStation-Setup-1.5.2.exe**，双击安装。

- 支持 Windows 10 / 11 x64，自带 .NET 运行环境，不需要安装开发工具。
- 安装器支持选择程序和素材存储位置；安装后自动启动，并在登录 Windows 时运行。
- 免安装包 `FloatingTransferStation-Windows-x64-1.5.2.zip` 解压后运行“悬浮中转站.exe”；免安装方式不会自动注册开机启动。
- GitHub 的 `Source code` 是源码，不是安装包。安装包暂未购买代码签名证书，Windows 可能显示未知发布者提示。

## 使用

- 复制的文字、图片始终进入“待分类”，查看其他类别不会改变接收位置。
- 默认类别：人物资产、场景、提示词、待分类。右上角 `+` 添加类别，双击名称改名（最多 6 个字符）；类别多时滚动查看。
- 把图片拖到其他类别：目标显示蓝色，松手后保存独立副本，原类别图片保留。文本拖动、卡片“移动到”菜单仍执行移动。
- 拖图片到其他软件，或拖文件到指定类别导入；目标软件须支持 Windows 文件拖放。
- 半透明置顶窗口，鼠标移入展开、移出后平滑收拢至右侧小条；收回途中移回鼠标即可取消。系统关闭动画时使用短淡出。通过标题区域拖动位置；卡片拖动不会拖走窗口。
- 在展开后的标题栏操作区点击 `◐` 可调节窗口透明度（35%—100%），滑块实时预览并自动保存；“恢复默认尺寸、位置和透明度”可一键回到默认值。
- `Ctrl + 单击` 或选择框多选，可批量置顶或取消置顶。
- `Ctrl + A`：选择当前分类全部内容。
- `Esc`：取消当前分类的全部选择。
- `Delete` 或 `Backspace`：只删除选中项。

内容保存到本机所选数据目录，默认 `%LOCALAPPDATA%\悬浮中转站\Data`。请勿手动删除仍被视频剪辑项目引用的素材文件。

## 源码与构建

需要 Windows、.NET 10 SDK。仓库包含自动化测试和 GitHub Actions Windows 构建流程：

```powershell
dotnet restore FloatingTransferStation.slnx
dotnet format FloatingTransferStation.slnx --verify-no-changes --no-restore
dotnet test FloatingTransferStation.slnx -c Release --no-restore
dotnet build FloatingTransferStation.slnx -c Release --no-restore -warnaserror
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

构建脚本下载仓库局部的 .NET / Inno Setup，产物位于 `artifacts/publish` 和 `artifacts/installer`。CI 额外输出安装启动探针、测试结果和真实 WPF 渲染截图。自动化结果不等于在所有第三方软件中完成拖拽实测。

## 项目来源

基于 [Oiawlm/floating-transfer-station](https://github.com/Oiawlm/floating-transfer-station) 的 Windows WPF 源码，以及 [SJT-bright 的 macOS 改造版](https://github.com/SJT-bright/floating-transfer-station) 的交互需求继续开发。遵循 [MIT License](LICENSE)，保留原作者版权声明。

这是独立 Windows 发布仓库，不会替换原 macOS 仓库。`macos/` 保留为历史参考，不参与 Windows 构建。贡献与规划见 [CONTRIBUTING.md](CONTRIBUTING.md)、[ROADMAP.md](ROADMAP.md)。
