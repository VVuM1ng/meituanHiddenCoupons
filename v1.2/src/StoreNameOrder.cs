using System;
using System.IO;
using System.Text;

internal static class StoreNameOrder
{
    // One initial per Unicode code point, from the first kMandarin reading in Unihan 17.0.
    private static readonly byte[] Mandarin = ReadInitials();

    private static byte[] ReadInitials()
    {
        using (var data = typeof(StoreNameOrder).Assembly.GetManifestResourceStream("pinyin-initials.bin"))
        using (var memory = new MemoryStream())
        {
            data.CopyTo(memory);
            return memory.ToArray();
        }
    }

    internal static string Initials(string name, out string hanCharacters)
    {
        var text = name.Normalize(NormalizationForm.FormKC);
        var initials = new StringBuilder(text.Length);
        var han = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int code = text[i];
            int length = 1;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = char.ConvertToUtf32(text[i], text[i + 1]);
                length = 2;
            }
            byte initial = code < Mandarin.Length ? Mandarin[code] : (byte)0;
            if (initial != 0 || IsHan(code))
            {
                han.Append(text, i, length);
                if (initial != 0) initials.Append((char)initial);
                else initials.Append(text, i, length);
            }
            else if (char.IsLetterOrDigit(text, i))
            {
                if (length == 2) initials.Append(text, i, length);
                else initials.Append(char.ToUpperInvariant(text[i]));
            }
            i += length - 1;
        }
        hanCharacters = han.ToString();
        return initials.ToString();
    }

    private static bool IsHan(int code)
    {
        return (code >= 0x3400 && code <= 0x4DBF) || (code >= 0x4E00 && code <= 0x9FFF)
            || (code >= 0xF900 && code <= 0xFAFF) || (code >= 0x20000 && code <= 0x3347F);
    }
}
