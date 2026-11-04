using System;
using UnityEngine;
using UnityEngine.UI;

namespace StremioVR
{
    public class VRKeyboard : MonoBehaviour
    {
        public static VRKeyboard Instance { get; private set; }
        public InputField ActiveField { get; private set; }

        private GameObject _panel;
        private bool _isShift;
        private Font _font;
        private Text[] _keyTexts;
        private string[][] _normalKeys;
        private string[][] _shiftKeys;

        public void Initialize(Transform canvasTransform, Font font)
        {
            Instance = this;
            _font = font;

            _normalKeys = new string[][]
            {
                new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" },
                new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
                new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", ".", "/" },
                new[] { "⇧", "z", "x", "c", "v", "b", "n", "m", "@", "_", "⌫" }
            };

            _shiftKeys = new string[][]
            {
                new[] { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "+" },
                new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" },
                new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ",", "?" },
                new[] { "⇧", "Z", "X", "C", "V", "B", "N", "M", ":", "\"", "⌫" }
            };

            BuildKeyboard(canvasTransform);
            Hide();
        }

        public void Attach(InputField field)
        {
            if (field == null) return;
            ActiveField = field;
            Show();
        }

        public void Show()
        {
            if (_panel != null)
            {
                _panel.SetActive(true);
                _panel.transform.SetAsLastSibling();
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (ActiveField != null)
            {
                TouchScreenKeyboard.Open(ActiveField.text ?? "", TouchScreenKeyboardType.Default, false, false, ActiveField.contentType == InputField.ContentType.Password, false, "Enter text...");
            }
#endif
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            ActiveField = null;
        }

        private void BuildKeyboard(Transform parent)
        {
            _panel = new GameObject("VRKeyboard", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(parent, false);

            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(140, 20); // Center over main panel
            rt.sizeDelta = new Vector2(960, 310);

            var bg = _panel.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.07f, 0.14f, 0.96f);

            var keyTextList = new System.Collections.Generic.List<Text>();
            float startY = 250;
            float rowHeight = 52;
            float spacing = 6;

            for (int r = 0; r < _normalKeys.Length; r++)
            {
                string[] row = _normalKeys[r];
                float totalWidth = 920;
                float keyWidth = (totalWidth - (row.Length - 1) * spacing) / row.Length;
                float y = startY - r * (rowHeight + spacing);

                for (int c = 0; c < row.Length; c++)
                {
                    string keyChar = row[c];
                    float x = -totalWidth * 0.5f + c * (keyWidth + spacing) + keyWidth * 0.5f;

                    float kw = keyWidth;
                    if (keyChar == "⇧" || keyChar == "⌫") kw = keyWidth * 1.3f;

                    var btn = CreateKey(keyChar, x, y, kw, rowHeight, () => OnKeyPress(keyChar));
                    keyTextList.Add(btn.GetComponentInChildren<Text>());
                }
            }

            // Bottom control row: Space, Clear, Done
            float btmY = startY - 4 * (rowHeight + spacing);
            CreateKey("Clear", -360, btmY, 130, rowHeight, () => {
                if (ActiveField != null) { ActiveField.text = ""; ActiveField.onValueChanged?.Invoke(""); }
            });

            CreateKey("␣ Space", 0, btmY, 460, rowHeight, () => {
                if (ActiveField != null) { ActiveField.text += " "; ActiveField.onValueChanged?.Invoke(ActiveField.text); }
            });

            var doneBtn = CreateKey("✓ Done", 360, btmY, 150, rowHeight, () => {
                if (ActiveField != null) ActiveField.onEndEdit?.Invoke(ActiveField.text);
                Hide();
            });
            doneBtn.GetComponent<Image>().color = new Color(0.12f, 0.45f, 0.35f, 0.95f);

            _keyTexts = keyTextList.ToArray();
        }

        private GameObject CreateKey(string label, float x, float y, float width, float height, Action onClick)
        {
            var go = new GameObject("Key_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.18f, 0.15f, 0.28f, 0.95f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.onClick.AddListener(() => onClick());

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);

            var trt = textGo.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(width, height);

            var t = textGo.GetComponent<Text>();
            t.font = _font;
            t.fontSize = label.Length > 2 ? 18 : 22;
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;

            return go;
        }

        private void OnKeyPress(string key)
        {
            if (key == "⇧")
            {
                _isShift = !_isShift;
                UpdateKeyLabels();
                return;
            }

            if (ActiveField == null) return;

            if (key == "⌫")
            {
                if (!string.IsNullOrEmpty(ActiveField.text))
                {
                    ActiveField.text = ActiveField.text.Substring(0, ActiveField.text.Length - 1);
                    ActiveField.onValueChanged?.Invoke(ActiveField.text);
                }
                return;
            }

            // Normal typing
            ActiveField.text += key;
            ActiveField.onValueChanged?.Invoke(ActiveField.text);

            if (_isShift)
            {
                _isShift = false;
                UpdateKeyLabels();
            }
        }

        private void UpdateKeyLabels()
        {
            int idx = 0;
            string[][] current = _isShift ? _shiftKeys : _normalKeys;
            for (int r = 0; r < current.Length; r++)
            {
                for (int c = 0; c < current[r].Length; c++)
                {
                    if (idx < _keyTexts.Length && _keyTexts[idx] != null)
                    {
                        _keyTexts[idx].text = current[r][c];
                    }
                    idx++;
                }
            }
        }
    }
}
