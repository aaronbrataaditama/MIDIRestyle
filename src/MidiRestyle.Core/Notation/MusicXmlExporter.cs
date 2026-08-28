using System.Globalization;
using System.Text;
using System.Xml;

namespace MidiRestyle.Core.Notation;

/// <summary>
/// Serialises a <see cref="NotationScore"/> as a MusicXML 4.0 partwise document.
/// </summary>
/// <remarks>
/// <para>
/// This class does no musical thinking. <see cref="NotationBuilder"/> has already split measures,
/// tied notes across barlines, packed voices, filled every gap with rests and decomposed each span
/// into written durations, so the only job left is to say all of that in MusicXML's vocabulary.
/// Re-deriving any of it here would guarantee that the exported file and the staff view - which
/// read the same model - eventually disagreed.
/// </para>
/// <para>
/// <b>The single moving cursor is the thing to understand.</b> MusicXML has one position per part,
/// which every <c>&lt;note&gt;</c> advances by its <c>&lt;duration&gt;</c>. Two things rewind or
/// hold it, and both are easy to get wrong:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <c>&lt;chord/&gt;</c> note sounds <em>with</em> the note before it and consumes no time. A
/// three-note chord must advance the cursor by one note's duration, not three.
/// </description></item>
/// <item><description>
/// A second voice or a second staff starts back at the beginning of the measure, so an explicit
/// <c>&lt;backup&gt;</c> has to rewind the cursor before it. Without it every voice after the first
/// is written after the measure it belongs in.
/// </description></item>
/// </list>
/// </remarks>
public static class MusicXmlExporter
{
    /// <summary>The MusicXML version this writes, and the value of the root's <c>version</c>.</summary>
    public const string Version = "4.0";

    /// <summary>Public identifier of the partwise DTD.</summary>
    public const string DocTypePublicId = "-//Recordare//DTD MusicXML 4.0 Partwise//EN";

    /// <summary>System identifier of the partwise DTD. Never fetched - it is a declaration only.</summary>
    public const string DocTypeSystemId = "http://www.musicxml.org/dtds/partwise.dtd";

    /// <summary>What lands in <c>&lt;software&gt;</c>.</summary>
    public const string SoftwareName = "MIDIRestyle";

    /// <summary>
    /// The <em>smallest</em> per-staff stride the voice numbering uses.
    /// </summary>
    /// <remarks>
    /// MusicXML voice numbers are unique across a whole <em>part</em>, but
    /// <see cref="NotationEntry.Voice"/> is unique only within a staff. On a grand staff the two
    /// staves' voice 1s must therefore be given different numbers, or a reader cannot tell which
    /// staff a voice belongs to. Four is the convention Finale and MuseScore both write - staff 1
    /// gets voices 1-4, staff 2 gets 5-8 - and it is <see cref="NotationBuilder.MaxVoicesPerStaff"/>
    /// for exactly that reason.
    /// <para>
    /// It is a floor rather than the stride itself because the builder's four is now a readability
    /// threshold and no longer a cap: a staff may legitimately carry up to
    /// <see cref="NotationBuilder.VoiceCeilingPerStaff"/> voices. A fixed stride of four against a
    /// staff using six would give staff 1's voice 5 and staff 2's voice 1 the same number, and the
    /// two staves' music would merge in the reader. <see cref="VoiceStrideFor"/> therefore widens
    /// the stride to whatever the part actually uses, and only then.
    /// </para>
    /// </remarks>
    private const int MinimumVoicesPerStaff = NotationBuilder.MaxVoicesPerStaff;

    /// <summary>No BOM: the declaration already says UTF-8, and some older readers choke on one.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Serialises <paramref name="score"/> to a MusicXML 4.0 partwise document.</summary>
    /// <exception cref="MusicXmlExportException">
    /// The score has no parts. MusicXML requires at least one, and a part-less document is one no
    /// reader will open - better to say so than to write a file that silently fails elsewhere.
    /// </exception>
    public static string ToXml(NotationScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (score.Parts.Count == 0)
        {
            throw new MusicXmlExportException(
                "There is nothing to export: this score has no parts. A MusicXML file needs at "
                + "least one part.");
        }

        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Utf8NoBom,
        };

        using Utf8StringWriter text = new();

        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            WriteScore(writer, score);
            writer.Flush();
        }

        return text.ToString();
    }

    /// <summary>Writes the document to <paramref name="path"/>, creating its folder if needed.</summary>
    /// <exception cref="MusicXmlExportException">
    /// The score cannot be serialised, or the file cannot be written.
    /// </exception>
    public static void Write(NotationScore score, string path)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string xml = ToXml(score);

        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, xml, Utf8NoBom);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException
                or System.Security.SecurityException)
        {
            throw new MusicXmlExportException(
                $"Could not write the MusicXML file: {ex.Message}", path, ex);
        }
    }

    // --- document ----------------------------------------------------------------------

    private static void WriteScore(XmlWriter writer, NotationScore score)
    {
        writer.WriteStartDocument();
        writer.WriteDocType("score-partwise", DocTypePublicId, DocTypeSystemId, null);

        writer.WriteStartElement("score-partwise");
        writer.WriteAttributeString("version", Version);

        WriteWork(writer, score);
        WriteIdentification(writer, score);
        WritePartList(writer, score);

        foreach (NotationPart part in score.Parts)
        {
            WritePart(writer, score, part);
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWork(XmlWriter writer, NotationScore score)
    {
        string title = Sanitise(score.Title);

        if (title.Length == 0)
        {
            return;
        }

        writer.WriteStartElement("work");
        writer.WriteElementString("work-title", title);
        writer.WriteEndElement();
    }

    private static void WriteIdentification(XmlWriter writer, NotationScore score)
    {
        writer.WriteStartElement("identification");

        writer.WriteStartElement("encoding");
        writer.WriteElementString("software", SoftwareName);
        writer.WriteElementString(
            "encoding-date", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        writer.WriteEndElement();

        // Which scale the file was restyled into is the one fact about this document that cannot be
        // recovered from its notes, since a maqam and a chromatic passage look identical on the
        // staff. `miscellaneous` is where MusicXML puts application facts it has no element for.
        string scale = Sanitise(score.ScaleName);

        if (scale.Length > 0)
        {
            writer.WriteStartElement("miscellaneous");
            writer.WriteStartElement("miscellaneous-field");
            writer.WriteAttributeString("name", "midirestyle-scale");
            writer.WriteString(scale);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WritePartList(XmlWriter writer, NotationScore score)
    {
        writer.WriteStartElement("part-list");

        foreach (NotationPart part in score.Parts)
        {
            writer.WriteStartElement("score-part");
            writer.WriteAttributeString("id", part.Id);

            // Part names come out of user files and may hold anything a byte can hold, including
            // the markup characters and the control characters XML forbids outright. XmlWriter
            // escapes the first kind; Sanitise removes the second, which would otherwise throw.
            writer.WriteElementString("part-name", Sanitise(part.Name));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    // --- parts and measures ------------------------------------------------------------

    private static void WritePart(XmlWriter writer, NotationScore score, NotationPart part)
    {
        writer.WriteStartElement("part");
        writer.WriteAttributeString("id", part.Id);

        // Once per part, not once per note: the stride has to be the same in every measure of the
        // part, or a voice number means something different in bar 2 than it did in bar 1.
        int voiceStride = VoiceStrideFor(part);

        if (part.Measures.Count == 0)
        {
            // A `<part>` with no `<measure>` is not legal MusicXML. One empty measure carrying the
            // attributes is, and reads as the empty part it is.
            writer.WriteStartElement("measure");
            writer.WriteAttributeString("number", "1");
            WriteAttributes(writer, score, part, beats: 4, beatUnit: 4, full: true);
            writer.WriteEndElement();
        }
        else
        {
            for (int i = 0; i < part.Measures.Count; i++)
            {
                WriteMeasure(
                    writer, score, part, part.Measures[i], voiceStride, isFirst: i == 0);
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteMeasure(
        XmlWriter writer,
        NotationScore score,
        NotationPart part,
        NotationMeasure measure,
        int voiceStride,
        bool isFirst)
    {
        writer.WriteStartElement("measure");
        writer.WriteAttributeString(
            "number", measure.Number.ToString(CultureInfo.InvariantCulture));

        if (isFirst)
        {
            WriteAttributes(
                writer, score, part, measure.BeatsPerMeasure, measure.BeatUnit, full: true);
        }
        else if (measure.TimeSignatureChanged)
        {
            // Only where the signature actually changes. MusicXML prints a `<time>` wherever it
            // finds one, so repeating it every measure would litter the score with 4/4s.
            WriteAttributes(
                writer, score, part, measure.BeatsPerMeasure, measure.BeatUnit, full: false);
        }

        // Accidentals are display-only and their effect stops at the barline, so what is currently
        // in force resets here. Keyed by staff position, which is what an accidental actually
        // qualifies: C-sharp and C-flat sit on the same line and cancel each other.
        Dictionary<(int Staff, int Diatonic), double> accidentals = [];

        long cursor = 0;

        foreach (List<NotationEntry> voice in GroupByStaffThenVoice(measure.Entries))
        {
            long voiceStart = Math.Max(0, voice[0].StartTicks - measure.StartTicks);

            // The rewind that makes multi-voice and grand-staff output open correctly. The builder
            // starts every voice at the barline, so in practice this backs up the whole measure;
            // the general form is kept because a voice that starts late is legal and must not be
            // silently dragged to the front.
            if (cursor > voiceStart)
            {
                WriteDurationElement(writer, "backup", cursor - voiceStart);
            }
            else if (cursor < voiceStart)
            {
                WriteDurationElement(writer, "forward", voiceStart - cursor);
            }

            cursor = voiceStart;

            foreach (NotationEntry entry in voice)
            {
                long duration = WriteNote(writer, entry, part, voiceStride, accidentals);

                // The classic MusicXML export bug lives on this line. A chord member sounds with
                // the note before it and takes no time of its own; advancing here would push every
                // later note and every `<backup>` in the measure out by the whole chord.
                if (!entry.IsChordMember)
                {
                    cursor += duration;
                }
            }
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// Groups a measure's entries by staff and then voice, keeping the builder's order within each
    /// group. MusicXML wants one complete voice at a time, not the time-ordered interleaving the
    /// model holds.
    /// </summary>
    private static List<List<NotationEntry>> GroupByStaffThenVoice(
        IReadOnlyList<NotationEntry> entries)
    {
        Dictionary<(int Staff, int Voice), List<NotationEntry>> groups = [];
        List<(int Staff, int Voice)> keys = [];

        foreach (NotationEntry entry in entries)
        {
            (int Staff, int Voice) key = (entry.Staff, entry.Voice);

            if (!groups.TryGetValue(key, out List<NotationEntry>? group))
            {
                group = [];
                groups[key] = group;
                keys.Add(key);
            }

            group.Add(entry);
        }

        keys.Sort(static (a, b) =>
            a.Staff != b.Staff ? a.Staff.CompareTo(b.Staff) : a.Voice.CompareTo(b.Voice));

        return [.. keys.Select(k => groups[k])];
    }

    private static void WriteAttributes(
        XmlWriter writer,
        NotationScore score,
        NotationPart part,
        int beats,
        int beatUnit,
        bool full)
    {
        writer.WriteStartElement("attributes");

        if (full)
        {
            // `<divisions>` is the file's own PPQN, so every `<duration>` below is a whole number of
            // ticks and nothing has to be rescaled on the way out. MusicXML requires it positive.
            int divisions = score.Divisions > 0 ? score.Divisions : 1;
            writer.WriteElementString(
                "divisions", divisions.ToString(CultureInfo.InvariantCulture));

            // Always zero, whatever the tonic. A restyled maqam, melakarta or pentatonic is not a
            // major or minor key, so there is no key signature that is correct for it - and one that
            // is merely close would silently re-spell every note that disagreed with it. Every
            // accidental is written explicitly instead, which is both correct and what the target
            // scale's own spelling already gives us.
            writer.WriteStartElement("key");
            writer.WriteElementString("fifths", "0");
            writer.WriteEndElement();
        }

        WriteTime(writer, beats, beatUnit);

        if (full)
        {
            if (part.StaffCount > 1)
            {
                writer.WriteElementString(
                    "staves", part.StaffCount.ToString(CultureInfo.InvariantCulture));
            }

            for (int staff = 1; staff <= part.StaffCount; staff++)
            {
                Clef clef = staff - 1 < part.Clefs.Count ? part.Clefs[staff - 1] : Clef.Treble;
                bool bass = clef == Clef.Bass;

                writer.WriteStartElement("clef");
                writer.WriteAttributeString(
                    "number", staff.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString("sign", bass ? "F" : "G");
                writer.WriteElementString("line", bass ? "4" : "2");
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteTime(XmlWriter writer, int beats, int beatUnit)
    {
        // A signature of 0/0 would be meaningless in the file and is not worth failing over; the
        // MIDI default is what a file with no signature at all is assumed to be anyway.
        int safeBeats = beats > 0 ? beats : MeasureGrid.DefaultNumerator;
        int safeUnit = beatUnit > 0 ? beatUnit : MeasureGrid.DefaultDenominator;

        writer.WriteStartElement("time");
        writer.WriteElementString("beats", safeBeats.ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString(
            "beat-type", safeUnit.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    // --- notes -------------------------------------------------------------------------

    /// <summary>
    /// Writes one <c>&lt;note&gt;</c> and returns the duration it was written with, which is what
    /// the caller advances its cursor by - unless the entry is a chord member.
    /// </summary>
    /// <remarks>
    /// Child order follows the MusicXML DTD exactly: <c>chord</c>, <c>pitch</c> or <c>rest</c>,
    /// <c>duration</c>, <c>tie</c>, <c>voice</c>, <c>type</c>, <c>dot</c>, <c>accidental</c>,
    /// <c>time-modification</c>, <c>staff</c>, <c>beam</c>, <c>notations</c>. Readers are strict
    /// about this. <c>beam</c> sits after <c>staff</c> and before <c>notations</c>; the DTD also
    /// allows <c>stem</c> and <c>notehead</c> between those two, neither of which this writer
    /// emits.
    /// </remarks>
    private static long WriteNote(
        XmlWriter writer,
        NotationEntry entry,
        NotationPart part,
        int voiceStride,
        Dictionary<(int Staff, int Diatonic), double> accidentals)
    {
        // A zero-length note is legal MIDI but `<duration>` must be positive. One tick keeps the
        // document valid, and returning the same value keeps the cursor and every later
        // `<backup>` consistent with what was actually written.
        long duration = Math.Max(1, entry.DurationTicks);

        writer.WriteStartElement("note");

        if (entry.IsChordMember)
        {
            WriteEmptyElement(writer, "chord");
        }

        if (entry.Note is { } note)
        {
            writer.WriteStartElement("pitch");
            writer.WriteElementString("step", char.ToString(note.LetterName));

            // `<alter>` carries the quantised accidental - a double, because a quarter-tone is
            // +/-0.5 and MusicXML's own `<alter>` takes exactly that.
            //
            // SpelledNote.ResidualCents is DELIBERATELY DROPPED here, and this is not an oversight
            // to be "fixed" later. Accidental-only output is what the user chose: what an
            // accidental cannot express, the staff does not claim. The residual survives in the
            // model for the staff renderer, which draws it as a comma mark rather than pretending
            // the written note is exact.
            if (note.Alter != 0)
            {
                writer.WriteElementString("alter", FormatAlter(note.Alter));
            }

            writer.WriteElementString(
                "octave", note.Octave.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }
        else
        {
            WriteEmptyElement(writer, "rest");
        }

        writer.WriteElementString("duration", duration.ToString(CultureInfo.InvariantCulture));

        // Rests are never tied. `Continue` is two ties, and the stop must come before the start.
        if (entry.Note is not null)
        {
            if (entry.Tie is TieState.Stop or TieState.Continue)
            {
                WriteTypedElement(writer, "tie", "stop");
            }

            if (entry.Tie is TieState.Start or TieState.Continue)
            {
                WriteTypedElement(writer, "tie", "start");
            }
        }

        writer.WriteElementString(
            "voice", VoiceNumber(entry, part, voiceStride).ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("type", entry.Duration.Value.MusicXmlType());

        for (int i = 0; i < entry.Duration.Dots; i++)
        {
            WriteEmptyElement(writer, "dot");
        }

        WriteAccidental(writer, entry, accidentals);

        Tuplet tuplet = entry.Duration.EffectiveTuplet;

        if (!tuplet.IsNone)
        {
            writer.WriteStartElement("time-modification");
            writer.WriteElementString(
                "actual-notes", tuplet.ActualNotes.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString(
                "normal-notes", tuplet.NormalNotes.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        if (part.StaffCount > 1)
        {
            writer.WriteElementString(
                "staff", entry.Staff.ToString(CultureInfo.InvariantCulture));
        }

        WriteBeams(writer, entry);

        // `<tie>` is the sound; `<tied>` inside `<notations>` is the printed slur joining the two
        // noteheads. Both are required - a file with only one of them either sounds right and looks
        // wrong or the reverse.
        if (entry.Note is not null && entry.Tie != TieState.None)
        {
            writer.WriteStartElement("notations");

            if (entry.Tie is TieState.Stop or TieState.Continue)
            {
                WriteTypedElement(writer, "tied", "stop");
            }

            if (entry.Tie is TieState.Start or TieState.Continue)
            {
                WriteTypedElement(writer, "tied", "start");
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        return duration;
    }

    /// <summary>
    /// The part-unique voice number for an entry. See <see cref="MinimumVoicesPerStaff"/> for why a
    /// grand staff's second staff cannot simply reuse voice 1.
    /// </summary>
    private static int VoiceNumber(NotationEntry entry, NotationPart part, int voiceStride) =>
        part.StaffCount > 1
            ? ((Math.Max(1, entry.Staff) - 1) * voiceStride) + entry.Voice
            : entry.Voice;

    /// <summary>
    /// How far apart consecutive staves' voice numbers are placed: the conventional four, or the
    /// highest voice the part actually uses where that is more.
    /// </summary>
    /// <remarks>
    /// Taken from the part rather than from <see cref="NotationBuilder.VoiceCeilingPerStaff"/> so
    /// that an ordinary grand staff still exports the 1-4 / 5-8 numbering every other application
    /// writes, and only a part that really does carry five or more voices on a staff pays for the
    /// wider spacing.
    /// </remarks>
    private static int VoiceStrideFor(NotationPart part)
    {
        if (part.StaffCount <= 1)
        {
            return MinimumVoicesPerStaff;
        }

        int highest = MinimumVoicesPerStaff;

        foreach (NotationMeasure measure in part.Measures)
        {
            foreach (NotationEntry entry in measure.Entries)
            {
                if (entry.Voice > highest)
                {
                    highest = entry.Voice;
                }
            }
        }

        return highest;
    }

    /// <summary>
    /// Writes one <c>&lt;beam&gt;</c> per level the entry takes part in.
    /// </summary>
    /// <remarks>
    /// The <c>number</c> attribute is the level: 1 for the eighth-note beam and upward. The builder
    /// stores them in that order and never emits more of them than the note has flags, so the index
    /// is the level and no re-derivation is needed here. A hook is spelled with a space -
    /// <c>forward hook</c>, not <c>forward-hook</c> - one of the few places MusicXML's vocabulary
    /// does not hyphenate.
    /// </remarks>
    private static void WriteBeams(XmlWriter writer, NotationEntry entry)
    {
        for (int level = 0; level < entry.Beams.Count; level++)
        {
            if (BeamText(entry.Beams[level]) is not { } text)
            {
                continue;
            }

            writer.WriteStartElement("beam");
            writer.WriteAttributeString(
                "number", (level + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteString(text);
            writer.WriteEndElement();
        }
    }

    /// <summary>The MusicXML <c>&lt;beam&gt;</c> text, or null where the level carries no beam.</summary>
    private static string? BeamText(BeamState state) => state switch
    {
        BeamState.Begin => "begin",
        BeamState.Continue => "continue",
        BeamState.End => "end",
        BeamState.ForwardHook => "forward hook",
        BeamState.BackwardHook => "backward hook",
        _ => null,
    };

    /// <summary>
    /// Writes an <c>&lt;accidental&gt;</c> where one is called for, and records what is now in
    /// force at that staff position.
    /// </summary>
    /// <remarks>
    /// With no key signature there is nothing an accidental could be redundant against, so every
    /// altered note gets one. A natural is written only where it is doing work - cancelling an
    /// accidental earlier in the same measure at the same staff position. A tie continuation gets
    /// none: it is the same notehead sounding on, and re-marking it is wrong engraving.
    /// </remarks>
    private static void WriteAccidental(
        XmlWriter writer,
        NotationEntry entry,
        Dictionary<(int Staff, int Diatonic), double> accidentals)
    {
        if (entry.Note is not { } note)
        {
            return;
        }

        double alter = QuantiseAlter(note.Alter);
        (int Staff, int Diatonic) key = (entry.Staff, note.DiatonicIndex);
        double inForce = accidentals.TryGetValue(key, out double previous) ? previous : 0;
        accidentals[key] = alter;

        if (entry.Tie is TieState.Stop or TieState.Continue)
        {
            return;
        }

        if (alter == 0 && inForce == 0)
        {
            return;
        }

        if (AccidentalName(alter) is { } name)
        {
            writer.WriteElementString("accidental", name);
        }
    }

    /// <summary>
    /// Snaps an alteration to the nearest half-semitone before it is named.
    /// </summary>
    /// <remarks>
    /// <see cref="MidpointRounding.AwayFromZero"/> for the reason it is used everywhere else in
    /// this codebase: a quarter-tone scale lands exactly on the tie, and banker's rounding would
    /// send two equal inflections in opposite directions.
    /// </remarks>
    private static double QuantiseAlter(double alter) =>
        Math.Round(alter * 2.0, MidpointRounding.AwayFromZero) / 2.0;

    /// <summary>The MusicXML <c>&lt;accidental&gt;</c> name, or null where none can draw it.</summary>
    private static string? AccidentalName(double alter) => alter switch
    {
        2.0 => "double-sharp",
        1.5 => "three-quarters-sharp",
        1.0 => "sharp",
        0.5 => "quarter-sharp",
        0.0 => "natural",
        -0.5 => "quarter-flat",
        -1.0 => "flat",
        -1.5 => "three-quarters-flat",
        -2.0 => "flat-flat",
        _ => null,
    };

    /// <summary>
    /// Formats an alteration for <c>&lt;alter&gt;</c>: "1", "-1", "-0.5". Invariant, because a
    /// comma decimal separator would make the file unreadable to every MusicXML parser there is.
    /// </summary>
    private static string FormatAlter(double alter) =>
        alter.ToString("0.###", CultureInfo.InvariantCulture);

    // --- primitives --------------------------------------------------------------------

    private static void WriteEmptyElement(XmlWriter writer, string name)
    {
        writer.WriteStartElement(name);
        writer.WriteEndElement();
    }

    private static void WriteTypedElement(XmlWriter writer, string name, string type)
    {
        writer.WriteStartElement(name);
        writer.WriteAttributeString("type", type);
        writer.WriteEndElement();
    }

    private static void WriteDurationElement(XmlWriter writer, string name, long duration)
    {
        writer.WriteStartElement(name);
        writer.WriteElementString(
            "duration", duration.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    /// <summary>
    /// Strips characters XML 1.0 cannot represent at all.
    /// </summary>
    /// <remarks>
    /// Markup characters are not this method's business - <see cref="XmlWriter"/> escapes
    /// <c>&amp;</c>, <c>&lt;</c> and the rest on its own. Control characters are: a track name is
    /// raw bytes from someone else's file and may hold any of them, and handing one to
    /// <see cref="XmlWriter"/> throws rather than escaping. Dropping them is the only lossless-enough
    /// answer, since no encoding of them is legal in XML 1.0.
    /// </remarks>
    private static string Sanitise(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool clean = true;

        foreach (char c in value)
        {
            if (!XmlConvert.IsXmlChar(c))
            {
                clean = false;
                break;
            }
        }

        if (clean)
        {
            return value;
        }

        StringBuilder builder = new(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (XmlConvert.IsXmlChar(c))
            {
                builder.Append(c);
            }
            else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                // An astral character - an emoji in a track name - is two chars, neither of which
                // is an XML char on its own. Kept as the pair it is.
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A <see cref="StringWriter"/> that reports UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> takes the encoding for its declaration from the writer it is given,
    /// and a plain <see cref="StringWriter"/> reports UTF-16 - so the document would announce
    /// <c>encoding="utf-16"</c> and then be saved as UTF-8, which is a lie a strict parser will
    /// reject.
    /// </remarks>
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Utf8NoBom;
    }
}
