using UnityEngine;

namespace PicoShot.Localization
{
    public static class RichExtenstions
    {
        static string ToHexString(this Color color)
        {
            return ((byte)(color.r * 255f)).ToString("X2") + ((byte)(color.g * 255f)).ToString("X2") + ((byte)(color.b * 255f)).ToString("X2") + ((byte)(color.a * 255f)).ToString("X2");
        }

        public static TextNode BoldModifier(this TextNode node)
        {
            return node.AddModifier(new RichModifier("b"));
        }

        public static TextNode ItalicModifier(this TextNode node)
        {
            return node.AddModifier(new RichModifier("i"));
        }

        public static TextNode ColorModifier(this TextNode node, Color color)
        {
            return node.AddModifier(new RichModifier("color", $"#{color.ToHexString().ToLower()}"));
        }

        public static TextNode ColorModifier(this TextNode node, string color)
        {
            return node.AddModifier(new RichModifier("color", color));
        }

        public static TextNode SizeModifier(this TextNode node, string size)
        {
            return node.AddModifier(new RichModifier("size", size));
        }

        public static TextNode StyleModifier(this TextNode node, string style)
        {
            return node.AddModifier(new RichModifier("style", style));
        }



        public static TextNode BoldModifier(this string node)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("b"));
        }

        public static TextNode ItalicModifier(this string node)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("i"));
        }

        public static TextNode ColorModifier(this string node, Color color)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("color", $"#{color.ToHexString().ToLower()}"));
        }

        public static TextNode ColorModifier(this string node, string color)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("color", color));
        }

        public static TextNode SizeModifier(this string node, string size)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("size", size));
        }

        public static TextNode StyleModifier(this string node, string style)
        {
            return TextNode.Text(node).AddModifier(new RichModifier("style", style));
        }
    }
}