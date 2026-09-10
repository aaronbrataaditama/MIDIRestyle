using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiRestyle.App.Tests;

/// <summary>Synthesised MIDI files - no binaries are committed. Each note is a quarter at 480 PPQN.</summary>
internal static class MidiFixtures
{
    /// <summary>Tonic-heavy C major so key detection lands on C major, not A minor.</summary>
    public static readonly (int Note, int Channel)[] CMajorNotes =
        [.. new[] { 60, 64, 67, 60, 62, 65, 67, 60, 69, 71, 72, 67, 60 }.Select(n => (n, 0))];

    /// <summary>Tonic-heavy A minor.</summary>
    public static readonly (int Note, int Channel)[] AMinorNotes =
        [.. new[] { 57, 60, 64, 57, 59, 62, 64, 57, 65, 67, 69, 64, 57 }.Select(n => (n, 0))];

    public static readonly (int Note, int Channel)[] DrumsOnlyNotes =
        [.. new[] { 36, 38, 42, 36, 38, 42 }.Select(n => (n, 9))];

    public static string Write(string path, IEnumerable<(int Note, int Channel)> notes, bool format0 = false)
    {
        var byChannel = new Dictionary<int, List<Note>>();
        long time = 0;
        foreach ((int note, int channel) in notes)
        {
            if (!byChannel.TryGetValue(channel, out List<Note>? list))
            {
                list = [];
                byChannel[channel] = list;
            }

            list.Add(new Note((SevenBitNumber)note, 480, time)
            {
                Channel = (FourBitNumber)channel,
                Velocity = (SevenBitNumber)90,
            });
            time += 480;
        }

        var file = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        var tempo = new TrackChunk(new SetTempoEvent(500_000));

        if (format0)
        {
            using (var manager = tempo.ManageNotes())
            {
                manager.Objects.Add(byChannel.Values.SelectMany(n => n));
            }

            file.Chunks.Add(tempo);
            file.Write(path, overwriteFile: true, format: MidiFileFormat.SingleTrack);
            return path;
        }

        file.Chunks.Add(tempo);
        foreach ((int channel, List<Note> channelNotes) in byChannel.OrderBy(kv => kv.Key))
        {
            var chunk = new TrackChunk();
            using (var manager = chunk.ManageNotes())
            {
                manager.Objects.Add(channelNotes);
            }

            file.Chunks.Add(chunk);
        }

        file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
        return path;
    }
}
