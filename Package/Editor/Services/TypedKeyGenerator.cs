using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Hashing;
using PicoShot.Localization.Config;
using UnityEditor;
using UnityEngine;

namespace PicoShot.Localization.Editor.Services
{
    /// <summary>
    /// Generates hash-backed, strongly typed localization keys in the user's Assets folder.
    /// </summary>
    public static class TypedKeyGenerator
    {
        public const string GeneratedDirectory = LocalizationConfigProvider.ConfigDirectory;
        public const string GeneratedCodePath = GeneratedDirectory + "/LocalizationKeys.cs";
        public const string AssemblyReferencePath = GeneratedDirectory + "/PicoShot.Localization.Generated.asmref";
        private const string RuntimeAssemblyName = "PicoShot.Localization";

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        public static bool Generate(LanguageEditorData data, bool showSuccessDialog = false)
        {
            if (data == null)
            {
                ReportFailure(new[] { "Localization editor data is unavailable." });
                return false;
            }

            try
            {
                var scalarKeys = data.Keys
                    .Where(key => !LanguageEditorData.IsArrayKey(data.LanguageData[key]))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList();
                var arrayKeys = data.Keys
                    .Where(key => LanguageEditorData.IsArrayKey(data.LanguageData[key]))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList();

                var errors = Validate(scalarKeys, arrayKeys);
                if (errors.Count > 0)
                {
                    ReportFailure(errors);
                    return false;
                }

                string assemblyGuid = FindRuntimeAssemblyGuid();
                if (string.IsNullOrEmpty(assemblyGuid))
                    throw new InvalidOperationException($"Could not locate the {RuntimeAssemblyName} assembly definition.");

                string absoluteDirectory = ToAbsoluteProjectPath(GeneratedDirectory);
                Directory.CreateDirectory(absoluteDirectory);

                bool changed = WriteIfChanged(
                    ToAbsoluteProjectPath(AssemblyReferencePath),
                    $"{{\n  \"reference\": \"GUID:{assemblyGuid}\"\n}}\n");
                changed |= WriteIfChanged(ToAbsoluteProjectPath(GeneratedCodePath), BuildSource(scalarKeys, arrayKeys));

                if (changed)
                    AssetDatabase.Refresh();

                if (showSuccessDialog)
                {
                    string status = changed ? "generated" : "already up to date";
                    EditorUtility.DisplayDialog("Typed Keys", $"Typed localization keys are {status}.", "OK");
                }

                return true;
            }
            catch (Exception exception)
            {
                ReportFailure(new[] { exception.Message });
                return false;
            }
        }

        internal static string ToIdentifier(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "_Empty";

            var builder = new StringBuilder(key.Length);
            bool capitalize = true;
            foreach (char character in key)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    capitalize = true;
                    continue;
                }

                if (character == '_')
                {
                    capitalize = true;
                    continue;
                }

                builder.Append(capitalize ? char.ToUpper(character, CultureInfo.InvariantCulture) : character);
                capitalize = false;
            }

            if (builder.Length == 0)
                builder.Append("_Key");
            if (!IsIdentifierStart(builder[0]))
                builder.Insert(0, '_');
            if (CSharpKeywords.Contains(builder.ToString().ToLowerInvariant()))
                builder.Insert(0, '_');

            return builder.ToString();
        }

        private static List<string> Validate(IReadOnlyList<string> scalarKeys, IReadOnlyList<string> arrayKeys)
        {
            var errors = new List<string>();
            ValidateNames("StringKeys", scalarKeys, errors);
            ValidateNames("ArrayKeys", arrayKeys, errors);

            var hashes = scalarKeys.Concat(arrayKeys)
                .GroupBy(key => Hash64.CreateIgnoreCase(key))
                .Where(group => group.Select(key => key).Distinct(StringComparer.Ordinal).Count() > 1);
            foreach (var collision in hashes)
            {
                errors.Add($"Hash collision ({collision.Key}): {string.Join(", ", collision.Select(Quote))}");
            }

            return errors;
        }

        private static void ValidateNames(string enumName, IEnumerable<string> keys, ICollection<string> errors)
        {
            foreach (var collision in keys.GroupBy(ToIdentifier, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                errors.Add($"{enumName} member collision '{collision.Key}': {string.Join(", ", collision.Select(Quote))}");
            }
        }

        private static string BuildSource(IReadOnlyList<string> scalarKeys, IReadOnlyList<string> arrayKeys)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("// Generated by PicoShot Localization. Changes will be overwritten.");
            builder.AppendLine();
            builder.AppendLine("namespace PicoShot.Localization.Generated");
            builder.AppendLine("{");
            AppendEnum(builder, "StringKeys", scalarKeys);
            builder.AppendLine();
            AppendEnum(builder, "ArrayKeys", arrayKeys);
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("namespace PicoShot.Localization");
            builder.AppendLine("{");
            builder.AppendLine("    public static partial class LocalizationManager");
            builder.AppendLine("    {");
            builder.AppendLine("        public static string GetText(Generated.StringKeys key, params object[] args) => GetText((long)key, args);");
            builder.AppendLine("        public static string[] GetArray(Generated.ArrayKeys key) => GetArrayByHash((long)key, key.ToString());");
            builder.AppendLine("        public static string GetArrayText(Generated.ArrayKeys key, int index)");
            builder.AppendLine("        {");
            builder.AppendLine("            var array = GetArray(key);");
            builder.AppendLine("            if (array == null || array.Length == 0)");
            builder.AppendLine("            {");
            builder.AppendLine("                UnityEngine.Debug.LogWarning($\"[LocalizationManager] Key '{key}' is not an array or is empty\");");
            builder.AppendLine("                return $\"[{key}]\";");
            builder.AppendLine("            }");
            builder.AppendLine("            if (index >= 0 && index < array.Length) return array[index] ?? string.Empty;");
            builder.AppendLine("            UnityEngine.Debug.LogWarning($\"[LocalizationManager] Array index {index} out of range for key '{key}'\");");
            builder.AppendLine("            return $\"[{key}:{index}]\";");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendEnum(StringBuilder builder, string name, IEnumerable<string> keys)
        {
            builder.AppendLine($"    public enum {name} : long");
            builder.AppendLine("    {");
            foreach (string key in keys)
            {
                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// {SecurityElement.Escape(key)}");
                builder.AppendLine("        /// </summary>");
                long hash = Hash64.CreateIgnoreCase(key);
                string literal = hash == long.MinValue
                    ? "long.MinValue"
                    : hash.ToString(CultureInfo.InvariantCulture) + "L";
                builder.AppendLine($"        {ToIdentifier(key)} = {literal},");
            }
            builder.AppendLine("    }");
        }

        private static string FindRuntimeAssemblyGuid()
        {
            foreach (string guid in AssetDatabase.FindAssets($"{RuntimeAssemblyName} t:asmdef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == RuntimeAssemblyName)
                    return guid;
            }
            return null;
        }

        private static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
                return false;

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
            return true;
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not determine the Unity project root.");
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        private static bool IsIdentifierStart(char value) => value == '_' || char.IsLetter(value);
        private static string Quote(string value) => $"'{value}'";

        private static void ReportFailure(IEnumerable<string> errors)
        {
            string message = string.Join("\n", errors);
            Debug.LogError($"[TypedKeyGenerator] Generation failed. Existing generated files were preserved.\n{message}");
            EditorUtility.DisplayDialog("Typed Key Generation Failed", message, "OK");
        }
    }
}
