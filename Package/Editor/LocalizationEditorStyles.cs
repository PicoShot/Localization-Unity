using UnityEditor;
using UnityEngine;

namespace PicoShot.Localization.Editor
{
    public static class LocalizationEditorStyles
    {
        public const float RowHeight = 24f;

        private static bool IsDark => EditorGUIUtility.isProSkin;

        // Colors
        public static Color RowEven => IsDark ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.03f);
        public static Color RowOdd => Color.clear;
        public static Color RowHover => IsDark ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.07f);
        public static Color RowSelected => IsDark ? new Color(0.24f, 0.48f, 0.90f, 0.22f) : new Color(0.17f, 0.42f, 0.85f, 0.16f);
        public static Color Separator => IsDark ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.18f);
        public static Color Accent => IsDark ? new Color(0.30f, 0.56f, 0.95f) : new Color(0.17f, 0.42f, 0.85f);
        public static Color Success => IsDark ? new Color(0.35f, 0.72f, 0.38f) : new Color(0.20f, 0.58f, 0.25f);
        public static Color Warning => IsDark ? new Color(0.95f, 0.62f, 0.18f) : new Color(0.85f, 0.50f, 0.05f);
        public static Color Danger => IsDark ? new Color(0.92f, 0.36f, 0.33f) : new Color(0.78f, 0.20f, 0.18f);
        public static Color Track => IsDark ? new Color(0f, 0f, 0f, 0.30f) : new Color(0f, 0f, 0f, 0.12f);
        public static Color MutedText => IsDark ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);

        // Styles
        private static GUIStyle _rowLabel;
        private static GUIStyle _rowLabelBold;
        private static GUIStyle _mutedLabel;
        private static GUIStyle _mutedLabelRight;
        private static GUIStyle _badgeText;
        private static GUIStyle _badge;
        private static GUIStyle _progressText;
        private static GUIStyle _sectionTitle;
        private static GUIStyle _emptyState;
        private static GUIStyle _inheritedField;
        private static Texture2D _badgeTexture;
        private static bool _badgeTextureDark;

        public static GUIStyle RowLabel => _rowLabel ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            padding = new RectOffset(2, 2, 0, 0)
        };

        public static GUIStyle RowLabelBold => _rowLabelBold ??= new GUIStyle(RowLabel)
        {
            fontStyle = FontStyle.Bold
        };

        public static GUIStyle MutedLabel
        {
            get
            {
                _mutedLabel ??= new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
                _mutedLabel.normal.textColor = MutedText;
                return _mutedLabel;
            }
        }

        public static GUIStyle MutedLabelRight
        {
            get
            {
                _mutedLabelRight ??= new GUIStyle(MutedLabel) { alignment = TextAnchor.MiddleRight };
                _mutedLabelRight.normal.textColor = MutedText;
                return _mutedLabelRight;
            }
        }

        public static GUIStyle SectionTitle => _sectionTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft
        };

        public static GUIStyle EmptyState
        {
            get
            {
                _emptyState ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(8, 8, 8, 8)
                };
                _emptyState.normal.textColor = MutedText;
                return _emptyState;
            }
        }

        /// <summary>
        /// Object field look-alike for values inherited from a default.
        /// </summary>
        public static GUIStyle InheritedField
        {
            get
            {
                _inheritedField ??= new GUIStyle(EditorStyles.objectField)
                {
                    fontStyle = FontStyle.Italic,
                    padding = new RectOffset(4, 4, EditorStyles.objectField.padding.top, EditorStyles.objectField.padding.bottom),
                    clipping = TextClipping.Clip,
                    imagePosition = ImagePosition.TextOnly
                };
                _inheritedField.normal.textColor = MutedText;
                _inheritedField.hover.textColor = MutedText;
                _inheritedField.focused.textColor = MutedText;
                return _inheritedField;
            }
        }

        private static GUIStyle BadgeText
        {
            get
            {
                _badgeText ??= new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 9,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _badgeText.normal.textColor = Color.white;
                return _badgeText;
            }
        }

        private static GUIStyle ProgressText
        {
            get
            {
                _progressText ??= new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 2, 0, 0)
                };
                _progressText.normal.textColor = MutedText;
                return _progressText;
            }
        }

        /// <summary>
        /// Accent-tinted pill style for use with GUILayout.
        /// </summary>
        public static GUIStyle Badge
        {
            get
            {
                if (_badgeTexture == null || _badgeTextureDark != IsDark)
                {
                    if (_badgeTexture != null)
                        Object.DestroyImmediate(_badgeTexture);

                    _badgeTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _badgeTexture.SetPixel(0, 0, Accent);
                    _badgeTexture.Apply();
                    _badgeTextureDark = IsDark;
                    _badge = null;
                }

                if (_badge == null)
                {
                    _badge = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 1, 1),
                        margin = new RectOffset(2, 4, 3, 3)
                    };
                    _badge.normal.background = _badgeTexture;
                    _badge.normal.textColor = Color.white;
                }

                return _badge;
            }
        }

        /// <summary>
        /// Fills a list row background with alternating stripes and a hover highlight.
        /// </summary>
        public static void DrawRowBackground(Rect rect, int index, bool hover)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, index % 2 == 0 ? RowEven : RowOdd);
            if (hover)
                EditorGUI.DrawRect(rect, RowHover);
        }

        /// <summary>
        /// Draws a 1px horizontal separator line using layout.
        /// </summary>
        public static void DrawSeparator(float spaceBefore = 2f, float spaceAfter = 2f)
        {
            GUILayout.Space(spaceBefore);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, Separator);
            GUILayout.Space(spaceAfter);
        }

        /// <summary>
        /// Draws a section title with an optional muted caption on the right, followed by a separator.
        /// </summary>
        public static void DrawSectionTitle(string title, string caption = null)
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            GUI.Label(rect, title, SectionTitle);
            if (!string.IsNullOrEmpty(caption))
                GUI.Label(rect, caption, MutedLabelRight);
            DrawSeparator(0f, 2f);
        }

        /// <summary>
        /// Draws a list row background, highlighting it when selected.
        /// </summary>
        public static void DrawRowBackground(Rect rect, int index, bool hover, bool selected)
        {
            DrawRowBackground(rect, index, hover && !selected);
            if (selected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, RowSelected);
        }

        /// <summary>
        /// Reserves a section title row and returns the rect to the right of the title for extra controls.
        /// </summary>
        public static Rect DrawSectionTitleWithControls(string title)
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var titleContent = new GUIContent(title);
            GUI.Label(rect, titleContent, SectionTitle);
            DrawSeparator(0f, 2f);

            float titleWidth = SectionTitle.CalcSize(titleContent).x + 8f;
            return new Rect(rect.x + titleWidth, rect.y, Mathf.Max(0f, rect.width - titleWidth), rect.height);
        }

        /// <summary>
        /// Draws a small colored pill with centered text and returns its width.
        /// </summary>
        public static float DrawBadge(Rect rect, string text, Color color, string tooltip = null)
        {
            var content = new GUIContent(text, tooltip);
            float width = Mathf.Ceil(BadgeText.CalcSize(content).x) + 10f;
            var badgeRect = new Rect(rect.x, rect.center.y - 7f, width, 14f);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(badgeRect, color);
            GUI.Label(badgeRect, content, BadgeText);
            return width;
        }

        /// <summary>
        /// Measures the width a badge with this text will take.
        /// </summary>
        public static float GetBadgeWidth(string text)
        {
            return Mathf.Ceil(BadgeText.CalcSize(new GUIContent(text)).x) + 10f;
        }

        /// <summary>
        /// Draws a thin progress bar with a percentage label to its right.
        /// </summary>
        public static void DrawProgressBar(Rect rect, float progress, string tooltip)
        {
            progress = Mathf.Clamp01(progress);
            const float labelWidth = 38f;

            var barRect = new Rect(rect.x, rect.center.y - 3f, Mathf.Max(0f, rect.width - labelWidth), 6f);
            var labelRect = new Rect(rect.xMax - labelWidth, rect.y, labelWidth, rect.height);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(barRect, Track);
                if (progress > 0f)
                {
                    Color fill = progress >= 1f ? Success : progress >= 0.5f ? Accent : Warning;
                    EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * progress, barRect.height), fill);
                }
            }

            GUI.Label(labelRect, $"{Mathf.FloorToInt(progress * 100f)}%", ProgressText);

            if (!string.IsNullOrEmpty(tooltip))
                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
        }
    }
}
