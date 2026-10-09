using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using PicoShot.Localization.Editor.Data;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization
{
    /// <summary>
    /// Searchable dropdown list. Closes when it loses focus, supports arrow keys and Enter,
    /// and can show a second line per item (for example a key's text).
    /// </summary>
    public class LocalizationSearchablePopup : EditorWindow
    {
        private const float RowHeight = 22f;
        private const float DetailRowHeight = 36f;
        private const float HeaderHeight = 22f;
        private const float FooterHeight = 20f;
        private const float RowPadding = 8f;
        private static readonly Vector2 DefaultSize = new(340f, 380f);

        private string[] _items;
        private string[] _details;
        private Func<int, string> _getDetail;
        private Action<int> _onItemSelected;
        private int _selectedIndex;
        private string _title;

        private SearchField _searchField;
        private string _search = "";
        private string _appliedSearch;
        private readonly List<int> _filtered = new();
        private int _highlight;
        private Vector2 _scroll;
        private bool _scrollToHighlight = true;
        private bool _focusSearch = true;
        private bool _committed;
        private float _viewHeight;

        /// <summary>
        /// Opens the list below a control. <paramref name="activatorRect"/> is in GUI space of the calling OnGUI.
        /// </summary>
        public static void Show(Rect activatorRect, string[] items, int selectedIndex, Action<int> onItemSelected,
            string title = null, Func<int, string> getDetail = null)
        {
            var anchor = GUIUtility.GUIToScreenRect(activatorRect);
            Open(anchor, Mathf.Max(activatorRect.width, DefaultSize.x), items, selectedIndex, onItemSelected, title, getDetail);
        }

        /// <summary>
        /// Opens the list centered on a window, for use outside a control (menus, shortcuts).
        /// </summary>
        public static void ShowCenteredOnWindow(Rect parentWindowRect, string[] items, int selectedIndex, Action<int> onItemSelected,
            string title = null, Func<int, string> getDetail = null)
        {
            var anchor = new Rect(
                parentWindowRect.x + (parentWindowRect.width - DefaultSize.x) * 0.5f,
                parentWindowRect.y + (parentWindowRect.height - DefaultSize.y) * 0.5f,
                DefaultSize.x, 0f);
            Open(anchor, DefaultSize.x, items, selectedIndex, onItemSelected, title, getDetail);
        }

        private static void Open(Rect screenAnchor, float width, string[] items, int selectedIndex, Action<int> onItemSelected,
            string title, Func<int, string> getDetail)
        {
            var window = CreateInstance<LocalizationSearchablePopup>();
            window._items = items ?? Array.Empty<string>();
            window._details = getDetail != null ? new string[window._items.Length] : null;
            window._getDetail = getDetail;
            window._selectedIndex = selectedIndex;
            window._onItemSelected = onItemSelected;
            window._title = title;
            window.wantsMouseMove = true;
            window.ApplyFilter();

            // ShowAsDropDown keeps the window on screen and closes it when focus moves elsewhere
            window.ShowAsDropDown(screenAnchor, new Vector2(width, DefaultSize.y));
        }

        private void OnGUI()
        {
            if (_items == null)
            {
                Close();
                return;
            }

            HandleKeyboard(Event.current);

            DrawHeader();
            DrawSearchField();
            ApplyFilterIfChanged();
            DrawList();
            DrawFooter();
            DrawBorder();
        }

        #region Drawing

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));
            var content = new Rect(rect.x + RowPadding, rect.y, rect.width - RowPadding * 2f, rect.height);

            GUI.Label(content, string.IsNullOrEmpty(_title) ? "Select" : _title, EditorStyles.boldLabel);

            string count = _filtered.Count == _items.Length ? $"{_items.Length:N0}" : $"{_filtered.Count:N0} of {_items.Length:N0}";
            GUI.Label(content, count, Styles.MutedLabelRight);
        }

        private void DrawSearchField()
        {
            _searchField ??= new SearchField();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var rect = GUILayoutUtility.GetRect(0f, 10000f, 18f, 18f, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
            rect.y += 1f;
            _search = _searchField.OnToolbarGUI(rect, _search);
            EditorGUILayout.EndHorizontal();

            if (_focusSearch)
            {
                _searchField.SetFocus();
                _focusSearch = false;
            }
        }

        private void DrawList()
        {
            var evt = Event.current;
            float rowHeight = _getDetail != null ? DetailRowHeight : RowHeight;
            var viewRect = GUILayoutUtility.GetRect(0f, 10000f, 0f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_filtered.Count == 0)
            {
                GUI.Label(viewRect, string.IsNullOrEmpty(_search) ? "Nothing to choose from." : $"No matches for \"{_search}\"", Styles.EmptyState);
                return;
            }

            if (evt.type != EventType.Layout)
            {
                _viewHeight = viewRect.height;
                if (_scrollToHighlight)
                {
                    float top = _highlight * rowHeight;
                    if (top < _scroll.y)
                        _scroll.y = top;
                    else if (top + rowHeight > _scroll.y + viewRect.height)
                        _scroll.y = top + rowHeight - viewRect.height;
                    _scrollToHighlight = false;
                }
            }

            float contentHeight = _filtered.Count * rowHeight;
            float contentWidth = contentHeight > viewRect.height ? viewRect.width - 14f : viewRect.width;

            _scroll = GUI.BeginScrollView(viewRect, _scroll, new Rect(0f, 0f, contentWidth, contentHeight));
            {
                int start = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / rowHeight));
                int end = Mathf.Min(_filtered.Count, start + Mathf.CeilToInt(viewRect.height / rowHeight) + 1);
                for (int i = start; i < end; i++)
                    DrawRow(i, new Rect(0f, i * rowHeight, contentWidth, rowHeight));
            }
            GUI.EndScrollView();
        }

        private void DrawRow(int position, Rect rect)
        {
            var evt = Event.current;
            int index = _filtered[position];
            bool highlighted = position == _highlight;
            bool current = index == _selectedIndex;

            if (evt.type == EventType.MouseMove && rect.Contains(evt.mousePosition) && _highlight != position)
            {
                _highlight = position;
                Repaint();
            }

            if (evt.type == EventType.Repaint)
            {
                if (highlighted)
                    EditorGUI.DrawRect(rect, Styles.RowSelected);
                else if (position % 2 == 0)
                    EditorGUI.DrawRect(rect, Styles.RowEven);

                if (current)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), Styles.Accent);
            }

            var content = new Rect(rect.x + RowPadding, rect.y, rect.width - RowPadding * 2f, rect.height);
            string detail = GetDetail(index);

            if (detail != null)
            {
                DrawItemName(new Rect(content.x, content.y + 2f, content.width, 18f), _items[index], current);
                GUI.Label(new Rect(content.x, content.y + 18f, content.width, 15f),
                    detail.Length > 0 ? detail : "(empty)", Styles.MutedLabel);
            }
            else
            {
                DrawItemName(content, _items[index], current);
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                evt.Use();
                Commit(index);
            }
        }

        /// <summary>
        /// Draws the item, with a view prefix like "menu." muted so the rest of the name stands out.
        /// </summary>
        private static void DrawItemName(Rect rect, string item, bool bold)
        {
            var style = bold ? Styles.RowLabelBold : Styles.RowLabel;
            int delimiter = item.IndexOf(LanguageEditorData.GetCurrentViewDelimiter());

            if (delimiter <= 0 || delimiter == item.Length - 1)
            {
                GUI.Label(rect, new GUIContent(item, item), style);
                return;
            }

            var prefix = new GUIContent(item.Substring(0, delimiter + 1));
            float prefixWidth = Mathf.Min(Styles.RowLabelMuted.CalcSize(prefix).x, rect.width * 0.5f);
            GUI.Label(new Rect(rect.x + 2f, rect.y, prefixWidth, rect.height), prefix, Styles.RowLabelMuted);
            GUI.Label(new Rect(rect.x + prefixWidth, rect.y, rect.width - prefixWidth, rect.height),
                new GUIContent(item.Substring(delimiter + 1), item), style);
        }

        private void DrawFooter()
        {
            var rect = GUILayoutUtility.GetRect(0f, FooterHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Styles.Separator);
            GUI.Label(new Rect(rect.x + RowPadding, rect.y + 1f, rect.width - RowPadding * 2f, rect.height - 1f),
                "↑ ↓ to move · Enter to choose · Esc to close", Styles.MutedLabel);
        }

        private void DrawBorder()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var r = new Rect(0f, 0f, position.width, position.height);
            var color = Styles.Separator;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), color);
        }

        #endregion

        #region Input

        /// <summary>
        /// Navigation keys are handled before the search field so typing and arrows work together.
        /// </summary>
        private void HandleKeyboard(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return;

            float rowHeight = _getDetail != null ? DetailRowHeight : RowHeight;
            int page = Mathf.Max(1, Mathf.FloorToInt(_viewHeight / rowHeight) - 1);

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveHighlight(1);
                    break;
                case KeyCode.UpArrow:
                    MoveHighlight(-1);
                    break;
                case KeyCode.PageDown:
                    MoveHighlight(page);
                    break;
                case KeyCode.PageUp:
                    MoveHighlight(-page);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlight >= 0 && _highlight < _filtered.Count)
                        Commit(_filtered[_highlight]);
                    break;
                case KeyCode.Escape:
                    Close();
                    break;
                default:
                    return;
            }

            evt.Use();
        }

        private void MoveHighlight(int delta)
        {
            if (_filtered.Count == 0)
                return;

            _highlight = Mathf.Clamp(_highlight + delta, 0, _filtered.Count - 1);
            _scrollToHighlight = true;
            Repaint();
        }

        private void Commit(int index)
        {
            if (_committed)
                return;

            _committed = true;
            var callback = _onItemSelected;
            Close();
            callback?.Invoke(index);
        }

        #endregion

        #region Filtering

        private string GetDetail(int index)
        {
            if (_details == null)
                return null;

            return _details[index] ??= _getDetail(index) ?? "";
        }

        private void ApplyFilterIfChanged()
        {
            if (Event.current.type != EventType.Layout || _search == _appliedSearch)
                return;

            ApplyFilter();
        }

        /// <summary>
        /// Every space-separated word must appear in the item (or its detail). Names that start with the
        /// search come first, then names that contain it, then matches found only in the detail.
        /// </summary>
        private void ApplyFilter()
        {
            _appliedSearch = _search;
            _filtered.Clear();

            string search = _search?.Trim() ?? "";
            if (search.Length == 0)
            {
                for (int i = 0; i < _items.Length; i++)
                    _filtered.Add(i);

                _highlight = _selectedIndex >= 0 && _selectedIndex < _items.Length ? _selectedIndex : 0;
                _scrollToHighlight = true;
                return;
            }

            var words = search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(int index, int score)>();

            for (int i = 0; i < _items.Length; i++)
            {
                string item = _items[i] ?? "";
                bool allInName = true;
                bool allMatch = true;

                foreach (var word in words)
                {
                    if (item.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    allInName = false;
                    string detail = GetDetail(i);
                    if (detail == null || detail.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (!allMatch)
                    continue;

                int score = !allInName ? 3
                    : item.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 0
                    : item.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                    : 2;
                scored.Add((i, score));
            }

            scored.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.index.CompareTo(b.index));
            foreach (var (index, _) in scored)
                _filtered.Add(index);

            _highlight = 0;
            _scroll = Vector2.zero;
        }

        #endregion
    }
}
