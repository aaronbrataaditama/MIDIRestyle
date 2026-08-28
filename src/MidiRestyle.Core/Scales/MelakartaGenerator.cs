namespace MidiRestyle.Core.Scales;

/// <summary>
/// Generates all 72 Carnatic melakarta as <see cref="Scale"/> instances from the katapayadi/chakra
/// scheme, rather than hand-authoring 72 near-identical JSON entries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The loop order is the single easiest thing to get wrong here.</b> Ma is the OUTERMOST loop -
/// melas 1-36 use Ma1 (500 cents), melas 37-72 use Ma2 (600 cents). Within each half, Ri-Ga is the
/// chakra digit, changing every 6 melas; Dha-Ni varies fastest, changing every mela. Getting the
/// nesting wrong (e.g. Ri-Ga outermost) still produces 72 distinct scales, so it looks correct at a
/// glance - but it misaligns every one of the 72 canonical names against the wrong cents.
/// </para>
/// <para>
/// Sa (0) and Pa (700) are fixed in every melakarta; only Ri, Ga, Ma, Dha and Ni vary.
/// </para>
/// </remarks>
public static class MelakartaGenerator
{
    /// <summary>Fewest valid mela number.</summary>
    public const int MinMela = 1;

    /// <summary>Most valid mela number.</summary>
    public const int MaxMela = 72;

    /// <summary>(Ri, Ga) cents pairs, indexed by the chakra digit 0..5 - the middle loop.</summary>
    private static readonly (double Ri, double Ga)[] RiGa =
    [
        (100, 200), (100, 300), (100, 400), (200, 300), (200, 400), (300, 400),
    ];

    /// <summary>(Dha, Ni) cents pairs, indexed 0..5 - the innermost loop, varies fastest.</summary>
    private static readonly (double Dha, double Ni)[] DhaNi =
    [
        (800, 900), (800, 1000), (800, 1100), (900, 1000), (900, 1100), (1000, 1100),
    ];

    /// <summary>Ma cents by half: index 0 is Ma1 (melas 1-36), index 1 is Ma2 (melas 37-72).</summary>
    private static readonly double[] MaByHalf = [500, 600];

    /// <summary>
    /// The 12 chakra names, six melas each, in katapayadi order. Chakra <c>i</c> (0-based) covers
    /// melas <c>6i+1 .. 6i+6</c>.
    /// </summary>
    private static readonly string[] ChakraNames =
    [
        "Indu", "Netra", "Agni", "Veda", "Bana", "Rutu",
        "Rishi", "Vasu", "Brahma", "Disi", "Rudra", "Aditya",
    ];

    /// <summary>
    /// The 72 canonical melakarta names, indexed 0..71 for melas 1..72.
    /// </summary>
    /// <remarks>
    /// Mela 56 is listed here as <b>Chamaram</b>, the Muthuswami Dikshitar school's katapayadi name,
    /// rather than the "Shanmukhapriya" heard far more often in concert programme notes. Both names
    /// denote the identical pitch set; Chamaram is used because it is the name the katapayadi scheme
    /// itself produces, and the two most common published melakarta lists disagree on this single
    /// entry - so it is called out explicitly rather than silently picking the popular one.
    /// </remarks>
    public static readonly IReadOnlyList<string> CanonicalNames =
    [
        "Kanakangi", "Ratnangi", "Ganamurti", "Vanaspati", "Manavati", "Tanarupi",
        "Senavati", "Hanumatodi", "Dhenuka", "Natakapriya", "Kokilapriya", "Rupavati",
        "Gayakapriya", "Vakulabharanam", "Mayamalavagowla", "Chakravakam", "Suryakantam", "Hatakambari",
        "Jhankaradhwani", "Natabhairavi", "Keeravani", "Kharaharapriya", "Gourimanohari", "Varunapriya",
        "Mararanjani", "Charukesi", "Sarasangi", "Harikambhoji", "Dheerasankarabharanam", "Naganandini",
        "Yagapriya", "Ragavardhini", "Gangeyabhushani", "Vagadheeswari", "Shulini", "Chalanata",
        "Salagam", "Jalarnavam", "Jhalavarali", "Navaneetam", "Pavani", "Raghupriya",
        "Gavambhodi", "Bhavapriya", "Shubhapantuvarali", "Shadvidamargini", "Suvarnangi", "Divyamani",
        "Dhavalambari", "Namanarayani", "Kamavardhini", "Ramapriya", "Gamanashrama", "Vishwambari",
        "Shamalangi", "Chamaram", "Simhendramadhyamam", "Hemavati", "Dharmavati", "Neetimati",
        "Kantamani", "Rishabhapriya", "Latangi", "Vachaspati", "Mechakalyani", "Chitrambari",
        "Sucharitra", "Jyotiswarupini", "Dhatuvardhani", "Nasikabhushani", "Kosalam", "Rasikapriya",
    ];

    private const string Source =
        "Venkatamakhin's 72 melakarta katapayadi scheme (Chaturdandi Prakasika, c. 1660) for the " +
        "chakra structure and the Ri/Ga/Ma/Dha/Ni degree layout generated here; the 12 chakra names " +
        "(Indu..Aditya) follow the same katapayadi numbering convention. Mela 56's name Chamaram " +
        "follows the Muthuswami Dikshitar school's katapayadi nomenclature rather than the popular " +
        "concert name Shanmukhapriya.";

    /// <summary>Generates all 72 melakarta, in mela order.</summary>
    public static IReadOnlyList<Scale> GenerateAll() =>
        [.. Enumerable.Range(MinMela, MaxMela - MinMela + 1).Select(Generate)];

    /// <summary>
    /// Generates the melakarta with the given mela number.
    /// </summary>
    /// <param name="melaNumber">1..72, in katapayadi numbering.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="melaNumber"/> is outside 1..72.
    /// </exception>
    public static Scale Generate(int melaNumber)
    {
        ValidateMelaNumber(melaNumber);

        int zeroBased = melaNumber - 1;
        int maIdx = zeroBased / 36;          // Ma is the OUTERMOST loop: 0 => Ma1, 1 => Ma2.
        int withinHalf = zeroBased % 36;
        int rg = withinHalf / 6;             // Ri-Ga: the chakra digit, changes every 6 melas.
        int dn = withinHalf % 6;             // Dha-Ni: the innermost loop, varies fastest.

        double ma = MaByHalf[maIdx];
        (double ri, double ga) = RiGa[rg];
        (double dha, double ni) = DhaNi[dn];

        double[] degreeCents = [0, ri, ga, ma, 700, dha, ni];

        string name = CanonicalNames[zeroBased];
        string slug = name.ToLowerInvariant();

        return new Scale(
            id: $"southasia.carnatic.melakarta.{melaNumber}-{slug}",
            name: $"Melakarta {melaNumber} {name}",
            tradition: "Carnatic",
            region: "South Asia",
            degreeCents: degreeCents,
            source: Source,
            notatable: true,
            spelling: null);
    }

    /// <summary>The chakra name (Indu..Aditya) covering the given mela number.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="melaNumber"/> is outside 1..72.
    /// </exception>
    public static string ChakraNameFor(int melaNumber)
    {
        ValidateMelaNumber(melaNumber);

        int chakraIndex = (melaNumber - 1) / 6;
        return ChakraNames[chakraIndex];
    }

    private static void ValidateMelaNumber(int melaNumber)
    {
        if (melaNumber is < MinMela or > MaxMela)
        {
            throw new ArgumentOutOfRangeException(
                nameof(melaNumber),
                melaNumber,
                $"Melakarta numbers run {MinMela}..{MaxMela}.");
        }
    }
}
