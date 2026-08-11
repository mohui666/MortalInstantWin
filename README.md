# MortalInstantWin — 活侠传战斗直接胜利/失败补丁

《活侠传》（Legend of Mortal）的 BepInEx 插件：进入**单挑**或**战役**后，屏幕上会出现一组**游戏水墨 UI 风格**的可拖动按钮，点击「直接勝利」或「直接失敗」即按游戏自身的对应流程结算（正常发放结算、进入后续剧情）。

## 功能

- **水墨风格界面**：面板按游戏官方 UI 风格手工设计——深色半透明圆角底、柔和细边框，边框纹理由代码烘焙 9-slice 精灵；文字优先使用游戏自带的动态字体（行书/宋体，自动跳过会缺字的烘焙字体），取不到时用内置字体兜底；构建失败时退回系统灰框，功能不受影响。
- 同时支持两种战斗：
  - **单挑**（决斗系统，`Mortal.Combat`）：调用 `CombatManager.GameOver(win)`，与一方战败时的流程完全一致（应用胜/负结算 Flag、加载后续场景；若该场失败是死局 DeadEnd 则进入 GameOver 画面）。
  - **战役**（群战系统，`Mortal.Battle`）：调用 `GameLevelManager.ShowGameOver(FriendWin / EnemyWin, true)`，与游戏内置按钮相同（胜利=暂停面板测试 Win，失败=暂停面板认输），含拾取银两结算。
- 拖动面板空白处移动按钮组，位置自动保存到 `BepInEx/config/com.mohui666.mortalinstantwin.cfg`。
- 只在战斗中显示，战斗结束/离开场景后自动隐藏；战役内触发单挑时优先结算单挑，单挑结束后可再次点击结算战役。
- 调试：按 **F9** 可在任意界面强制显示/隐藏面板（也可在配置文件设 `[Debug] ForceShowPanel = true`）。

## 安装

1. 确保游戏已安装 **BepInEx 6（Mono, x86）**。已能运行其他 BepInEx 插件（如 DiceMaster）则可跳过此步。
   - 下载 [BepInEx 6.0.0-be](https://github.com/BepInEx/BepInEx/releases) 的 `BepInEx-Unity.Mono-win-x86-6.0.0-be.*.zip`，解压到游戏根目录（与 `Mortal.exe` 同级）并运行一次游戏。
2. 将本插件 `MortalInstantWin.dll` 放入：
   ```
   <游戏目录>\BepInEx\plugins\MortalInstantWin\MortalInstantWin.dll
   ```
3. 启动游戏，进入任意单挑或战役，屏幕左侧即出现「直接結算」按钮组。

## 使用

- 拖动面板空白处移动按钮组位置。
- 点击「直接勝利」立即获胜，点击「直接失敗」立即按战败/认输流程结算。

## 从源码构建

需要 .NET SDK，并把游戏目录传给构建（默认值已是 Steam 默认安装路径）：

```bash
dotnet build -c Release
# 非默认安装路径：
dotnet build -c Release -p:MortalPath="D:\Games\LegendOfMortal"
```

产物在 `bin/Release/MortalInstantWin.dll`。

## 实现说明

插件通过轮询检测当前场景中的 `Mortal.Combat.CombatManager`（单挑）与 `Mortal.Battle.GameLevelManager.Instance`（战役）判断是否在战斗中；界面用 uGUI 实现（纯静态实现、不持有常驻 MonoBehaviour，面板被销毁会自动重建），按钮/面板纹理由代码烘焙，字体取自场景中的游戏动态字体；点击后调用游戏自身的胜负结算方法，不修改任何游戏文件、不影响存档结构。

- 目标框架：.NET Framework 4.8（游戏为 Unity 2020.3.49f1，Mono x86）
- 仅编译期引用游戏目录下的程序集，运行时由游戏进程提供。

## 免责声明

仅供学习交流。使用本插件会降低游戏乐趣，请酌情使用；使用第三方 Mod 导致的存档或游戏问题需自行承担风险。
