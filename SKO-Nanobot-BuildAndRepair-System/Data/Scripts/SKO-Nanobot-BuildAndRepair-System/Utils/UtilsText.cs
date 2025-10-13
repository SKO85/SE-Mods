using System;
using System.Text;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    internal static class UtilsText
    {
        /// <summary>
        /// Wraps a long text into multiple lines, inserting newlines
        /// so that each line does not exceed the specified maximum length.
        /// Words are kept intact unless a single word is longer than the limit,
        /// in which case it is placed on its own line.
        /// </summary>
        public static string WrapText(string text, int maxLineLength)
        {
            if (string.IsNullOrEmpty(text) || maxLineLength < 1)
                return text;

            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var currentLineLength = 0;

            foreach (var word in words)
            {
                // If the word itself is longer than the line cap, start a new line
                if (word.Length > maxLineLength)
                {
                    // Start new line if current line already has content
                    if (currentLineLength > 0)
                    {
                        sb.AppendLine();
                        currentLineLength = 0;
                    }

                    sb.AppendLine(word);
                    currentLineLength = 0;
                    continue;
                }

                // If adding this word exceeds the cap, start a new line
                if (currentLineLength + word.Length + (currentLineLength > 0 ? 1 : 0) > maxLineLength)
                {
                    sb.AppendLine();
                    sb.Append(word);
                    currentLineLength = word.Length;
                }
                else
                {
                    if (currentLineLength > 0)
                    {
                        sb.Append(' ');
                        currentLineLength++;
                    }

                    sb.Append(word);
                    currentLineLength += word.Length;
                }
            }

            return sb.ToString();
        }
    }
}