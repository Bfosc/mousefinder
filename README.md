# 🖱️ MouseFinder - 鼠标寻找器

一个轻量级的 Windows 桌面工具，当鼠标静止超过指定时间后，会在屏幕中央显示一个醒目的大箭头指向鼠标位置。即使鼠标藏在屏幕边缘，也能轻松找到！

## ✨ 功能特性

- 🎯 **智能定位** — 鼠标静止超时后自动显示方向箭头
- 🖱️ **即动即消** — 鼠标一动，箭头立即消失
- 📍 **边缘指示** — 鼠标靠近屏幕边缘时，边框显示指示标记
- ⌨️ **全局快捷键** — `Ctrl+Alt+F` 随时呼出/隐藏箭头
- 🎨 **可定制** — 箭头颜色、大小、超时时间均可配置
- 💫 **脉冲动画** — 醒目的呼吸动画效果
- 🚀 **开机自启** — 可选开机自动启动
- 📌 **系统托盘** — 最小化到托盘，不占任务栏
- 💾 **配置持久化** — 设置自动保存到 `%AppData%\MouseFinder`

## 📸 使用场景

- 多显示器环境找不到鼠标
- 大屏幕上鼠标指针太小
- 演示/录屏时快速定位鼠标
- 长时间工作后鼠标"失踪"

## 🛠️ 技术栈

- **框架**: .NET 8.0 + WPF
- **平台**: Windows 10/11
- **语言**: C# 12

## 🚀 快速开始

### 方法一：下载即用（推荐）

1. 从 [Releases](https://github.com/Bfosc/mousefinder/releases) 下载 `MouseFinder-win-x64.zip`
2. 解压
3. 双击 `启动MouseFinder.bat` 运行

> 无需安装任何运行时，开箱即用。

### 方法二：从源码构建

```bash
# 克隆仓库
git clone https://github.com/Bfosc/mousefinder.git
cd mousefinder

# 运行（需要 .NET 8 SDK）
dotnet run --project src/MouseFinder/MouseFinder.csproj -c Release
```

## ⚙️ 配置说明

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| 超时时间 | 3 秒 | 鼠标静止多久后显示箭头 (1~30秒) |
| 箭头大小 | 80 px | 箭头的显示大小 (30~200px) |
| 箭头颜色 | 🔴 红色 | 可选红/绿/黄/青 |
| 脉冲动画 | 开启 | 箭头呼吸动画效果 |
| 边缘指示 | 开启 | 鼠标贴边时显示边框标记 |
| 全局快捷键 | Ctrl+Alt+F | 立即显示/隐藏箭头 |
| 开机自启 | 关闭 | Windows 启动时自动运行 |

配置文件保存在: `%AppData%\MouseFinder\settings.json`

## 📁 项目结构

```
mousefinder/
├── MouseFinder.sln
├── README.md
├── LICENSE
├── .gitignore
├── publish/                          # 发布输出（下载即用）
│   ├── MouseFinder.dll
│   ├── 启动MouseFinder.bat
│   └── ...
└── src/MouseFinder/
    ├── MouseFinder.csproj
    ├── App.xaml / App.xaml.cs        # 应用入口
    ├── AppSettings.cs                # 配置管理
    ├── MouseTracker.cs               # 鼠标位置监听
    ├── OverlayWindow.cs              # 全屏透明覆盖层（箭头绘制）
    ├── SettingsWindow.xaml/.cs        # 设置界面
    ├── GlobalHotkey.cs               # 全局快捷键注册
    ├── TrayIcon.cs                   # 系统托盘图标
    └── MainWindow.xaml/.cs           # (保留，未使用)
```

## 📄 License

[MIT License](LICENSE)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！
