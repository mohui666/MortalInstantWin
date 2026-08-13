using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using Fungus;
using UnityEngine;
using BattleManager = Mortal.Battle.GameLevelManager;
using BattleGameOverType = Mortal.Battle.GameOverType;
using DuelManager = Mortal.Combat.CombatManager;
using StoryManager = Mortal.Story.StoryManager;

namespace lom_assistant
{
    /// <summary>
    /// 活侠传助手：战斗直接胜利/失败 + 剧情自动快进（一键快进已读对话）。
    /// 进入单挑（Mortal.Combat）或战役（Mortal.Battle）后，屏幕上出现一组
    /// 水墨风格的可拖动按钮（按游戏官方 UI 风格手工设计），点击按正常胜负流程结算；
    /// 进入剧情场景后显示「一键快进」开关（或按 Ctrl 开关）：打开后调用游戏自身的
    /// 快进流程自动推进已读对话，遇到未读台词自动暂停，读过后自动继续。
    /// 两种功能互斥：战斗上下文中自动快进失效，剧情上下文中不显示战斗按钮。
    /// 万一风格面板构建失败，退回 IMGUI 灰框兜底。按 F9 可在任意界面强制显示/隐藏面板。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.mohui666.lomassistant";
        public const string NAME = "lom_assistant";
        public const string VERSION = "2.0.0";

        private const float PollInterval = 0.25f;
        private const int WindowId = 0x4C4F4D; // "LOM"，仅 IMGUI 兜底使用

        // 对话结束/中断后若快进仍在继续，等待该时长后恢复正常速度，避免 10 倍速泄漏到剧情外
        private const float SkipIdleTimeout = 2f;

        // CombatManager._combatLevel 为私有字段，异步加载完成前触发 GameOver 会空引用
        private static readonly FieldInfo CombatLevelField =
            typeof(DuelManager).GetField("_combatLevel", BindingFlags.NonPublic | BindingFlags.Instance);

        // StoryManager 的快进相关状态是私有字段。其中 _enableSkip 由游戏在显示每句台词时
        // 维护（已读，或设置中允许快进未读时为 true），正是“只快进已读内容”的判定来源；
        // _skipDialog 为快进是否进行中；_logOpen 为对话记录是否打开（此时游戏禁止快进）
        private static readonly FieldInfo EnableSkipField =
            typeof(StoryManager).GetField("_enableSkip", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SkipDialogField =
            typeof(StoryManager).GetField("_skipDialog", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LogOpenField =
            typeof(StoryManager).GetField("_logOpen", BindingFlags.NonPublic | BindingFlags.Instance);

        // SayDialog.GetWriter() 是 protected，用于判断台词是否正在输出/等待点击
        private static readonly MethodInfo GetWriterMethod =
            typeof(SayDialog).GetMethod("GetWriter", BindingFlags.NonPublic | BindingFlags.Instance);

        private ConfigEntry<float> _panelX;
        private ConfigEntry<float> _panelY;
        private ConfigEntry<bool> _debugConfigShow;
        private ConfigEntry<bool> _ctrlToggle;

        private DuelManager _duel;
        private BattleManager _battle;
        private StoryManager _story;
        private bool _duelEnded;
        private bool _skipActive;
        private float _skipIdleSince = -1f;
        private float _nextPollTime;
        private Rect _fallbackRect = new Rect(20f, 200f, 170f, 126f);
        private bool _debugForceShow;
        private bool _wasContextActive;

        private void Awake()
        {
            _panelX = Config.Bind("General", "PanelX", -720f, "面板位置 X（以屏幕中心为原点的坐标）");
            _panelY = Config.Bind("General", "PanelY", 0f, "面板位置 Y（以屏幕中心为原点的坐标）");
            _debugConfigShow = Config.Bind("Debug", "ForceShowPanel", false, "调试：任意界面强制显示面板");
            _ctrlToggle = Config.Bind("Story", "CtrlToggleFastForward", true,
                "剧情场景中按 Ctrl 开关自动快进（设为 false 恢复游戏默认的按住 Ctrl 快进）");

            GameStylePanel.Log = delegate (string msg) { Logger.LogInfo(msg); };
            GameStylePanel.OnWin = delegate { TriggerSettle(true); };
            GameStylePanel.OnLose = delegate { TriggerSettle(false); };
            GameStylePanel.OnSkipRead = delegate { TriggerSkipRead(); };
            GameStylePanel.OnMoved = delegate (Vector2 pos)
            {
                _panelX.Value = pos.x;
                _panelY.Value = pos.y;
            };

            if (!SkipReflectionOk)
                Logger.LogWarning("自动快进所需的反射目标缺失（游戏版本可能已更新），该功能不可用。");

            Logger.LogInfo("lom_assistant 已加载：单挑/战役出现【直接胜利/失败】按钮，剧情场景出现【一键快进】按钮（或按 Ctrl 开关自动快进），按 F9 可强制显示/隐藏面板。");
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;

            // F9：任意界面强制显示面板（调试/预览用）
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
            {
                _debugForceShow = !_debugForceShow;
                Logger.LogInfo("F9 调试：强制显示面板 = " + _debugForceShow);
            }

            // Ctrl：剧情场景中开关自动快进（与面板「一键快进」按钮等效；战斗上下文中不响应）
            if (_ctrlToggle.Value && StoryExclusive && keyboard != null &&
                (keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame))
            {
                TriggerSkipRead();
            }

            if (Time.unscaledTime >= _nextPollTime)
            {
                _nextPollTime = Time.unscaledTime + PollInterval;

                _duel = FindDuelManager();
                if (_duel == null) _duelEnded = false;
                _battle = FindBattleManager();
                _story = FindStoryManager();

                UpdateSkipState();

                var active = ContextActive;
                if (active && !_wasContextActive)
                    Logger.LogInfo("检测到" + ContextName + "，准备显示面板。");
                _wasContextActive = active;

                var show = active || _debugForceShow || _debugConfigShow.Value;
                if (show && !GameStylePanel.Failed)
                {
                    if (!GameStylePanel.Ready &&
                        GameStylePanel.TryBuild(new Vector2(_panelX.Value, _panelY.Value)))
                    {
                        Logger.LogInfo("游戏风格面板构建完成。");
                    }
                    if (GameStylePanel.Ready)
                    {
                        if (StoryExclusive)
                        {
                            GameStylePanel.SetContext(true, GameStylePanel.Mode.Story, true);
                            GameStylePanel.SetSkipState(_skipActive);
                        }
                        else
                        {
                            GameStylePanel.SetContext(true, GameStylePanel.Mode.Combat, DuelAvailable || !BattleAvailable);
                        }
                    }
                }
                else if (GameStylePanel.Ready)
                {
                    GameStylePanel.SetContext(false, GameStylePanel.Mode.Combat, DuelAvailable);
                }
            }

            // 战斗中游戏可能隐藏并锁定鼠标，此处恢复以便点击按钮
            if (ContextActive || _debugForceShow || _debugConfigShow.Value)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private static DuelManager FindDuelManager()
        {
            var mgr = Object.FindObjectOfType<DuelManager>();
            if (mgr == null || !mgr.gameObject.activeInHierarchy) return null;
            if (CombatLevelField == null || CombatLevelField.GetValue(mgr) == null) return null;
            return mgr;
        }

        private static BattleManager FindBattleManager()
        {
            var mgr = BattleManager.Instance;
            if (mgr == null || !mgr.gameObject.activeInHierarchy) return null;
            return mgr;
        }

        private static StoryManager FindStoryManager()
        {
            var mgr = StoryManager.Instance;
            if (mgr == null || !mgr.gameObject.activeInHierarchy) return null;
            return mgr;
        }

        private bool DuelAvailable
        {
            get { return _duel != null && !_duelEnded; }
        }

        private bool BattleAvailable
        {
            get { return _battle != null && !_battle.IsGameOver; }
        }

        private bool StoryAvailable
        {
            get { return _story != null; }
        }

        /// <summary>剧情独占上下文（在剧情且不在战斗）：只有此时才允许自动快进，与战斗功能彻底隔离。</summary>
        private bool StoryExclusive
        {
            get { return StoryAvailable && !DuelAvailable && !BattleAvailable; }
        }

        private bool ContextActive
        {
            get { return DuelAvailable || BattleAvailable || StoryAvailable; }
        }

        private string ContextName
        {
            get { return DuelAvailable ? "单挑" : (BattleAvailable ? "战役" : "剧情对话"); }
        }

        private void TriggerSettle(bool win)
        {
            if (DuelAvailable)
            {
                // 与游戏中一方战败时的正常流程一致：GameOver(win) 会应用对应结算并进入后续剧情
                _duelEnded = true;
                Logger.LogInfo(win ? "触发单挑直接胜利。" : "触发单挑直接失败。");
                _duel.StartCoroutine(_duel.GameOver(win));
            }
            else if (BattleAvailable)
            {
                // 与游戏内置按钮一致：胜利=暂停面板的测试 Win，失败=暂停面板的认输
                Logger.LogInfo(win ? "触发战役直接胜利。" : "触发战役直接失败。");
                Time.timeScale = 1f;
                _battle.ShowGameOver(win ? BattleGameOverType.FriendWin : BattleGameOverType.EnemyWin, true);
            }
        }

        // ---- 自动快进已读对话 ----

        /// <summary>自动快进所需的反射目标是否齐全，缺失时整体禁用该功能。</summary>
        private static bool SkipReflectionOk
        {
            get
            {
                return EnableSkipField != null && SkipDialogField != null &&
                       LogOpenField != null && GetWriterMethod != null;
            }
        }

        /// <summary>台词是否正在显示/等待点击，或有选项菜单弹出。</summary>
        private static bool DialogActive
        {
            get
            {
                var dialog = SayDialog.ActiveSayDialog;
                if (dialog != null && GetWriterMethod != null)
                {
                    var writer = GetWriterMethod.Invoke(dialog, null) as Writer;
                    if (writer != null && (writer.IsWriting || writer.IsWaitingForInput)) return true;
                }
                var menu = MenuDialog.ActiveMenuDialog;
                return menu != null && menu.gameObject.activeInHierarchy;
            }
        }

        /// <summary>当前是否满足快进条件：与游戏按住快进键的判定一致（当前台词已读才快进）。</summary>
        private bool CanEngageSkip()
        {
            // 战斗上下文中绝不快进：防止 StoryManager 驻留时把 10 倍速带进战斗
            if (!StoryExclusive || !SkipReflectionOk) return false;
            if (!_story.EnableAction || _story.IsStoryPause) return false;
            if ((bool)LogOpenField.GetValue(_story)) return false;
            // _enableSkip：当前台词已读（或设置允许快进未读）时为 true，未读台词不快进
            if (!(bool)EnableSkipField.GetValue(_story)) return false;
            return DialogActive;
        }

        /// <summary>开关自动快进：打开后已读对话自动快进，遇未读自动暂停；再次触发关闭。</summary>
        private void TriggerSkipRead()
        {
            if (!StoryExclusive) return;
            if (!SkipReflectionOk)
            {
                Logger.LogWarning("自动快进所需的反射目标缺失（游戏版本可能已更新），功能不可用。");
                return;
            }
            if (_skipActive)
            {
                _skipActive = false;
                // 游戏已自行停止时不再调用，避免打乱时间流速（如菜单暂停中）
                if ((bool)SkipDialogField.GetValue(_story))
                    _story.SkipDialog(false);
                Logger.LogInfo("自动快进已关闭。");
                return;
            }
            _skipActive = true;
            _skipIdleSince = -1f;
            if (CanEngageSkip())
            {
                // 与按住游戏快进键完全相同的流程：10 倍速自动推进，遇未读台词游戏会自动停下
                _story.SkipDialog(true);
                Logger.LogInfo("自动快进已开启：开始快进已读对话。");
            }
            else
            {
                Logger.LogInfo("自动快进已开启：出现已读对话时自动快进，遇未读台词自动暂停。");
            }
        }

        /// <summary>自动快进的看护：进入战斗立即关闭；游戏停下快进时按条件恢复；对话结束后恢复正常速度。</summary>
        private void UpdateSkipState()
        {
            if (!_skipActive) return;
            if (_story == null || !_story.gameObject.activeInHierarchy)
            {
                // 剧情场景已卸载，StoryManager.OnDestroy 会把 Time.timeScale 恢复为 1
                _skipActive = false;
                return;
            }
            if (DuelAvailable || BattleAvailable)
            {
                // 保险：剧情直接进入战斗而 StoryManager 未卸载时，立即关闭快进防止 10 倍速进战斗
                _skipActive = false;
                if ((bool)SkipDialogField.GetValue(_story))
                    _story.SkipDialog(false);
                Logger.LogInfo("进入战斗，自动快进已关闭。");
                return;
            }
            if ((bool)SkipDialogField.GetValue(_story))
            {
                if (DialogActive)
                {
                    _skipIdleSince = -1f;
                    return;
                }
                // 对话已结束而快进仍在继续：恢复正常速度（开关保持开启，下段对话再自动快进）
                if (_skipIdleSince < 0f)
                {
                    _skipIdleSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _skipIdleSince >= SkipIdleTimeout)
                {
                    _story.SkipDialog(false);
                    _skipIdleSince = -1f;
                    Logger.LogInfo("对话已结束，恢复正常速度（自动快进保持开启）。");
                }
                return;
            }
            // 游戏停下了快进（松开 Ctrl、遇到未读台词、开关面板等）：
            // 仅当当前台词已读且对话仍在进行时恢复快进；未读台词会停住等玩家阅读
            if (CanEngageSkip())
            {
                _story.SkipDialog(true);
                Logger.LogInfo("继续快进已读对话。");
            }
        }

        // ---- IMGUI 兜底：风格面板构建失败时使用 ----

        private void OnGUI()
        {
            if ((!ContextActive && !_debugForceShow) || GameStylePanel.Ready) return;
            _fallbackRect.height = StoryExclusive ? 80f : 126f;
            _fallbackRect.position = GUI.Window(WindowId, _fallbackRect, DrawFallbackWindow,
                "活侠传 · " + (StoryExclusive ? "快进对话" : "直接结算")).position;
        }

        private void DrawFallbackWindow(int id)
        {
            var width = _fallbackRect.width - 20f;
            if (StoryExclusive)
            {
                if (GUI.Button(new Rect(10f, 26f, width, 40f), _skipActive ? "停止快进" : "一键快进"))
                    TriggerSkipRead();
            }
            else
            {
                var scene = DuelAvailable ? "单挑" : "战役";
                if (GUI.Button(new Rect(10f, 26f, width, 40f), scene + "：直接胜利"))
                    TriggerSettle(true);
                if (GUI.Button(new Rect(10f, 74f, width, 40f), scene + "：直接失败"))
                    TriggerSettle(false);
            }
            GUI.DragWindow(new Rect(0f, 0f, _fallbackRect.width, 22f));
        }
    }
}
