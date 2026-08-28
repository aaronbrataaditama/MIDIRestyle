using Avalonia;
using Avalonia.Media;

namespace MidiRestyle.App.Controls;

/// <summary>
/// Outlines for the notation symbols that need a real engraved shape rather than an approximation:
/// the two clefs and the quarter rest.
/// </summary>
/// <remarks>
/// <para>
/// These three were hand-authored as stroked beziers until 2026-08-28 and all three read wrong - the
/// G clef in particular came out narrow and mirror-ish, because a treble clef is a specific
/// calligraphic figure and not something that can be approximated freehand from memory. They are now
/// the genuine outlines, taken verbatim from public-domain SVGs on Wikimedia Commons
/// (<c>Music-GClef.svg</c>, <c>Music-Fclef.svg</c>, <c>Crochet2.svg</c>).
/// </para>
/// <para>
/// The path strings are kept <b>exactly as published</b>, in their own source files' coordinates, so
/// they can be diffed against the originals. Nothing is retyped into staff units by hand. All the
/// adaptation happens in <see cref="Normalise"/>, which maps a source file's own staff geometry onto
/// ours: each glyph declares how many source units its staff space is and which y its reference line
/// sits on, and comes back centred on x with that reference line at y = 0.
/// </para>
/// <para>
/// They are still filled paths drawn by us - no font is required, which is the point. Nothing in
/// this app's dependency set ships the Unicode musical symbols block, and requiring an installed
/// font would break the portable single-file promise.
/// </para>
/// </remarks>
internal static class StaffGlyphs
{
    /// <summary>The G clef, from <c>Music-GClef.svg</c>.</summary>
    /// <remarks>
    /// Its own staff lines are drawn at y = 51.638, 75.638, 99.638, 123.638 and 147.638, so a staff
    /// space is 24 units and the G line - the second from the bottom, which the spiral centres on -
    /// is at 123.638.
    /// </remarks>
    private const string GClefPath =
        "M104.549 111.639c-2.589.845-4.7286 2.271-6.419 4.279-1.6906 2.007-2.7207 4.2-3.0905 6.577-.3698 2.377-.066 4.728.9113 7.052.9773 2.325 2.8131 4.2 5.5072 5.627.634 0 1.03.264 1.189.792.158.528-.08.792-.713.792-2.589-.528-4.8606-1.611-6.8152-3.248-3.6452-3.012-5.6526-6.894-6.0224-11.649-.2113-2.377.0132-4.675.6736-6.894.6603-2.219 1.5716-4.253 2.7338-6.102 1.4263-1.954 3.1168-3.645 5.0715-5.071.1056-.106.4094-.343.9112-.713.5019-.37.9905-.713 1.466-1.03.4755-.317 1.1885-.819 2.1395-1.506L99.1601 86.44c-2.5886 2.166-5.1507 4.556-7.6864 7.171-2.5358 2.615-4.8338 5.376-6.8941 8.281-2.0602 2.906-3.7111 5.983-4.9526 9.232-1.2414 3.249-1.8621 6.669-1.8621 10.262 0 3.328.6999 6.458 2.0999 9.39 1.3999 2.932 3.2621 5.481 5.5865 7.646 2.3244 2.166 5.0054 3.87 8.043 5.112 3.0376 1.241 6.1148 1.862 9.2317 1.862.106 0 .594-.053 1.466-.159.872-.105 1.796-.237 2.773-.396.978-.158 1.876-.33 2.695-.515.818-.185 1.228-.383 1.228-.594l-.476-2.219c-2.06-10.407-4.015-20.365-5.863-29.874zm2.773-.317 6.498 31.459c3.751-1.427 6.286-3.87 7.607-7.33 1.321-3.46 1.624-6.973.911-10.539-.713-3.566-2.39-6.723-5.032-9.47-2.641-2.747-5.969-4.12-9.984-4.12zm-8.3204-42.394c1.6374-.846 3.1564-2.113 4.5564-3.804 1.4-1.69 2.694-3.5 3.883-5.428 1.188-1.928 2.219-3.896 3.09-5.904.872-2.007 1.572-3.829 2.1-5.467.581-1.743.977-3.698 1.189-5.864.211-2.166-.132-3.988-1.03-5.468-.634-1.32-1.466-2.086-2.496-2.298-1.031-.211-2.061-.132-3.091.238-1.03.37-2.007.964-2.932 1.783-.924.819-1.598 1.545-2.02 2.179-1.163 2.06-2.18 4.358-3.0513 6.894-.8717 2.536-1.466 5.164-1.7829 7.885-.317 2.72-.3566 5.335-.1189 7.845.2377 2.509.8056 4.979 1.7037 7.409zm-2.3772 2.456c-.8981-3.486-1.6905-6.907-2.3773-10.262-.6868-3.354-1.0301-6.801-1.0301-10.341 0-2.588.1849-5.428.5547-8.518.3697-3.09 1.0433-6.102 2.0206-9.034.9773-2.932 2.3244-5.56 4.0413-7.884 1.7174-2.325 4.0014-4.015 6.8544-5.072.264-.105.528-.158.792-.158.37 0 .806.211 1.308.634.502.422 1.03 1.043 1.585 1.862.554.819 1.043 1.664 1.466 2.536.422.871.739 1.466.951 1.783 1.426 2.694 2.469 5.56 3.13 8.597.66 3.038 1.043 6.062 1.149 9.073.211 4.544-.04 9.034-.753 13.472-.713 4.437-2.153 8.769-4.319 12.995-.739 1.268-1.492 2.549-2.258 3.843-.766 1.295-1.677 2.549-2.734 3.764-.211.212-.594.595-1.149 1.149l-1.704 1.704c-.581.581-1.096 1.123-1.545 1.624-.449.502-.673.806-.673.912l3.248 15.848c.021.104 1.625 0 1.625 0 3.1.039 6.382.541 9.232 1.664 2.747 1.268 5.111 3.011 7.092 5.23 1.981 2.219 3.565 4.715 4.754 7.488 1.189 2.774 1.783 5.587 1.783 8.44 0 2.852-.423 5.758-1.268 8.716-2.166 5.6-5.626 9.747-10.38 12.441-.529.317-1.282.674-2.259 1.07-.977.396-1.36 1.017-1.149 1.862 1.268 5.756 2.126 9.716 2.576 11.886.449 2.17.779 4.97.99 8.4.211 3.28-.357 6.23-1.704 8.87-1.347 2.65-3.156 4.8-5.428 6.46-2.271 1.67-4.345 2.64-6.22 2.94-1.876.29-3.157.43-3.843.43-2.3776 0-4.702-.45-6.9736-1.35-2.7999-1.05-5.1507-2.66-7.0525-4.83-1.9018-2.17-2.8527-4.81-2.8527-7.92 0-1.96.5679-3.97 1.7037-6.03 1.1358-2.06 2.6282-3.54 4.4771-4.43 2.0603-1.06 3.9225-1.35 5.5866-.88 1.664.48 3.0375 1.38 4.1205 2.7 1.0829 1.32 1.8359 2.92 2.2589 4.79.422 1.88.396 3.63-.08 5.27-.475 1.64-1.439 3.03-2.892 4.16-1.4528 1.14-3.4735 1.65-6.062 1.55 1.0565 1.9 2.5357 3.1 4.4375 3.6 1.9018.51 3.8565.54 5.8635.12 2.008-.42 3.896-1.2 5.666-2.34 1.77-1.13 3.157-2.36 4.16-3.68.634-.95 1.11-2.19 1.427-3.72.317-1.54.502-3.13.554-4.8.053-1.66 0-2.96-.158-3.88-.159-.93-.423-2.37-.793-4.32-1.584-6.39-2.588-10.41-3.011-12.05-.211-.523-.779-.695-1.703-.51-.925.185-1.704.357-2.338.51-4.543.59-8.3468.32-11.4108-.787-4.7545-1.268-8.9411-3.527-12.5598-6.776-3.6187-3.248-6.5242-7.184-8.7166-11.807-2.1923-4.622-3.2885-8.135-3.2885-10.539v-4.556c0-4.279.7396-8.294 2.2188-12.045 2.7998-5.864 6.1148-11.252 9.9448-16.165 3.83-4.913 8.2015-9.483 13.1145-13.709z";

    /// <summary>The F clef's body, from <c>Music-Fclef.svg</c>.</summary>
    /// <remarks>
    /// That file's staff lines sit at y = 50, 73.405, 97.693, 121 and 146, so its staff space is 24
    /// units and the F line - the second from the top, which the two dots straddle - is at 73.405.
    /// The dots are <b>not</b> taken from the file: it places them a little off its own F line, and
    /// they are trivially placeable correctly, so <see cref="StaffView"/> draws them exactly half a
    /// space either side instead.
    /// </remarks>
    private const string FClefPath =
        "m 62.511677,127.84048 c 0,-0.83977 4.041963,-4.29526 8.982141,-7.67885 10.621365,-7.27471 18.291956,-15.2339 22.753427,-23.609504 10.231245,-19.207279 6.990215,-39.234197 -6.645392,-41.063116 -7.541825,-1.01157 -17.090176,4.491435 -17.090176,9.84959 0,1.98778 0.508501,2.147884 6.037438,1.900912 6.764925,-0.302182 11.341654,6.282702 7.680759,11.050886 -5.784567,7.53419 -19.718197,3.205925 -19.718197,-6.12515 0,-8.178104 4.976735,-14.686661 13.736031,-17.963933 18.744999,-7.013402 36.588252,5.915889 35.025872,25.379878 -1.28058,15.953347 -15.170703,30.852697 -42.135847,45.197347 -6.39814,3.40362 -8.626056,4.19445 -8.626056,3.06194 z";

    /// <summary>
    /// The quarter rest, from <c>Crochet2.svg</c> - the two filled contours of its zigzag, joined.
    /// </summary>
    /// <remarks>
    /// That file's staff lines sit at y = 50.418, 74.443, 98.467, 122.492 and 146.516, so its staff
    /// space is 24.0245 units and the middle line the rest centres on is at 98.467. Concatenating
    /// the two contours is valid path data - the second begins with its own <c>m</c> - and one
    /// geometry means one fill call.
    /// </remarks>
    private const string QuarterRestPath =
        "M 33.585446,59.378537 49.000347,80.448853 C 34.510389,96.966456 43.303241,103.77053 46.891412,113.31714 L 30.195758,89.013879 c 9.651793,-11.411594 5.787047,-20.20785 2.345326,-29.067873 -0.002,-0.0045 1.042493,-0.561506 1.044362,-0.567469 z " +
        "m 11.981073,51.226143 c -17.76994,-15.91987 -24.592214,4.82994 -7.083379,19.74003 -2.252919,-3.86658 -8.756028,-22.85814 7.953256,-17.07143 z";

    /// <summary>
    /// Maps a glyph from its source file's coordinates into ours: centred on x, with its reference
    /// line at y = 0, and scaled so the source staff space measures <paramref name="staffSpace"/>.
    /// </summary>
    private static Geometry Normalise(string data, double sourceSpace, double referenceY, double staffSpace)
    {
        Geometry geometry = Geometry.Parse(data);

        // Read before the transform is attached, so this is the raw outline's own extent.
        Rect bounds = geometry.Bounds;
        double scale = staffSpace / sourceSpace;

        geometry.Transform = new MatrixTransform(
            Matrix.CreateTranslation(-bounds.Center.X, -referenceY) * Matrix.CreateScale(scale, scale));

        return geometry;
    }

    /// <summary>The G clef, with the centre of its spiral at the origin - place that on the G line.</summary>
    public static Geometry TrebleClef(double staffSpace) =>
        Normalise(GClefPath, 24.0, 123.638, staffSpace);

    /// <summary>The F clef, with the origin on the F line - the dots are drawn separately.</summary>
    public static Geometry BassClef(double staffSpace) =>
        Normalise(FClefPath, 24.0, 73.405, staffSpace);

    /// <summary>The quarter rest, with the origin on the middle line.</summary>
    public static Geometry QuarterRest(double staffSpace) =>
        Normalise(QuarterRestPath, 24.0245, 98.467, staffSpace);
}
