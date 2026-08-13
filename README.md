# lom_assistant — 活侠传助手（战斗直接结算 / 自动快进已读对话）

《活侠传》（Legend of Mortal）的 BepInEx 插件：屏幕上会出现一组**游戏水墨 UI 风格**的可拖动按钮，按场景自动切换——

- **战斗直接结算**：进入**单挑**或**战役**后显示「直接胜利」「直接失败」，点击即按游戏自身的对应流程结算（正常发放结算、进入后续剧情）。
- **自动快进已读对话**：进入剧情场景后显示「一键快进」开关（或在剧情中**按 Ctrl** 开关）：打开后调用游戏自身的快进流程自动推进已读对话，**遇到未读台词自动暂停**，等你读过后自动继续；再按一次 Ctrl / 再点一次按钮关闭。

> 2.0 起插件由 MortalInstantWin 更名为 **lom_assistant**，请删除旧的 `MortalInstantWin.dll` 后再安装（见下文）。

## 功能

- **水墨风格界面**：面板按游戏官方 UI 风格手工设计——深色半透明圆角底、柔和细边框，边框纹理由代码烘焙 9-slice 精灵；文字优先使用游戏自带的动态字体（行书/宋体，自动跳过会缺字的烘焙字体），取不到时用内置字体兜底；构建失败时退回系统灰框，功能不受影响。
- **战斗直接结算**，同时支持两种战斗：
  - **单挑**（决斗系统，`Mortal.Combat`）：调用 `CombatManager.GameOver(win)`，与一方战败时的流程完全一致（应用胜/负结算 Flag、加载后续场景；若该场失败是死局 DeadEnd 则进入 GameOver 画面）。
  - **战役**（群战系统，`Mortal.Battle`）：调用 `GameLevelManager.ShowGameOver(FriendWin / EnemyWin, true)`，与游戏内置按钮相同（胜利=暂停面板测试 Win，失败=暂停面板认输），含拾取银两结算。
- **自动快进已读对话**（`Mortal.Story`）：
  - 与按住游戏自带快进键完全相同的流程（10 倍速自动推进），但做成开关：按一下 Ctrl（或点面板按钮）即可，不用一直按着。
  - 游戏在显示每句台词时判定是否已读：**遇到未读台词会自动暂停**，不会漏看新剧情；你手动点过之后，后面的已读台词会自动继续快进；弹出选项菜单时也会停下等你选择。
  - 再按一次 Ctrl / 再点一次按钮（按钮在开启时显示「停止快进」）即可随时关闭。
  - 对话结束/剧情切换场景后自动恢复正常游戏速度，不会把加速带到剧情外；进入战斗时自动快进会强制关闭，绝不把 10 倍速带进战斗。
  - 不需要可在配置里关闭 Ctrl 开关：把 `BepInEx/config/com.mohui666.lomassistant.cfg` 中 `[Story] CtrlToggleFastForward` 设为 `false`，恢复游戏默认的按住 Ctrl 快进。
- 拖动面板空白处移动按钮组，位置自动保存到 `BepInEx/config/com.mohui666.lomassistant.cfg`。
- 两种功能互斥：战斗面板只在战斗中显示，剧情面板只在剧情场景（且非战斗）显示，离开对应场景自动隐藏；战役内触发单挑时优先结算单挑，单挑结束后可再次点击结算战役。
- 调试：按 **F9** 可在任意界面强制显示/隐藏面板（也可在配置文件设 `[Debug] ForceShowPanel = true`）。

## 安装

1. 确保游戏已安装 **BepInEx 6（Mono, x86）**。已能运行其他 BepInEx 插件（如 DiceMaster）则可跳过此步。
   - 下载 [BepInEx 6.0.0-be](https://github.com/BepInEx/BepInEx/releases) 的 `BepInEx-Unity.Mono-win-x86-6.0.0-be.*.zip`，解压到游戏根目录（与 `Mortal.exe` 同级）并运行一次游戏。
2. **从旧版 MortalInstantWin 升级**：先删除 `<游戏目录>\BepInEx\plugins\MortalInstantWin\` 整个文件夹。
3. 将本插件 `lom_assistant.dll` 放入：
   ```
   <游戏目录>\BepInEx\plugins\lom_assistant\lom_assistant.dll
   ```
4. 启动游戏：进入单挑/战役出现「直接结算」按钮组，进入剧情场景出现「快进对话」按钮。

## 使用

- 拖动面板空白处移动按钮组位置。
- 战斗中点击「直接胜利」立即获胜，点击「直接失败」立即按战败/认输流程结算。
- 剧情中按 **Ctrl**（或点击「一键快进」）开启自动快进：已读对话自动快进，遇未读台词/选项自动暂停，读过后自动继续；再按一次 Ctrl（或点击「停止快进」）关闭。

## 从源码构建

需要 .NET SDK，并把游戏目录传给构建（默认值已是 Steam 默认安装路径）：

```bash
dotnet build -c Release
# 非默认安装路径：
dotnet build -c Release -p:MortalPath="D:\Games\LegendOfMortal"
```

产物在 `bin/Release/lom_assistant.dll`。

## 实现说明

插件通过轮询检测当前场景中的 `Mortal.Combat.CombatManager`（单挑）、`Mortal.Battle.GameLevelManager.Instance`（战役）与 `Mortal.Story.StoryManager.Instance`（剧情）判断当前上下文，战斗与剧情功能互斥；界面用 uGUI 实现（纯静态实现、不持有常驻 MonoBehaviour，面板被销毁会自动重建），按钮/面板纹理由代码烘焙，字体取自场景中的游戏动态字体。

- 战斗结算调用游戏自身的胜负结算方法，不修改任何游戏文件、不影响存档结构。
- 对话快进复用游戏内建机制：调用 `StoryManager.SkipDialog(true)` 开启快进；每句台词显示时游戏按 `ReadStorySystem` 的已读记录刷新可快进状态，未读台词会自动停下。插件通过反射读取 `StoryManager._enableSkip` / `_skipDialog` / `_logOpen` 与 `SayDialog.GetWriter()` 判断当前状态：快进被游戏停下时（松开 Ctrl、读完未读台词等）若当前台词已读则自动恢复，对话结束超时后调用 `SkipDialog(false)` 恢复 `Time.timeScale`，进入战斗时强制关闭快进。
- 目标框架：.NET Framework 4.8（游戏为 Unity 2020.3.49f1，Mono x86）
- 仅编译期引用游戏目录下的程序集，运行时由游戏进程提供。

## 免责声明

仅供学习交流。使用本插件会降低游戏乐趣，请酌情使用；使用第三方 Mod 导致的存档或游戏问题需自行承担风险。
