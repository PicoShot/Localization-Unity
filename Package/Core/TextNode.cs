//#define NETCODE

using System;

#if NETCODE
using Unity.Netcode;
#endif
#if MIRROR
using Mirror;
#endif

namespace PicoShot.Localization
{
    /// <summary>
    /// TextNode represents a text structure composed of multiple nodes instead of a plain string.
    /// 
    /// A node can contain:
    /// - Plain text
    /// - Localized text entries
    /// - Formatting information (color, bold, etc.)
    /// - Other nested TextNodes as arguments
    /// 
    /// This allows complex text structures to be serialized and transferred over the network
    /// without losing localization or formatting data.
    /// 
    /// Example:
    /// A message sent from one player to another can be reconstructed and localized
    /// on the receiving side using that player's selected language.
    /// 
    /// Main use case:
    /// Multiplayer/networked games where text should automatically appear
    /// in the recipient's own language.
    /// 
    /// NOTE:
    /// If your project uses Netcode, add "NETCODE" to your
    /// Scripting Define Symbols.
    /// </summary>
    public struct TextNode
#if NETCODE
        : INetworkSerializable
#endif
    {
        public string Value;
        public NodeType Type;
        public TextNode[] Nodes;
        public RichModifier[] RichModifiers;

        private string applyModifiers(in string text)
        {
            if (RichModifiers == null || RichModifiers.Length == 0)
                return text;

            string output = string.Empty;

            for (int i = 0; i < RichModifiers.Length; i++)
                output += RichModifiers[i].Begin();

            output += text;

            for (int i = RichModifiers.Length - 1; i >= 0; i--)
                output += RichModifiers[i].End();

            return output;
        }

        public TextNode(string value)
        {
            Value = value;
            Type = NodeType.PlainText;
            Nodes = Array.Empty<TextNode>();
            RichModifiers = Array.Empty<RichModifier>();
        }
        public override string ToString()
        {
            if (Type == NodeType.PlainText)
                return applyModifiers(Value);

            Nodes ??= Array.Empty<TextNode>();

            var arguments = new string[Nodes.Length];
            for (int i = 0; i < Nodes.Length; i++)
                arguments[i] = Nodes[i].ToString();

            return applyModifiers(Type switch
            {
                NodeType.LocalizedText => LocalizationManager.GetText(Value, arguments),
                NodeType.FormattedText => string.Format(Value, arguments),
                _ => throw new NotImplementedException()
            });
        }

        public static TextNode Text(string text)
        {
            return new TextNode()
            {
                Value = text,
                Type = NodeType.PlainText,
                Nodes = Array.Empty<TextNode>()
            };
        }
        public static TextNode Localized(string translationKeyName, params TextNode[] arguments)
        {
            arguments ??= Array.Empty<TextNode>();

            return new TextNode()
            {
                Value = translationKeyName,
                Type = NodeType.LocalizedText,
                Nodes = arguments
            };
        }
        public static TextNode Formatted(string format, params TextNode[] arguments)
        {
            arguments ??= Array.Empty<TextNode>();

            return new TextNode()
            {
                Value = format,
                Type = NodeType.FormattedText,
                Nodes = arguments
            };
        }
        public static TextNode Combine(params TextNode[] others)
        {
            if (others == null || others.Length < 1)
                return default;

            var combined = new TextNode()
            {
                Type = NodeType.FormattedText,
                Value = string.Empty,
                Nodes = others
            };

            for (int i = 0; i < others.Length; i++)
                combined.Value += "{" + i + "}";

            return combined;
        }

        public TextNode AddModifier(RichModifier modifier)
        {
            if (RichModifiers != null)
                Array.Resize(ref RichModifiers, RichModifiers.Length + 1);
            else
                RichModifiers = new RichModifier[1];

            RichModifiers[^1] = modifier;

            return this;
        }

#if NETCODE
        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref Type);

            int nodeCount = Nodes != null ? Nodes.Length : 0;
            serializer.SerializeValue(ref nodeCount);

            if (serializer.IsReader)
                Nodes = new TextNode[nodeCount];

            if (nodeCount > 0)
                for (int i = 0; i < nodeCount; i++)
                    serializer.SerializeNetworkSerializable(ref Nodes[i]);

            int modifiers = RichModifiers != null ? RichModifiers.Length : 0;
            serializer.SerializeValue(ref modifiers);

            if (serializer.IsReader)
                RichModifiers = new RichModifier[modifiers];

            if (modifiers > 0)
                for (int i = 0; i < modifiers; i++)
                    serializer.SerializeNetworkSerializable(ref RichModifiers[i]);
        }
#endif

        public static implicit operator string(TextNode node) => node.ToString();
        public static implicit operator TextNode(string value) => new(value);

        public enum NodeType : byte
        {
            PlainText = 0,
            LocalizedText = 1,
            FormattedText = 2
        }
    }

    public struct RichModifier
#if NETCODE
    : INetworkSerializable
#endif
    {
        public string Tag;
        public string Argument;

        public RichModifier(string tag, string argument = null)
        {
            Tag = tag;
            Argument = argument ?? string.Empty;
        }

        public string Begin()
        {
            if (string.IsNullOrEmpty(Tag))
                return string.Empty;

            if (string.IsNullOrEmpty(Argument))
                return $"<{Tag}>";

            return $"<{Tag}={Argument}>";
        }

        public string End()
        {
            if (string.IsNullOrEmpty(Tag))
                return string.Empty;

            return $"</{Tag}>";
        }

        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(Tag))
                return text;

            return $"{Begin()}{text}{End()}";
        }
#if NETCODE
        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            Tag ??= string.Empty;
            serializer.SerializeValue(ref Tag);

            Argument ??= string.Empty;
            serializer.SerializeValue(ref Argument);
        }
#endif
    }

#if MIRROR
    public static class TextNodeMirrorSerializer
    {
        public static void WriteRichModifier(this NetworkWriter writer, RichModifier value)
        {
            writer.WriteString(value.Tag);
            writer.WriteString(value.Argument);
        }

        public static RichModifier ReadRichModifier(this NetworkReader reader)
        {
            return new RichModifier()
            {
                Tag = reader.ReadString(),
                Argument = reader.ReadString()
            };
        }

        public static void WriteTextNode(this NetworkWriter writer, TextNode value)
        {
            writer.WriteString(value.Value);
            writer.WriteByte((byte)value.Type);

            writer.WriteInt(value.Nodes?.Length ?? 0);
            if (value.Nodes != null)
                for (int i = 0; i < value.Nodes.Length; i++)
                    writer.WriteTextNode(value.Nodes[i]);

            writer.WriteInt(value.RichModifiers?.Length ?? 0);
            if (value.RichModifiers != null)
                for (int i = 0; i < value.RichModifiers.Length; i++)
                    writer.WriteRichModifier(value.RichModifiers[i]);
        }

        public static TextNode ReadTextNode(this NetworkReader reader)
        {
            var node = new TextNode()
            {
                Value = reader.ReadString(),
                Type = (TextNode.NodeType)reader.ReadByte()
            };

            int nodeCount = reader.ReadInt();
            node.Nodes = new TextNode[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                node.Nodes[i] = reader.ReadTextNode();

            int modifierCount = reader.ReadInt();
            node.RichModifiers = new RichModifier[modifierCount];
            for (int i = 0; i < modifierCount; i++)
                node.RichModifiers[i] = reader.ReadRichModifier();

            return node;
        }
    }
#endif
}
