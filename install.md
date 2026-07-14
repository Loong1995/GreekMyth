
步骤 1.1 安装 Git for Windows
下载:https://git-scm.com/download/win ,一路默认选项 Next 即可。
验证: 打开 PowerShell,输入 git --version,显示 git version 2.x.x = 通过。
用途:版本控制(Claude 改坏代码时能回滚,单人+AI 开发的保命工具)+ 部分 Unity 包需要通过 Git 安装。

步骤 1.2 安装 Node.js LTS
下载:https://nodejs.org ,选 LTS 版本(22.x),一路默认。
验证: 新开一个 PowerShell 窗口,node -v 显示 v22.x,npm -v 显示版本号 = 通过。
步骤 1.3 安装 Claude Code
步骤 2.1 编辑器基础设置
Unity 菜单 Edit → Preferences → External Tools:

External Script Editor 选 Visual Studio 2022(有就选,没有选 Open by file extension,不影响)
勾选 Generate .csproj files 下的所有选项(Embedded/Local/Registry/Git packages 等)→ 点 Regenerate project files
Edit → Project Settings → Player → Other Settings:

Api Compatibility Level 设为 .NET Standard 2.1
验证: 项目根目录出现若干 .csproj 文件和一个 .sln 文件 = 通过。(这些文件让 Claude 能看到完整的依赖关系。)

步骤 2.2 安装官方包
Window → Package Manager → 左上角 "+" 或 Unity Registry 中搜索安装:

包	说明
Addressables	资源管理/热更基座,第一天就装
Input System	新输入系统
2D Animation + PSD Importer	立绘微动态(2D 模板可能已带)
Cinemachine	镜头演出
Newtonsoft Json	左上角 + → Add package by name → 输入 com.unity.nuget.newtonsoft-json
(Timeline、TextMeshPro/UGUI 在 Unity 6 已内置,无需单独装。)

步骤 2.3 安装 Git 类开源包
Package Manager → "+" → Add package from git URL,依次添加三条:


awk
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer
https://github.com/mob-sakai/ParticleEffectForUGUI.git

步骤 2.4 安装 DOTween(免费版)
浏览器打开 https://assetstore.unity.com ,用和 Unity Hub 相同的账号登录
搜索 "DOTween (HOTween v2)",点 Add to My Assets(免费)
回到 Unity → Window → Package Manager → 左上角下拉选 My Assets → 找到 DOTween → Download → Import → 全选导入
导入后会弹出 DOTween Setup 面板(或菜单 Tools → Demigiant → DOTween Utility Panel)→ 点 Setup DOTween → Apply
本阶段总验证: Console 无红色报错;Package Manager 的 In Project 列表能看到上述所有包;Assets/Plugins/Demigiant 文件夹存在 = 通过。

步骤 2.5 Git 仓库初始化
在项目根目录打开 PowerShell(资源管理器地址栏输入 powershell 回车):

powershell
git init
curl -o .gitignore https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore
git add -A
git commit -m "init: empty URP 2D project with packages"

验证: git log 能看到一条提交;git status 中不应出现 Library/ 文件夹(被 gitignore 正确排除)= 通过。

阶段 3:Claude ↔ Unity 桥接(claude-ready 的核心)
没有这一步,Claude 写完代码后不知道编译是否通过,你就得手动复制 Console 报错给它,效率崩塌。

步骤 3.1 安装 Unity MCP
推荐 Unity MCP(justinpbarnett/unity-mcp,社区最主流方案)。它分两部分:Unity 侧插件 + 本机 MCP 服务。具体步骤:

按其 GitHub README(https://github.com/justinpbarnett/unity-mcp)先安装依赖(它需要 Python 的 uv 工具,README 有一行安装命令),再通过 Package Manager 的 Git URL 安装 Unity 侧包
在 Unity 菜单里打开它的配置窗口,点击为 Claude Code 自动写入 MCP 配置(或按 README 手动执行 claude mcp add 命令)