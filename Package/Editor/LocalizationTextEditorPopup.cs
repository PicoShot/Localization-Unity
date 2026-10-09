using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Editor.Mcp;
using PicoShot.Localization.Rtl;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization
{
    /// <summary>
    /// Editor window for long translations (with the source text, placeholder checks and a rich-text preview)
    /// and a compact variant for entering key names with live validation.
    /// </summary>
    public class LocalizationTextEditorPopup : EditorWindow
    {
        private const string TextControlName = "LocalizationTextEditor_Text";
        private static readonly Vector2 TextWindowSize = new(560f, 420f);
        private static readonly Vector2 KeyWindowSize = new(440f, 132f);
        private const float MaxReferenceHeight = 96f;

        private static readonly Regex PlaceholderPattern = new(@"(?<!\{)\{[^{}]+\}(?!\})", RegexOptions.Compiled);
        private static readonly Regex TagPattern = new(@"</?[a-zA-Z][^<>]*>", RegexOptions.Compiled);

        private bool _isKeyName;
        private string _text = "";
        private string _initialText = "";
        private Action<string> _onSave;
        private Func<string, string> _validate;
        private string _title;
        private string _subtitle;
        private string _reference;
        private string _referenceLabel;
        private bool _rightToLeft;

        private bool _preview;
        private bool _focused;
        private bool _closing;
        private Vector2 _scroll;
        private Vector2 _referenceScroll;
        private List<string> _referenceTokens;
        private GUIStyle _previewStyle;

        private static string ActionKeyName => Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

        /// <summary>
        /// Opens the text editor, or the key name editor when <paramref name="isKeyName"/> is true.
        /// </summary>
        public static void Open(string initialText, Action<string> saveCallback, bool isKeyName = false)
        {
            if (isKeyName)
                OpenKeyName(initialText, saveCallback);
            else
                OpenText(initialText, saveCallback);
        }

        /// <summary>
        /// Opens a resizable editor for a translation.
        /// </summary>
        /// <param name="title">Usually the key name.</param>
        /// <param name="subtitle">Usually the language name.</param>
        /// <param name="referenceText">Source text shown above the editor and used for placeholder checks.</param>
        /// <param name="referenceLabel">Label for the source text, e.g. "English (source)".</param>
        /// <param name="rightToLeft">Shape and right-align the preview.</param>
        public static void OpenText(string text, Action<string> onSave, string title = null, string subtitle = null,
            string referenceText = null, string referenceLabel = null, bool rightToLeft = false)
        {
            var window = CreateInstance<LocalizationTextEditorPopup>();
            window._isKeyName = false;
            window._text = window._initialText = text ?? "";
            window._onSave = onSave;
            window._title = string.IsNullOrEmpty(title) ? "Edit Text" : title;
            window._subtitle = subtitle;
            window._reference = string.IsNullOrWhiteSpace(referenceText) ? null : referenceText;
            window._referenceLabel = referenceLabel;
            window._rightToLeft = rightToLeft;
            window._referenceTokens = window._reference != null ? ExtractTokens(window._reference) : new List<string>();
            window.titleContent = new GUIContent(subtitle != null ? $"{window._title} · {subtitle}" : window._title);
            window.minSize = new Vector2(420f, 300f);
            window.position = CenterOnEditor(TextWindowSize);
            window.ShowUtility();
        }

        /// <summary>
        /// Opens a compact editor for a key name. <paramref name="validate"/> returns an error message, or null when valid.
        /// </summary>
        public static void OpenKeyName(string name, Action<string> onSave, string title = null, Func<string, string> validate = null)
        {
            var window = CreateInstance<LocalizationTextEditorPopup>();
            window._isKeyName = true;
            window._text = window._initialText = FilterKeyName(name ?? "");
            window._onSave = onSave;
            window._validate = validate;
            window._title = string.IsNullOrEmpty(title) ? "Key Name" : title;
            window.titleContent = new GUIContent(window._title);
            window.minSize = window.maxSize = KeyWindowSize;
            window.position = CenterOnEditor(KeyWindowSize);
            window.ShowUtility();
        }

        /// <summary>
        /// Removes any character that is not an ASCII letter, digit, underscore or dot.
        /// </summary>
        public static string FilterKeyName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static Rect CenterOnEditor(Vector2 size)
        {
            var parent = focusedWindow != null ? focusedWindow.position : EditorGUIUtility.GetMainWindowPosition();
            return new Rect(parent.center.x - size.x * 0.5f, parent.center.y - size.y * 0.5f, size.x, size.y);
        }

        private bool IsDirty => _text != _initialText;

        private void OnGUI()
        {
            if (_onSave == null)
            {
                // Lost its callback after a domain reload
                _closing = true;
                Close();
                return;
            }

            HandleKeyboard(Event.current);

            if (_isKeyName)
                DrawKeyNameEditor();
            else
                DrawTextEditor();
        }

        private void OnDestroy()
        {
            if (_closing || _isKeyName || !IsDirty || _onSave == null)
                return;

            if (EditorUtility.DisplayDialog("Unsaved Text", $"Save your changes to {_title}?", "Save", "Discard"))
                _onSave.Invoke(_text);
        }

        #region Text Editor

        private void DrawTextEditor()
        {
            EditorGUILayout.Space(4);
            DrawHeader();

            if (_reference != null)
                DrawReference();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label(_reference != null ? "Translation" : "Text", Styles.MutedLabel);
                GUILayout.FlexibleSpace();
                _preview = GUILayout.Toggle(_preview, new GUIContent("Preview", "Show the text with rich-text tags applied" + (_rightToLeft ? " and RTL shaping" : "")),
                    EditorStyles.miniButton, GUILayout.Width(64));
            }
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            {
                if (_preview)
                {
                    _previewStyle ??= new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
                    _previewStyle.alignment = _rightToLeft ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                    string shown = _rightToLeft ? RtlTextHandler.Fix(_text) : _text;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
                    GUILayout.Label(shown.Length > 0 ? shown : " ", _previewStyle);
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    GUI.SetNextControlName(TextControlName);
                    _text = EditorGUILayout.TextArea(_text, Styles.TextArea, GUILayout.ExpandHeight(true));

                    if (!_focused)
                    {
                        _focused = true;
                        EditorGUI.FocusTextInControl(TextControlName);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            DrawTokens();
            DrawTextStatus();
            DrawFooter(true, $"{ActionKeyName}+Enter or {ActionKeyName}+S to save · Esc to cancel");
        }

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var content = new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height);

            var titleContent = new GUIContent(_title, _title);
            float titleWidth = Mathf.Min(Styles.SectionTitle.CalcSize(titleContent).x + 4f, content.width * 0.6f);
            GUI.Label(new Rect(content.x, content.y, titleWidth, content.height), titleContent, Styles.SectionTitle);

            float x = content.x + titleWidth + 6f;
            if (!string.IsNullOrEmpty(_subtitle))
            {
                var subtitle = new GUIContent(_subtitle);
                float w = Styles.MutedLabel.CalcSize(subtitle).x + 4f;
                GUI.Label(new Rect(x, content.y, w, content.height), subtitle, Styles.MutedLabel);
                x += w + 4f;
            }

            if (_rightToLeft)
                Styles.DrawBadge(new Rect(x, content.y, 0f, content.height), "RTL", Styles.MutedText, "Right-to-left language");

            Styles.DrawSeparator(0f, 4f);
        }

        private void DrawReference()
        {
            GUILayout.Label(_referenceLabel ?? "Source", Styles.MutedLabel);

            var style = EditorStyles.wordWrappedLabel;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // Width inside the help box, minus room for a scrollbar in case one is needed
                float width = position.width - 40f;
                float height = style.CalcHeight(new GUIContent(_reference), width);

                if (height <= MaxReferenceHeight)
                {
                    // Short source texts fit as they are; no scroll view, so no scrollbar
                    EditorGUILayout.SelectableLabel(_reference, style, GUILayout.Height(height));
                }
                else
                {
                    _referenceScroll = EditorGUILayout.BeginScrollView(_referenceScroll, false, false,
                        GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(MaxReferenceHeight));
                    EditorGUILayout.SelectableLabel(_reference, style, GUILayout.Height(height));
                    EditorGUILayout.EndScrollView();
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Placeholders and rich-text tags from the source, green when the translation has them and orange when missing.
        /// Click one to copy it.
        /// </summary>
        private void DrawTokens()
        {
            if (_referenceTokens.Count == 0)
                return;

            var present = new HashSet<string>(ExtractTokens(_text));
            float maxWidth = position.width - 12f;

            EditorGUILayout.Space(2);
            var rowRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            float x = rowRect.x + 4f;
            float y = rowRect.y;

            var labelContent = new GUIContent("From source");
            float labelWidth = Styles.MutedLabel.CalcSize(labelContent).x + 6f;
            GUI.Label(new Rect(x, y, labelWidth, 18f), labelContent, Styles.MutedLabel);
            x += labelWidth;

            foreach (var token in _referenceTokens)
            {
                float width = Styles.GetBadgeWidth(token);
                if (x + width > maxWidth)
                    break;

                bool has = present.Contains(token);
                var badgeRect = new Rect(x, y, width, 18f);
                Styles.DrawBadge(badgeRect, token, has ? Styles.Success : Styles.Warning,
                    has ? "In the translation. Click to copy." : "Missing from the translation. Click to copy.");
                if (GUI.Button(badgeRect, GUIContent.none, GUIStyle.none))
                {
                    EditorGUIUtility.systemCopyBuffer = token;
                    ShowNotification(new GUIContent($"Copied {token}"), 0.6f);
                }
                x += width + 4f;
            }
        }

        private void DrawTextStatus()
        {
            var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            var content = new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height);

            string issue = _reference != null && !string.IsNullOrWhiteSpace(_text) ? McpTextChecks.FindMismatch(_reference, _text) : null;
            int lines = _text.Length == 0 ? 0 : _text.Count(c => c == '\n') + 1;
            string stats = $"{_text.Length:N0} characters · {lines} {(lines == 1 ? "line" : "lines")}";

            var statsContent = new GUIContent(stats);
            float statsWidth = Styles.MutedLabelRight.CalcSize(statsContent).x + 4f;
            GUI.Label(new Rect(content.xMax - statsWidth, content.y, statsWidth, content.height), statsContent, Styles.MutedLabelRight);

            if (issue != null)
            {
                GUI.Label(new Rect(content.x, content.y + 1f, 16f, 16f), EditorGUIUtility.IconContent("console.warnicon.sml"));
                GUI.Label(new Rect(content.x + 17f, content.y, content.width - statsWidth - 21f, content.height),
                    new GUIContent(issue, issue), Styles.WarningLabel);
            }
        }

        private static List<string> ExtractTokens(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            foreach (Match match in PlaceholderPattern.Matches(text))
            {
                if (!tokens.Contains(match.Value))
                    tokens.Add(match.Value);
            }
            foreach (Match match in TagPattern.Matches(text))
            {
                if (!tokens.Contains(match.Value))
                    tokens.Add(match.Value);
            }
            return tokens;
        }

        #endregion

        #region Key Name Editor

        private void DrawKeyNameEditor()
        {
            EditorGUILayout.Space(6);
            GUILayout.Label(_title, Styles.SectionTitle);
            EditorGUILayout.Space(2);

            GUI.SetNextControlName(TextControlName);
            _text = FilterKeyName(EditorGUILayout.TextField(_text));
            if (!_focused)
            {
                _focused = true;
                EditorGUI.FocusTextInControl(TextControlName);
            }

            string error = Validate();
            GUILayout.Label(error ?? "Letters, digits, '_' and '.' only. Use the view delimiter to group keys.",
                error != null ? Styles.WarningLabel : Styles.MutedLabel);

            GUILayout.FlexibleSpace();
            DrawFooter(error == null, "Enter to save · Esc to cancel");
        }

        private string Validate()
        {
            if (!_isKeyName)
                return null;
            if (string.IsNullOrWhiteSpace(_text))
                return "Enter a key name.";
            return _text == _initialText ? null : _validate?.Invoke(_text);
        }

        #endregion

        #region Footer & Input

        private void DrawFooter(bool canSave, string hint)
        {
            Styles.DrawSeparator(4f, 4f);
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label(hint, Styles.MutedLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(72)))
                    Cancel(confirm: false);

                using (new EditorGUI.DisabledScope(!canSave))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(72)))
                        Save();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        /// <summary>
        /// Shortcuts are read before the text field so they work while typing.
        /// </summary>
        private void HandleKeyboard(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return;

            bool action = EditorGUI.actionKey;
            bool enter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;

            if ((action && evt.keyCode == KeyCode.S) || (enter && (action || _isKeyName)))
            {
                evt.Use();
                if (Validate() == null)
                    Save();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.Use();
                Cancel(confirm: true);
            }
        }

        private void Save()
        {
            if (Validate() != null)
                return;

            var callback = _onSave;
            string text = _text;
            _closing = true;
            Close();
            callback?.Invoke(text);
        }

        private void Cancel(bool confirm)
        {
            if (confirm && !_isKeyName && IsDirty &&
                !EditorUtility.DisplayDialog("Discard Changes", $"Discard your changes to {_title}?", "Discard", "Keep Editing"))
                return;

            _closing = true;
            Close();
        }

        #endregion
    }
}
