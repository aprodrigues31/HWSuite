using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HWSuite.Core.Sensors;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.ViewModels;

/// <summary>One row of the sensor list; created once per sensor and updated in place every second.</summary>
public sealed class SensorItem(SensorReading reading, string kindLabel) : INotifyPropertyChanged
{
    private Brush? _valueBrush;
    private string _value = "—";
    private string _min = "—";
    private string _max = "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; } = reading.Id;

    public string Name { get; } = reading.Name;

    public string Hardware { get; } = reading.Hardware;

    public SensorKind Kind { get; } = reading.Kind;

    /// <summary>Latest reading, used to match alert rules and to create one from this sensor.</summary>
    public SensorReading Reading { get; set; } = reading;

    /// <summary>Red while the value is beyond an alert limit, the normal text color otherwise.</summary>
    public Brush? ValueBrush
    {
        get => _valueBrush;
        set
        {
            if (!ReferenceEquals(_valueBrush, value))
            {
                _valueBrush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueBrush)));
            }
        }
    }

    public string KindLabel { get; } = kindLabel;

    public string ValueText
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public string MinText
    {
        get => _min;
        set => Set(ref _min, value);
    }

    public string MaxText
    {
        get => _max;
        set => Set(ref _max, value);
    }

    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}

/// <summary>Sensors of one device, shown under a group header.</summary>
public sealed class SensorGroup(string title) : ObservableCollection<SensorItem>
{
    public string Title { get; } = title;
}
