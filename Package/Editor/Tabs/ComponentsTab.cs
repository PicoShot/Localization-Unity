using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PicoShot.Localization.Editor.Data;
using System.Collections.Generic;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for managing LocalizationTextComponent on GameObjects.
    /// </summary>
    public sealed class ComponentsTab : LocalizationEditorTabBase
    {
        private Color _dragAreaColor = Color.white;

        public ComponentsTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Components";

        public override void Draw()
        {
            Data.ComponentsScrollPosition = EditorGUILayout.BeginScrollView(Data.ComponentsScrollPosition);

            DrawSectionHeader("Component Manager");
            DrawDragAndDropArea();

            EditorGUILayout.Space();

            if (Data.SelectedGameObject != null)
            {
                DrawComponentsList();
            }

            EditorGUILayout.Space();
            DrawExistingComponentsSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawDragAndDropArea()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Active GameObject:", EditorStyles.boldLabel, GUILayout.Width(120));
            var newSelection = (GameObject)EditorGUILayout.ObjectField(
                Data.SelectedGameObject, typeof(GameObject), true);

            if (newSelection != Data.SelectedGameObject)
            {
                Data.SelectedGameObject = newSelection;
                GUI.FocusControl(null);
            }

            if (Data.SelectedGameObject != null && GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.SelectedGameObject = null;
            }
            EditorGUILayout.EndHorizontal();

            const float dragAreaHeight = 60f;
            var rect = GUILayoutUtility.GetRect(0, dragAreaHeight, GUILayout.ExpandWidth(true));
            var dragAreaStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (rect.Contains(evt.mousePosition))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        _dragAreaColor = new Color(0.1f, 1f, 0.1f);

                        if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            foreach (var draggedObject in DragAndDrop.objectReferences)
                            {
                                if (draggedObject is GameObject go)
                                {
                                    Data.SelectedGameObject = go;
                                    break;
                                }
                            }
                        }
                    }
                    evt.Use();
                    break;

                case EventType.DragExited:
                    _dragAreaColor = Color.white;
                    break;
            }

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = _dragAreaColor;
            GUI.Box(rect,
                Data.SelectedGameObject == null
                    ? "Drag & Drop GameObjects Here\nor use the Object Field above"
                    : $"Currently Managing: {Data.SelectedGameObject.name}",
                dragAreaStyle);
            GUI.backgroundColor = prevColor;

            EditorGUILayout.EndVertical();
        }

        private void DrawComponentsList()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Available Components", EditorStyles.boldLabel);

            var tmpTexts = Data.SelectedGameObject.GetComponentsInChildren<TMP_Text>(true);
            var tmpDropdowns = Data.SelectedGameObject.GetComponentsInChildren<TMP_Dropdown>(true);
            var legacyTexts = Data.SelectedGameObject.GetComponentsInChildren<Text>(true);
            var legacyDropdowns = Data.SelectedGameObject.GetComponentsInChildren<Dropdown>(true);
            var textMeshes = Data.SelectedGameObject.GetComponentsInChildren<TextMesh>(true);

            int totalComponents = tmpTexts.Length + tmpDropdowns.Length + legacyTexts.Length +
                                  legacyDropdowns.Length + textMeshes.Length;

            if (totalComponents == 0)
            {
                DrawHelpBox("No text components found in the selected GameObject hierarchy.\n\n" +
                    "Supported: TMP_Text, TMP_Dropdown, Text (Legacy), Dropdown (Legacy), TextMesh");
                EditorGUILayout.EndVertical();
                return;
            }

            Data.ComponentsScrollPos = EditorGUILayout.BeginScrollView(Data.ComponentsScrollPos, GUILayout.Height(300));

            if (tmpTexts.Length > 0)
            {
                EditorGUILayout.LabelField("TMP Text Components", EditorStyles.boldLabel);
                foreach (var text in tmpTexts)
                    DrawComponentSection(text.gameObject, text);
            }

            if (legacyTexts.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Legacy Text Components", EditorStyles.boldLabel);
                foreach (var text in legacyTexts)
                    DrawComponentSection(text.gameObject, text);
            }

            if (textMeshes.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("TextMesh Components (3D)", EditorStyles.boldLabel);
                foreach (var textMesh in textMeshes)
                    DrawComponentSection(textMesh.gameObject, textMesh);
            }

            if (tmpDropdowns.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("TMP Dropdown Components", EditorStyles.boldLabel);
                foreach (var dropdown in tmpDropdowns)
                    DrawComponentSection(dropdown.gameObject, dropdown);
            }

            if (legacyDropdowns.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Legacy Dropdown Components", EditorStyles.boldLabel);
                foreach (var dropdown in legacyDropdowns)
                    DrawComponentSection(dropdown.gameObject, dropdown);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawComponentSection(GameObject go, Component component)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("☉", GUILayout.Width(23)))
                EditorGUIUtility.PingObject(component.gameObject);

            string parentName = go.transform.parent?.name ?? "Root";
            EditorGUILayout.LabelField($"{parentName}: {go.name}", EditorStyles.boldLabel);
            var langComponent = go.GetComponent<LocalizationTextComponent>();

            if (langComponent == null)
            {
                if (GUILayout.Button("Add Language Support", GUILayout.Width(150)))
                {
                    langComponent = Undo.AddComponent<LocalizationTextComponent>(go);
                    Data.PendingKeySelection = langComponent;
                }
            }
            else
            {
                if (GUILayout.Button("Remove Language Support", GUILayout.Width(180)))
                {
                    Undo.DestroyObjectImmediate(langComponent);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (langComponent != null)
            {
                DrawComponentKeySelector(langComponent);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawComponentKeySelector(LocalizationTextComponent langComponent)
        {
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Language Key:", GUILayout.Width(100));

            var buttonRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            var selectedIndex = Data.Keys.IndexOf(langComponent.TranslationKey);
            var displayText = selectedIndex >= 0 ? Data.Keys[selectedIndex] : "Select a key...";

            var style = new GUIStyle(EditorStyles.popup)
            {
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };

            bool userClicked = EditorGUI.DropdownButton(buttonRect, new GUIContent(displayText), FocusType.Keyboard, style);
            bool autoOpen = Data.PendingKeySelection == langComponent;

            if (autoOpen)
                Data.PendingKeySelection = null;

            if (userClicked || autoOpen)
            {
                if (autoOpen)
                {
                    LocalizationSearchablePopup.ShowCenteredOnWindow(WindowPosition, Data.Keys.ToArray(), selectedIndex, (index) =>
                    {
                        if (index == selectedIndex || index < 0) return;
                        Undo.RecordObject(langComponent, "Change Language Key");
                        langComponent.TranslationKey = Data.Keys[index];
                        EditorUtility.SetDirty(langComponent);
                    });
                }
                else
                {
                    LocalizationSearchablePopup.Show(buttonRect, Data.Keys.ToArray(), selectedIndex, (index) =>
                    {
                        if (index == selectedIndex || index < 0) return;
                        Undo.RecordObject(langComponent, "Change Language Key");
                        langComponent.TranslationKey = Data.Keys[index];
                        EditorUtility.SetDirty(langComponent);
                    });
                }
            }

            EditorGUILayout.EndHorizontal();

            DrawArrayControlsIfNeeded(langComponent);
        }

        private void DrawArrayControlsIfNeeded(LocalizationTextComponent langComponent)
        {
            if (langComponent.TranslationKey == null) return;
            if (!Data.LanguageData.TryGetValue(langComponent.TranslationKey, out var keyData)) return;
            if (!LanguageEditorData.IsArrayKey(keyData)) return;

            var firstValue = LanguageEditorData.GetFirstValue(keyData);
            if (firstValue is not List<string> array) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Array Index:", GUILayout.Width(100));

            var newArrayIndex = EditorGUILayout.IntSlider(langComponent.ArrayIndex, -1, array.Count - 1);

            EditorGUILayout.LabelField("Array Size Limit:", GUILayout.Width(100));
            var newArraySizeLimit = EditorGUILayout.IntSlider(langComponent.ArraySizeLimit, 0, array.Count);
            langComponent.ArraySizeLimit = newArraySizeLimit;

            if (newArrayIndex != langComponent.ArrayIndex)
            {
                Undo.RecordObject(langComponent, "Change Array Index");
                langComponent.ArrayIndex = newArrayIndex;
                EditorUtility.SetDirty(langComponent);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawExistingComponentsSection()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            Data.ShowExistingComponents = EditorGUILayout.Foldout(Data.ShowExistingComponents, "Existing Language Components", true);

            if (Data.ShowExistingComponents)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
                Data.ComponentSearchFilter = EditorGUILayout.TextField(Data.ComponentSearchFilter, GUILayout.Width(140));
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    Data.ComponentSearchFilter = "";
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (Data.ShowExistingComponents)
            {
                ShowExistingComponentsList();
            }

            EditorGUILayout.EndVertical();
        }

        private void ShowExistingComponentsList()
        {
#if UNITY_6000_4_OR_NEWER
            var allComponents = Object.FindObjectsByType<LocalizationTextComponent>(FindObjectsInactive.Include);
#else
            var allComponents = Object.FindObjectsByType<LocalizationTextComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif

            if (allComponents.Length == 0)
            {
                DrawHelpBox("No language components found in the scene.");
                return;
            }

            var filteredComponents = allComponents
                .Where(c => string.IsNullOrEmpty(Data.ComponentSearchFilter) ||
                            c.gameObject.name.ToLower().Contains(Data.ComponentSearchFilter.ToLower()) ||
                            c.TranslationKey.ToLower().Contains(Data.ComponentSearchFilter.ToLower()))
                .OrderBy(c => c.gameObject.name)
                .ToList();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Found {filteredComponents.Count} components", EditorStyles.miniBoldLabel);

            Data.ExistingComponentsScrollPos = EditorGUILayout.BeginScrollView(Data.ExistingComponentsScrollPos, GUILayout.Height(500));

            foreach (var component in filteredComponents)
            {
                DrawExistingComponentEntry(component);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawExistingComponentEntry(LocalizationTextComponent component)
        {
            if (component == null) return;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("☉", GUILayout.Width(23)))
                EditorGUIUtility.PingObject(component.gameObject);

            string parentName = component.gameObject.transform.parent != null
                ? component.gameObject.transform.parent.name
                : "No Parent";
            EditorGUILayout.LabelField($"{parentName}: {component.gameObject.name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Key: {component.TranslationKey}", EditorStyles.miniLabel, GUILayout.Width(200));

            string componentType = GetComponentTypeName(component);
            EditorGUILayout.LabelField(componentType, EditorStyles.miniLabel, GUILayout.Width(60));

            if (GUILayout.Button("Edit", GUILayout.Width(60)))
            {
                Data.SelectedGameObject = component.gameObject;
            }

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("✕", GUILayout.Width(25)))
            {
                if (EditorUtility.DisplayDialog("Remove Language Component",
                        $"Are you sure you want to remove the language component from {component.gameObject.name}?",
                        "Yes", "No"))
                {
                    Undo.DestroyObjectImmediate(component);
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static string GetComponentTypeName(Component component)
        {
            var go = component.gameObject;
            if (go.GetComponent<TMP_Dropdown>() != null) return "TMP Dropdown";
            if (go.GetComponent<TMP_Text>() != null) return "TMP Text";
            if (go.GetComponent<Dropdown>() != null) return "Legacy Dropdown";
            if (go.GetComponent<Text>() != null) return "Legacy Text";
            if (go.GetComponent<TextMesh>() != null) return "TextMesh";
            return "Unknown";
        }
    }
}
