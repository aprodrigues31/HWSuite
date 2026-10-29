using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;

namespace HWSuite.Tests;

public class ResultTablesTests
{
    private static readonly Strings Pt = Strings.For("pt-BR");
    private static readonly DateTimeOffset At = new(2026, 10, 28, 22, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Cpu_table_has_clear_columns_and_marks_the_best_run()
    {
        var table = ResultTables.Cpu(
        [
            new BenchmarkEntry(At, "CPU v1", "AMD Ryzen 7 5800XT 8-Core Processor", 700, 7200, 16),
            new BenchmarkEntry(At.AddHours(-1), "CPU v1", "AMD Ryzen 7 5800XT 8-Core Processor", 690, 8000, 16),
        ], Pt);

        Assert.True(table.Columns.Select(c => c.Header).SequenceEqual(["Data", "Processador", "Todos os núcleos", "Um núcleo", "Ganho", "Teste"]));
        Assert.Equal(2, table.BarColumn);
        Assert.Equal("AMD Ryzen 7 5800XT", table.Rows[0].Cells[1]);
        Assert.Equal("7.200,0", table.Rows[0].Cells[2]);
        Assert.Equal("10,3x", table.Rows[0].Cells[4]);
        Assert.Equal(0.9, table.Rows[0].Bar, 3);
        Assert.False(table.Rows[0].IsBest);
        Assert.True(table.Rows[1].IsBest);
        Assert.Equal(1, table.Rows[1].Bar);
    }

    [Fact]
    public void Gpu_table_shows_gflops_and_tflops()
    {
        var table = ResultTables.Gpu([new GpuBenchmarkResult("GPU v1", "AMD Radeon RX 7800 XT", 18765.4, At)], Pt);
        Assert.True(table.Rows[0].Cells.Skip(1).SequenceEqual(["AMD Radeon RX 7800 XT", "18.765", "18,77", "GPU v1"])); // the date depends on the time zone
        Assert.True(table.Rows[0].IsBest);
    }

    [Fact]
    public void Disk_table_shows_read_and_write_per_test()
    {
        var tests = new[]
        {
            new DiskTestResult("SEQ1M Q8T1", 3412.3, 2876.9, 0, 0),
            new DiskTestResult("SEQ1M Q1T1", 1900, 2500, 0, 0),
            new DiskTestResult("RND4K Q32T1", 400, 350, 0, 0),
            new DiskTestResult("RND4K Q1T1", 55.4, 160.2, 0, 0),
        };
        var table = ResultTables.Disk([new DiskBenchmarkResult("Disk v1", "C:", "WD Green SN350 1TB", 1L << 30, tests, At)], Pt);

        Assert.Equal("SEQ1M Q8T1 (MB/s leitura / gravação)", table.Columns[2].Header);
        Assert.Equal("C: WD Green SN350 1TB", table.Rows[0].Cells[1]);
        Assert.Equal("3.412 / 2.877", table.Rows[0].Cells[2]);
        Assert.Equal("55,4 / 160", table.Rows[0].Cells[5]);
    }

    [Fact]
    public void Empty_tables_have_no_rows() =>
        Assert.Empty(ResultTables.Cpu([], Pt).Rows);
}
