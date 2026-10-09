using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Editor.Data;
using Styles = PicoShot.Localization.Editor.LocalizationEditorStyles;

namespace PicoShot.Localization.Editor
{
    public sealed class LocalizationWelcomeWindow : EditorWindow
    {
        private const string SeenPref = "PicoShot_Localization_TourSeenVersion";
        private const float HeaderHeight = 78f;
        private const float SidePadding = 24f;
        private static readonly Vector2 WindowSize = new(640f, 560f);

        private enum Extra
        {
            None,
            BadgeLegend,
            Shortcuts
        }

        private sealed class Page
        {
            public string Title;
            public string Subtitle;
            public string Tab;
            public (string heading, string text)[] Items = Array.Empty<(string, string)>();
            public Extra Extra;
        }

        private List<Page> _pages;
        private int _page;
        private Vector2 _scroll;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _bodyStyle;

        private static string Mod => Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

        #region Opening

        [InitializeOnLoadMethod]
        private static void ScheduleAutoOpen()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += AutoOpen;
        }

        private static void AutoOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || HasSeenCurrentTour())
                return;

            bool returningUser = LooksLikeExistingProject();
            EditorPrefs.SetString(SeenPref, PackageVersion.Current);
            Open(returningUser);
        }

        public static void OpenFromMenu()
        {
            Open(LooksLikeExistingProject());
        }

        public static void Open(bool returningUser)
        {
            var window = GetWindow<LocalizationWelcomeWindow>(true, "PicoShot Localization", true);
            window._pages = BuildPages(returningUser);
            window._page = 0;
            window._scroll = Vector2.zero;
            window.minSize = window.maxSize = WindowSize;

            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center.x - WindowSize.x * 0.5f, main.center.y - WindowSize.y * 0.5f, WindowSize.x, WindowSize.y);
        }

        private static bool HasSeenCurrentTour()
        {
            // Compare major.minor only, so patch releases don't show the tour again
            string seen = EditorPrefs.GetString(SeenPref, "");
            return Version.TryParse(seen, out var seenVersion) &&
                   (seenVersion.Major > PackageVersion.Major ||
                    (seenVersion.Major == PackageVersion.Major && seenVersion.Minor >= PackageVersion.Minor));
        }

        /// <summary>
        /// True when the package was used here before: a previous tour, saved locale files, or settings and
        /// API keys written by an older version.
        /// </summary>
        private static bool LooksLikeExistingProject()
        {
            if (EditorPrefs.HasKey(SeenPref))
                return true;

            if (EditorPrefs.HasKey(LanguageEditorData.DeeplApiKeyPref) || EditorPrefs.HasKey(LanguageEditorData.GeminiApiKeyPref))
                return true;

            if (PlayerPrefs.HasKey(LanguageEditorData.TranslationProviderPref) || PlayerPrefs.HasKey(LanguageEditorData.ViewDelimiterPref) ||
                EditorUserSettings.GetConfigValue(LanguageEditorData.TranslationProviderPref) != null)
                return true;

            try
            {
                string path = LocalizationManager.LanguagesPath;
                return Directory.Exists(path) && Directory.GetFiles(path, "*" + LocalizationManager.FileExtension).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Content

        private static List<Page> BuildPages(bool returningUser)
        {
            var pages = new List<Page>();

            if (returningUser)
            {
                pages.Add(new Page
                {
                    Title = $"What's new in {PackageVersion.Major}.{PackageVersion.Minor}",
                    Subtitle = "The Language Editor has been redesigned. Here's a one-minute tour of what changed.",
                    Items = new[]
                    {
                        ("A cleaner editor", "Every tab was rebuilt around tables, badges and search, so the state of your project is visible at a glance."),
                        ("Undo everywhere", $"{Mod}+Z and {Mod}+Y now work for key edits, translations, deletes, imports and bulk actions."),
                        ("Safer changes", "Removing a language deletes its file only when you save, and it can be undone. Editor settings no longer live in your game's PlayerPrefs."),
                        ("Problems surface early", "Missing translations, broken placeholders, missing font glyphs and broken component keys are flagged right where they happen."),
                        ("Faster, smaller locale files (BLOC v3)", "About 25% smaller files, a language loads several times faster with a fraction of the memory, and key lookups are near-instant. Older files still load, and the editor offers to upgrade them."),
                        ("AI agents via MCP", "A built-in MCP server lets agents such as Claude Code or Codex list, add, rename and translate keys in batches, and check placeholders.")
                    }
                });
            }
            else
            {
                pages.Add(new Page
                {
                    Title = "Welcome to PicoShot Localization",
                    Subtitle = "Get your game translated in four steps. The next pages show each part of the editor.",
                    Items = new[]
                    {
                        ("1. Add languages", "In the Localization tab, add the languages you want to ship. Your source language is marked DEFAULT."),
                        ("2. Create keys", "In the Keys tab, press + New Key and type the source text. Translate inline, or let DeepL or Gemini fill in the rest."),
                        ("3. Localize your UI", "In the Components tab, select a menu in the Hierarchy and press Localize All. Keys are created from the text already there."),
                        ("4. Save and use", $"{Mod}+S writes the locale files. In code, use LocalizationManager.GetText(\"menu.play\").")
                    }
                });
            }

            pages.Add(new Page
            {
                Title = "Languages & Fonts",
                Subtitle = "The Localization tab: which languages you ship, and the fonts they use.",
                Tab = "Localization",
                Items = new[]
                {
                    ("Your languages at a glance", "Each language shows its translation progress. Right-click a row to set it as the default, show its untranslated keys, or remove it."),
                    ("Adding a language", "Search by name, code or native name. Common shows the usual game languages; Enter adds the only match."),
                    ("Fonts that cover your text", "The Fonts sub-tab checks every character of each language against its font and fallbacks, and tells you how many glyphs are missing.")
                }
            });

            pages.Add(new Page
            {
                Title = "Keys",
                Subtitle = "Where you write and translate. Click a key on the left, edit it on the right.",
                Tab = "Keys",
                Items = new[]
                {
                    ("Edit in place", $"Type translations directly. {Mod}+E opens a larger editor with the source text and placeholder checks."),
                    ("Search and filter", "Search also matches translation text. Filter by type, missing translations or problems."),
                    ("Many keys at once", $"{Mod}+click or Shift+click selects several keys to translate, move to another view, or delete together.")
                },
                Extra = Extra.BadgeLegend
            });

            pages.Add(new Page
            {
                Title = "Components",
                Subtitle = "Connect text in your scenes and prefabs to keys.",
                Tab = "Components",
                Items = new[]
                {
                    ("Follows your selection", "Select a GameObject in the Hierarchy to see every text under it. Use the lock to stay on one object."),
                    ("Localize in one click", "Localize creates a key from the current text. Localize All does the whole hierarchy and reuses keys with the same text."),
                    ("Audit scenes and prefabs", "Scene and Prefabs modes list every localized component and flag keys that are missing or broken.")
                }
            });

            pages.Add(new Page
            {
                Title = "Tools & Settings",
                Subtitle = "Project-wide checks and the options behind the scenes.",
                Tab = "Tools",
                Items = new[]
                {
                    ("Translation Health", "One place for missing translations, placeholder mismatches, empty source text and duplicates."),
                    ("Bulk Translate", "Fill every missing translation with DeepL or Gemini, with progress, Cancel, and a single undo step."),
                    ("Character sets", "Copy or save the exact characters each language needs for static TMP font assets."),
                    ("Settings", "Test your API key, see where each setting is stored, and manage compression, protection and the MCP server.")
                }
            });

            pages.Add(new Page
            {
                Title = "AI Agents (MCP)",
                Subtitle = "Let an AI coding agent do the repetitive translation work, through the same locale files the editor uses.",
                Tab = "Settings",
                Items = new[]
                {
                    ("What it does", "The MCP server lets agents such as Claude Code, Codex or opencode read and edit your keys, translations and languages."),
                    ("Getting started", "In Settings > MCP, press Start, then Copy Agent Config and paste it into your agent's MCP settings. Turn on Start automatically to keep it running."),
                    ("Built for batches", "Agents fetch the untranslated text for a language, translate it, and write up to 500 values per call. validate reports missing keys and placeholder or tag mismatches."),
                    ("Stays in sync", "Changes an agent makes reload in the editor automatically. If you have unsaved edits, the editor tells you instead of overwriting them; use the menu next to Save > Reload from Disk.")
                }
            });

            pages.Add(new Page
            {
                Title = "Shortcuts",
                Subtitle = "Save is in the bottom-right corner of the editor and lights up when you have unsaved changes.",
                Extra = Extra.Shortcuts
            });

            return pages;
        }

        #endregion

        #region Drawing

        private void OnGUI()
        {
            if (_pages == null || _pages.Count == 0)
                _pages = BuildPages(LooksLikeExistingProject());

            _page = Mathf.Clamp(_page, 0, _pages.Count - 1);
            InitStyles();
            HandleKeyboard(Event.current);

            var page = _pages[_page];
            DrawHeader(page);

            GUILayout.Space(HeaderHeight + 14f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(SidePadding);
            EditorGUILayout.BeginVertical();
            {
                foreach (var (heading, text) in page.Items)
                {
                    GUILayout.Label(heading, _headingStyle);
                    GUILayout.Label(text, _bodyStyle);
                    EditorGUILayout.Space(10);
                }

                if (page.Extra == Extra.BadgeLegend)
                    DrawBadgeLegend();
                else if (page.Extra == Extra.Shortcuts)
                    DrawShortcuts();

                if (page.Tab != null)
                {
                    EditorGUILayout.Space(4);
                    if (GUILayout.Button($"Show me the {page.Tab} tab", EditorStyles.miniButton, GUILayout.Width(180)))
                        LocalizationEditor.OpenOnTab(page.Tab);
                }
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(SidePadding);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void InitStyles()
        {
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, wordWrap = false };
            _subtitleStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
            _subtitleStyle.normal.textColor = Styles.MutedText;
            _headingStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _bodyStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
        }

        private void DrawHeader(Page page)
        {
            var rect = new Rect(0f, 0f, position.width, HeaderHeight);
            if (Event.current.type == EventType.Repaint)
            {
                var tint = Styles.Accent;
                tint.a = 0.14f;
                EditorGUI.DrawRect(rect, tint);
                EditorGUI.DrawRect(new Rect(0f, 0f, 4f, rect.height), Styles.Accent);
                EditorGUI.DrawRect(new Rect(0f, rect.yMax - 1f, rect.width, 1f), Styles.Separator);
            }

            GUI.Label(new Rect(SidePadding, 14f, rect.width - SidePadding * 2f - 60f, 26f), page.Title, _titleStyle);
            GUI.Label(new Rect(rect.width - SidePadding - 60f, 16f, 60f, 20f), $"{_page + 1} / {_pages.Count}", Styles.MutedLabelRight);
            GUI.Label(new Rect(SidePadding, 42f, rect.width - SidePadding * 2f, 32f), page.Subtitle, _subtitleStyle);
        }

        private void DrawBadgeLegend()
        {
            GUILayout.Label("What the badges mean", _headingStyle);
            DrawLegendRow("2", Styles.Warning, "Languages still missing a translation");
            DrawLegendRow("!", Styles.Danger, "Placeholder or rich-text tag mismatch with the source");
            DrawLegendRow("SOURCE", Styles.Accent, "Default language other languages are translated from");
            DrawLegendRow("RTL", Styles.MutedText, "Right-to-left language");
            EditorGUILayout.Space(8);
        }

        private static void DrawLegendRow(string badge, Color color, string meaning)
        {
            var rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            Styles.DrawBadge(new Rect(rect.x, rect.y, 0f, rect.height), badge, color);
            GUI.Label(new Rect(rect.x + 70f, rect.y, rect.width - 70f, rect.height), meaning, EditorStyles.label);
        }

        private void DrawShortcuts()
        {
            var shortcuts = new (string keys, string action)[]
            {
                ($"{Mod}+S", "Save"),
                ($"{Mod}+Z  /  {Mod}+Y", "Undo / redo"),
                ($"{Mod}+F", "Search keys"),
                ($"{Mod}+E", "Edit the focused value in a larger window"),
                ("F2", "Rename key (or double-click it)"),
                ($"{Mod}+T", "Translate missing languages"),
                ($"{Mod} / Shift + click", "Select several keys"),
                ($"{Mod}+A", "Select every listed key"),
                ("Alt + ↑ / ↓", "Move key up or down"),
                ("Delete", "Delete the selected keys")
            };

            for (int i = 0; i < shortcuts.Length; i++)
            {
                var rect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
                Styles.DrawRowBackground(rect, i, false);
                Styles.DrawBadge(new Rect(rect.x + 6f, rect.y, 0f, rect.height), shortcuts[i].keys, Styles.MutedText);
                GUI.Label(new Rect(rect.x + 170f, rect.y, rect.width - 176f, rect.height), shortcuts[i].action, Styles.RowLabel);
            }
        }

        private void DrawFooter()
        {
            Styles.DrawSeparator(0f, 6f);
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(SidePadding - 4f);
                bool last = _page == _pages.Count - 1;

                if (!last && GUILayout.Button("Skip Tour", EditorStyles.miniButton, GUILayout.Width(80)))
                    Close();

                GUILayout.FlexibleSpace();
                DrawPageDots();
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_page == 0))
                {
                    if (GUILayout.Button("Back", GUILayout.Width(80)))
                        GoTo(_page - 1);
                }

                if (last)
                {
                    if (GUILayout.Button("Open Editor", GUILayout.Width(110)))
                    {
                        Close();
                        LocalizationEditor.OpenOnTab(LooksLikeExistingProject() ? "Keys" : "Localization");
                    }
                }
                else if (GUILayout.Button("Next", GUILayout.Width(80)))
                {
                    GoTo(_page + 1);
                }

                GUILayout.Space(SidePadding - 4f);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
        }

        private void DrawPageDots()
        {
            const float size = 8f;
            const float gap = 6f;
            var rect = GUILayoutUtility.GetRect(_pages.Count * (size + gap), 20f, GUILayout.ExpandWidth(false));

            for (int i = 0; i < _pages.Count; i++)
            {
                var dot = new Rect(rect.x + i * (size + gap), rect.center.y - size * 0.5f, size, size);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(dot, i == _page ? Styles.Accent : Styles.Separator);

                EditorGUIUtility.AddCursorRect(dot, MouseCursor.Link);
                if (GUI.Button(new Rect(dot.x - 2f, dot.y - 4f, size + 4f, size + 8f), GUIContent.none, GUIStyle.none))
                    GoTo(i);
            }
        }

        private void HandleKeyboard(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return;

            switch (evt.keyCode)
            {
                case KeyCode.RightArrow:
                    GoTo(_page + 1);
                    break;
                case KeyCode.LeftArrow:
                    GoTo(_page - 1);
                    break;
                case KeyCode.Escape:
                    Close();
                    break;
                default:
                    return;
            }
            evt.Use();
        }

        private void GoTo(int page)
        {
            _page = Mathf.Clamp(page, 0, _pages.Count - 1);
            _scroll = Vector2.zero;
            Repaint();
        }

        #endregion
    }
}
