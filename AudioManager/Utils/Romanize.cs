namespace Audio.Utils;

public static class Romanize
{
    public static string FromCyrillic(ReadOnlySpan<char> cyrillic_text)
    {
        var romanized_text = string.Empty;
        foreach (var c in cyrillic_text)
        {
            var romanized = CyrillicToRomanSwitch(c);
            romanized_text += char.IsLower(c) ? romanized :
                romanized.Length > 1 ? char.ToUpper(romanized[0]) + romanized[1..] : char.ToUpper(romanized[0]);
        }

        return romanized_text;
    }

    public static string CyrillicToRomanSwitch(char letter)
    {
        return char.ToLower(letter) switch
        {
            'в' => 'v',
            'е' => 'e',
            'р' => 'r',
            'т' => 't',
            'ъ' => 'u',
            'у' => 'u',
            'и' => 'i',
            'о' => 'o',
            'п' => 'p',
            'а' => 'a',
            'с' => 's',
            'д' => 'd',
            'ф' => 'f',
            'г' => 'g',
            'х' => 'h',
            'й' => 'y',
            'к' => 'k',
            'л' => 'l',
            'з' => 'z',
            'ь' => 'y',
            'ц' => 'c',
            'б' => 'b',
            'н' => 'n',
            'м' => 'm',

            'я' => "ya",
            'ж' => "zh",
            'ч' => "ch",
            'ш' => "sh",
            'щ' => "sht",
            'ю' => "yu",

            // Russian characters
            'ы' => 'y',
            _ => letter
        } + "";
    }
}