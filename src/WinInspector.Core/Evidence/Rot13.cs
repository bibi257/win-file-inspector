namespace WinInspector.Core.Evidence;
public static class Rot13 { public static string Decode(string value)=>new(value.Select(c=>c is >= 'a' and <= 'z'?(char)('a'+(c-'a'+13)%26):c is >= 'A' and <= 'Z'?(char)('A'+(c-'A'+13)%26):c).ToArray()); }
