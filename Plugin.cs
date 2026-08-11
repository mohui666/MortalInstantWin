using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using UnityEngine;
using BattleManager = Mortal.Battle.GameLevelManager;
using BattleGameOverType = Mortal.Battle.GameOverType;
using DuelManager = Mortal.Combat.CombatManager;

namespace MortalInstantWin
{
    /// <summary>
    /// 活侠传战斗直接胜利/失败补丁。
    /// 进入单挑（Mortal.Combat）或战役（Mortal.Battle）后，屏幕上出现一组
    /// 游戏 UI 风格的可拖动按钮（克隆自游戏自带按钮），点击按正常胜负流程结算。
    /// 万一克隆不到游戏按钮，退回 IMGUI 灰框兜底。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.mohui666.mortalinstantwin";
        public const string NAME = "MortalInstantWin";
        public const string VERSION = "1.2.0";

        private const float PollInterval = 0.25f;
        private const int WindowId = 0x4D5749; // "MWI"，仅 IMGUI 兜底使用

        // CombatManager._combatLevel 为私有字段，异步加载完成前触发 GameOver 会空引用
        private static readonly FieldInfo CombatLevelField =
            typeof(DuelManager).GetField("_combatLevel", BindingFlags.NonPublic | BindingFlags.Instance);

        private ConfigEntry<float> _panelX;
        private ConfigEntry<float> _panelY;

        private DuelManager _duel;
        private BattleManager _battle;
        private bool _duelEnded;
        private float _nextPollTime;

        private GameStylePanel _overlay;
        private Rect _fallbackRect = new Rect(20f, 200f, 170f, 126f);

        private void Awake()
        {
            _panelX = Config.Bind("General", "PanelX", -720f, "面板位置 X（以屏幕中心为原点的坐标）");
            _panelY = Config.Bind("General", "PanelY", 0f, "面板位置 Y（以屏幕中心为原点的坐标）");

            var go = new GameObject("MortalInstantWinOverlay");
            DontDestroyOnLoad(go);
            _overlay = go.AddComponent<GameStylePanel>();
            _overlay.OnWin = delegate { TriggerSettle(true); };
            _overlay.OnLose = delegate { TriggerSettle(false); };
            _overlay.OnMoved = delegate (Vector2 pos)
            {
                _panelX.Value = pos.x;
                _panelY.Value = pos.y;
            };

            Logger.LogInfo("MortalInstantWin 已加载：进入单挑或战役后会出现可拖动的【直接胜利/失败】按钮。");
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextPollTime)
            {
                _nextPollTime = Time.unscaledTime + PollInterval;

                _duel = FindDuelManager();
                if (_duel == null) _duelEnded = false;
                _battle = FindBattleManager();

                if (ContextActive && !_overlay.Failed)
                {
                    if (!_overlay.Ready &&
                        _overlay.TryBuild(new Vector2(_panelX.Value, _panelY.Value)))
                    {
                        Logger.LogInfo("已克隆游戏内按钮样式，游戏风格面板构建完成。");
                    }
                    if (_overlay.Ready)
                        _overlay.SetContext(true, DuelAvailable);
                }
                else if (_overlay.Ready)
                {
                    _overlay.SetContext(false, DuelAvailable);
                }
            }

            // 战斗中游戏可能隐藏并锁定鼠标，此处恢复以便点击按钮
            if (ContextActive)
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

        private bool DuelAvailable
        {
            get { return _duel != null && !_duelEnded; }
        }

        private bool BattleAvailable
        {
            get { return _battle != null && !_battle.IsGameOver; }
        }

        private bool ContextActive
        {
            get { return DuelAvailable || BattleAvailable; }
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

        // ---- IMGUI 兜底：克隆不到游戏按钮时使用 ----

        private void OnGUI()
        {
            if (!ContextActive || _overlay.Ready) return;
            _fallbackRect.position = GUI.Window(WindowId, _fallbackRect, DrawFallbackWindow, "活侠传 · 直接结算").position;
        }

        private void DrawFallbackWindow(int id)
        {
            var scene = DuelAvailable ? "单挑" : "战役";
            var width = _fallbackRect.width - 20f;
            if (GUI.Button(new Rect(10f, 26f, width, 40f), scene + "：直接胜利"))
                TriggerSettle(true);
            if (GUI.Button(new Rect(10f, 74f, width, 40f), scene + "：直接失败"))
                TriggerSettle(false);
            GUI.DragWindow(new Rect(0f, 0f, _fallbackRect.width, 22f));
        }
    }
}
