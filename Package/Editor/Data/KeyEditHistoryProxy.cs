using UnityEngine;

namespace PicoShot.Localization.Editor.Data
{
    /// <summary>
    /// Hidden object recorded with Unity's Undo so key edits take part in the editor's undo stack.
    /// Its revision tells <see cref="KeyEditHistory"/> how many steps were undone or redone.
    /// </summary>
    public sealed class KeyEditHistoryProxy : ScriptableObject
    {
        public int revision;
    }
}
