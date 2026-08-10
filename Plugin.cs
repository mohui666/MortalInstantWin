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
    /// 活侠传战斗直接胜利补丁。
    /// 进入单挑（Mortal.Combat）或战役（Mortal.Battle）后，
    /// 屏幕上出现一个可拖动的按钮，点击即按正常胜利流程结算。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.mohui666.mortalinstantwin";
        public const string NAME = "MortalInstantWin";
        public const string VERSION = "1.0.0";

        private const float PollInterval = 0.25f;
        private const int WindowId = 0x4D5749; // "MWI"

        // CombatManager._combatLevel 为私有字段，异步加载完成前触发 GameOver 会空引用
        private static readonly FieldInfo CombatLevelField =
            typeof(DuelManager).GetField("_combatLevel", BindingFlags.NonPublic | BindingFlags.Instance);

        private ConfigEntry<float> _windowX;
        private ConfigEntry<float> _windowY;

        private DuelManager _duel;
        private BattleManager _battle;
        private bool _duelWon;
        private float _nextPollTime;
        private Rect _windowRect;
        private bool _positionChanged;

        private void Awake()
        {
            _windowX = Config.Bind("General", "WindowX", 20f, "按钮窗口左上角 X 坐标（像素）");
            _windowY = Config.Bind("General", "WindowY", 200f, "按钮窗口左上角 Y 坐标（像素）");
            _windowRect = new Rect(_windowX.Value, _windowY.Value, 170f, 82f);
            Logger.LogInfo("MortalInstantWin 已加载：进入单挑或战役后会出现可拖动的【直接胜利】按钮。");
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + PollInterval;

            _duel = FindDuelManager();
            if (_duel == null) _duelWon = false;
            _battle = FindBattleManager();

            // 拖动结束（松开鼠标）后再把窗口位置写入配置，避免拖动途中频繁写盘
            if (_positionChanged && !Input.GetMouseButton(0))
            {
                _positionChanged = false;
                _windowX.Value = _windowRect.x;
                _windowY.Value = _windowRect.y;
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
            get { return _duel != null && !_duelWon; }
        }

        private bool BattleAvailable
        {
            get { return _battle != null && !_battle.IsGameOver; }
        }

        private void OnGUI()
        {
            if (!DuelAvailable && !BattleAvailable) return;

            // 战斗/单挑中游戏可能隐藏并锁定鼠标，此处恢复以便点击
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "活侠传 · 直接胜利");
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - _windowRect.height);

            if (!Mathf.Approximately(_windowRect.x, _windowX.Value) ||
                !Mathf.Approximately(_windowRect.y, _windowY.Value))
            {
                _positionChanged = true;
            }
        }

        private void DrawWindow(int id)
        {
            var label = DuelAvailable ? "单挑：直接胜利" : "战役：直接胜利";
            if (GUI.Button(new Rect(10f, 26f, _windowRect.width - 20f, 44f), label))
            {
                TriggerWin();
            }
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 22f));
        }

        private void TriggerWin()
        {
            if (DuelAvailable)
            {
                // 与游戏中敌人战败时的正常流程一致：GameOver(true) 会应用胜利结算并进入后续剧情
                _duelWon = true;
                Logger.LogInfo("触发单挑直接胜利。");
                _duel.StartCoroutine(_duel.GameOver(true));
            }
            else if (BattleAvailable)
            {
                // 与游戏内置测试按钮（暂停面板的 Win）一致：友方胜利并完成战役
                Logger.LogInfo("触发战役直接胜利。");
                Time.timeScale = 1f;
                _battle.ShowGameOver(BattleGameOverType.FriendWin, true);
            }
        }
    }
}
