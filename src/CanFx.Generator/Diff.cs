using System.Text;

namespace CanFx.Generator;

/// <summary>Önizleme için satır farkı (birleşik biçim, değişikliğin çevresinde birkaç satır bağlam).</summary>
internal static class Diff
{
    public static string Unified(string? before, string? after, int context = 2)
    {
        string[] a = Split(before);
        string[] b = Split(after);

        // En uzun ortak alt dizi (dosyalar küçük: istek, controller, eşleme).
        int[,] lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        var lines = new List<(char Kind, string Text)>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                lines.Add((' ', a[x])); x++; y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                lines.Add(('+', b[y])); y++;
            }
            else
            {
                lines.Add(('-', a[x])); x++;
            }
        }

        // Yalnızca değişen satırlar ve çevresi.
        var keep = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == ' ')
                continue;
            for (int k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++)
                keep[k] = true;
        }

        var output = new StringBuilder();
        bool gap = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!keep[i])
            {
                gap = true;
                continue;
            }

            if (gap && output.Length > 0)
                output.AppendLine("   ...");
            gap = false;
            output.Append(lines[i].Kind).Append(' ').AppendLine(lines[i].Text);
        }

        return output.ToString();
    }

    private static string[] Split(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
