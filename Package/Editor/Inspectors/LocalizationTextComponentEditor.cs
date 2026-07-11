using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PicoShot.Localization.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizationTextComponent))]
    [CanEditMultipleObjects]
    public class LocalizationTextComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _translationKeyProp;
        private SerializedProperty _arrayIndexProp;
        private SerializedProperty _arraySizeLimitProp;
        private SerializedProperty _formatParametersProp;
        private SerializedProperty _onTextUpdatedProp;

        private string _selectedTable = "All Keys";
        private List<string> _availableTables = new List<string>();
        private List<string> _availableKeys = new List<string>();
        private List<string> _displayKeys = new List<string>();
        
        private bool _isDataInitialized = false;

        private void OnEnable()
        {
            _translationKeyProp = serializedObject.FindProperty("translationKey");
            _arrayIndexProp = serializedObject.FindProperty("arrayIndex");
            _arraySizeLimitProp = serializedObject.FindProperty("arraySizeLimit");
            _formatParametersProp = serializedObject.FindProperty("formatParameters");
            _onTextUpdatedProp = serializedObject.FindProperty("onTextUpdated");
            
            RefreshData();
            SyncSelectedTableWithKey();
        }

        private void SyncSelectedTableWithKey()
        {
            if (_translationKeyProp == null) return;
            string fullKey = _translationKeyProp.stringValue;
            
            if (!string.IsNullOrEmpty(fullKey) && fullKey.Contains("."))
            {
                string table = fullKey.Substring(0, fullKey.IndexOf('.'));
                if (_availableTables.Contains(table))
                {
                    _selectedTable = table;
                    UpdateDisplayKeys();
                }
            }
        }

        private void RefreshData()
        {
            var keys = LocalizationManager.AllTranslationKeys.ToList();
            
            // Extract tables
            var tables = keys.Where(k => k.Contains('.'))
                             .Select(k => k.Substring(0, k.IndexOf('.')))
                             .Distinct()
                             .OrderBy(t => t)
                             .ToList();
                             
            _availableTables.Clear();
            _availableTables.Add("All Keys");
            _availableTables.AddRange(tables);

            _availableKeys = keys.OrderBy(k => k).ToList();
            UpdateDisplayKeys();
            
            _isDataInitialized = true;
        }

        private void UpdateDisplayKeys()
        {
            _displayKeys.Clear();
            if (_selectedTable == "All Keys")
            {
                _displayKeys.AddRange(_availableKeys);
            }
            else
            {
                string prefix = _selectedTable + ".";
                var filtered = _availableKeys.Where(k => k.StartsWith(prefix)).ToList();
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

            if (!_isDataInitialized)
            {
                RefreshData();
            }

            // --- TABLE SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Table");
            
            var tableRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            int tableIndex = _availableTables.IndexOf(_selectedTable);
            if (tableIndex < 0) tableIndex = 0;
            
            if (EditorGUI.DropdownButton(tableRect, new GUIContent(_availableTables[tableIndex]), FocusType.Keyboard, EditorStyles.popup))
            {
                LocalizationSearchablePopup.Show(tableRect, _availableTables.ToArray(), tableIndex, (index) =>
                {
                    if (index >= 0 && index < _availableTables.Count && _selectedTable != _availableTables[index])
                    {
                        _selectedTable = _availableTables[index];
                        UpdateDisplayKeys();
                        
                        // Clear the selected key when table changes
                        _translationKeyProp.stringValue = "";
                        serializedObject.ApplyModifiedProperties();
                        
                        if (!Application.isPlaying)
                        {
                            component.TranslationKey = "";
                            EditorUtility.SetDirty(component);
                        }

                        Repaint();
                    }
                });
            }
            EditorGUILayout.EndHorizontal();

            // --- KEY SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Translation Key");

            var keyRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            
            // Determine current key display
            string currentFullKey = _translationKeyProp.stringValue ?? "";
            string currentDisplayKey = currentFullKey;
            
            if (_selectedTable != "All Keys" && currentFullKey.StartsWith(_selectedTable + "."))
            {
                currentDisplayKey = currentFullKey.Substring(_selectedTable.Length + 1);
            }
            else if (_selectedTable != "All Keys" && !string.IsNullOrEmpty(currentFullKey))
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
                        string newFullKey = _selectedTable == "All Keys" ? newLocalKey : $"{_selectedTable}.{newLocalKey}";
                        
                        _translationKeyProp.stringValue = newFullKey;
                        serializedObject.ApplyModifiedProperties();
                        
                        // Force update text in editor if playing or if we have an OnValidate
                        if (!Application.isPlaying)
                        {
                            component.TranslationKey = newFullKey;
                        }
                    }
                });
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
