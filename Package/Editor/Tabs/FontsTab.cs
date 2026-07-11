using UnityEditor;
using UnityEngine;
using TMPro;
using PicoShot.Localization.Config;
using PicoShot.Localization.Editor.Data;
using System.Linq;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Tabs
{
    public sealed class FontsTab : LocalizationEditorTabBase
    {
        private Vector2 _scrollPosition;

        public FontsTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Fonts";

        public override void Draw()
        {
            var config = LocalizationConfigProvider.Config;

            if (!config.IsFontSystemEnabled)
            {
                DrawDisabledState(config);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawSectionHeader("Font System");
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Disable Font System", GUILayout.Width(150)))
            {
                config.SetFontSystemEnabled(false);
                LocalizationConfigProvider.SaveConfig();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            DrawDefaultFontsSection(config);

            EditorGUILayout.Space();
            DrawLanguageFontsSection(config);

            EditorGUILayout.EndScrollView();
        }

        private void DrawDisabledState(LocalizationConfig config)
        {
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            EditorGUILayout.LabelField("Language-Specific Fonts", EditorStyles.boldLabel, GUILayout.Height(30));
            EditorGUILayout.HelpBox("The Font System is currently disabled. Enable it to assign specific fonts to different languages, which will automatically update UI text components when the language changes.", MessageType.Info);
            
            EditorGUILayout.Space();
            
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("Enable Font System", GUILayout.Height(40)))
            {
                config.SetFontSystemEnabled(true);
                LocalizationConfigProvider.SaveConfig();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUILayout.FlexibleSpace();
        }

        private void DrawDefaultFontsSection(LocalizationConfig config)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Default Fonts (Fallback)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These fonts will be used if a specific language does not have an override.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            
            var newTMP = (TMP_FontAsset)EditorGUILayout.ObjectField("Default TMP Font", config.DefaultTMPFont, typeof(TMP_FontAsset), false);
            var newLegacy = (Font)EditorGUILayout.ObjectField("Default Legacy Font", config.DefaultLegacyFont, typeof(Font), false);

            if (EditorGUI.EndChangeCheck())
            {
                config.SetDefaultFonts(newTMP, newLegacy);
                LocalizationConfigProvider.SaveConfig();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLanguageFontsSection(LocalizationConfig config)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Language Overrides", EditorStyles.boldLabel);

            var activeLanguages = Data.LanguageCodes;
            
            if (activeLanguages.Count == 0)
            {
                EditorGUILayout.HelpBox("No languages found. Add languages in the Languages tab.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var mappings = config.FontMappings.ToList();

            foreach (var langCode in activeLanguages)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Language: {langCode}", EditorStyles.boldLabel);
                
                var mapping = mappings.FirstOrDefault(m => m.languageCode == langCode);
                
                EditorGUI.BeginChangeCheck();
                
                var newTMP = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP Font", mapping.tmpFont, typeof(TMP_FontAsset), false);
                var newLegacy = (Font)EditorGUILayout.ObjectField("Legacy Font", mapping.legacyFont, typeof(Font), false);
                
                if (EditorGUI.EndChangeCheck())
                {
                    config.SetFontMapping(langCode, newTMP, newLegacy);
                    LocalizationConfigProvider.SaveConfig();
                }
                
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
        }
    }
}
