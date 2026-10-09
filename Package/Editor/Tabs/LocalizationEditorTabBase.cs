using UnityEngine;
using UnityEditor;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Base class for localization editor tabs providing common functionality.
    /// </summary>
    public abstract class LocalizationEditorTabBase : ILocalizationEditorTab
    {
        protected readonly LocalizationEditor Editor;
        protected readonly LanguageEditorData Data;

        protected LocalizationEditorTabBase(LocalizationEditor editor, LanguageEditorData data)
        {
            Editor = editor;
            Data = data;
        }

        public abstract string TabName { get; }

        public virtual void OnEnter() { }

        public virtual void OnExit() { }

        public abstract void Draw();

        public virtual bool HandleKeyboardInput(Event evt) => false;

        /// <summary>
        /// Helper to get the window position.
        /// </summary>
        protected Rect WindowPosition => Editor.position;
    }
}
