using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Power;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class BatteryTests
{
    private static byte[] Information(uint capabilities, string chemistry, uint design, uint full, uint cycles)
    {
        var b = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(b, capabilities);
        b[4] = 1; // rechargeable
        Encoding.ASCII.GetBytes(chemistry).CopyTo(b, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), design);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), full);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), cycles);
        return b;
    }

    private static byte[] Status(uint state, uint capacity, uint mv, int mw)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(b, state);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), capacity);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), mv);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), mw);
        return b;
    }

    [Fact]
    public void A_worn_laptop_battery_on_battery_power()
    {
        var battery = new BatteryInfo { Name = "DELL 7FHHV11", Manufacturer = "SMP" };
        Batteries.ApplyInformation(battery, Information(Batteries.CapabilitySystemBattery, "LION", 57000, 49020, 312));
        Batteries.ApplyStatus(battery, Status(Batteries.PowerDischarging, 42600, 12600, -8400));
        battery.EstimatedTime = Batteries.EstimatedTime(5100);

        Assert.False(battery.IsUps);
        Assert.Equal("Li-ion", battery.Chemistry);
        Assert.Equal(86, Batteries.HealthPercent(battery)!.Value, 0);
        Assert.Equal(86.9, Batteries.ChargePercent(battery)!.Value, 1);

        var inventory = new HardwareInventory();
        inventory.Batteries.Add(battery);
        var rows = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(s => s.Title == "Bateria e nobreak").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Bateria", rows["Tipo"]);
        Assert.Equal("87% · na bateria", rows["Carga"]);
        Assert.Equal("1 h 25 min", rows["Autonomia estimada"]);
        Assert.Equal("86% da capacidade original (desgaste de 14%)", rows["Saúde"]);
        Assert.Equal("57.000 / 49.020 / 42.600 mWh", rows["Capacidade (projeto / carga total / atual)"]);
        Assert.Equal("312", rows["Ciclos de carga"]);
        Assert.Equal("12,60 V · -8,4 W", rows["Tensão · potência"]);
    }

    [Fact]
    public void A_usb_ups_reports_relative_capacity()
    {
        var ups = new BatteryInfo { Name = "SMS Net 4+", Manufacturer = "SMS" };
        Batteries.ApplyInformation(ups, Information(Batteries.CapabilitySystemBattery | Batteries.CapabilityRelative | Batteries.CapabilityShortTerm, "PbAc", 100, 100, 0));
        Batteries.ApplyStatus(ups, Status(Batteries.PowerOnLine, 100, 0xFFFFFFFF, unchecked((int)0x80000000)));

        Assert.True(ups.IsUps);
        Assert.Equal("Lead acid", ups.Chemistry);
        Assert.Null(Batteries.HealthPercent(ups)); // percentages: no wear estimate
        Assert.Equal(100, Batteries.ChargePercent(ups));
        Assert.Null(ups.VoltageMv);
        Assert.Null(ups.RateMw);
        Assert.Null(ups.CycleCount);

        var inventory = new HardwareInventory();
        inventory.Batteries.Add(ups);
        var rows = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(s => s.Title == "Bateria e nobreak").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Nobreak (USB)", rows["Tipo"]);
        Assert.Equal("100% · na tomada", rows["Carga"]);
    }

    [Theory]
    [InlineData(0xFFFFFFFFu, null)]
    [InlineData(0u, null)]
    [InlineData(2981u, 25.0)]
    public void Temperature_is_tenths_of_kelvin(uint raw, double? celsius) => Assert.Equal(celsius, Batteries.Celsius(raw));

    [Fact]
    public void Manufacture_date_and_unknown_time()
    {
        Assert.Equal("2023-03-15", Batteries.ManufactureDate([15, 3, 0xE7, 0x07]));
        Assert.Null(Batteries.ManufactureDate([0, 0, 0, 0]));
        Assert.Null(Batteries.EstimatedTime(0xFFFFFFFF));
    }

    [Fact]
    public void Serial_is_redacted_and_desktops_have_no_section()
    {
        var inventory = new HardwareInventory();
        Assert.DoesNotContain(ReportModel.Build(inventory, Strings.For("pt-BR")), s => s.Title == "Bateria e nobreak");
        inventory.Batteries.Add(new BatteryInfo { SerialNumber = "12345" });
        InventoryRedactor.Redact(inventory);
        Assert.Null(inventory.Batteries[0].SerialNumber);
    }

    [Fact]
    public void Windows_provider_runs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inventory = new HardwareInventory();
        new HWSuite.Providers.Windows.WindowsBatteryProvider().Contribute(inventory, null!);
        Assert.NotNull(inventory.Batteries);
    }
}
