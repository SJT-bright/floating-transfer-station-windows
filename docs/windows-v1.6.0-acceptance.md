# Windows 1.6.0 验收记录

日期：2026-10-14。正式发布：[v1.6.0](https://github.com/SJT-bright/floating-transfer-station-windows/releases/tag/v1.6.0)。

## 来源与构建

- 安装包对应应用源码：`76707bd7b7860ad551e36d19777db48868510186`，Release 标签固定到该提交。
- [Windows 原生验收运行](https://github.com/SJT-bright/floating-transfer-station-windows/actions/runs/38502287245)：严格构建零警告、零错误；407 项测试在回归步骤和打包步骤各通过一次，无跳过项。
- 真实运行环境：GitHub `windows-latest`，本次为 Windows Server 2025，.NET 10。产品目标为 Windows 10/11 x64，不把构建机验收等同于所有个人电脑配置实测。
- 产物归档 SHA-256：`f5fee4174ef76a3d72cc7b3856ab762de32518edfd2c84a9abbbeaaff06176a8`。下载后已核对完整归档与两个安装包哈希。
- [发布运行](https://github.com/SJT-bright/floating-transfer-station-windows/actions/runs/38503920320) 验证原生验收成功、源码提交和草稿身份；下载同一份产物、核对校验文件与远端资产摘要后发布，没有重新编译替换产物。

## 已验证

- 名称持久化、未命名排除搜索、跨分类总览搜索、两行正文、改名保持展开、DIY 开关。
- 每条多选框、单卡/批量置顶、源显示顺序、拖放身份、排序分区和保存失败恢复。
- 文件导入的独立副本、文件名与字节、导出副本、失败清理、原文件不被删除。
- 清空保留置顶、容量按到达顺序淘汰、删除分类保存失败恢复、未知未来数据格式禁止覆盖、缺失素材记录保留。
- 原始 PNG 字节保持、图片无长期文件锁、静态与多帧格式检查；悬停帧检查不执行全像素解码。
- 左右停靠与缩放、真实 WPF 像素截图、可中断收回动画、自定义透明度、原生窗口尺寸不逐帧重排。
- 安装后应用响应、登录启动注册和独立重启入口。重启探针确认旧进程退出、新进程启动且响应。
- 安装包包含 Skia 原生依赖授权声明；免安装程序确认为 Windows x86-64 PE32+ 可执行文件。

## 产物摘要

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| FloatingTransferStation-Setup-1.6.0.exe | 47364934 | `f1f9e4a5f5e46409cc3fc5301344b15a90f0d9936bd4532ab4bd0104d81f55aa` |
| FloatingTransferStation-Windows-x64-1.6.0.zip | 65987993 | `e6f0bc7e09327ac44942724b6573ac2386eb788126998b7df8b7837fb34b1592` |

GitHub 资产状态为 `uploaded`，远端 `digest` 与本表一致。

## 仍需按实际环境验收

真实鼠标向每一种第三方浏览器、聊天与剪辑软件投递，以及多显示器、混合 DPI 和实际声卡听感，不能由组件测试或渲染截图代替。透明玻璃采用 Windows 透明高光近似，不宣称是苹果原生折射材质。安装包尚未购买商业代码签名证书。
