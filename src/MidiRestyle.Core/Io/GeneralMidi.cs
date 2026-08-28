namespace MidiRestyle.Core.Io;

/// <summary>
/// The General MIDI program name tables, used to label a track-channel from its Program Change.
/// </summary>
/// <remarks>
/// A bare program number tells a user nothing, and the track-name meta event is absent from a great
/// many real files - so this is often the only human-readable label a track has. The tables are the
/// GM 1 sound set, which every GM/GS/XG device agrees on for programs 0..127.
/// <para>
/// Channel 10 (0-indexed 9) is looked up in a <em>different</em> table. Under GM a program change on
/// the percussion channel selects a drum <em>kit</em>, not a melodic instrument, so reporting
/// "Acoustic Grand Piano" for a standard kit would be actively misleading.
/// </para>
/// </remarks>
public static class GeneralMidi
{
    /// <summary>The GM 1 melodic sound set, indexed by program number 0..127.</summary>
    private static readonly string[] Instruments =
    [
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano",
        "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone",
        "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ",
        "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)",
        "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar",
        "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass",
        "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass",
        "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2",
        "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet",
        "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax",
        "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute",
        "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)",
        "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
        "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)",
        "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
        "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)",
        "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
        "Sitar", "Banjo", "Shamisen", "Koto",
        "Kalimba", "Bagpipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock",
        "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet",
        "Telephone Ring", "Helicopter", "Applause", "Gunshot",
    ];

    /// <summary>
    /// The GM 2 / GS drum kits, keyed by the program number that selects them. Sparse by design -
    /// only these nine program numbers name a kit; anything else is a vendor extension.
    /// </summary>
    private static readonly Dictionary<int, string> DrumKits = new()
    {
        [0] = "Standard Kit",
        [8] = "Room Kit",
        [16] = "Power Kit",
        [24] = "Electronic Kit",
        [25] = "TR-808 Kit",
        [32] = "Jazz Kit",
        [40] = "Brush Kit",
        [48] = "Orchestra Kit",
        [56] = "Sound FX Kit",
    };

    /// <summary>Number of GM programs. Program numbers are 0-based here, not the 1-based form printed on gear.</summary>
    public const int ProgramCount = 128;

    /// <summary>
    /// The instrument or kit name for <paramref name="programNumber"/> on
    /// <paramref name="channel"/>, or <see langword="null"/> if the program is out of range.
    /// </summary>
    public static string? NameFor(int programNumber, int channel) =>
        channel == Model.TrackInfo.DrumChannel
            ? DrumKitName(programNumber)
            : InstrumentName(programNumber);

    /// <summary>The GM melodic instrument for a program number, or <see langword="null"/> if out of range.</summary>
    public static string? InstrumentName(int programNumber) =>
        programNumber is >= 0 and < ProgramCount ? Instruments[programNumber] : null;

    /// <summary>
    /// The drum kit for a program number on the percussion channel. Unnamed program numbers fall back
    /// to a generic label rather than null: the channel is certainly a kit of some sort, and saying so
    /// is more useful than saying nothing.
    /// </summary>
    public static string? DrumKitName(int programNumber) =>
        programNumber is < 0 or >= ProgramCount ? null
        : DrumKits.TryGetValue(programNumber, out string? kit) ? kit
        : "Drum Kit";
}
