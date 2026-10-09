using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PicoShot.Localization.Config;
using PicoShot.Localization.Editor.Data;
using Object = UnityEngine.Object;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for localizing text components: the selected hierarchy, or an audit of every localized component in open scenes.
    /// </summary>
    public sealed class ComponentsTab : LocalizationEditorTabBase
    {
        private enum Mode
        {
            Selection,
            Scene,
            Prefabs
        }

        private enum StatusFilter
        {
            All,
            NotLocalized,
            Broken,
            Untranslated
        }

        private enum TypeFilter
        {
            All,
            TMP,
            Legacy,
            TextMesh,
            Dropdowns
        }

        private enum RowState
        {
            NotLocalized,
            NoKey,
            MissingKey,
            BadIndex,
            Untranslated,
            Ok
        }

        private sealed class Row
        {
            public GameObject GameObject;
            public Component Text;
            public TextComponentType Type;
            public LocalizationTextComponent Localization;
            public string Path;
            public string Scene;

            public GameObject PrefabRoot;
            public string InnerPath;

            public RowState State;
            public string Detail;
            public string Preview;
        }

        private struct RowLayout
        {
            public Rect Object;
            public Rect Type;
            public Rect Key;
            public Rect Preview;
            public Rect Status;
            public Rect Menu;
        }

        private const float RowHeight = 24f;
        private const float RowPadding = 6f;
        private const float StatusWidth = 72f;
        private const float TypeWidth = 62f;
        private const float IndexFieldWidth = 40f;
        private const float MenuWidth = 20f;
        private const int MaxPlanLines = 12;

        private static readonly string[] ModeLabels = { "Selection", "Scene", "Prefabs" };

        private static readonly HashSet<string> GenericObjectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "text", "text (tmp)", "text (legacy)", "label", "caption", "tmp", "textmeshpro", "new text"
        };

        private Mode _mode;
        private bool _locked;
        private SearchField _searchField;
        private string _search = "";
        private StatusFilter _statusFilter;
        private TypeFilter _typeFilter;

        private readonly List<Row> _rows = new();
        private bool _rowsDirty = true;
        private Vector2 _scroll;
        private bool _hadScrollbar;
        private GameObject _highlighted;

        private string _prefix = "";
        private GameObject _prefixTarget;

        private int _textLookupVersion = -1;
        private readonly Dictionary<string, string> _keyByText = new();

        private bool _subscribed;
        private GUIStyle _lockStyle;

        // Prefab scan results; kept until the next scan
        private readonly List<Row> _prefabRows = new();
        private DateTime? _prefabScanTime;
        private int _prefabsScanned;
        private int _prefabsWithText;

        public ComponentsTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Components";

        private static string DefaultLanguage => LocalizationConfigProvider.Config.DefaultLanguage;

        public override void OnEnter()
        {
            _rowsDirty = true;
        }

        public override void OnExit()
        {
            Unsubscribe();
        }

        #region Events

        private void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnHierarchyChanged;
            _subscribed = true;

            OnSelectionChanged();
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnHierarchyChanged;
            _subscribed = false;
        }

        private bool EditorClosed()
        {
            if (Editor != null)
                return false;

            Unsubscribe();
            return true;
        }

        private void OnSelectionChanged()
        {
            if (EditorClosed() || _locked)
                return;

            var go = Selection.activeGameObject;
            if (go == null || EditorUtility.IsPersistent(go) || go == Data.SelectedGameObject)
                return;

            SetTarget(go);
        }

        private void OnHierarchyChanged()
        {
            if (EditorClosed())
                return;

            _rowsDirty = true;
            Editor.Repaint();
        }

        private void SetTarget(GameObject go)
        {
            Data.SelectedGameObject = go;
            _rowsDirty = true;
            _scroll = Vector2.zero;
            _highlighted = null;
            Editor.Repaint();
        }

        #endregion

        public override void Draw()
        {
            EnsureSubscribed();

            Editor.wantsMouseMove = true;
            if (Event.current.type == EventType.MouseMove)
                Editor.Repaint();

            var area = EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawToolbar();
                RefreshRowsIfNeeded();

                var visible = GetVisibleRows();
                DrawSummary();

                if (!DrawEmptyState(visible))
                    DrawTable(visible);

                DrawFooter();
            }
            EditorGUILayout.EndVertical();

            HandleDragAndDrop(area);
        }

        #region Toolbar & Summary

        private void DrawToolbar()
        {
            _searchField ??= new SearchField();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                int mode = GUILayout.Toolbar((int)_mode, ModeLabels, EditorStyles.toolbarButton, GUILayout.Width(210));
                if (mode != (int)_mode)
                {
                    _mode = (Mode)mode;
                    if (_mode != Mode.Selection && _statusFilter == StatusFilter.NotLocalized)
                        _statusFilter = StatusFilter.All;
                    _rowsDirty = true;
                    _scroll = Vector2.zero;
                    GUIUtility.keyboardControl = 0;
                }

                GUILayout.Space(6);

                var searchRect = GUILayoutUtility.GetRect(80f, 2000f, 18f, 18f, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                searchRect.y += 1f;
                _search = _searchField.OnToolbarGUI(searchRect, _search);

                GUILayout.Space(4);

                int active = (_statusFilter != StatusFilter.All ? 1 : 0) + (_typeFilter != TypeFilter.All ? 1 : 0);
                var filterContent = new GUIContent(active > 0 ? $"Filter ({active})" : "Filter", "Filter by status and component type");
                var filterRect = GUILayoutUtility.GetRect(filterContent, EditorStyles.toolbarDropDown, GUILayout.Width(72));
                if (EditorGUI.DropdownButton(filterRect, filterContent, FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowFilterMenu(filterRect);

                if (_mode == Mode.Scene)
                {
                    if (GUILayout.Button(new GUIContent("Refresh", "Rescan open scenes"), EditorStyles.toolbarButton, GUILayout.Width(56)))
                        _rowsDirty = true;
                }
                else if (_mode == Mode.Prefabs)
                {
                    var scanContent = new GUIContent(_prefabScanTime == null ? "Scan" : "Rescan", "Scan every prefab under Assets");
                    if (GUILayout.Button(scanContent, EditorStyles.toolbarButton, GUILayout.Width(56)))
                    {
                        ScanPrefabs();
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    DrawLockToggle();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLockToggle()
        {
            _lockStyle ??= GUI.skin.FindStyle("IN LockButton");
            var tooltip = _locked ? "Locked to this object. Click to follow the Hierarchy selection again." : "Lock to the current object";

            bool locked;
            if (_lockStyle != null)
            {
                var rect = GUILayoutUtility.GetRect(16f, 16f, _lockStyle, GUILayout.Width(16f));
                rect.y += 2f;
                locked = GUI.Toggle(rect, _locked, new GUIContent("", tooltip), _lockStyle);
                GUILayout.Space(4);
            }
            else
            {
                locked = GUILayout.Toggle(_locked, new GUIContent("Lock", tooltip), EditorStyles.toolbarButton, GUILayout.Width(40));
            }

            if (locked != _locked)
            {
                _locked = locked;
                if (!_locked)
                    OnSelectionChanged();
            }
        }

        private void ShowFilterMenu(Rect rect)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Status/All"), _statusFilter == StatusFilter.All, () => _statusFilter = StatusFilter.All);
            if (_mode == Mode.Selection)
                menu.AddItem(new GUIContent("Status/Not Localized"), _statusFilter == StatusFilter.NotLocalized, () => _statusFilter = StatusFilter.NotLocalized);
            menu.AddItem(new GUIContent("Status/Broken Key"), _statusFilter == StatusFilter.Broken, () => _statusFilter = StatusFilter.Broken);
            menu.AddItem(new GUIContent("Status/Untranslated"), _statusFilter == StatusFilter.Untranslated, () => _statusFilter = StatusFilter.Untranslated);

            menu.AddItem(new GUIContent("Type/All"), _typeFilter == TypeFilter.All, () => _typeFilter = TypeFilter.All);
            menu.AddItem(new GUIContent("Type/TextMesh Pro"), _typeFilter == TypeFilter.TMP, () => _typeFilter = TypeFilter.TMP);
            menu.AddItem(new GUIContent("Type/Legacy UI"), _typeFilter == TypeFilter.Legacy, () => _typeFilter = TypeFilter.Legacy);
            menu.AddItem(new GUIContent("Type/TextMesh (3D)"), _typeFilter == TypeFilter.TextMesh, () => _typeFilter = TypeFilter.TextMesh);
            menu.AddItem(new GUIContent("Type/Dropdowns"), _typeFilter == TypeFilter.Dropdowns, () => _typeFilter = TypeFilter.Dropdowns);

            menu.AddSeparator("");
            if (_statusFilter != StatusFilter.All || _typeFilter != TypeFilter.All)
                menu.AddItem(new GUIContent("Clear Filters"), false, () =>
                {
                    _statusFilter = StatusFilter.All;
                    _typeFilter = TypeFilter.All;
                });
            else
                menu.AddDisabledItem(new GUIContent("Clear Filters"));

            menu.DropDown(rect);
        }

        private void DrawSummary()
        {
            int localized = 0, broken = 0, untranslated = 0;
            foreach (var row in _rows)
            {
                if (row.State == RowState.NotLocalized)
                    continue;
                localized++;
                if (IsBroken(row.State)) broken++;
                else if (row.State == RowState.Untranslated) untranslated++;
            }

            var parts = new List<string>();
            string title;
            if (_mode == Mode.Selection)
            {
                var target = Data.SelectedGameObject;
                if (target == null)
                    return;
                title = target.name;
                parts.Add(_rows.Count == 1 ? "1 text component" : $"{_rows.Count} text components");
                parts.Add($"{localized} localized");
            }
            else if (_mode == Mode.Scene)
            {
                int scenes = SceneManager.sceneCount;
                title = scenes == 1 ? "Open scene" : $"{scenes} open scenes";
                parts.Add(localized == 1 ? "1 localized component" : $"{localized} localized components");
            }
            else
            {
                if (_prefabScanTime == null)
                    return;
                title = "Project prefabs";
                parts.Add($"{localized} localized {(localized == 1 ? "component" : "components")} in {_prefabsWithText} of {_prefabsScanned} prefabs");
            }

            if (broken > 0) parts.Add($"{broken} broken");
            if (untranslated > 0) parts.Add($"{untranslated} untranslated");
            if (_mode == Mode.Prefabs && _prefabScanTime.HasValue)
                parts.Add($"scanned {FormatAge(DateTime.Now - _prefabScanTime.Value)}");

            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var titleContent = new GUIContent(title);
            float titleWidth = Mathf.Min(Styles.SectionTitle.CalcSize(titleContent).x + 4f, rect.width * 0.5f);
            GUI.Label(new Rect(rect.x + 2f, rect.y, titleWidth, rect.height), titleContent, Styles.SectionTitle);
            GUI.Label(new Rect(rect.x + titleWidth + 8f, rect.y, rect.width - titleWidth - 8f, rect.height),
                string.Join(" · ", parts), Styles.MutedLabel);
        }

        private bool DrawEmptyState(List<Row> visible)
        {
            string title = null, message = null;

            if (_mode == Mode.Prefabs && _prefabScanTime == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Scan project prefabs", Styles.CenteredTitle);
                GUILayout.Label("Finds localized text in every prefab under Assets, including broken keys in prefabs nobody has open. " +
                                "Prefabs are loaded only when you scan.", Styles.EmptyState);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Scan Prefabs", GUILayout.Width(120), GUILayout.Height(24)))
                {
                    ScanPrefabs();
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                return true;
            }

            if (_mode == Mode.Selection && Data.SelectedGameObject == null)
            {
                title = "Nothing selected";
                message = "Select a GameObject in the Hierarchy, or drag one here, to localize the text components under it.";
            }
            else if (_rows.Count == 0)
            {
                title = _mode == Mode.Selection ? "No text components" : "No localized components";
                message = _mode switch
                {
                    Mode.Selection => "Supported: TextMesh Pro text and dropdowns, legacy UI Text and Dropdown, and 3D TextMesh.",
                    Mode.Scene => "Localize text in Selection mode, then audit it here.",
                    _ => $"None of the {_prefabsScanned} prefabs under Assets use localized text."
                };
            }
            else if (visible.Count == 0)
            {
                title = "No matches";
                message = "Nothing matches the search or filters.";
            }

            if (title == null)
                return false;

            GUILayout.FlexibleSpace();
            GUILayout.Label(title, Styles.CenteredTitle);
            GUILayout.Label(message, Styles.EmptyState);
            GUILayout.FlexibleSpace();
            return true;
        }

        private void HandleDragAndDrop(Rect area)
        {
            var evt = Event.current;
            if ((evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) || !area.Contains(evt.mousePosition))
                return;

            var go = DragAndDrop.objectReferences.OfType<GameObject>().FirstOrDefault(o => !EditorUtility.IsPersistent(o));
            if (go == null)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                _mode = Mode.Selection;
                _locked = true;
                SetTarget(go);
            }
            evt.Use();
        }

        #endregion

        #region Rows

        private void RefreshRowsIfNeeded()
        {
            if (!_rowsDirty && _rows.All(r => r.GameObject != null))
                return;

            _rowsDirty = false;
            _rows.Clear();

            if (_mode == Mode.Selection)
                CollectSelectionRows();
            else if (_mode == Mode.Scene)
                CollectSceneRows();
            else
                _rows.AddRange(_prefabRows.Where(r => r.GameObject != null));
        }

        private void CollectSelectionRows()
        {
            var target = Data.SelectedGameObject;
            if (target == null)
                return;

            if (_prefixTarget != target)
            {
                _prefixTarget = target;
                _prefix = string.IsNullOrEmpty(Data.SelectedView)
                    ? ToKeySegment(target.name) + Data.CurrentViewDelimiter
                    : Data.SelectedView + Data.CurrentViewDelimiter;
            }

            var excluded = CollectDrivenTexts(target);
            foreach (var transform in target.GetComponentsInChildren<Transform>(true))
            {
                var (text, type) = GetPrimaryText(transform.gameObject, excluded);
                if (text == null)
                    continue;

                _rows.Add(new Row
                {
                    GameObject = transform.gameObject,
                    Text = text,
                    Type = type,
                    Localization = transform.GetComponent<LocalizationTextComponent>(),
                    Path = GetPath(transform, target.transform)
                });
            }
        }

        private void CollectSceneRows()
        {
#if UNITY_6000_4_OR_NEWER
            var components = Object.FindObjectsByType<LocalizationTextComponent>(FindObjectsInactive.Include);
#else
            var components = Object.FindObjectsByType<LocalizationTextComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            var none = new HashSet<Component>();
            foreach (var component in components)
            {
                if (component == null || EditorUtility.IsPersistent(component))
                    continue;

                var (text, type) = GetPrimaryText(component.gameObject, none);
                _rows.Add(new Row
                {
                    GameObject = component.gameObject,
                    Text = text,
                    Type = type,
                    Localization = component,
                    Path = GetPath(component.transform, null),
                    Scene = component.gameObject.scene.name
                });
            }

            _rows.Sort((a, b) =>
            {
                int scene = string.CompareOrdinal(a.Scene, b.Scene);
                return scene != 0 ? scene : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Texts owned by dropdowns (caption, item template) and input fields (typed text) are not localized on their own.
        /// </summary>
        private static HashSet<Component> CollectDrivenTexts(GameObject root)
        {
            var excluded = new HashSet<Component>();
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (dropdown.captionText != null) excluded.Add(dropdown.captionText);
                if (dropdown.itemText != null) excluded.Add(dropdown.itemText);
            }
            foreach (var dropdown in root.GetComponentsInChildren<Dropdown>(true))
            {
                if (dropdown.captionText != null) excluded.Add(dropdown.captionText);
                if (dropdown.itemText != null) excluded.Add(dropdown.itemText);
            }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (input.textComponent != null) excluded.Add(input.textComponent);
            }
            foreach (var input in root.GetComponentsInChildren<InputField>(true))
            {
                if (input.textComponent != null) excluded.Add(input.textComponent);
            }
            return excluded;
        }

        /// <summary>
        /// The text component LocalizationTextComponent would drive on this object, in the same priority order.
        /// </summary>
        private static (Component, TextComponentType) GetPrimaryText(GameObject go, HashSet<Component> excluded)
        {
            if (go.TryGetComponent<TMP_Dropdown>(out var tmpDropdown)) return (tmpDropdown, TextComponentType.TMPDropdown);
            if (go.TryGetComponent<TMP_Text>(out var tmpText) && !excluded.Contains(tmpText)) return (tmpText, TextComponentType.TMPText);
            if (go.TryGetComponent<Dropdown>(out var dropdown)) return (dropdown, TextComponentType.LegacyDropdown);
            if (go.TryGetComponent<Text>(out var text) && !excluded.Contains(text)) return (text, TextComponentType.LegacyText);
            if (go.TryGetComponent<TextMesh>(out var textMesh)) return (textMesh, TextComponentType.TextMesh);
            return (null, TextComponentType.None);
        }

        private static string GetPath(Transform transform, Transform root)
        {
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent)
            {
                parts.Add(t.name);
                if (t == root)
                    break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private List<Row> GetVisibleRows()
        {
            var visible = new List<Row>();
            string search = _search?.Trim() ?? "";

            foreach (var row in _rows)
            {
                if (row.GameObject == null)
                    continue;

                UpdateRowState(row);

                if (!MatchesType(row.Type))
                    continue;

                if (_statusFilter == StatusFilter.NotLocalized && row.State != RowState.NotLocalized) continue;
                if (_statusFilter == StatusFilter.Broken && !IsBroken(row.State)) continue;
                if (_statusFilter == StatusFilter.Untranslated && row.State != RowState.Untranslated) continue;

                if (search.Length > 0 &&
                    row.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (row.Localization == null || row.Localization.TranslationKey == null ||
                     row.Localization.TranslationKey.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (row.Preview == null || row.Preview.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                visible.Add(row);
            }

            return visible;
        }

        private bool MatchesType(TextComponentType type)
        {
            return _typeFilter switch
            {
                TypeFilter.TMP => type is TextComponentType.TMPText or TextComponentType.TMPDropdown,
                TypeFilter.Legacy => type is TextComponentType.LegacyText or TextComponentType.LegacyDropdown,
                TypeFilter.TextMesh => type == TextComponentType.TextMesh,
                TypeFilter.Dropdowns => type is TextComponentType.TMPDropdown or TextComponentType.LegacyDropdown,
                _ => true
            };
        }

        private static bool IsBroken(RowState state) => state is RowState.NoKey or RowState.MissingKey or RowState.BadIndex;

        private static bool IsDropdown(TextComponentType type) => type is TextComponentType.TMPDropdown or TextComponentType.LegacyDropdown;

        private void UpdateRowState(Row row)
        {
            row.Detail = null;

            if (row.Localization == null)
            {
                row.State = RowState.NotLocalized;
                row.Preview = GetCurrentText(row);
                return;
            }

            string key = row.Localization.TranslationKey;
            if (string.IsNullOrEmpty(key))
            {
                row.State = RowState.NoKey;
                row.Detail = "No key is set";
                row.Preview = null;
                return;
            }

            if (!Data.LanguageData.TryGetValue(key, out var keyData))
            {
                row.State = RowState.MissingKey;
                row.Detail = $"Key '{key}' doesn't exist. It may have been renamed or deleted.";
                row.Preview = null;
                return;
            }

            keyData.TryGetValue(DefaultLanguage, out var value);
            var list = LanguageEditorData.ConvertToList(value);
            if (list != null)
            {
                int index = row.Localization.ArrayIndex;
                if (IsDropdown(row.Type))
                {
                    int limit = row.Localization.ArraySizeLimit;
                    row.Preview = string.Join(", ", limit > 0 ? list.Take(limit) : list);
                }
                else if (index >= list.Count)
                {
                    row.State = RowState.BadIndex;
                    row.Detail = $"Element {index} doesn't exist; the array has {list.Count}.";
                    row.Preview = null;
                    return;
                }
                else
                {
                    row.Preview = index >= 0 ? list[index] : list.FirstOrDefault();
                }
            }
            else
            {
                row.Preview = value as string;
            }

            row.Preview = row.Preview?.Replace('\n', ' ');

            var status = Data.GetKeyStatus(key);
            if (status.Missing > 0)
            {
                row.State = RowState.Untranslated;
                row.Detail = "Missing: " + string.Join(", ", Data.LanguageCodes
                    .Where(lang => !Data.IsTranslated(key, lang))
                    .Select(lang => PicoShot.Localization.Data.LanguageDefinitions.GetDisplayName(lang)));
                return;
            }

            row.State = RowState.Ok;
        }

        private static string GetCurrentText(Row row)
        {
            string text = row.Text switch
            {
                TMP_Dropdown dropdown => string.Join(", ", dropdown.options.Select(o => o.text)),
                Dropdown dropdown => string.Join(", ", dropdown.options.Select(o => o.text)),
                TMP_Text tmp => tmp.text,
                Text legacy => legacy.text,
                TextMesh mesh => mesh.text,
                _ => ""
            };
            return text?.Replace('\n', ' ') ?? "";
        }

        private static List<string> GetDropdownOptions(Row row)
        {
            return row.Text switch
            {
                TMP_Dropdown dropdown => dropdown.options.Select(o => o.text ?? "").ToList(),
                Dropdown dropdown => dropdown.options.Select(o => o.text ?? "").ToList(),
                _ => null
            };
        }

        private static string GetRawText(Row row)
        {
            return row.Text switch
            {
                TMP_Text tmp => tmp.text,
                Text legacy => legacy.text,
                TextMesh mesh => mesh.text,
                _ => null
            };
        }

        #endregion

        #region Table

        private RowLayout LayoutRow(Rect row)
        {
            var content = new Rect(row.x + RowPadding, row.y, row.width - RowPadding * 2f, row.height);
            var layout = new RowLayout
            {
                Menu = new Rect(content.xMax - MenuWidth, content.y + 2f, MenuWidth, content.height - 4f)
            };
            layout.Status = new Rect(layout.Menu.x - 6f - StatusWidth, content.y, StatusWidth, content.height);

            float remaining = Mathf.Max(0f, layout.Status.x - 6f - content.x);
            float objectWidth = Mathf.Max(100f, remaining * 0.32f);
            layout.Object = new Rect(content.x, content.y, Mathf.Min(objectWidth, remaining), content.height);
            layout.Type = new Rect(layout.Object.xMax + 6f, content.y, TypeWidth, content.height);

            float keyX = layout.Type.xMax + 6f;
            float rest = Mathf.Max(0f, layout.Status.x - 6f - keyX);
            bool showPreview = rest >= 260f;
            float keyWidth = showPreview ? rest * 0.5f : rest;
            layout.Key = new Rect(keyX, content.y, keyWidth, content.height);
            layout.Preview = showPreview ? new Rect(layout.Key.xMax + 6f, content.y, rest - keyWidth - 6f, content.height) : default;

            return layout;
        }

        private void DrawTable(List<Row> rows)
        {
            var headerRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            if (_hadScrollbar)
                headerRect.width -= 14f;
            var header = LayoutRow(headerRect);

            GUI.Label(header.Object, "Object", Styles.MutedLabel);
            GUI.Label(header.Type, "Type", Styles.MutedLabel);
            GUI.Label(header.Key, "Key", Styles.MutedLabel);
            if (header.Preview.width > 0f)
                GUI.Label(header.Preview, new GUIContent("Text", $"Text in the default language ({DefaultLanguage})"), Styles.MutedLabel);
            GUI.Label(header.Status, "Status", Styles.MutedLabel);

            var viewRect = GUILayoutUtility.GetRect(0f, float.MaxValue, 0f, float.MaxValue,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            float contentHeight = rows.Count * RowHeight;
            bool scrollbar = contentHeight > viewRect.height;
            if (Event.current.type == EventType.Repaint && scrollbar != _hadScrollbar)
            {
                _hadScrollbar = scrollbar;
                Editor.Repaint();
            }
            float contentWidth = scrollbar ? viewRect.width - 14f : viewRect.width;

            _scroll = GUI.BeginScrollView(viewRect, _scroll, new Rect(0f, 0f, contentWidth, contentHeight));
            {
                int start = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / RowHeight));
                int end = Mathf.Min(rows.Count, start + Mathf.CeilToInt(viewRect.height / RowHeight) + 1);
                for (int i = start; i < end; i++)
                    DrawRow(rows[i], i, new Rect(0f, i * RowHeight, contentWidth, RowHeight));
            }
            GUI.EndScrollView();
        }

        private void DrawRow(Row row, int index, Rect rect)
        {
            var evt = Event.current;
            var layout = LayoutRow(rect);
            bool hover = rect.Contains(evt.mousePosition);
            bool highlighted = row.GameObject == _highlighted;

            Styles.DrawRowBackground(rect, index, hover, highlighted);

            // Object
            var pathStyle = row.GameObject.activeInHierarchy ? Styles.RowLabel : Styles.RowLabelMuted;
            string pathTooltip = row.Scene != null ? $"{row.Scene}: {row.Path}" : row.Path;
            GUI.Label(layout.Object, new GUIContent(row.Path, pathTooltip), pathStyle);

            // Type
            var (typeText, typeTooltip) = GetTypeLabel(row.Type);
            Styles.DrawBadge(layout.Type, typeText, Styles.MutedText, typeTooltip);

            // Key and text
            if (row.State == RowState.NotLocalized)
            {
                var textRect = layout.Preview.width > 0f
                    ? new Rect(layout.Key.x, layout.Key.y, layout.Preview.xMax - layout.Key.x, layout.Key.height)
                    : layout.Key;
                string current = string.IsNullOrEmpty(row.Preview) ? "(empty)" : $"\"{row.Preview}\"";
                GUI.Label(textRect, new GUIContent(current, "Current text. Not localized yet."), Styles.MutedLabel);
            }
            else
            {
                DrawKeyCell(row, layout.Key);
                if (layout.Preview.width > 0f && !string.IsNullOrEmpty(row.Preview))
                    GUI.Label(layout.Preview, new GUIContent(row.Preview, row.Preview), Styles.MutedLabel);
            }

            DrawStatusCell(row, layout.Status);

            if (GUI.Button(layout.Menu, EditorGUIUtility.IconContent("_Menu"), EditorStyles.iconButton))
                ShowRowMenu(row, layout.Menu);

            // Row click: highlight and ping; in Scene mode also select
            if (evt.type == EventType.MouseDown && evt.button == 0 && hover &&
                !layout.Key.Contains(evt.mousePosition) && !layout.Status.Contains(evt.mousePosition) && !layout.Menu.Contains(evt.mousePosition))
            {
                _highlighted = row.GameObject;
                if (_mode == Mode.Prefabs)
                {
                    EditorGUIUtility.PingObject(row.PrefabRoot);
                    if (evt.clickCount == 2)
                        OpenInPrefabMode(row);
                }
                else
                {
                    EditorGUIUtility.PingObject(row.GameObject);
                    if (_mode == Mode.Scene || evt.clickCount == 2)
                        Selection.activeGameObject = row.GameObject;
                }
                GUIUtility.keyboardControl = 0;
                evt.Use();
            }
            else if (evt.type == EventType.ContextClick && hover)
            {
                _highlighted = row.GameObject;
                ShowRowMenu(row, null);
                evt.Use();
            }
        }

        private static (string, string) GetTypeLabel(TextComponentType type)
        {
            return type switch
            {
                TextComponentType.TMPText => ("TMP", "TextMesh Pro text"),
                TextComponentType.TMPDropdown => ("TMP List", "TextMesh Pro dropdown"),
                TextComponentType.LegacyText => ("Text", "Legacy UI Text"),
                TextComponentType.LegacyDropdown => ("List", "Legacy UI Dropdown"),
                TextComponentType.TextMesh => ("3D", "TextMesh (3D)"),
                _ => ("?", "No supported text component")
            };
        }

        private void DrawKeyCell(Row row, Rect rect)
        {
            var loc = row.Localization;
            string key = loc.TranslationKey;

            bool isArray = !string.IsNullOrEmpty(key) && Data.LanguageData.TryGetValue(key, out var keyData) && LanguageEditorData.IsArrayKey(keyData);
            if (isArray)
            {
                var fieldRect = new Rect(rect.xMax - IndexFieldWidth, rect.y + 3f, IndexFieldWidth, rect.height - 6f);
                rect.width -= IndexFieldWidth + 4f;

                bool dropdown = IsDropdown(row.Type);
                string property = dropdown ? "arraySizeLimit" : "arrayIndex";
                int current = dropdown ? loc.ArraySizeLimit : loc.ArrayIndex;
                string tooltip = dropdown ? "Maximum number of options (0 = all)" : "Array element to show (-1 = first)";

                EditorGUI.BeginChangeCheck();
                int value = EditorGUI.IntField(fieldRect, current);
                GUI.Label(fieldRect, new GUIContent("", tooltip));
                if (EditorGUI.EndChangeCheck())
                    SetIntProperty(loc, property, dropdown ? Mathf.Max(0, value) : Mathf.Max(-1, value));
            }

            var popupRect = new Rect(rect.x, rect.y + 3f, rect.width, rect.height - 6f);
            var content = new GUIContent(string.IsNullOrEmpty(key) ? "Select a key…" : key, key);
            if (EditorGUI.DropdownButton(popupRect, content, FocusType.Keyboard, EditorStyles.popup))
                ShowKeyPicker(loc, popupRect, centered: false);

            if (row.State == RowState.MissingKey)
            {
                float w = Styles.GetBadgeWidth("MISSING");
                Styles.DrawBadge(new Rect(popupRect.xMax - w - 16f, rect.y, w, rect.height), "MISSING", Styles.Danger, row.Detail);
            }
        }

        private void DrawStatusCell(Row row, Rect rect)
        {
            switch (row.State)
            {
                case RowState.NotLocalized:
                    var buttonRect = new Rect(rect.x, rect.y + 3f, rect.width, rect.height - 6f);
                    if (EditorGUI.DropdownButton(buttonRect, new GUIContent("Localize", "Create or pick a key for this text"),
                            FocusType.Passive, EditorStyles.miniPullDown))
                        ShowLocalizeMenu(row, buttonRect);
                    break;
                case RowState.NoKey:
                    Styles.DrawBadge(rect, "No key", Styles.Danger, row.Detail);
                    break;
                case RowState.MissingKey:
                    Styles.DrawBadge(rect, "Broken", Styles.Danger, row.Detail);
                    break;
                case RowState.BadIndex:
                    Styles.DrawBadge(rect, "Bad index", Styles.Danger, row.Detail);
                    break;
                case RowState.Untranslated:
                    int missing = Data.GetKeyStatus(row.Localization.TranslationKey).Missing;
                    Styles.DrawBadge(rect, $"{missing} missing", Styles.Warning, row.Detail);
                    break;
                default:
                    Styles.DrawBadge(rect, "OK", Styles.Success, "Key exists and is translated in every language");
                    break;
            }
        }

        #endregion

        #region Menus

        private void ShowRowMenu(Row row, Rect? dropDownRect)
        {
            var menu = new GenericMenu();
            var go = row.GameObject;

            if (_mode == Mode.Prefabs)
            {
                menu.AddItem(new GUIContent("Open Prefab"), false, () => OpenInPrefabMode(row));
                menu.AddItem(new GUIContent("Select Prefab Asset"), false, () =>
                {
                    Selection.activeObject = row.PrefabRoot;
                    EditorGUIUtility.PingObject(row.PrefabRoot);
                });
            }
            else
            {
                menu.AddItem(new GUIContent("Select in Hierarchy"), false, () =>
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                });
            }

            if (row.Localization != null)
            {
                var loc = row.Localization;
                string key = loc.TranslationKey;

                if (!string.IsNullOrEmpty(key) && Data.LanguageData.ContainsKey(key))
                    menu.AddItem(new GUIContent("Open Key in Keys Tab"), false, () => Editor.ShowKey(key));
                else
                    menu.AddDisabledItem(new GUIContent("Open Key in Keys Tab"));

                menu.AddItem(new GUIContent("Change Key…"), false, () => ShowKeyPicker(loc, default, centered: true));
                menu.AddSeparator("");
                if (_mode == Mode.Prefabs)
                {
                    menu.AddDisabledItem(new GUIContent("Remove Localization (open the prefab to remove it)"));
                }
                else
                {
                    menu.AddItem(new GUIContent("Remove Localization"), false, () =>
                    {
                        if (loc == null)
                            return;
                        Undo.DestroyObjectImmediate(loc);
                        _rowsDirty = true;
                        Editor.ShowNotification(new GUIContent($"Removed localization from {go.name} ({UndoShortcut} to undo)"));
                    });
                }
            }
            else
            {
                menu.AddSeparator("");
                AddLocalizeItems(menu, row, "Localize/");
            }

            if (dropDownRect.HasValue)
                menu.DropDown(dropDownRect.Value);
            else
                menu.ShowAsContext();
        }

        private void ShowLocalizeMenu(Row row, Rect rect)
        {
            var menu = new GenericMenu();
            AddLocalizeItems(menu, row, "");
            menu.DropDown(rect);
        }

        private void AddLocalizeItems(GenericMenu menu, Row row, string path)
        {
            string text = IsDropdown(row.Type) ? null : GetRawText(row);
            var options = GetDropdownOptions(row);
            bool hasContent = !string.IsNullOrWhiteSpace(text) || (options != null && options.Any(o => !string.IsNullOrWhiteSpace(o)));
            string suggested = SuggestKey(row, null);

            if (hasContent)
            {
                menu.AddItem(new GUIContent($"{path}Create Key '{suggested}'"), false, () => LocalizeRow(row, suggested, true));
                menu.AddItem(new GUIContent($"{path}Create Key with Name…"), false, () =>
                    LocalizationTextEditorPopup.Open(suggested, name =>
                    {
                        name = LocalizationTextEditorPopup.FilterKeyName(name?.Trim() ?? "");
                        if (string.IsNullOrEmpty(name))
                            return;
                        if (Data.LanguageData.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            EditorUtility.DisplayDialog("Create Key", $"Key '{name}' already exists.", "OK");
                            return;
                        }
                        LocalizeRow(row, name, true);
                    }, isKeyName: true));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"{path}Create Key (component has no text)"));
            }

            string existing = text != null ? FindKeyWithText(text) : null;
            if (existing != null)
                menu.AddItem(new GUIContent($"{path}Use Existing Key '{existing}' (same text)"), false, () => LocalizeRow(row, existing, false));

            menu.AddItem(new GUIContent($"{path}Choose Key…"), false, () =>
            {
                var loc = LocalizeRow(row, null, false);
                if (loc != null)
                    ShowKeyPicker(loc, default, centered: true);
            });
            menu.AddItem(new GUIContent($"{path}Add Component Only"), false, () => LocalizeRow(row, null, false));
        }

        private void ShowKeyPicker(LocalizationTextComponent loc, Rect rect, bool centered)
        {
            var keys = Data.Keys.ToArray();
            int selected = Array.IndexOf(keys, loc.TranslationKey);

            void OnPicked(int index)
            {
                if (index < 0 || index >= keys.Length || loc == null)
                    return;
                SetKeyProperty(loc, keys[index]);
                Editor.Repaint();
            }

            if (centered)
                LocalizationSearchablePopup.ShowCenteredOnWindow(WindowPosition, keys, selected, OnPicked);
            else
                LocalizationSearchablePopup.Show(rect, keys, selected, OnPicked);
        }

        private static string UndoShortcut => Application.platform == RuntimePlatform.OSXEditor ? "Cmd+Z" : "Ctrl+Z";

        #endregion

        #region Localize

        /// <summary>
        /// Adds the component (if needed), optionally creates the key from the current text, and assigns it, as one undo step.
        /// </summary>
        private LocalizationTextComponent LocalizeRow(Row row, string key, bool createKey)
        {
            if (row.GameObject == null)
                return null;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            if (createKey)
            {
                Data.History.RecordKeys("Create Key", key);
                CreateKeyFromRow(row, key);
            }

            var loc = row.Localization != null ? row.Localization : Undo.AddComponent<LocalizationTextComponent>(row.GameObject);
            if (key != null)
                SetKeyProperty(loc, key);

            Undo.SetCurrentGroupName("Localize Text");
            Undo.CollapseUndoOperations(group);

            row.Localization = loc;
            _rowsDirty = true;
            if (createKey)
                Editor.ShowNotification(new GUIContent($"Created '{key}'. Save to write it to the locale files."));
            Editor.Repaint();
            return loc;
        }

        private void CreateKeyFromRow(Row row, string key)
        {
            var options = GetDropdownOptions(row);
            bool isArray = options != null;
            if (!Data.AddKey(key, isArray))
                return;

            string defaultLang = DefaultLanguage;
            var keyData = Data.LanguageData[key];
            if (isArray)
            {
                foreach (var lang in Data.LanguageCodes)
                    ((List<string>)keyData[lang]).AddRange(lang == defaultLang ? options : options.Select(_ => ""));
            }
            else
            {
                keyData[defaultLang] = GetRawText(row) ?? "";
            }
            Data.HasUnsavedChanges = true;
        }

        private void LocalizeAll()
        {
            var pending = _rows.Where(r => r.GameObject != null && r.Localization == null).ToList();
            var plan = new List<(Row row, string key, bool create)>();
            var skipped = new List<Row>();
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var createdByText = new Dictionary<string, string>();

            foreach (var row in pending)
            {
                var options = GetDropdownOptions(row);
                string text = options != null ? string.Join("\n", options) : GetRawText(row);
                if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetter))
                {
                    skipped.Add(row);
                    continue;
                }

                string existing = options == null ? FindKeyWithText(text) : null;
                if (existing != null)
                {
                    plan.Add((row, existing, false));
                }
                else if (createdByText.TryGetValue(text, out var sameText))
                {
                    plan.Add((row, sameText, false));
                }
                else
                {
                    string key = SuggestKey(row, reserved);
                    reserved.Add(key);
                    createdByText[text] = key;
                    plan.Add((row, key, true));
                }
            }

            if (plan.Count == 0)
            {
                EditorUtility.DisplayDialog("Localize All", skipped.Count > 0
                    ? "Every remaining component is empty or has no letters (numbers, symbols), so there is nothing to localize."
                    : "Everything is already localized.", "OK");
                return;
            }

            var created = plan.Where(p => p.create).ToList();
            var message = new StringBuilder();
            message.AppendLine($"Localize {plan.Count} {(plan.Count == 1 ? "component" : "components")}?");
            if (created.Count > 0)
            {
                message.AppendLine();
                message.AppendLine($"New keys ({created.Count}):");
                foreach (var (row, key, _) in created.Take(MaxPlanLines))
                    message.AppendLine($"  {key} = \"{Truncate(GetCurrentText(row), 40)}\"");
                if (created.Count > MaxPlanLines)
                    message.AppendLine($"  … and {created.Count - MaxPlanLines} more");
            }
            int reused = plan.Count - created.Count;
            if (reused > 0)
                message.AppendLine($"\nReusing existing keys with the same text: {reused}");
            if (skipped.Count > 0)
                message.AppendLine($"Skipped without text (empty or numbers only): {skipped.Count}");

            if (!EditorUtility.DisplayDialog("Localize All", message.ToString(), "Localize", "Cancel"))
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            if (created.Count > 0)
            {
                Data.History.RecordKeys("Create Keys", created.Select(p => p.key).ToArray());
                foreach (var (row, key, _) in created)
                    CreateKeyFromRow(row, key);
            }

            foreach (var (row, key, _) in plan)
            {
                var loc = Undo.AddComponent<LocalizationTextComponent>(row.GameObject);
                SetKeyProperty(loc, key);
            }

            Undo.SetCurrentGroupName("Localize All Text");
            Undo.CollapseUndoOperations(group);

            _rowsDirty = true;
            Editor.ShowNotification(new GUIContent($"Localized {plan.Count} components. Save to write new keys."));
            Editor.Repaint();
        }

        private static string Truncate(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length) + "…";
        }

        /// <summary>
        /// Suggests a unique key from the prefix and object name, e.g. "main_menu.play_button".
        /// Generic names like "Text (TMP)" use the parent's name instead.
        /// </summary>
        private string SuggestKey(Row row, HashSet<string> reserved)
        {
            var transform = row.GameObject.transform;
            string name = transform.name;
            if (GenericObjectNames.Contains(name.Trim()) && transform.parent != null)
                name = transform.parent.name;

            string prefix = _mode == Mode.Selection ? _prefix : (string.IsNullOrEmpty(Data.SelectedView) ? "" : Data.SelectedView + Data.CurrentViewDelimiter);
            string baseKey = prefix + ToKeySegment(name);
            string key = baseKey;

            for (int i = 2; KeyTaken(key) || (reserved != null && reserved.Contains(key)); i++)
                key = $"{baseKey}_{i}";

            return key;
        }

        private bool KeyTaken(string key)
        {
            return Data.LanguageData.Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// "PlayButton (1)" → "play_button_1"
        /// </summary>
        private static string ToKeySegment(string name)
        {
            var sb = new StringBuilder();
            char previous = '\0';
            foreach (char c in name ?? "")
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    if (char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous)) && sb.Length > 0)
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
                previous = c;
            }

            string result = sb.ToString().Trim('_');
            return result.Length > 0 ? result : "text";
        }

        /// <summary>
        /// Finds a string key whose default-language text equals the given text.
        /// </summary>
        private string FindKeyWithText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (_textLookupVersion != Data.DataVersion)
            {
                _keyByText.Clear();
                string defaultLang = DefaultLanguage;
                foreach (var key in Data.Keys)
                {
                    if (Data.LanguageData[key].TryGetValue(defaultLang, out var value) &&
                        value is string str && !string.IsNullOrWhiteSpace(str) && !_keyByText.ContainsKey(str.Trim()))
                        _keyByText[str.Trim()] = key;
                }
                _textLookupVersion = Data.DataVersion;
            }

            return _keyByText.TryGetValue(text.Trim(), out var found) ? found : null;
        }

        private static void SetKeyProperty(LocalizationTextComponent loc, string key)
        {
            var serialized = new SerializedObject(loc);
            serialized.FindProperty("translationKey").stringValue = key;
            serialized.ApplyModifiedProperties();
            SaveIfAsset(loc);
        }

        private static void SetIntProperty(LocalizationTextComponent loc, string property, int value)
        {
            var serialized = new SerializedObject(loc);
            serialized.FindProperty(property).intValue = value;
            serialized.ApplyModifiedProperties();
            SaveIfAsset(loc);
        }

        /// <summary>
        /// Edits made from Prefabs mode change the prefab asset directly; write it to disk right away.
        /// </summary>
        private static void SaveIfAsset(Object target)
        {
            if (EditorUtility.IsPersistent(target))
                AssetDatabase.SaveAssetIfDirty(target);
        }

        #endregion

        #region Prefab Scan

        /// <summary>
        /// Loads every prefab under Assets and collects its localized components. Components inherited
        /// unchanged from a nested prefab or a variant base are listed only once, at their source prefab.
        /// </summary>
        private void ScanPrefabs()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _prefabRows.Clear();
            int scanned = 0;
            int withText = 0;
            bool cancelled = false;
            var noExclusions = new HashSet<Component>();

            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning Prefabs", path, (float)i / paths.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    scanned++;
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null)
                        continue;

                    bool found = false;
                    foreach (var loc in root.GetComponentsInChildren<LocalizationTextComponent>(true))
                    {
                        if (IsInheritedUnchanged(loc))
                            continue;

                        var (text, type) = GetPrimaryText(loc.gameObject, noExclusions);
                        string inner = GetInnerPath(loc.transform, root.transform);
                        _prefabRows.Add(new Row
                        {
                            GameObject = loc.gameObject,
                            Text = text,
                            Type = type,
                            Localization = loc,
                            PrefabRoot = root,
                            InnerPath = inner,
                            Path = inner.Length == 0 ? root.name : $"{root.name}/{inner}",
                            Scene = path
                        });
                        found = true;
                    }

                    if (found)
                        withText++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _prefabsScanned = scanned;
            _prefabsWithText = withText;
            _prefabScanTime = DateTime.Now;
            _rowsDirty = true;
            _scroll = Vector2.zero;

            if (cancelled)
                Editor.ShowNotification(new GUIContent($"Scan stopped after {scanned} of {paths.Count} prefabs"));
        }

        private static bool IsInheritedUnchanged(LocalizationTextComponent loc)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(loc);
            return source != null &&
                   source.TranslationKey == loc.TranslationKey &&
                   source.ArrayIndex == loc.ArrayIndex &&
                   source.ArraySizeLimit == loc.ArraySizeLimit;
        }

        /// <summary>
        /// Path below the prefab root, without the root's own name ("" for the root itself).
        /// </summary>
        private static string GetInnerPath(Transform transform, Transform root)
        {
            var parts = new List<string>();
            for (var t = transform; t != null && t != root; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void OpenInPrefabMode(Row row)
        {
            var root = row.PrefabRoot;
            string inner = row.InnerPath;

            EditorApplication.delayCall += () =>
            {
                if (root == null || !AssetDatabase.OpenAsset(root))
                    return;

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                    return;

                var contents = stage.prefabContentsRoot.transform;
                var target = string.IsNullOrEmpty(inner) ? contents : contents.Find(inner);
                if (target == null)
                    return;

                Selection.activeGameObject = target.gameObject;
                EditorGUIUtility.PingObject(target.gameObject);
            };
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60) return "just now";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
            return $"at {DateTime.Now - age:HH:mm}";
        }

        #endregion

        #region Footer

        private void DrawFooter()
        {
            Styles.DrawSeparator(2f, 0f);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
            {
                if (_mode == Mode.Selection && Data.SelectedGameObject != null)
                {
                    int notLocalized = _rows.Count(r => r.GameObject != null && r.Localization == null);
                    GUILayout.Label(notLocalized == 0 ? "Everything here is localized" : $"{notLocalized} not localized", Styles.MutedLabel);
                    GUILayout.FlexibleSpace();

                    GUILayout.Label(new GUIContent("Key prefix", "Prepended to suggested key names"), Styles.MutedLabel);
                    _prefix = LocalizationTextEditorPopup.FilterKeyName(EditorGUILayout.TextField(_prefix, GUILayout.Width(140)));

                    using (new EditorGUI.DisabledScope(notLocalized == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Localize All…", "Create keys from the current text and localize every remaining component"),
                                EditorStyles.miniButton, GUILayout.Width(96)))
                        {
                            LocalizeAll();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                else if (_mode == Mode.Scene)
                {
                    GUILayout.Label("Click a row to select the object · right-click for more", Styles.MutedLabel);
                    GUILayout.FlexibleSpace();
                }
                else if (_mode == Mode.Prefabs && _prefabScanTime != null)
                {
                    GUILayout.Label("Click to find the prefab · double-click to open it · right-click for more", Styles.MutedLabel);
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
