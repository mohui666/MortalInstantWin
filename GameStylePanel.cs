using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalInstantWin
{
    /// <summary>
    /// 游戏风格的可拖动结算面板：运行时从当前场景提取游戏 UI 的样式素材
    /// （按钮底图 Sprite 来自场景中 Selectable 的 targetGraphic，字体/颜色来自
    /// 场景中的按钮标签 Text），从零拼装出与游戏 UI 一致的面板，
    /// 挂到新建的 Overlay Canvas 上，拖动面板背景移动。
    /// </summary>
    public class GameStylePanel : MonoBehaviour
    {
        private static readonly Vector2 PanelSize = new Vector2(240f, 172f);
        private static readonly Vector2 ButtonSize = new Vector2(200f, 56f);

        private RectTransform _panel;
        private Text _titleText;
        private Text _winLabel;
        private Text _loseLabel;
        private bool _built;
        private bool _failed;
        private bool _visible = true;
        private bool _isDuel = true;

        public Action OnWin;
        public Action OnLose;
        public Action<Vector2> OnMoved;

        public bool Ready
        {
            get { return _built; }
        }

        public bool Failed
        {
            get { return _failed; }
        }

        /// <summary>设置面板可见性与当前战斗类型（单挑/战役），并刷新按钮文字。</summary>
        public void SetContext(bool visible, bool isDuel)
        {
            _visible = visible;
            _isDuel = isDuel;
            if (!_built) return;
            if (_panel.gameObject.activeSelf != visible)
                _panel.gameObject.SetActive(visible);
            if (visible)
            {
                var scene = isDuel ? "單挑" : "戰役";
                _winLabel.text = scene + "：直接勝利";
                _loseLabel.text = scene + "：直接失敗";
            }
        }

        /// <summary>尝试构建面板；场景里暂时找不到游戏 UI 样式素材时返回 false，稍后重试。</summary>
        public bool TryBuild(Vector2 anchoredPos)
        {
            if (_built) return true;
            if (_failed) return false;
            try
            {
                var style = FindStyleSources();
                if (style == null) return false;
                Build(style, anchoredPos);
            }
            catch (Exception e)
            {
                Debug.LogError("[MortalInstantWin] 构建游戏风格面板失败: " + e);
                _failed = true;
                return false;
            }
            _built = true;
            if (!_visible) _panel.gameObject.SetActive(false);
            return true;
        }

        private class StyleSources
        {
            public Sprite ButtonSprite;
            public Image.Type ButtonSpriteType;
            public Color ButtonColor;
            public ColorBlock ButtonColors;
            public Font Font;
            public Color FontColor;
            public int FontSize;
        }

        /// <summary>
        /// 在场景中寻找游戏按钮的样式素材：
        /// 底图取自带文本标签的 Selectable 的 targetGraphic（优先当前可见的），
        /// 字体取自其标签 Text（或场景中任意 Text）。
        /// </summary>
        private static StyleSources FindStyleSources()
        {
            // 第一轮：在 Selectable（Button/Toggle 等游戏按钮）中评分取材，
            // 底图 Sprite、文本标签、当前可见性加分
            StyleSources style = null;
            var bestScore = -1;
            var selectables = FindObjectsOfType<Selectable>(true);
            foreach (var s in selectables)
            {
                if (s == null) continue;
                var image = s.targetGraphic as Image;
                if (image == null) continue;
                var label = s.GetComponentInChildren<Text>(true);
                var score = (image.sprite != null ? 4 : 0) +
                            (label != null ? 2 : 0) +
                            (s.gameObject.activeInHierarchy ? 1 : 0);
                if (score <= bestScore) continue;
                bestScore = score;
                style = new StyleSources
                {
                    ButtonSprite = image.sprite,
                    ButtonSpriteType = image.type,
                    ButtonColor = image.color,
                    ButtonColors = s.colors,
                    Font = label != null ? label.font : null,
                    FontColor = label != null ? label.color : Color.white,
                    FontSize = label != null ? label.fontSize : 0
                };
            }
            if (style == null)
            {
                // 场景里没有任何可取材的按钮，先用默认值，靠后续保底填充
                style = new StyleSources
                {
                    ButtonColors = ColorBlock.defaultColorBlock,
                    FontColor = Color.white
                };
            }

            // 第二轮：缺底图时从场景任意 Image 借（优先挂在 Selectable 下、当前可见的）
            if (style.ButtonSprite == null)
            {
                Image best = null;
                var images = FindObjectsOfType<Image>(true);
                foreach (var img in images)
                {
                    if (img == null || img.sprite == null) continue;
                    if (best == null ||
                        (img.GetComponentInParent<Selectable>() != null && best.GetComponentInParent<Selectable>() == null) ||
                        (img.gameObject.activeInHierarchy && !best.gameObject.activeInHierarchy))
                    {
                        best = img;
                    }
                }
                if (best != null)
                {
                    style.ButtonSprite = best.sprite;
                    style.ButtonSpriteType = best.type;
                    style.ButtonColor = best.color;
                }
            }

            // 第三轮：缺字体时从场景任意 Text 借（优先挂在 Selectable 下的标签）
            if (style.Font == null)
            {
                Text best = null;
                var texts = FindObjectsOfType<Text>(true);
                foreach (var t in texts)
                {
                    if (t == null || t.font == null) continue;
                    if (best == null ||
                        (t.GetComponentInParent<Selectable>() != null && best.GetComponentInParent<Selectable>() == null) ||
                        (t.gameObject.activeInHierarchy && !best.gameObject.activeInHierarchy))
                    {
                        best = t;
                    }
                }
                if (best != null)
                {
                    style.Font = best.font;
                    style.FontColor = best.color;
                    style.FontSize = best.fontSize;
                }
            }
            if (style.Font == null)
                style.Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (style.FontSize <= 0) style.FontSize = 26;
            return style;
        }

        private void Build(StyleSources style, Vector2 anchoredPos)
        {
            Debug.Log("[MortalInstantWin] 样式取材：底图=" +
                      (style.ButtonSprite != null ? style.ButtonSprite.name : "(纯色)") +
                      "，字体=" + (style.Font != null ? style.Font.name : "(默认)"));
            var canvasGo = new GameObject("MIW_Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            // 面板：背景沿用游戏按钮底图
            var panelGo = new GameObject("MIW_Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = PanelSize;
            _panel.anchoredPosition = anchoredPos;
            ApplySprite(panelGo.GetComponent<Image>(), style);
            var drag = panelGo.AddComponent<PanelDragHandler>();
            drag.Panel = _panel;
            drag.Canvas = canvas;
            drag.OnMoved = delegate (Vector2 pos) { if (OnMoved != null) OnMoved(pos); };

            // 标题
            _titleText = CreateLabel("MIW_Title", _panel, style, "直接結算", 24);
            Place(_titleText, new Vector2(0f, -6f), new Vector2(PanelSize.x, 30f));

            // 两个游戏风格按钮
            var winButton = CreateButton("MIW_WinButton", _panel, style, new Vector2(0f, -42f));
            var loseButton = CreateButton("MIW_LoseButton", _panel, style, new Vector2(0f, -108f));
            _winLabel = winButton.GetComponentInChildren<Text>();
            _loseLabel = loseButton.GetComponentInChildren<Text>();
            winButton.onClick.AddListener(delegate { if (OnWin != null) OnWin(); });
            loseButton.onClick.AddListener(delegate { if (OnLose != null) OnLose(); });

            SetContext(_visible, _isDuel);
        }

        private static void ApplySprite(Image image, StyleSources style)
        {
            image.sprite = style.ButtonSprite;
            image.type = style.ButtonSpriteType;
            image.color = style.ButtonColor;
        }

        private static Button CreateButton(string name, Transform parent, StyleSources style, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            ApplySprite(go.GetComponent<Image>(), style);
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.colors = style.ButtonColors;
            button.targetGraphic = go.GetComponent<Image>();
            button.interactable = true;

            var label = CreateLabel(name + "_Text", go.transform, style, "", Mathf.Clamp(style.FontSize, 20, 28));
            Place(label, Vector2.zero, ButtonSize);
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

        private static Text CreateLabel(string name, Transform parent, StyleSources style, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = style.Font;
            text.color = style.FontColor;
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

        /// <summary>战斗场景一般已有 EventSystem；万一没有则按游戏所用的新输入系统补一个。</summary>
        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("MIW_EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            module.AssignDefaultActions();
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
