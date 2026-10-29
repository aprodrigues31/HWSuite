using System.Text.RegularExpressions;

namespace HWSuite.Core.Cpuid;

/// <summary>
/// Reference specifications of a desktop processor as published by AMD or Intel. Clocks in MHz (base / maximum boost;
/// Intel hybrid parts: P-core clocks), <see cref="L3Mb"/> the last-level cache, <see cref="TdpW"/> the rated power
/// (AMD TDP, Intel base power), <see cref="Memory"/> the officially supported memory.
/// </summary>
public sealed record CpuSpec(
    string Model,
    string Codename,
    string Socket,
    string Process,
    string Released,
    int Cores,
    int Threads,
    int BaseMhz,
    int BoostMhz,
    int L3Mb,
    int TdpW,
    string Memory,
    string? Graphics)
{
    /// <summary>Intel hybrid parts: performance and efficiency cores.</summary>
    public int? PerformanceCores { get; init; }

    public int? EfficiencyCores { get; init; }
}

/// <summary>
/// Desktop processors since 2017: AMD Ryzen 1000 to 9000 (and the G APUs) and Intel Core 8th to 14th generation and
/// Core Ultra 200S. Sources: AMD and Intel product pages as compiled in the Wikipedia processor lists. Intel "F" models
/// (no integrated graphics) and "KF" share the entry of the model without the F.
/// </summary>
public static partial class CpuDatabase
{
    private const string Am4 = "AM4";
    private const string Am5 = "AM5";
    private const string Ddr4_2666 = "DDR4-2666";
    private const string Ddr4_2933 = "DDR4-2933";
    private const string Ddr4_3200 = "DDR4-3200";
    private const string Ddr5_5200 = "DDR5-5200";
    private const string Ddr5_5600 = "DDR5-5600";
    private const string Raphael = "Raphael (Zen 4)";
    private const string Granite = "Granite Ridge (Zen 5)";
    private const string Vermeer = "Vermeer (Zen 3)";
    private const string Cezanne = "Cezanne (Zen 3)";
    private const string Matisse = "Matisse (Zen 2)";
    private const string N7 = "TSMC N7 + GF 12nm (I/O)";
    private const string N5 = "TSMC N5 + N6 (I/O)";
    private const string N4P = "TSMC N4P + N6 (I/O)";
    private const string Lga1151 = "LGA 1151";
    private const string Lga1200 = "LGA 1200";
    private const string Lga1700 = "LGA 1700";
    private const string Lga1851 = "LGA 1851";
    private const string Intel14 = "Intel 14nm";
    private const string Intel7 = "Intel 7";
    private const string Alder = "Alder Lake";
    private const string Raptor = "Raptor Lake";
    private const string RaptorR = "Raptor Lake Refresh";
    private const string Ddr45AL = "DDR5-4800 / DDR4-3200";
    private const string Ddr45RL = "DDR5-5600 / DDR4-3200";
    private const string Uhd630 = "UHD 630";
    private const string Uhd730 = "UHD 730";
    private const string Uhd770 = "UHD 770";
    private const string Radeon2Cu = "Radeon Graphics (2 CU)";

    public static IReadOnlyList<CpuSpec> All { get; } =
    [
        // AMD Ryzen 1000 / 2000 (Zen, Zen+) and the first APUs
        new("Ryzen 3 1200", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-07-27", 4, 4, 3100, 3400, 8, 65, Ddr4_2666, null),
        new("Ryzen 5 1400", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-04-11", 4, 8, 3200, 3400, 8, 65, Ddr4_2666, null),
        new("Ryzen 5 1600", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-04-11", 6, 12, 3200, 3600, 16, 65, Ddr4_2666, null),
        new("Ryzen 5 1600X", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-04-11", 6, 12, 3600, 4000, 16, 95, Ddr4_2666, null),
        new("Ryzen 7 1700", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-03-02", 8, 16, 3000, 3700, 16, 65, Ddr4_2666, null),
        new("Ryzen 7 1700X", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-03-02", 8, 16, 3400, 3800, 16, 95, Ddr4_2666, null),
        new("Ryzen 7 1800X", "Summit Ridge (Zen)", Am4, "GF 14nm", "2017-03-02", 8, 16, 3600, 4000, 16, 95, Ddr4_2666, null),
        new("Ryzen 3 2200G", "Raven Ridge (Zen)", Am4, "GF 14nm", "2018-02-12", 4, 4, 3500, 3700, 4, 65, Ddr4_2933, "Radeon Vega 8"),
        new("Ryzen 5 2400G", "Raven Ridge (Zen)", Am4, "GF 14nm", "2018-02-12", 4, 8, 3600, 3900, 4, 65, Ddr4_2933, "Radeon RX Vega 11"),
        new("Ryzen 5 2600", "Pinnacle Ridge (Zen+)", Am4, "GF 12nm", "2018-04-19", 6, 12, 3400, 3900, 16, 65, Ddr4_2933, null),
        new("Ryzen 5 2600X", "Pinnacle Ridge (Zen+)", Am4, "GF 12nm", "2018-04-19", 6, 12, 3600, 4200, 16, 95, Ddr4_2933, null),
        new("Ryzen 7 2700", "Pinnacle Ridge (Zen+)", Am4, "GF 12nm", "2018-04-19", 8, 16, 3200, 4100, 16, 65, Ddr4_2933, null),
        new("Ryzen 7 2700X", "Pinnacle Ridge (Zen+)", Am4, "GF 12nm", "2018-04-19", 8, 16, 3700, 4300, 16, 105, Ddr4_2933, null),
        new("Ryzen 3 3200G", "Picasso (Zen+)", Am4, "GF 12nm", "2019-07-07", 4, 4, 3600, 4000, 4, 65, Ddr4_2933, "Radeon Vega 8"),
        new("Ryzen 5 3400G", "Picasso (Zen+)", Am4, "GF 12nm", "2019-07-07", 4, 8, 3700, 4200, 4, 65, Ddr4_2933, "Radeon RX Vega 11"),

        // AMD Ryzen 3000 (Zen 2)
        new("Ryzen 3 3100", Matisse, Am4, N7, "2020-05-21", 4, 8, 3600, 3900, 16, 65, Ddr4_3200, null),
        new("Ryzen 3 3300X", Matisse, Am4, N7, "2020-05-21", 4, 8, 3800, 4300, 16, 65, Ddr4_3200, null),
        new("Ryzen 5 3600", Matisse, Am4, N7, "2019-07-07", 6, 12, 3600, 4200, 32, 65, Ddr4_3200, null),
        new("Ryzen 5 3600X", Matisse, Am4, N7, "2019-07-07", 6, 12, 3800, 4400, 32, 95, Ddr4_3200, null),
        new("Ryzen 5 3600XT", Matisse, Am4, N7, "2020-07-07", 6, 12, 3800, 4500, 32, 95, Ddr4_3200, null),
        new("Ryzen 7 3700X", Matisse, Am4, N7, "2019-07-07", 8, 16, 3600, 4400, 32, 65, Ddr4_3200, null),
        new("Ryzen 7 3800X", Matisse, Am4, N7, "2019-07-07", 8, 16, 3900, 4500, 32, 105, Ddr4_3200, null),
        new("Ryzen 7 3800XT", Matisse, Am4, N7, "2020-07-07", 8, 16, 3900, 4700, 32, 105, Ddr4_3200, null),
        new("Ryzen 9 3900X", Matisse, Am4, N7, "2019-07-07", 12, 24, 3800, 4600, 64, 105, Ddr4_3200, null),
        new("Ryzen 9 3900XT", Matisse, Am4, N7, "2020-07-07", 12, 24, 3800, 4700, 64, 105, Ddr4_3200, null),
        new("Ryzen 9 3950X", Matisse, Am4, N7, "2019-11-25", 16, 32, 3500, 4700, 64, 105, Ddr4_3200, null),

        // AMD Ryzen 5000 (Zen 3)
        new("Ryzen 5 5500", Cezanne, Am4, "TSMC N7", "2022-04-04", 6, 12, 3600, 4200, 16, 65, Ddr4_3200, null),
        new("Ryzen 5 5600", Vermeer, Am4, N7, "2022-04-04", 6, 12, 3500, 4400, 32, 65, Ddr4_3200, null),
        new("Ryzen 5 5600X", Vermeer, Am4, N7, "2020-11-05", 6, 12, 3700, 4600, 32, 65, Ddr4_3200, null),
        new("Ryzen 5 5600XT", Vermeer, Am4, N7, "2024-10-31", 6, 12, 3700, 4700, 32, 65, Ddr4_3200, null),
        new("Ryzen 5 5600G", Cezanne, Am4, "TSMC N7", "2021-04-13", 6, 12, 3900, 4400, 16, 65, Ddr4_3200, "Radeon Vega 7"),
        new("Ryzen 7 5700", Cezanne, Am4, "TSMC N7", "2023-07-31", 8, 16, 3700, 4600, 16, 65, Ddr4_3200, null),
        new("Ryzen 7 5700G", Cezanne, Am4, "TSMC N7", "2021-04-13", 8, 16, 3800, 4600, 16, 65, Ddr4_3200, "Radeon Vega 8"),
        new("Ryzen 7 5700X", Vermeer, Am4, N7, "2022-04-04", 8, 16, 3400, 4600, 32, 65, Ddr4_3200, null),
        new("Ryzen 7 5700X3D", Vermeer, Am4, N7, "2024-01-08", 8, 16, 3000, 4100, 96, 105, Ddr4_3200, null),
        new("Ryzen 7 5800X", Vermeer, Am4, N7, "2020-11-05", 8, 16, 3800, 4700, 32, 105, Ddr4_3200, null),
        new("Ryzen 7 5800X3D", Vermeer, Am4, N7, "2022-04-20", 8, 16, 3400, 4500, 96, 105, Ddr4_3200, null),
        new("Ryzen 7 5800XT", Vermeer, Am4, N7, "2024-07-31", 8, 16, 3800, 4800, 32, 105, Ddr4_3200, null),
        new("Ryzen 9 5900X", Vermeer, Am4, N7, "2020-11-05", 12, 24, 3700, 4800, 64, 105, Ddr4_3200, null),
        new("Ryzen 9 5900XT", Vermeer, Am4, N7, "2024-07-31", 16, 32, 3300, 4800, 64, 105, Ddr4_3200, null),
        new("Ryzen 9 5950X", Vermeer, Am4, N7, "2020-11-05", 16, 32, 3400, 4900, 64, 105, Ddr4_3200, null),

        // AMD Ryzen 7000 (Zen 4) and 8000G
        new("Ryzen 5 7500F", Raphael, Am5, N5, "2023-07-22", 6, 12, 3700, 5000, 32, 65, Ddr5_5200, null),
        new("Ryzen 5 7600", Raphael, Am5, N5, "2023-01-10", 6, 12, 3800, 5100, 32, 65, Ddr5_5200, Radeon2Cu),
        new("Ryzen 5 7600X", Raphael, Am5, N5, "2022-09-27", 6, 12, 4700, 5300, 32, 105, Ddr5_5200, Radeon2Cu),
        new("Ryzen 7 7700", Raphael, Am5, N5, "2023-01-10", 8, 16, 3800, 5300, 32, 65, Ddr5_5200, Radeon2Cu),
        new("Ryzen 7 7700X", Raphael, Am5, N5, "2022-09-27", 8, 16, 4500, 5400, 32, 105, Ddr5_5200, Radeon2Cu),
        new("Ryzen 7 7800X3D", Raphael, Am5, N5, "2023-04-06", 8, 16, 4200, 5000, 96, 120, Ddr5_5200, Radeon2Cu),
        new("Ryzen 9 7900", Raphael, Am5, N5, "2023-01-10", 12, 24, 3700, 5400, 64, 65, Ddr5_5200, Radeon2Cu),
        new("Ryzen 9 7900X", Raphael, Am5, N5, "2022-09-27", 12, 24, 4700, 5600, 64, 170, Ddr5_5200, Radeon2Cu),
        new("Ryzen 9 7900X3D", Raphael, Am5, N5, "2023-02-28", 12, 24, 4400, 5600, 128, 120, Ddr5_5200, Radeon2Cu),
        new("Ryzen 9 7950X", Raphael, Am5, N5, "2022-09-27", 16, 32, 4500, 5700, 64, 170, Ddr5_5200, Radeon2Cu),
        new("Ryzen 9 7950X3D", Raphael, Am5, N5, "2023-02-28", 16, 32, 4200, 5700, 128, 120, Ddr5_5200, Radeon2Cu),
        new("Ryzen 5 8500G", "Phoenix 2 (Zen 4 + Zen 4c)", Am5, "TSMC N4", "2024-01-31", 6, 12, 3500, 5000, 16, 65, Ddr5_5200, "Radeon 740M"),
        new("Ryzen 5 8600G", "Phoenix (Zen 4)", Am5, "TSMC N4", "2024-01-31", 6, 12, 4300, 5000, 16, 65, Ddr5_5200, "Radeon 760M"),
        new("Ryzen 7 8700G", "Phoenix (Zen 4)", Am5, "TSMC N4", "2024-01-31", 8, 16, 4200, 5100, 16, 65, Ddr5_5200, "Radeon 780M"),

        // AMD Ryzen 9000 (Zen 5)
        new("Ryzen 5 9600", Granite, Am5, N4P, "2025-02-20", 6, 12, 3800, 5200, 32, 65, Ddr5_5600, Radeon2Cu),
        new("Ryzen 5 9600X", Granite, Am5, N4P, "2024-08-08", 6, 12, 3900, 5400, 32, 65, Ddr5_5600, Radeon2Cu),
        new("Ryzen 7 9700X", Granite, Am5, N4P, "2024-08-08", 8, 16, 3800, 5500, 32, 65, Ddr5_5600, Radeon2Cu),
        new("Ryzen 7 9800X3D", Granite, Am5, N4P, "2024-11-07", 8, 16, 4700, 5200, 96, 120, Ddr5_5600, Radeon2Cu),
        new("Ryzen 9 9900X", Granite, Am5, N4P, "2024-08-15", 12, 24, 4400, 5600, 64, 120, Ddr5_5600, Radeon2Cu),
        new("Ryzen 9 9900X3D", Granite, Am5, N4P, "2025-03-12", 12, 24, 4400, 5500, 128, 120, Ddr5_5600, Radeon2Cu),
        new("Ryzen 9 9950X", Granite, Am5, N4P, "2024-08-15", 16, 32, 4300, 5700, 64, 170, Ddr5_5600, Radeon2Cu),
        new("Ryzen 9 9950X3D", Granite, Am5, N4P, "2025-03-12", 16, 32, 4300, 5700, 128, 170, Ddr5_5600, Radeon2Cu),

        // Intel Core 8th / 9th generation (Coffee Lake, LGA 1151 v2)
        new("Core i3-8100", "Coffee Lake", Lga1151, Intel14, "2017-10-05", 4, 4, 3600, 3600, 6, 65, "DDR4-2400", Uhd630),
        new("Core i5-8400", "Coffee Lake", Lga1151, Intel14, "2017-10-05", 6, 6, 2800, 4000, 9, 65, Ddr4_2666, Uhd630),
        new("Core i5-8600K", "Coffee Lake", Lga1151, Intel14, "2017-10-05", 6, 6, 3600, 4300, 9, 95, Ddr4_2666, Uhd630),
        new("Core i7-8700", "Coffee Lake", Lga1151, Intel14, "2017-10-05", 6, 12, 3200, 4600, 12, 65, Ddr4_2666, Uhd630),
        new("Core i7-8700K", "Coffee Lake", Lga1151, Intel14, "2017-10-05", 6, 12, 3700, 4700, 12, 95, Ddr4_2666, Uhd630),
        new("Core i3-9100", "Coffee Lake Refresh", Lga1151, Intel14, "2019-04-23", 4, 4, 3600, 4200, 6, 65, "DDR4-2400", Uhd630),
        new("Core i5-9400", "Coffee Lake Refresh", Lga1151, Intel14, "2019-01-08", 6, 6, 2900, 4100, 9, 65, Ddr4_2666, Uhd630),
        new("Core i5-9600K", "Coffee Lake Refresh", Lga1151, Intel14, "2018-10-19", 6, 6, 3700, 4600, 9, 95, Ddr4_2666, Uhd630),
        new("Core i7-9700", "Coffee Lake Refresh", Lga1151, Intel14, "2019-04-23", 8, 8, 3000, 4700, 12, 65, Ddr4_2666, Uhd630),
        new("Core i7-9700K", "Coffee Lake Refresh", Lga1151, Intel14, "2018-10-19", 8, 8, 3600, 4900, 12, 95, Ddr4_2666, Uhd630),
        new("Core i9-9900K", "Coffee Lake Refresh", Lga1151, Intel14, "2018-10-19", 8, 16, 3600, 5000, 16, 95, Ddr4_2666, Uhd630),

        // Intel Core 10th / 11th generation (Comet Lake, Rocket Lake; LGA 1200)
        new("Core i3-10100", "Comet Lake", Lga1200, Intel14, "2020-04-30", 4, 8, 3600, 4300, 6, 65, Ddr4_2666, Uhd630),
        new("Core i5-10400", "Comet Lake", Lga1200, Intel14, "2020-04-30", 6, 12, 2900, 4300, 12, 65, Ddr4_2666, Uhd630),
        new("Core i5-10600K", "Comet Lake", Lga1200, Intel14, "2020-04-30", 6, 12, 4100, 4800, 12, 125, Ddr4_2666, Uhd630),
        new("Core i7-10700", "Comet Lake", Lga1200, Intel14, "2020-04-30", 8, 16, 2900, 4800, 16, 65, Ddr4_2933, Uhd630),
        new("Core i7-10700K", "Comet Lake", Lga1200, Intel14, "2020-04-30", 8, 16, 3800, 5100, 16, 125, Ddr4_2933, Uhd630),
        new("Core i9-10900K", "Comet Lake", Lga1200, Intel14, "2020-04-30", 10, 20, 3700, 5300, 20, 125, Ddr4_2933, Uhd630),
        new("Core i5-11400", "Rocket Lake", Lga1200, Intel14, "2021-03-30", 6, 12, 2600, 4400, 12, 65, Ddr4_3200, Uhd730),
        new("Core i5-11600K", "Rocket Lake", Lga1200, Intel14, "2021-03-30", 6, 12, 3900, 4900, 12, 125, Ddr4_3200, "UHD 750"),
        new("Core i7-11700K", "Rocket Lake", Lga1200, Intel14, "2021-03-30", 8, 16, 3600, 5000, 16, 125, Ddr4_3200, "UHD 750"),
        new("Core i9-11900K", "Rocket Lake", Lga1200, Intel14, "2021-03-30", 8, 16, 3500, 5300, 16, 125, Ddr4_3200, "UHD 750"),

        // Intel Core 12th to 14th generation (Alder Lake, Raptor Lake; LGA 1700). P-core clocks.
        new("Core i3-12100", Alder, Lga1700, Intel7, "2022-01-04", 4, 8, 3300, 4300, 12, 60, Ddr45AL, Uhd730),
        new("Core i5-12400", Alder, Lga1700, Intel7, "2022-01-04", 6, 12, 2500, 4400, 18, 65, Ddr45AL, Uhd730),
        new("Core i5-12600K", Alder, Lga1700, Intel7, "2021-11-04", 10, 16, 3700, 4900, 20, 125, Ddr45AL, Uhd770) { PerformanceCores = 6, EfficiencyCores = 4 },
        new("Core i7-12700", Alder, Lga1700, Intel7, "2022-01-04", 12, 20, 2100, 4900, 25, 65, Ddr45AL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 4 },
        new("Core i7-12700K", Alder, Lga1700, Intel7, "2021-11-04", 12, 20, 3600, 5000, 25, 125, Ddr45AL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 4 },
        new("Core i9-12900K", Alder, Lga1700, Intel7, "2021-11-04", 16, 24, 3200, 5200, 30, 125, Ddr45AL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 8 },
        new("Core i3-13100", Raptor, Lga1700, Intel7, "2023-01-03", 4, 8, 3400, 4500, 12, 60, Ddr45RL, Uhd730),
        new("Core i5-13400", Raptor, Lga1700, Intel7, "2023-01-03", 10, 16, 2500, 4600, 20, 65, Ddr45RL, Uhd730) { PerformanceCores = 6, EfficiencyCores = 4 },
        new("Core i5-13600K", Raptor, Lga1700, Intel7, "2022-10-20", 14, 20, 3500, 5100, 24, 125, Ddr45RL, Uhd770) { PerformanceCores = 6, EfficiencyCores = 8 },
        new("Core i7-13700", Raptor, Lga1700, Intel7, "2023-01-03", 16, 24, 2100, 5200, 30, 65, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 8 },
        new("Core i7-13700K", Raptor, Lga1700, Intel7, "2022-10-20", 16, 24, 3400, 5400, 30, 125, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 8 },
        new("Core i9-13900K", Raptor, Lga1700, Intel7, "2022-10-20", 24, 32, 3000, 5800, 36, 125, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 16 },
        new("Core i3-14100", RaptorR, Lga1700, Intel7, "2024-01-08", 4, 8, 3500, 4700, 12, 60, Ddr45RL, Uhd730),
        new("Core i5-14400", RaptorR, Lga1700, Intel7, "2024-01-08", 10, 16, 2500, 4700, 20, 65, Ddr45RL, Uhd730) { PerformanceCores = 6, EfficiencyCores = 4 },
        new("Core i5-14600K", RaptorR, Lga1700, Intel7, "2023-10-17", 14, 20, 3500, 5300, 24, 125, Ddr45RL, Uhd770) { PerformanceCores = 6, EfficiencyCores = 8 },
        new("Core i7-14700", RaptorR, Lga1700, Intel7, "2024-01-08", 20, 28, 2100, 5400, 33, 65, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 12 },
        new("Core i7-14700K", RaptorR, Lga1700, Intel7, "2023-10-17", 20, 28, 3400, 5600, 33, 125, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 12 },
        new("Core i9-14900K", RaptorR, Lga1700, Intel7, "2023-10-17", 24, 32, 3200, 6000, 36, 125, Ddr45RL, Uhd770) { PerformanceCores = 8, EfficiencyCores = 16 },

        // Intel Core Ultra 200S (Arrow Lake, LGA 1851; no Hyper-Threading)
        new("Core Ultra 5 225", "Arrow Lake", Lga1851, "TSMC N3B", "2025-01-06", 10, 10, 3300, 4900, 20, 65, "DDR5-6400", "Intel Graphics (2 Xe)") { PerformanceCores = 6, EfficiencyCores = 4 },
        new("Core Ultra 5 245K", "Arrow Lake", Lga1851, "TSMC N3B", "2024-10-24", 14, 14, 4200, 5200, 24, 125, "DDR5-6400", "Intel Graphics (4 Xe)") { PerformanceCores = 6, EfficiencyCores = 8 },
        new("Core Ultra 7 265K", "Arrow Lake", Lga1851, "TSMC N3B", "2024-10-24", 20, 20, 3900, 5500, 30, 125, "DDR5-6400", "Intel Graphics (4 Xe)") { PerformanceCores = 8, EfficiencyCores = 12 },
        new("Core Ultra 9 285K", "Arrow Lake", Lga1851, "TSMC N3B", "2024-10-24", 24, 24, 3700, 5700, 36, 125, "DDR5-6400", "Intel Graphics (4 Xe)") { PerformanceCores = 8, EfficiencyCores = 16 },
    ];

    /// <summary>
    /// The entry for a processor name ("AMD Ryzen 7 5800XT 8-Core Processor", "13th Gen Intel(R) Core(TM) i5-13600KF"),
    /// with <paramref name="noGraphics"/> set for Intel F models. Null for unknown processors.
    /// </summary>
    public static CpuSpec? Find(string? name, out bool noGraphics)
    {
        noGraphics = false;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var text = Gpu.GpuDatabase.Normalize(name);
        if (IntelModel().Match(text) is { Success: true } intel)
        {
            var (family, number, suffix) = (intel.Groups["family"].Value, intel.Groups["number"].Value, intel.Groups["suffix"].Value);
            if (suffix is not ("" or "F" or "K" or "KF"))
            {
                return null; // T, KS, S, E...: other clocks or power, not in the list
            }

            noGraphics = suffix.Contains('F', StringComparison.Ordinal);
            var key = (family.StartsWith("ULTRA", StringComparison.Ordinal) ? $"CORE {family} {number}" : $"CORE {family}-{number}")
                + (suffix.Contains('K', StringComparison.Ordinal) ? "K" : "");
            return All.FirstOrDefault(c => Gpu.GpuDatabase.Normalize(c.Model) == key);
        }

        var matches = All.Where(c => c.Model.StartsWith("Ryzen", StringComparison.Ordinal) && Words(text, Gpu.GpuDatabase.Normalize(c.Model))).ToList();
        return matches.Count == 0 ? null : matches.MaxBy(c => c.Model.Length);
    }

    private static bool Words(string text, string model)
    {
        var at = text.IndexOf(model, StringComparison.Ordinal);
        while (at >= 0)
        {
            var end = at + model.Length;
            if ((at == 0 || text[at - 1] == ' ') && (end == text.Length || text[end] is ' ' or '-'))
            {
                return true;
            }

            at = text.IndexOf(model, end, StringComparison.Ordinal);
        }

        return false;
    }

    // "I7-12700KF", "ULTRA 7 265K".
    [GeneratedRegex(@"\b(?<family>I[3579]|ULTRA [579])[ -](?<number>\d{3,5})(?<suffix>[A-Z]*)\b")]
    private static partial Regex IntelModel();
}
