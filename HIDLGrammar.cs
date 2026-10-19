namespace Horizon.HIDL;

/// <summary>
/// The language's TextMate grammar, the one file that colours HIDL in VS Code (editors/vscode) and in Hex alike. It
/// is built into this assembly as a resource so an editor made with the engine never has to go looking for it.
/// </summary>
public static class HIDLGrammar
{
    /// <summary>The scope every token of the grammar is under, what a TextMate registry loads it by.</summary>
    public const string ScopeName = "source.hidl";

    /// <summary>The file extensions that are HIDL, a layout or data (.hor), a move list (.jab) and Hex's workspace file.</summary>
    public static readonly string[] Extensions = [".hor", ".jab", ".hex-workspace"];

    private const string RESOURCE = "hidl.tmLanguage.json";

    /// <summary>The grammar as JSON, read anew every time.</summary>
    public static Stream Open() =>
        typeof(HIDLGrammar).Assembly.GetManifestResourceStream(RESOURCE)
        ?? throw new InvalidOperationException($"The HIDL assembly has no {RESOURCE} in it, the grammar wasn't embedded.");

    /// <summary>The grammar as text, for a test or a tool that writes it out.</summary>
    public static string Read()
    {
        using var reader = new StreamReader(Open());
        return reader.ReadToEnd();
    }
}
