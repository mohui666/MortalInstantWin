using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalInstantWin
{
    /// <summary>
    /// 游戏风格的可拖动结算面板：运行时从当前场景克隆游戏自带的 Button
    /// （底图、字体、颜色与游戏 UI 一致），挂到新建的 Overlay Canvas 上，
    /// 拖动面板背景移动位置。
    /// </summary>
    public class GameStylePanel : MonoBehaviour
    {
        private static readonly Vector2 PanelSize = new Vector2(240f, 172f);
        private static readonly Vector2 ButtonSize = new Vector2(200f, 56f);

        // 克隆按钮时按白名单保留组件（子节点只留文本相关），其余（本地化、热键、音效脚本等）一律移除
        private static readonly HashSet<string> KeepOnRoot = new HashSet<string>
            { "RectTransform", "CanvasRenderer", "Image", "Button" };
        private static readonly HashSet<string> KeepOnChild = new HashSet<string>
            { "RectTransform", "CanvasRenderer", "Text", "Outline", "Shadow" };

        private RectTransform _panel;
        private Button _winButton;
        private Button _loseButton;
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
                SetButtonText(_winButton, scene + "：直接勝利");
                SetButtonText(_loseButton, scene + "：直接失敗");
            }
        }

        /// <summary>尝试构建面板；场景里暂时找不到可克隆的游戏按钮时返回 false，稍后重试。</summary>
        public bool TryBuild(Vector2 anchoredPos)
        {
            if (_built) return true;
            if (_failed) return false;
            var template = FindTemplateButton();
            if (template == null) return false;
            try
            {
                Build(template, anchoredPos);
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

        /// <summary>在场景里找一个带文本和底图的游戏按钮作为克隆模板，优先取当前可见的。</summary>
        private static Button FindTemplateButton()
        {
            var buttons = FindObjectsOfType<Button>(true);
            Button best = null;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                if (b.GetComponent<Image>() == null) continue;
                if (b.GetComponentInChildren<Text>(true) == null) continue;
                if (best == null || (b.gameObject.activeInHierarchy && !best.gameObject.activeInHierarchy))
                    best = b;
            }
            return best;
        }

        private void Build(Button template, Vector2 anchoredPos)
        {
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

            // 面板背景沿用模板按钮的底图样式
            var panelGo = new GameObject("MIW_Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = PanelSize;
            _panel.anchoredPosition = anchoredPos;
            var bg = panelGo.GetComponent<Image>();
            var tplImage = template.GetComponent<Image>();
            if (tplImage.sprite != null)
            {
                bg.sprite = tplImage.sprite;
                bg.type = tplImage.type;
                bg.color = tplImage.color;
            }
            else
            {
                bg.color = new Color(0f, 0f, 0f, 0.7f);
            }
            var drag = panelGo.AddComponent<PanelDragHandler>();
            drag.Panel = _panel;
            drag.Canvas = canvas;
            drag.OnMoved = delegate (Vector2 pos) { if (OnMoved != null) OnMoved(pos); };

            // 标题（字体取自模板按钮文本）
            var tplText = template.GetComponentInChildren<Text>(true);
            var title = CreateText("MIW_Title", _panel, tplText, "直接結算", 24);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -6f);
            titleRt.sizeDelta = new Vector2(PanelSize.x, 30f);

            _winButton = CloneButton(template, _panel, new Vector2(0f, -42f));
            _loseButton = CloneButton(template, _panel, new Vector2(0f, -108f));
            _winButton.onClick.AddListener(delegate { if (OnWin != null) OnWin(); });
            _loseButton.onClick.AddListener(delegate { if (OnLose != null) OnLose(); });

            SetContext(_visible, _isDuel);
        }

        private Button CloneButton(Button template, Transform parent, Vector2 anchoredPos)
        {
            var go = Instantiate(template.gameObject, parent, false);
            go.name = template.name + "_MIW";
            go.SetActive(true);
            StripComponents(go.transform);

            var btn = go.GetComponent<Button>();
            // 清掉原按钮携带的全部事件（含持久化监听），避免误触游戏逻辑
            btn.onClick = new Button.ButtonClickedEvent();
            btn.interactable = true;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = ButtonSize;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return btn;
        }

        private static void StripComponents(Transform root)
        {
            var all = root.GetComponentsInChildren<Component>(true);
            foreach (var c in all)
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                var keep = c.transform == root ? KeepOnRoot.Contains(name) : KeepOnChild.Contains(name);
                if (!keep) Destroy(c);
            }
        }

        private static void SetButtonText(Button btn, string label)
        {
            foreach (var t in btn.GetComponentsInChildren<Text>(true))
            {
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.alignment = TextAnchor.MiddleCenter;
                t.text = label;
            }
        }

        private static Text CreateText(string name, Transform parent, Text styleSource, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            if (styleSource != null)
            {
                text.font = styleSource.font;
                text.color = styleSource.color;
            }
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = content;
            return text;
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
