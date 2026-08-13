using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace lom_assistant
{
    /// <summary>
    /// 游戏风格的可拖动面板（静态实现，不持有常驻 MonoBehaviour，
    /// 避免 BepInEx 环境下组件被销毁导致的假空引用；面板若被销毁会自动重建）。
    ///
    /// 外观按《活侠传》官方水墨 UI 风格手工设计：深色半透明底、细白边框、
    /// 行书字体（字体取自场景中游戏自带文本，取不到则用系统默认字体），
    /// 边框纹理由代码烘焙 9-slice 精灵，不依赖场景模板。
    ///
    /// 面板有两种模式：战斗（直接胜利/失败两个按钮）与剧情（一键快进开关按钮）。
    /// </summary>
    public static class GameStylePanel
    {
        public enum Mode
        {
            Combat,
            Story
        }

        private static readonly Vector2 PanelSize = new Vector2(240f, 176f);
        private static readonly Vector2 StoryPanelSize = new Vector2(240f, 110f);
        private static readonly Vector2 ButtonSize = new Vector2(200f, 56f);
        private static readonly Color TextColor = new Color(0.95f, 0.94f, 0.90f);

        private static RectTransform _panel;
        private static Text _title;
        private static Button _winButton;
        private static Button _loseButton;
        private static Button _skipButton;
        private static Text _winLabel;
        private static Text _loseLabel;
        private static Text _skipLabel;
        private static bool _built;
        private static bool _failed;
        private static bool _visible = true;
        private static Mode _mode = Mode.Combat;
        private static bool _isDuel = true;

        public static Action OnWin;
        public static Action OnLose;
        public static Action OnSkipRead;
        public static Action<Vector2> OnMoved;
        public static Action<string> Log;

        public static bool Ready
        {
            get { return _built && _panel != null; }
        }

        public static bool Failed
        {
            get { return _failed; }
        }

        private static void LogInfo(string message)
        {
            if (Log != null) Log(message);
        }

        /// <summary>设置面板可见性与当前模式（战斗/剧情、单挑/战役），并刷新布局与按钮文字。</summary>
        public static void SetContext(bool visible, Mode mode, bool isDuel)
        {
            _visible = visible;
            _mode = mode;
            _isDuel = isDuel;
            // 面板在运行中被销毁时，重置为未构建，等待下次 TryBuild 重建
            if (_built && _panel == null)
            {
                LogInfo("检测到面板已被销毁，将尝试重建。");
                _built = false;
                return;
            }
            if (!_built) return;
            if (_panel.gameObject.activeSelf != visible)
                _panel.gameObject.SetActive(visible);
            if (visible)
                ApplyMode();
        }

        /// <summary>剧情模式下刷新快进开关文字：开启中显示「停止快进」，关闭时显示「一键快进」。</summary>
        public static void SetSkipState(bool skipping)
        {
            if (!Ready) return;
            _skipLabel.text = skipping ? "停止快进" : "一键快进";
        }

        /// <summary>按当前模式切换布局：战斗显示直接胜利/失败，剧情显示一键快进。</summary>
        private static void ApplyMode()
        {
            var story = _mode == Mode.Story;
            _panel.sizeDelta = story ? StoryPanelSize : PanelSize;
            _title.text = story ? "快进对话" : "直接结算";
            _winButton.gameObject.SetActive(!story);
            _loseButton.gameObject.SetActive(!story);
            _skipButton.gameObject.SetActive(story);
            if (!story)
            {
                var scene = _isDuel ? "单挑" : "战役";
                _winLabel.text = scene + "：直接胜利";
                _loseLabel.text = scene + "：直接失败";
            }
        }

        /// <summary>尝试构建面板；失败仅记录日志并置 Failed，由调用方退回兜底界面。</summary>
        public static bool TryBuild(Vector2 anchoredPos)
        {
            if (_built && _panel == null) _built = false;
            if (_built) return true;
            if (_failed) return false;
            try
            {
                Build(anchoredPos);
            }
            catch (Exception e)
            {
                LogInfo("构建游戏风格面板失败: " + e);
                _failed = true;
                return false;
            }
            _built = true;
            if (!_visible) _panel.gameObject.SetActive(false);
            return true;
        }

        /// <summary>取游戏中显示过中文的动态字体（烘焙字体缺字会渲染空白，一律不用），取不到退回内置动态字体。</summary>
        private static Font FindGameFont()
        {
            Text best = null;
            var bestScore = -1;
            var candidates = new System.Text.StringBuilder();
            var texts = UnityEngine.Object.FindObjectsOfType<Text>(true);
            foreach (var t in texts)
            {
                if (t == null || t.font == null) continue;
                var name = t.font.name;
                if (candidates.Length < 240)
                    candidates.Append(name).Append(t.font.dynamic ? "(动态) " : "(烘焙) ");
                if (!t.font.dynamic) continue;                             // 烘焙字体可能缺字，禁用
                if (name.Contains("Arial") || name.Contains("LegacyRuntime")) continue;
                var score = 0;
                if (ContainsCjk(t.text)) score += 4;                       // 显示中文 → 基本就是游戏字体
                if (name.Contains("Xing") || name.Contains("Kai") ||
                    name.Contains("Han") || name.Contains("Song") ||
                    name.Contains("DF") || name.Contains("Noto")) score += 2; // 书法/宋体类字体名
                if (t.gameObject.activeInHierarchy) score += 1;
                if (score <= bestScore) continue;
                bestScore = score;
                best = t;
            }
            LogInfo("字体候选：" + (candidates.Length > 0 ? candidates.ToString() : "无"));
            if (best != null)
            {
                LogInfo("使用游戏字体：" + best.font.name);
                return best.font;
            }
            // Unity 2020 内置字体叫 Arial.ttf，2022+ 叫 LegacyRuntime.ttf，逐级兜底
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (fallback == null) fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (fallback == null)
            {
                // 最后手段：场景里任意可用字体
                foreach (var t in texts)
                    if (t != null && t.font != null) { fallback = t.font; break; }
            }
            LogInfo("未找到游戏字体，使用兜底字体：" + (fallback != null ? fallback.name : "null"));
            return fallback;
        }

        private static bool ContainsCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (c >= '一' && c <= '鿿') return true;
            return false;
        }

        /// <summary>烘焙水墨风格圆角边框精灵：深色半透明填充 + 柔和细边框，9-slice 可拉伸。</summary>
        private static Sprite CreateInkSprite()
        {
            const int size = 32;
            const int border = 1;
            const float radius = 6f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var fill = new Color(0f, 0f, 0f, 0.62f);
            var edge = new Color(1f, 1f, 1f, 0.5f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // 圆角：角部像素按到圆心距离判定
                    var cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                    var cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                    var dx = x - cx;
                    var dy = y - cy;
                    var dist = dx * dx + dy * dy;
                    if (dist > radius * radius)
                    {
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }
                    var isBorder = x < border || y < border || x >= size - border || y >= size - border ||
                                   dist > (radius - border) * (radius - border);
                    tex.SetPixel(x, y, isBorder ? edge : fill);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
        }

        private static void Build(Vector2 anchoredPos)
        {
            var font = FindGameFont();
            var inkSprite = CreateInkSprite();

            var rootGo = new GameObject("LOMA_OverlayRoot");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);

            var canvasGo = new GameObject("LOMA_Canvas");
            canvasGo.transform.SetParent(rootGo.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            // 面板
            var panelGo = new GameObject("LOMA_Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = PanelSize;
            _panel.anchoredPosition = anchoredPos;
            var panelImage = panelGo.GetComponent<Image>();
            panelImage.sprite = inkSprite;
            panelImage.type = Image.Type.Sliced;
            var drag = panelGo.AddComponent<PanelDragHandler>();
            drag.Panel = _panel;
            drag.Canvas = canvas;
            drag.OnMoved = delegate (Vector2 pos) { if (OnMoved != null) OnMoved(pos); };

            // 标题
            _title = CreateLabel("LOMA_Title", _panel, font, "直接结算", 23);
            Place(_title, new Vector2(0f, -8f), new Vector2(PanelSize.x, 30f));

            // 战斗按钮
            _winButton = CreateButton("LOMA_WinButton", _panel, inkSprite, font, new Vector2(0f, -44f));
            _loseButton = CreateButton("LOMA_LoseButton", _panel, inkSprite, font, new Vector2(0f, -110f));
            _winLabel = _winButton.GetComponentInChildren<Text>();
            _loseLabel = _loseButton.GetComponentInChildren<Text>();
            _winButton.onClick.AddListener(delegate { if (OnWin != null) OnWin(); });
            _loseButton.onClick.AddListener(delegate { if (OnLose != null) OnLose(); });

            // 剧情按钮（位置同胜利按钮，剧情模式下只显示它）
            _skipButton = CreateButton("LOMA_SkipButton", _panel, inkSprite, font, new Vector2(0f, -44f));
            _skipLabel = _skipButton.GetComponentInChildren<Text>();
            _skipLabel.text = "一键快进";
            _skipButton.onClick.AddListener(delegate { if (OnSkipRead != null) OnSkipRead(); });

            SetContext(_visible, _mode, _isDuel);
        }

        private static Button CreateButton(string name, Transform parent, Sprite sprite, Font font, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;

            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            button.targetGraphic = image;
            button.interactable = true;

            var label = CreateLabel(name + "_Text", go.transform, font, "", 26);
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = ButtonSize;
            return button;
        }

        private static Text CreateLabel(string name, Transform parent, Font font, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.color = TextColor;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            return text;
        }

        private static void Place(Graphic graphic, Vector2 anchoredPos, Vector2 size)
        {
            var rt = (RectTransform)graphic.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        /// <summary>战斗场景一般已有 EventSystem；万一没有则尝试补一个，失败也不影响面板显示。</summary>
        private static void EnsureEventSystem()
        {
            try
            {
                if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
                var go = new GameObject("LOMA_EventSystem");
                go.AddComponent<EventSystem>();
                try
                {
                    // 游戏使用新输入系统
                    var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                    module.AssignDefaultActions();
                }
                catch (Exception e)
                {
                    LogInfo("InputSystemUIInputModule 创建失败，退回 StandaloneInputModule: " + e.Message);
                    var old = go.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                    if (old != null) UnityEngine.Object.Destroy(old);
                    go.AddComponent<StandaloneInputModule>();
                }
            }
            catch (Exception e)
            {
                LogInfo("EventSystem 创建失败（战斗场景通常已有，可忽略）: " + e.Message);
            }
        }

        private class PanelDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Panel;
            public Canvas Canvas;
            public Action<Vector2> OnMoved;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Panel == null || Canvas == null) return;
                Panel.anchoredPosition += eventData.delta / Canvas.scaleFactor;
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (Panel != null && OnMoved != null) OnMoved(Panel.anchoredPosition);
            }
        }
    }
}
