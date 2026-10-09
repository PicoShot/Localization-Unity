using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Editor.Data;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Multi-selection in the keys list (Ctrl/Cmd+click, Shift+click, Shift+arrows, Select All)
    /// and the bulk actions shown when more than one key is selected.
    /// </summary>
    public sealed partial class KeysTab
    {
        private const float SelectionActionWidth = 160f;
        private const int MaxNamesInDialog = 10;

        private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
        private string _anchor;
        private Vector2 _selectionScroll;

        private bool _bulkCancel;
        private int _bulkDone;
        private int _bulkTotal;

        private bool IsMultiSelect => _selection.Count > 1;

        private static GUIStyle _removeButtonStyle;

        private static GUIStyle RemoveButtonStyle => _removeButtonStyle ??= new GUIStyle(EditorStyles.iconButton)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
            fixedWidth = 0f,
            fixedHeight = 0f
        };

        /// <summary>
        /// Keeps the selection consistent with the data: drops deleted keys and follows the primary key
        /// when something else (undo, search, another tab) changed it.
        /// </summary>
        private void SyncSelection()
        {
            if (_selection.Count > 0)
                _selection.RemoveWhere(key => !Data.LanguageData.ContainsKey(key));

            string primary = Data.SelectedKey;
            if (string.IsNullOrEmpty(primary) || !Data.LanguageData.ContainsKey(primary))
            {
                _selection.Clear();
                _anchor = null;
                return;
            }

            if (!_selection.Contains(primary))
            {
                _selection.Clear();
                _selection.Add(primary);
                _anchor = primary;
            }
        }

        private List<string> GetSelectedKeys()
        {
            return Data.Keys.Where(_selection.Contains).ToList();
        }

        /// <summary>
        /// Plain click selects one key, Ctrl/Cmd+click toggles, Shift+click selects a range from the anchor.
        /// </summary>
        private void HandleListClick(string key, Event evt)
        {
            if (evt.shift && _anchor != null)
            {
                SelectRange(_anchor, key, additive: EditorGUI.actionKey);
            }
            else if (EditorGUI.actionKey)
            {
                ToggleSelection(key);
            }
            else
            {
                SelectKey(key);
                if (evt.clickCount == 2)
                    RenameKey();
            }

            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        private void ToggleSelection(string key)
        {
            if (_selection.Contains(key))
            {
                _selection.Remove(key);
                if (Data.SelectedKey == key)
                    Data.SelectedKey = GetSelectedKeys().FirstOrDefault();
            }
            else
            {
                _selection.Add(key);
                Data.SelectedKey = key;
            }
            _anchor = key;
        }

        private void SelectRange(string from, string to, bool additive)
        {
            var filtered = Data.GetFilteredKeys();
            int start = IndexOf(filtered, from);
            int end = IndexOf(filtered, to);
            if (start < 0 || end < 0)
            {
                SelectKey(to);
                return;
            }

            if (!additive)
                _selection.Clear();

            for (int i = Mathf.Min(start, end); i <= Mathf.Max(start, end); i++)
                _selection.Add(filtered[i]);

            Data.SelectedKey = to;
        }

        private void SelectAllFiltered()
        {
            var filtered = Data.GetFilteredKeys();
            if (filtered.Count == 0)
                return;

            _selection.Clear();
            foreach (var key in filtered)
                _selection.Add(key);

            if (string.IsNullOrEmpty(Data.SelectedKey) || !_selection.Contains(Data.SelectedKey))
                Data.SelectedKey = filtered[0];
            _anchor = Data.SelectedKey;
            GUIUtility.keyboardControl = 0;
            Editor.Repaint();
        }

        /// <summary>
        /// Keeps only the primary key selected.
        /// </summary>
        private void CollapseSelection()
        {
            SelectKey(Data.SelectedKey);
        }

        #region Selection Panel

        private void DrawSelectionPanel()
        {
            var keys = GetSelectedKeys();
            int arrays = keys.Count(k => LanguageEditorData.IsArrayKey(Data.LanguageData[k]));
            int needTranslation = GetTranslatableKeys(keys).Count;
            int withProblems = keys.Count(k => Data.GetKeyStatus(k).Problems > 0);

            var header = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(header.x + 2f, header.y, header.width - 120f, header.height), $"{keys.Count} keys selected", Styles.SectionTitle);
            if (GUI.Button(new Rect(header.xMax - 110f, header.y + 3f, 108f, header.height - 6f), "Clear Selection", EditorStyles.miniButton))
                CollapseSelection();

            var parts = new List<string> { $"{keys.Count - arrays} strings", $"{arrays} arrays" };
            if (needTranslation > 0) parts.Add($"{needTranslation} need translations");
            if (withProblems > 0) parts.Add($"{withProblems} with problems");
            GUILayout.Label(string.Join(" · ", parts), Styles.MutedLabel);
            Styles.DrawSeparator(4f, 6f);

            string provider = Data.ActiveTranslationProvider.ToString();

            if (_isTranslating && _bulkTotal > 0)
            {
                EditorGUILayout.BeginHorizontal();
                var barRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(barRect, (float)_bulkDone / _bulkTotal, $"Translating {_bulkDone} / {_bulkTotal}");
                using (new EditorGUI.DisabledScope(_bulkCancel))
                {
                    if (GUILayout.Button(_bulkCancel ? "Stopping…" : "Cancel", EditorStyles.miniButton, GUILayout.Width(70), GUILayout.Height(20)))
                        _bulkCancel = true;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6);
            }
            else
            {
                using (new EditorGUI.DisabledScope(_isTranslating || needTranslation == 0))
                {
                    if (DrawSelectionAction(needTranslation > 0 ? $"Translate Missing ({needTranslation})" : "All Translated",
                            $"Fill empty languages with {provider}. One undo step for all keys."))
                        TranslateSelection(GetTranslatableKeys(keys));
                }
            }

            if (DrawSelectionAction("Move to View…", "Change the view prefix of every selected key, e.g. hud.score → menu.score.", out var moveRect))
                ShowMoveToViewMenu(moveRect, keys);

            if (DrawSelectionAction("Copy Names", "Copy the key names, one per line."))
                CopyToClipboard(string.Join("\n", keys), $"Copied {keys.Count} key names");

            if (DrawSelectionAction("Clear Translations", $"Empty every language but keep the keys. {ActionKeyName}+Z undoes it."))
                ClearSelectionTranslations(keys);

            if (DrawSelectionAction($"Delete {keys.Count} Keys…", "Remove the keys and all their translations."))
            {
                ConfirmDeleteKeys(keys);
                GUIUtility.ExitGUI();
            }

            Styles.DrawSeparator(6f, 4f);
            GUILayout.Label("Selected", Styles.MutedLabel);
            DrawSelectedKeysList(keys);
        }

        private bool DrawSelectionAction(string label, string description)
        {
            return DrawSelectionAction(label, description, out _);
        }

        private bool DrawSelectionAction(string label, string description, out Rect buttonRect)
        {
            EditorGUILayout.BeginHorizontal();
            bool clicked = GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(SelectionActionWidth));
            buttonRect = GUILayoutUtility.GetLastRect();
            GUILayout.Label(description, Styles.Description);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
            return clicked;
        }

        private void DrawSelectedKeysList(List<string> keys)
        {
            string remove = null;
            string open = null;

            _selectionScroll = EditorGUILayout.BeginScrollView(_selectionScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                var rect = GUILayoutUtility.GetRect(0f, LanguageEditorData.KeyItemHeight, GUILayout.ExpandWidth(true));
                bool hover = rect.Contains(Event.current.mousePosition);
                Styles.DrawRowBackground(rect, i, hover);

                var removeRect = new Rect(rect.xMax - 22f, rect.y + 2f, 20f, rect.height - 4f);
                float nameWidth = Mathf.Min(rect.width * 0.5f, Styles.RowLabel.CalcSize(new GUIContent(key)).x + 8f);
                GUI.Label(new Rect(rect.x + 6f, rect.y, nameWidth, rect.height), new GUIContent(key, "Click to edit only this key"), Styles.RowLabel);
                GUI.Label(new Rect(rect.x + 10f + nameWidth, rect.y, Mathf.Max(0f, removeRect.x - rect.x - nameWidth - 14f), rect.height),
                    Data.GetPreviewText(key), Styles.MutedLabel);

                if (GUI.Button(removeRect, new GUIContent("×", "Remove from selection"), RemoveButtonStyle))
                    remove = key;
                else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
                {
                    open = key;
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();

            if (remove != null)
            {
                ToggleSelection(remove);
                Editor.Repaint();
            }
            else if (open != null)
            {
                SelectKey(open);
            }
        }

        #endregion

        #region Bulk Actions

        private void ShowSelectionMenu(Rect? dropDownRect)
        {
            var keys = GetSelectedKeys();
            var menu = new GenericMenu();

            var translatable = GetTranslatableKeys(keys);
            if (!_isTranslating && translatable.Count > 0)
                menu.AddItem(new GUIContent($"Translate Missing ({translatable.Count})"), false, () => TranslateSelection(translatable));
            else
                menu.AddDisabledItem(new GUIContent("Translate Missing"));

            menu.AddItem(new GUIContent("Move to View…"), false, () => ShowMoveToViewMenu(null, keys));
            menu.AddItem(new GUIContent("Copy Names"), false, () => CopyToClipboard(string.Join("\n", keys), $"Copied {keys.Count} key names"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Clear Translations"), false, () => ClearSelectionTranslations(keys));
            menu.AddItem(new GUIContent($"Delete {keys.Count} Keys…"), false, () => ConfirmDeleteKeys(keys));

            if (dropDownRect.HasValue)
                menu.DropDown(dropDownRect.Value);
            else
                menu.ShowAsContext();
        }

        /// <summary>
        /// Keys with at least one empty target language and a source text to translate from.
        /// </summary>
        private List<string> GetTranslatableKeys(List<string> keys)
        {
            string defaultLang = DefaultLanguage;
            return keys.Where(k => Data.IsTranslated(k, defaultLang) && CountMissingTargets(k) > 0).ToList();
        }

        private async void TranslateSelection(List<string> keys)
        {
            if (_isTranslating || keys.Count == 0)
                return;

            Data.History.RecordKeys("Translate Keys", keys.ToArray());
            GUIUtility.keyboardControl = 0;

            _isTranslating = true;
            _bulkCancel = false;
            _bulkDone = 0;
            _bulkTotal = keys.Count;
            int failed = 0;

            try
            {
                foreach (var key in keys)
                {
                    if (_bulkCancel || Editor == null)
                        break;

                    if (Data.LanguageData.ContainsKey(key))
                    {
                        try
                        {
                            await _translationService.TranslateAndFill(key);
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Debug.LogError($"[Localization] Translation failed for '{key}': {ex.Message}");
                        }
                    }

                    _bulkDone++;
                    Data.MarkKeysChanged();
                    if (Editor != null)
                        Editor.Repaint();
                }
            }
            finally
            {
                bool cancelled = _bulkCancel;
                _isTranslating = false;
                _bulkCancel = false;
                _bulkTotal = 0;

                if (Editor != null)
                {
                    string message = cancelled ? $"Stopped after {_bulkDone} of {keys.Count} keys" : $"Translated {_bulkDone} keys";
                    if (failed > 0)
                        message += $" ({failed} failed, see Console)";
                    Editor.ShowNotification(new GUIContent(message));
                    Editor.Repaint();
                }
            }
        }

        private void ClearSelectionTranslations(List<string> keys)
        {
            Data.History.RecordKeys("Clear Translations", keys.ToArray());
            foreach (var key in keys)
                Data.ClearKeyTranslations(key);

            GUIUtility.keyboardControl = 0;
            Editor.ShowNotification(new GUIContent($"Cleared {keys.Count} keys ({ActionKeyName}+Z to undo)"));
            Editor.Repaint();
        }

        private void ConfirmDeleteKeys(List<string> keys)
        {
            if (keys.Count == 0)
                return;

            if (keys.Count == 1)
            {
                ConfirmDeleteKey(keys[0]);
                return;
            }

            string names = string.Join("\n", keys.Take(MaxNamesInDialog));
            if (keys.Count > MaxNamesInDialog)
                names += $"\n… and {keys.Count - MaxNamesInDialog} more";

            if (!EditorUtility.DisplayDialog("Delete Keys",
                    $"Delete {keys.Count} keys and all their translations?\n\n{names}\n\nYou can undo this with {ActionKeyName}+Z.",
                    "Delete", "Cancel"))
                return;

            Data.History.RecordKeys("Delete Keys", keys.ToArray());
            foreach (var key in keys)
                Data.RemoveKey(key);

            Data.SelectedKey = null;
            _selection.Clear();
            GUIUtility.keyboardControl = 0;
            Editor.ShowNotification(new GUIContent($"Deleted {keys.Count} keys"));
            Editor.Repaint();
        }

        private void ShowMoveToViewMenu(Rect? rect, List<string> keys)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("No View (top level)"), false, () => MoveKeysToView(keys, ""));

            var views = Data.GetViews().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            if (views.Count > 0)
                menu.AddSeparator("");
            foreach (var view in views)
            {
                string captured = view;
                menu.AddItem(new GUIContent(view), false, () => MoveKeysToView(keys, captured));
            }

            menu.AddSeparator("");
            char delimiter = Data.CurrentViewDelimiter;
            menu.AddItem(new GUIContent("New View…"), false, () =>
                LocalizationTextEditorPopup.OpenKeyName("", name => MoveKeysToView(keys, name), "New View Name",
                    name => name.IndexOf(delimiter) >= 0 ? $"A view name can't contain '{delimiter}'." : null));

            if (rect.HasValue)
                menu.DropDown(rect.Value);
            else
                menu.ShowAsContext();
        }

        /// <summary>
        /// Replaces each key's view prefix (the part before the first delimiter) with <paramref name="view"/>.
        /// Keys whose new name is taken are skipped after asking.
        /// </summary>
        private void MoveKeysToView(List<string> keys, string view)
        {
            char delimiter = Data.CurrentViewDelimiter;
            var existing = new HashSet<string>(Data.Keys, StringComparer.OrdinalIgnoreCase);
            var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moves = new List<(string from, string to)>();
            var conflicts = new List<string>();

            foreach (var key in keys)
            {
                int index = key.IndexOf(delimiter);
                string local = index >= 0 ? key.Substring(index + 1) : key;
                string target = string.IsNullOrEmpty(view) ? local : view + delimiter + local;

                if (target == key)
                    continue;

                if (!planned.Add(target))
                {
                    conflicts.Add($"{key} → {target}");
                    continue;
                }

                moves.Add((key, target));
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                var movingAway = new HashSet<string>(moves.Select(m => m.from), StringComparer.OrdinalIgnoreCase);
                for (int i = moves.Count - 1; i >= 0; i--)
                {
                    var (from, to) = moves[i];
                    if (existing.Contains(to) && !movingAway.Contains(to))
                    {
                        conflicts.Add($"{from} → {to}");
                        moves.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            if (conflicts.Count > 0)
            {
                string list = string.Join("\n", conflicts.Take(MaxNamesInDialog));
                if (conflicts.Count > MaxNamesInDialog)
                    list += $"\n… and {conflicts.Count - MaxNamesInDialog} more";

                if (moves.Count == 0)
                {
                    EditorUtility.DisplayDialog("Move to View", $"None of the keys can be moved; their new names are already taken:\n\n{list}", "OK");
                    return;
                }

                if (!EditorUtility.DisplayDialog("Move to View",
                        $"{conflicts.Count} keys can't be moved because their new names are already taken:\n\n{list}\n\nMove the other {moves.Count}?",
                        "Move", "Cancel"))
                    return;
            }

            if (moves.Count == 0)
            {
                Editor.ShowNotification(new GUIContent("The keys are already in that view"));
                return;
            }

            Data.History.RecordKeys("Move Keys to View", moves.Select(m => m.from).Concat(moves.Select(m => m.to)).ToArray());

            var renamed = new List<string>();
            foreach (var (from, to) in moves)
            {
                if (existing.Contains(to))
                {
                    string temp = $"{to}__moving_{Guid.NewGuid():N}";
                    Data.RenameKey(from, temp);
                    renamed.Add(temp);
                }
                else
                {
                    Data.RenameKey(from, to);
                    renamed.Add(to);
                }
            }
            for (int i = 0; i < moves.Count; i++)
            {
                if (renamed[i] != moves[i].to)
                    Data.RenameKey(renamed[i], moves[i].to);
            }

            Data.SelectedView = view;
            _selection.Clear();
            foreach (var (_, to) in moves)
                _selection.Add(to);
            foreach (var key in keys)
            {
                if (Data.LanguageData.ContainsKey(key))
                    _selection.Add(key);
            }
            Data.SelectedKey = moves[0].to;
            _anchor = Data.SelectedKey;

            GUIUtility.keyboardControl = 0;
            Editor.ShowNotification(new GUIContent($"Moved {moves.Count} keys to {(string.IsNullOrEmpty(view) ? "the top level" : $"'{view}'")}"));
            Editor.Repaint();
        }

        #endregion
    }
}
