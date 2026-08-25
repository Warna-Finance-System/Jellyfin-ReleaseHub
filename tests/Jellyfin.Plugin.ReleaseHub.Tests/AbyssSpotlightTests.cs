using Jellyfin.Plugin.ReleaseHub.Integration;
using Xunit;

namespace Jellyfin.Plugin.ReleaseHub.Tests;

/// <summary>
/// Verifies how the Abyss spotlight tag is recognised in a served document.
/// </summary>
/// <remarks>
/// <para>
/// The tag is written into jellyfin-web by a theme installer that ReleaseHub does not control and
/// cannot pin: a future Abyss is free to reorder its attributes or rename one of them. These cases
/// pin down what "recognised" has to mean so that a cosmetic change upstream does not silently turn
/// the feature off.
/// </para>
/// <para>
/// The production methods are called directly, not a copy of the expression: a test that reimplements
/// what it is checking passes forever, including after the real behaviour has broken. Only the
/// configuration lookup is left out, since it needs a running server.
/// </para>
/// </remarks>
public class AbyssSpotlightTests
{
    private const string RealTag =
        """<script src="ui/spotlight-loader.js" data-abyss-spotlight></script>""";

    [Theory]
    [InlineData(RealTag)]
    // Attributes reordered.
    [InlineData("""<script data-abyss-spotlight src="ui/spotlight-loader.js"></script>""")]
    // Marker attribute dropped, source kept.
    [InlineData("""<script src="ui/spotlight-loader.js"></script>""")]
    // Source moved, marker attribute kept.
    [InlineData("""<script src="/web/assets/abyss/boot.js" data-abyss-spotlight></script>""")]
    // Defer added, absolute path, uppercase tag.
    [InlineData("""<SCRIPT defer src="/web/ui/spotlight-loader.js"></SCRIPT>""")]
    public void RecognisedForms_AreRemoved(string tag)
    {
        var document = "<body><div id=\"reactRoot\"></div>" + tag + "</body>";

        Assert.True(TransformationPatches.HasAbyssSpotlight(document));
        Assert.Equal("<body><div id=\"reactRoot\"></div></body>", Strip(document));
    }

    [Theory]
    // A different plugin's script must survive untouched.
    [InlineData("""<script src="/HomeScreen/home-screen-sections.js" defer></script>""")]
    // Media Bar's slideshow is the carousel being kept, not the one being removed.
    [InlineData("""<script defer src="https://cdn.jsdelivr.net/gh/x/jellyfin-plugin-media-bar@main/slideshowpure.js"></script>""")]
    public void UnrelatedScripts_AreLeftAlone(string tag)
    {
        var document = "<body>" + tag + "</body>";

        Assert.False(TransformationPatches.HasAbyssSpotlight(document));
        Assert.Equal(document, Strip(document));
    }

    [Fact]
    public void InlineScriptMentioningTheName_IsNotSwallowed()
    {
        // Only an element with nothing between its tags is a loader reference. A script that merely
        // names the file in its own code — a mod detecting the theme, say — carries logic that must
        // not be deleted along with it.
        var document =
            """<body><script>if (document.querySelector('[data-abyss-spotlight]')) { init(); }</script></body>""";

        Assert.Equal(document, Strip(document));
    }

    [Fact]
    public void DocumentWithoutTheTag_IsUnchanged()
    {
        var document = "<html><body><div id=\"reactRoot\"></div></body></html>";

        Assert.Equal(document, Strip(document));
    }

    /// <summary>
    /// Applies only the removal, bypassing the configuration lookup the public entry point performs.
    /// </summary>
    /// <param name="document">The document to strip.</param>
    /// <returns>The document without any Abyss spotlight loader element.</returns>
    private static string Strip(string document) => TransformationPatches.StripAbyssSpotlight(document);
}
