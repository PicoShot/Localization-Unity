using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizationTextComponent))]
    public class LocalizationTextComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _translationKeyProp;
        private SerializedProperty _arrayIndexProp;
        private SerializedProperty _arraySizeLimitProp;
        private SerializedProperty _formatParametersProp;
        private SerializedProperty _onTextUpdatedProp;

        private string _selectedView = "All Keys";
        private List<string> _availableViews = new List<string>();
        private List<string> _availableKeys = new List<string>();
        private List<string> _displayKeys = new List<string>();
        
        private bool _isDataInitialized = false;
        private char _lastViewDelimiter;

        private static char GetViewDelimiter() => LanguageEditorData.GetCurrentViewDelimiter();

        private void OnEnable()
        {
            _translationKeyProp = serializedObject.FindProperty("translationKey");
            _arrayIndexProp = serializedObject.FindProperty("arrayIndex");
            _arraySizeLimitProp = serializedObject.FindProperty("arraySizeLimit");
            _formatParametersProp = serializedObject.FindProperty("formatParameters");
            _onTextUpdatedProp = serializedObject.FindProperty("onTextUpdated");
            
            RefreshData();
            SyncSelectedViewWithKey();
        }

        private void SyncSelectedViewWithKey()
        {
            if (_translationKeyProp == null) return;
            string fullKey = _translationKeyProp.stringValue;
            char delimiter = GetViewDelimiter();
            
            if (!string.IsNullOrEmpty(fullKey) && fullKey.Contains(delimiter))
            {
                string view = fullKey.Substring(0, fullKey.IndexOf(delimiter)).ToLowerInvariant();
                if (_availableViews.Any(v => v.Equals(view, StringComparison.OrdinalIgnoreCase)))
                {
                    _selectedView = _availableViews.First(v => v.Equals(view, StringComparison.OrdinalIgnoreCase));
                    UpdateDisplayKeys();
                }
            }
        }

        private void RefreshData()
        {
            var keys = LocalizationManager.AllTranslationKeys.ToList();
            
            // Extract views
            char delimiter = GetViewDelimiter();
            var views = keys.Where(k => k.Contains(delimiter))
                             .Select(k => k.Substring(0, k.IndexOf(delimiter)))
                             .Distinct()
                             .OrderBy(t => t)
                             .ToList();
                             
            _availableViews.Clear();
            _availableViews.Add("All Keys");
            _availableViews.AddRange(views);

            if (!_availableViews.Contains(_selectedView))
                _selectedView = "All Keys";

            _availableKeys = keys.OrderBy(k => k).ToList();
            UpdateDisplayKeys();
            _lastViewDelimiter = delimiter;
            
            _isDataInitialized = true;
        }

        private void UpdateDisplayKeys()
        {
            _displayKeys.Clear();
            if (_selectedView == "All Keys")
            {
                _displayKeys.AddRange(_availableKeys);
            }
            else
            {
                string prefix = _selectedView + GetViewDelimiter();
                var filtered = _availableKeys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach(var key in filtered)
                {
                    _displayKeys.Add(key.Substring(prefix.Length));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var component = (LocalizationTextComponent)target;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Localization Key", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh Keys", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                LocalizationManager.Initialize(); 
                RefreshData();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (!_isDataInitialized || _lastViewDelimiter != GetViewDelimiter())
            {
                RefreshData();
                SyncSelectedViewWithKey();
            }

            // --- VIEW SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("View");
            
            var viewRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            int viewIndex = _availableViews.IndexOf(_selectedView);
            if (viewIndex < 0) viewIndex = 0;
            
            if (EditorGUI.DropdownButton(viewRect, new GUIContent(_availableViews[viewIndex]), FocusType.Keyboard, EditorStyles.popup))
            {
                LocalizationSearchablePopup.Show(viewRect, _availableViews.ToArray(), viewIndex, (index) =>
                {
                    if (index >= 0 && index < _availableViews.Count && _selectedView != _availableViews[index])
                    {
                        _selectedView = _availableViews[index];
                        UpdateDisplayKeys();
                        
                        // Clear the selected key when view changes
                        _translationKeyProp.stringValue = "";
                        serializedObject.ApplyModifiedProperties();
                        
                        if (!Application.isPlaying)
                        {
                            component.TranslationKey = "";
                            EditorUtility.SetDirty(component);
                        }

                        Repaint();
                    }
                }, "Select View");
            }
            EditorGUILayout.EndHorizontal();

            // --- KEY SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Translation Key");

            var keyRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            
            // Determine current key display
            string currentFullKey = _translationKeyProp.stringValue ?? "";
            string currentDisplayKey = currentFullKey;
            
            if (_selectedView != "All Keys" && currentFullKey.StartsWith(_selectedView + GetViewDelimiter(), StringComparison.OrdinalIgnoreCase))
            {
                currentDisplayKey = currentFullKey.Substring(_selectedView.Length + 1);
            }
            else if (_selectedView != "All Keys" && !string.IsNullOrEmpty(currentFullKey))
            {
                currentDisplayKey = $"[{currentFullKey}]";
            }
            
            if (string.IsNullOrEmpty(currentDisplayKey))
            {
                currentDisplayKey = "Select a key...";
            }

            int keyIndex = _displayKeys.IndexOf(currentDisplayKey);

            if (EditorGUI.DropdownButton(keyRect, new GUIContent(currentDisplayKey), FocusType.Keyboard, EditorStyles.popup))
            {
                LocalizationSearchablePopup.Show(keyRect, _displayKeys.ToArray(), keyIndex, (index) =>
                {
                    if (index >= 0 && index < _displayKeys.Count)
                    {
                        string newLocalKey = _displayKeys[index];
                        string newFullKey = _selectedView == "All Keys" ? newLocalKey : $"{_selectedView}{GetViewDelimiter()}{newLocalKey}";
                        
                        _translationKeyProp.stringValue = newFullKey;
                        serializedObject.ApplyModifiedProperties();
                        
                        // Force update text preview in Edit Mode.
                        if (!Application.isPlaying)
                        {
                            component.UpdateText();
                            EditorUtility.SetDirty(component);
                        }
                    }
                }, "Select Key");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            
            // --- REMAINING PROPERTIES ---
            EditorGUILayout.PropertyField(_arrayIndexProp);
            EditorGUILayout.PropertyField(_arraySizeLimitProp);
            
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_formatParametersProp, true);
            
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_onTextUpdatedProp);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
