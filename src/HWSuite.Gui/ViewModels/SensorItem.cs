using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HWSuite.Core.Sensors;

namespace HWSuite.Gui.ViewModels;

/// <summary>One row of the sensor list; created once per sensor and updated in place every second.</summary>
public sealed class SensorItem(string id, string name, SensorKind kind, string kindLabel) : INotifyPropertyChanged
{
    private string _value = "—";
    private string _min = "—";
    private string _max = "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public SensorKind Kind { get; } = kind;

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
