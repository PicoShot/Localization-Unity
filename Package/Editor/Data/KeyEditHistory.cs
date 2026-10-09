using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PicoShot.Localization.Editor.Data
{
    /// <summary>
    /// Undo and redo for key and translation edits. Each change stores a snapshot of the affected keys
    /// and bumps a hidden proxy object through Unity's Undo, so Ctrl+Z, Ctrl+Y and Edit > Undo work as usual.
    /// </summary>
    public sealed class KeyEditHistory : IDisposable
    {
        private sealed class Snapshot
        {
            public List<string> Keys;
            public List<string> Languages;
            public Dictionary<string, Dictionary<string, object>> Values;
            public bool IsFull;
            public string SelectedKey;
        }

        private sealed class Entry
        {
            public string CoalesceId;
            public string[] AffectedKeys;
            public Snapshot Before;
            public Snapshot After;
        }

        private readonly LanguageEditorData _data;
        private readonly List<Entry> _entries = new();
        private KeyEditHistoryProxy _proxy;
        private int _baseRevision;
        private int _currentRevision;

        /// <summary>
        /// Raised after an undo or redo changed the data.
        /// </summary>
        public event Action Changed;

        public KeyEditHistory(LanguageEditorData data)
        {
            _data = data;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private KeyEditHistoryProxy Proxy
        {
            get
            {
                if (_proxy == null)
                {
                    _proxy = ScriptableObject.CreateInstance<KeyEditHistoryProxy>();
                    _proxy.hideFlags = HideFlags.HideAndDontSave;
                    _entries.Clear();
                    _baseRevision = _currentRevision = _proxy.revision;
                }
                return _proxy;
            }
        }

        /// <summary>
        /// Records the current state of one key before it changes. Consecutive records with the same
        /// coalesce id (for example typing in one field) merge into a single undo step.
        /// </summary>
        public void Record(string label, string key, string coalesceId = null)
        {
            RecordInternal(label, coalesceId, new[] { key });
        }

        /// <summary>
        /// Records the key order and the given keys before they change. Keys that don't exist yet are
        /// removed again on undo.
        /// </summary>
        public void RecordKeys(string label, params string[] keys)
        {
            RecordInternal(label, null, keys ?? Array.Empty<string>());
        }

        /// <summary>
        /// Records every key, for operations that can touch any of them (imports, bulk changes).
        /// </summary>
        public void RecordAll(string label)
        {
            RecordInternal(label, null, null);
        }

        /// <summary>
        /// Forgets all steps, for example after the data was reloaded from disk.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            if (_proxy != null)
                _baseRevision = _currentRevision = _proxy.revision;
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            _entries.Clear();

            if (_proxy != null)
            {
                Undo.ClearUndo(_proxy);
                UnityEngine.Object.DestroyImmediate(_proxy);
                _proxy = null;
            }
        }

        private void RecordInternal(string label, string coalesceId, string[] keys)
        {
            var proxy = Proxy;
            int index = _currentRevision - _baseRevision;

            // History got out of step with Unity's undo stack; start over from here
            if (proxy.revision != _currentRevision || index < 0 || index > _entries.Count)
            {
                _entries.Clear();
                _baseRevision = _currentRevision = proxy.revision;
                index = 0;
            }

            if (coalesceId != null && index == _entries.Count && index > 0 && _entries[index - 1].CoalesceId == coalesceId)
                return;

            if (index < _entries.Count)
                _entries.RemoveRange(index, _entries.Count - index);

            _entries.Add(new Entry
            {
                CoalesceId = coalesceId,
                AffectedKeys = keys,
                Before = Capture(keys)
            });

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(proxy, label);
            proxy.revision++;
            _currentRevision = proxy.revision;
        }

        private void OnUndoRedoPerformed()
        {
            if (_proxy == null || _proxy.revision == _currentRevision)
                return;

            int target = _proxy.revision;
            bool changed = false;

            while (_currentRevision > target)
            {
                int i = _currentRevision - _baseRevision - 1;
                if (i < 0 || i >= _entries.Count)
                {
                    _currentRevision = target;
                    break;
                }

                var entry = _entries[i];
                if (!LanguagesMatch(entry.Before))
                {
                    Invalidate();
                    return;
                }

                entry.After = Capture(entry.AffectedKeys);
                Apply(entry.Before);
                _currentRevision--;
                changed = true;
            }

            while (_currentRevision < target)
            {
                int i = _currentRevision - _baseRevision;
                if (i < 0 || i >= _entries.Count || _entries[i].After == null)
                {
                    _currentRevision = target;
                    break;
                }

                var entry = _entries[i];
                if (!LanguagesMatch(entry.After))
                {
                    Invalidate();
                    return;
                }

                Apply(entry.After);
                _currentRevision++;
                changed = true;
            }

            if (changed)
            {
                _data.HasUnsavedChanges = true;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Languages were added or removed since the snapshot; restoring it would corrupt the data.
        /// </summary>
        private void Invalidate()
        {
            _entries.Clear();
            _baseRevision = _currentRevision = _proxy.revision;
            Debug.LogWarning("[Localization] Key edit history was cleared because the project languages changed.");
        }

        private bool LanguagesMatch(Snapshot snapshot)
        {
            return snapshot.Languages.Count == _data.LanguageCodes.Count &&
                   snapshot.Languages.All(_data.LanguageCodes.Contains);
        }

        private Snapshot Capture(string[] keys)
        {
            var snapshot = new Snapshot
            {
                Keys = new List<string>(_data.Keys),
                Languages = new List<string>(_data.LanguageCodes),
                Values = new Dictionary<string, Dictionary<string, object>>(),
                IsFull = keys == null,
                SelectedKey = _data.SelectedKey
            };

            foreach (var key in keys ?? _data.Keys.ToArray())
            {
                snapshot.Values[key] = _data.LanguageData.TryGetValue(key, out var keyData) ? Copy(keyData) : null;
            }

            return snapshot;
        }

        private void Apply(Snapshot snapshot)
        {
            _data.Keys.Clear();
            _data.Keys.AddRange(snapshot.Keys);

            if (snapshot.IsFull)
                _data.LanguageData.Clear();

            foreach (var pair in snapshot.Values)
            {
                if (pair.Value == null)
                    _data.LanguageData.Remove(pair.Key);
                else
                    _data.LanguageData[pair.Key] = Copy(pair.Value);
            }

            _data.SelectedKey = snapshot.SelectedKey != null && _data.LanguageData.ContainsKey(snapshot.SelectedKey)
                ? snapshot.SelectedKey
                : null;
        }

        private static Dictionary<string, object> Copy(Dictionary<string, object> source)
        {
            var copy = new Dictionary<string, object>(source.Count, source.Comparer);
            foreach (var pair in source)
            {
                copy[pair.Key] = pair.Value switch
                {
                    List<string> list => new List<string>(list),
                    string[] array => (string[])array.Clone(),
                    _ => pair.Value
                };
            }
            return copy;
        }
    }
}
