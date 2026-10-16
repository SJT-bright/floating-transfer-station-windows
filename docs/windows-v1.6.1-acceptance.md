# Windows 1.6.1 验收与发布记录

日期：2026-10-16。正式发布：[Windows v1.6.1](https://github.com/SJT-bright/floating-transfer-station-windows/releases/tag/v1.6.1)。

## 交付范围

- 右键文字卡片或全文窗口进入独立正文编辑；保存、Ctrl+S、取消、多行及原生撤销／重做。
- 先保存分离快照，再更新卡片正文；失败保留原文和草稿，拦截空文本、过期草稿和已删除记录。名称、分类、置顶、采集时间、到达序号与顺序保持不变。
- 左上角小号手动收起按钮，屏边与自由位置均可使用；阻止旧悬停回调立即重开，点击标签可立即展开，编辑与拖动期间不强行收起。

## Windows 原生验证

- 对应源码：`b86ae6eb90ec47cefe5d926f7497f340066c3fc5`。
- [原生构建与验收 38924476060](https://github.com/SJT-bright/floating-transfer-station-windows/actions/runs/38924476060)：格式检查和严格编译通过；完整测试连续两轮 416 / 416 通过，0 失败、0 跳过。
- WPF 运行时交互覆盖正文入口、保存、取消、空白禁用、失败保留草稿、预览刷新，以及屏边／自由悬浮下的手动收起和点击重开。实际 WPF 渲染图 `windows-1.6.1-body-editor.png` 和 `windows-1.6.1-collapse-button.png` 随 Actions 的 `windows-build` 附件保存。
- 自带运行环境的安装器在 Windows runner 中实际安装、启动并响应，登录启动登记通过；独立重启成功替换原进程，新进程继续响应。
- 首轮 3 项版本契约仍固定为 1.6.0；同步版本预期及项目指南后完整原生检查通过，未降低断言。

## 发布及下载验证

[发布流水线 38925275715](https://github.com/SJT-bright/floating-transfer-station-windows/actions/runs/38925275715) 校验成功构建的 SHA、分支与草稿身份，直接上传该构建附件，核对包哈希和远端 `assets.digest` 后公开并标记 latest。

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| FloatingTransferStation-Setup-1.6.1.exe | 47369633 | `c0c437ea4091f7518055ffaea261e808b460b361b2db3d96dbd6eb04782613b5` |
| FloatingTransferStation-Windows-x64-1.6.1.zip | 65996537 | `b5cb052c2d33aff41bd64575c90fa127f02126d76761e6f430c9be882fda02cf` |

两份公开附件重新完整下载后均通过 SHA256SUMS 校验，且与成功 Actions 构建副本一致。未登录请求可读取两个公开下载地址的前 1024 字节，分别为 PE／ZIP，且与完整下载副本的对应前缀一致。校验文件保留 Windows CRLF；macOS 校验工具需要在输入流移除 CR，不修改原文件。

## 验收边界

上述为 Windows CI 原生程序化交互、实际 WPF 渲染、安装及进程重启检查，不表示已在用户 Windows 电脑上安装，也不代替真实输入法、扬声器听感、所有第三方软件拖放和不同显卡／多显示器组合的现场验收。macOS 正式应用及素材数据不在本次变更范围。
