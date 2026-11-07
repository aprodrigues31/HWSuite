using System.Globalization;
using HWSuite.Core.Snmp;
using HWSuite.Gui.Hud;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Network devices over SNMP (a router like the ER605, switches, printers...): status, uptime, processor and memory,
/// the WAN links, every port with speed, traffic and errors, a traffic chart and the events (link down, speed
/// drop, device not answering).
/// </summary>
public sealed partial class DevicesPage : Page
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _selected;
    private int? _chartPort;
    private bool _filling;
    private IReadOnlyList<SnmpDeviceState>? _demo;

    public DevicesPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = s["snmp.tag"].ToUpper(c);
        PageTitle.Text = s["snmp.title"].ToUpper(c);
        Subtitle.Text = s["snmp.subtitle"];
        AddButton.Content = s["snmp.add"];
        EditButton.Content = s["snmp.edit"];
        RemoveButton.Content = s["snmp.remove"];
        EmptyText.Text = s["snmp.empty"];
        DeviceTag.Text = ("// " + s["snmp.device"]).ToUpper(c);
        WanTag.Text = ("// " + s["snmp.wan"]).ToUpper(c);
        ChartTag.Text = ("// " + s["snmp.traffic"]).ToUpper(c);
        EventsTag.Text = ("// " + s["snmp.events"]).ToUpper(c);
        PortsTag.Text = ("// " + s["snmp.ports"]).ToUpper(c);
        OnlyUpSwitch.OnContent = s["snmp.onlyUp"];
        OnlyUpSwitch.OffContent = s["snmp.onlyUp"];
        ChartLegend.Text = s["snmp.legend"].ToUpper(c);
        _refresh.Tick += (_, _) => Render();
        if (App.IsDemo)
        {
            _demo = DemoStates();
        }
    }

    private IReadOnlyList<SnmpDeviceState> States => _demo ?? App.Snmp.Devices;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _refresh.Start();
        RenderChips();
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refresh.Stop();
        base.OnNavigatedFrom(e);
    }

    private void RenderChips()
    {
        DeviceChips.Children.Clear();
        var states = States;
        _selected = states.Any(d => d.Config.Id == _selected) ? _selected : states.FirstOrDefault()?.Config.Id;
        foreach (var state in states)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = state.Online ? HudUi.Good : state.Failures > 0 ? HudUi.Crit : HudUi.TextDim, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = $"{state.Config.Name} · {state.Config.Host}" });
            var chip = new ToggleButton { Content = content, IsChecked = state.Config.Id == _selected, Tag = state.Config.Id };
            chip.Click += (_, _) =>
            {
                _selected = (string)chip.Tag;
                _chartPort = null;
                RenderChips();
                Render();
            };
            DeviceChips.Children.Add(chip);
        }

        EmptyText.Visibility = states.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = states.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EditButton.IsEnabled = RemoveButton.IsEnabled = states.Count > 0 && !App.IsDemo;
    }

    private void Render()
    {
        var state = States.FirstOrDefault(d => d.Config.Id == _selected);
        if (state is null)
        {
            return;
        }

        // Status dots of the chips.
        foreach (var chip in DeviceChips.Children.OfType<ToggleButton>())
        {
            if (States.FirstOrDefault(d => d.Config.Id == (string)chip.Tag) is { } st
                && chip.Content is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse dot)
            {
                dot.Fill = st.Online ? HudUi.Good : st.Failures > 0 ? HudUi.Crit : HudUi.TextDim;
            }
        }

        var s = App.Text;
        var c = s.Culture;
        var poll = state.Last;
        DeviceRows.Children.Clear();
        void Row(string label, string? value, Microsoft.UI.Xaml.Media.Brush? brush = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, TextAlignment = TextAlignment.Right, IsTextSelectionEnabled = true };
                if (brush is not null)
                {
                    text.Foreground = brush;
                }

                DeviceRows.Children.Add(HudUi.FactRow(label, text));
            }
        }

        Row(s["snmp.status"], state.Online ? s["snmp.online"] : state.Failures == 0 ? s["snmp.connecting"] : StatusError(state), state.Online ? HudUi.Good : state.Failures == 0 ? null : HudUi.Crit);
        Row(s["snmp.host"], $"{state.Config.Host}:{state.Config.Port} · {VersionText(state.Config)}");
        if (poll is not null)
        {
            Row(s["snmp.name"], poll.System.Name);
            Row(s["snmp.description"], poll.System.Description);
            Row(s["snmp.uptime"], poll.System.Uptime is { } up ? Uptime(up, s) : null);
            Row(s["snmp.cpu"], poll.System.CpuPercent is { } cpu ? $"{cpu.ToString("0", c)}%" : null);
            Row(s["snmp.memory"], poll.System.MemoryUsedPercent is { } mem ? $"{mem.ToString("0", c)}%" : null);
            Row(s["snmp.location"], poll.System.Location);
            Row(s["snmp.lastRead"], poll.At.LocalDateTime.ToString("T", c));
        }

        var ports = poll?.Ports.Where(p => !p.IsSoftware).ToList() ?? [];
        var rates = state.Rates.ToDictionary(r => r.Index);

        // WAN links (the router's Internet connections).
        var wans = ports.Where(p => p.IsWan).ToList();
        WanPanel.Visibility = wans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WanRows.Children.Clear();
        foreach (var wan in wans)
        {
            rates.TryGetValue(wan.Index, out var rate);
            var box = new StackPanel { Spacing = 2 };
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = wan.Label, FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 15 });
            var status = new TextBlock
            {
                Text = (wan.OperUp ? s["snmp.up"] : s["snmp.down"]).ToUpper(c),
                Foreground = wan.OperUp ? HudUi.Good : HudUi.Crit,
                FontFamily = HudUi.Font("HudMonoStrongFont"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            head.Children.Add(status);
            box.Children.Add(head);
            box.Children.Add(new TextBlock
            {
                Text = $"↓ {Mbps(rate?.InMbps, c)}   ↑ {Mbps(rate?.OutMbps, c)}" + (wan.SpeedMbps is { } sp ? $"   · {SnmpReader.Speed(sp)}" : ""),
                FontFamily = HudUi.Font("HudMonoFont"),
                FontSize = 13,
                Foreground = HudUi.TextDim,
            });
            WanRows.Children.Add(box);
        }

        // Port list for the chart.
        var chartable = ports.Where(p => p.OperUp || p.Index == _chartPort).ToList();
        _chartPort ??= wans.FirstOrDefault(p => p.OperUp)?.Index ?? chartable.FirstOrDefault()?.Index;
        _filling = true;
        if (PortBox.Items.Count != chartable.Count || PortBox.Items.Cast<ComboBoxItem>().Select(i => (int)i.Tag).SequenceEqual(chartable.Select(p => p.Index)) == false)
        {
            PortBox.Items.Clear();
            foreach (var port in chartable)
            {
                PortBox.Items.Add(new ComboBoxItem { Content = port.Label, Tag = port.Index });
            }
        }

        PortBox.SelectedItem = PortBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _chartPort);
        _filling = false;
        if (_chartPort is { } chosen && state.History.TryGetValue(chosen, out var history) && history.Count > 0)
        {
            TrafficChart.Capacity = SnmpMonitor.HistoryLength;
            TrafficChart.Set(
            [
                new ChartSeries([.. history.Select(h => h.In)], HudUi.Accent, Fill: true),
                new ChartSeries([.. history.Select(h => h.Out)], HudUi.Warn),
            ]);
        }
        else
        {
            TrafficChart.Set([]);
        }

        // Ports.
        PortRows.Children.Clear();
        PortRows.Children.Add(PortHeader(s));
        foreach (var port in ports.Where(p => OnlyUpSwitch.IsOn != true || p.OperUp))
        {
            rates.TryGetValue(port.Index, out var rate);
            PortRows.Children.Add(PortRow(port, rate, c));
        }

        // Events of this device.
        EventRows.Children.Clear();
        var events = (App.IsDemo ? DemoEvents(state.Config.Id) : App.Snmp.Events.Where(e => e.DeviceId == state.Config.Id)).Take(12).ToList();
        if (events.Count == 0)
        {
            EventRows.Children.Add(new TextBlock { Text = s["snmp.noEvents"], Foreground = HudUi.TextDim, FontSize = 13 });
        }

        foreach (var e in events)
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = e.At.LocalDateTime.ToString("dd/MM HH:mm", c), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.TextDim });
            var text = new TextBlock { Text = EventText(e), TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = e.Problem ? HudUi.Crit : HudUi.Good };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            EventRows.Children.Add(row);
        }

        TopPanels.Reflow();
    }

    private static Grid PortGrid()
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        foreach (var width in new[] { 14.0, -2, 110, 110, 110, 90 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(-width, GridUnitType.Star) : new GridLength(width) });
        }

        return grid;
    }

    private static Grid PortHeader(Core.Localization.Strings s)
    {
        var grid = PortGrid();
        var labels = new[] { "", s["snmp.port"], s["snmp.speed"], "↓ " + s["snmp.in"], "↑ " + s["snmp.out"], s["snmp.errors"] };
        for (var i = 0; i < labels.Length; i++)
        {
            var t = new TextBlock { Text = labels[i].ToUpper(s.Culture), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 11, Foreground = HudUi.Brush("HudTagBrush") };
            Grid.SetColumn(t, i);
            grid.Children.Add(t);
        }

        return grid;
    }

    private static Grid PortRow(SnmpPort port, PortRate? rate, CultureInfo c)
    {
        var s = App.Text;
        var grid = PortGrid();
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = port.OperUp ? HudUi.Good : port.AdminUp ? HudUi.Crit : HudUi.TextDim,
            VerticalAlignment = VerticalAlignment.Center,
        });
        void Cell(int column, string text, Microsoft.UI.Xaml.Media.Brush? brush = null, bool mono = true)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            if (mono)
            {
                t.FontFamily = HudUi.Font("HudMonoFont");
            }

            if (brush is not null)
            {
                t.Foreground = brush;
            }

            Grid.SetColumn(t, column);
            grid.Children.Add(t);
        }

        Cell(1, port.Label + (port.OperUp ? "" : port.AdminUp ? $" ({s["snmp.down"]})" : $" ({s["snmp.disabled"]})"), port.OperUp ? HudUi.Text : HudUi.TextDim, mono: false);
        Cell(2, port.OperUp && port.SpeedMbps is { } sp ? SnmpReader.Speed(sp) : "—", port.OperUp && port.SpeedMbps is < 1000 and > 0 && !port.IsWan ? HudUi.Warn : null);
        Cell(3, port.OperUp ? Mbps(rate?.InMbps, c) : "—");
        Cell(4, port.OperUp ? Mbps(rate?.OutMbps, c) : "—");
        var errors = (port.InErrors ?? 0) + (port.OutErrors ?? 0);
        Cell(5, errors.ToString("N0", c), rate?.NewErrors > 0 ? HudUi.Crit : errors > 0 ? HudUi.Warn : HudUi.TextDim);
        if (port.Description is { } d && d != port.Name)
        {
            ToolTipService.SetToolTip(grid, d);
        }

        return grid;
    }

    private static string Mbps(double? value, CultureInfo c) => value switch
    {
        null => "—",
        >= 100 => value.Value.ToString("0", c) + " Mbps",
        >= 1 => value.Value.ToString("0.0", c) + " Mbps",
        _ => (value.Value * 1000).ToString("0", c) + " kbps",
    };

    private static string Uptime(TimeSpan up, Core.Localization.Strings s) =>
        up.TotalDays >= 1 ? s.Format("snmp.uptimeDays", (int)up.TotalDays, up.Hours, up.Minutes) : s.Format("snmp.uptimeHours", up.Hours, up.Minutes);

    private static string VersionText(SnmpDeviceConfig d) => d.Version switch
    {
        SnmpVersion.V3 => "SNMPv3 · " + (d.Auth == SnmpAuth.None ? "noAuth" : d.Auth.ToString().ToUpperInvariant() + (d.Priv == SnmpPriv.None ? "" : " + " + d.Priv.ToString().ToUpperInvariant())),
        SnmpVersion.V2c => "SNMPv2c",
        _ => "SNMPv1",
    };

    private static string StatusError(SnmpDeviceState state)
    {
        var s = App.Text;
        return state.ErrorKind switch
        {
            SnmpError.Timeout => s["snmp.err.timeout"],
            SnmpError.UnknownUser => s["snmp.err.user"],
            SnmpError.WrongPassword => s["snmp.err.auth"],
            SnmpError.DecryptionError => s["snmp.err.priv"],
            SnmpError.UnsupportedSecurityLevel => s["snmp.err.level"],
            _ => state.Error ?? s["snmp.err.timeout"],
        };
    }

    /// <summary>An event as one line ("WAN1 went down", "Port 3: 1 Gbps → 100 Mbps").</summary>
    public static string EventText(SnmpEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var s = App.Text;
        return e.Kind switch
        {
            SnmpEventKind.DeviceDown => s["snmp.ev.deviceDown"],
            SnmpEventKind.DeviceUp => s["snmp.ev.deviceUp"],
            SnmpEventKind.PortDown => s.Format("snmp.ev.portDown", e.Subject),
            SnmpEventKind.PortUp => s.Format("snmp.ev.portUp", e.Subject, e.Detail ?? ""),
            SnmpEventKind.SpeedChanged => s.Format("snmp.ev.speed", e.Subject, e.Detail ?? ""),
            SnmpEventKind.Errors => s.Format("snmp.ev.errors", e.Subject, e.Detail ?? ""),
            SnmpEventKind.Restarted => s["snmp.ev.restarted"],
            _ => e.Subject,
        };
    }

    private void PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && PortBox.SelectedItem is ComboBoxItem { Tag: int index })
        {
            _chartPort = index;
            Render();
        }
    }

    private void OnlyUpSwitch_Toggled(object sender, RoutedEventArgs e) => Render();

    // ---- Add / edit / remove --------------------------------------------------------------------------------------

    private async void AddButton_Click(object sender, RoutedEventArgs e) => await EditAsync(null);

    private async void EditButton_Click(object sender, RoutedEventArgs e) =>
        await EditAsync(App.Settings.Current.SnmpDevices?.FirstOrDefault(d => d.Id == _selected));

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (App.Settings.Current.SnmpDevices?.FirstOrDefault(d => d.Id == _selected) is not { } device)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s.Format("snmp.removeConfirm", device.Name),
            PrimaryButtonText = s["snmp.remove"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        App.Settings.Update(x => x with { SnmpDevices = [.. (x.SnmpDevices ?? []).Where(d => d.Id != device.Id)] });
        App.Snmp.Apply(App.Settings.Current.SnmpDevices);
        _selected = null;
        RenderChips();
        Render();
    }

    private async Task EditAsync(SnmpDeviceConfig? existing)
    {
        if (App.IsDemo)
        {
            return;
        }

        var s = App.Text;
        var name = new TextBox { Header = s["snmp.f.name"], Text = existing?.Name ?? "", PlaceholderText = "ER605" };
        var host = new TextBox { Header = s["snmp.f.host"], Text = existing?.Host ?? "", PlaceholderText = "192.168.0.1" };
        var port = new NumberBox { Header = s["snmp.f.port"], Value = existing?.Port ?? 161, Minimum = 1, Maximum = 65535, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var version = new ComboBox { Header = s["snmp.f.version"], ItemsSource = new[] { "SNMPv3", "SNMPv2c", "SNMPv1" }, SelectedIndex = existing?.Version switch { SnmpVersion.V2c => 1, SnmpVersion.V1 => 2, _ => 0 } };
        var community = new PasswordBox { Header = s["snmp.f.community"], PlaceholderText = existing?.CommunityProtected is null ? "" : s["snmp.f.keep"] };
        var user = new TextBox { Header = s["snmp.f.user"], Text = existing?.User ?? "" };
        var levels = new[] { s["snmp.f.level.authPriv"], s["snmp.f.level.auth"], s["snmp.f.level.none"] };
        var level = new ComboBox
        {
            Header = s["snmp.f.level"],
            ItemsSource = levels,
            SelectedIndex = existing is null ? 0 : existing.Auth == SnmpAuth.None ? 2 : existing.Priv == SnmpPriv.None ? 1 : 0,
        };
        var authKinds = new[] { SnmpAuth.Sha1, SnmpAuth.Sha256, SnmpAuth.Sha512, SnmpAuth.Sha224, SnmpAuth.Sha384, SnmpAuth.Md5 };
        var auth = new ComboBox { Header = s["snmp.f.auth"], ItemsSource = new[] { "SHA", "SHA-256", "SHA-512", "SHA-224", "SHA-384", "MD5" } };
        auth.SelectedIndex = Math.Max(0, Array.IndexOf(authKinds, existing?.Auth ?? SnmpAuth.Sha1));
        var authPass = new PasswordBox { Header = s["snmp.f.authPass"], PlaceholderText = existing?.AuthPasswordProtected is null ? "" : s["snmp.f.keep"] };
        var privKinds = new[] { SnmpPriv.Aes128, SnmpPriv.Aes192, SnmpPriv.Aes256, SnmpPriv.Des };
        var priv = new ComboBox { Header = s["snmp.f.priv"], ItemsSource = new[] { "AES (128)", "AES-192", "AES-256", "DES" } };
        priv.SelectedIndex = Math.Max(0, Array.IndexOf(privKinds, existing?.Priv ?? SnmpPriv.Aes128));
        var privPass = new PasswordBox { Header = s["snmp.f.privPass"], PlaceholderText = existing?.PrivPasswordProtected is null ? s["snmp.f.samePass"] : s["snmp.f.keep"] };
        var interval = new ComboBox { Header = s["snmp.f.interval"], ItemsSource = new[] { "5 s", "10 s", "30 s", "60 s" } };
        int[] intervals = [5, 10, 30, 60];
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(intervals, existing?.PollSeconds ?? 10));
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var test = new Button { Content = s["snmp.f.test"] };

        var v3 = new StackPanel { Spacing = 10 };
        v3.Children.Add(user);
        v3.Children.Add(level);
        v3.Children.Add(auth);
        v3.Children.Add(authPass);
        v3.Children.Add(priv);
        v3.Children.Add(privPass);
        void Visibilities()
        {
            var isV3 = version.SelectedIndex == 0;
            v3.Visibility = isV3 ? Visibility.Visible : Visibility.Collapsed;
            community.Visibility = isV3 ? Visibility.Collapsed : Visibility.Visible;
            auth.Visibility = authPass.Visibility = level.SelectedIndex < 2 ? Visibility.Visible : Visibility.Collapsed;
            priv.Visibility = privPass.Visibility = level.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        version.SelectionChanged += (_, _) => Visibilities();
        level.SelectionChanged += (_, _) => Visibilities();
        Visibilities();

        SnmpDeviceConfig Build()
        {
            var isV3 = version.SelectedIndex == 0;
            string? Keep(string typed, string? stored) => typed.Length > 0 ? Secrets.Protect(typed) : stored;
            var authOn = isV3 && level.SelectedIndex < 2;
            var privOn = isV3 && level.SelectedIndex == 0;
            return (existing ?? new SnmpDeviceConfig()) with
            {
                Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : host.Text.Trim(),
                Host = host.Text.Trim(),
                Port = double.IsNaN(port.Value) ? 161 : (int)port.Value,
                Version = version.SelectedIndex switch { 1 => SnmpVersion.V2c, 2 => SnmpVersion.V1, _ => SnmpVersion.V3 },
                CommunityProtected = isV3 ? null : Keep(community.Password, existing?.CommunityProtected),
                User = isV3 ? user.Text.Trim() : null,
                Auth = authOn ? authKinds[Math.Max(0, auth.SelectedIndex)] : SnmpAuth.None,
                Priv = privOn ? privKinds[Math.Max(0, priv.SelectedIndex)] : SnmpPriv.None,
                AuthPasswordProtected = authOn ? Keep(authPass.Password, existing?.AuthPasswordProtected) : null,
                PrivPasswordProtected = privOn ? Keep(privPass.Password, existing?.PrivPasswordProtected) : null,
                PollSeconds = intervals[Math.Max(0, interval.SelectedIndex)],
            };
        }

        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            result.Foreground = HudUi.TextDim;
            result.Text = s["snmp.f.testing"];
            try
            {
                var poll = await SnmpMonitor.TestAsync(Build().Target(Secrets.Unprotect));
                result.Foreground = HudUi.Good;
                result.Text = s.Format("snmp.f.ok", poll.System.Name ?? "?", poll.System.Description ?? "", poll.Ports.Count(p => !p.IsSoftware));
            }
            catch (Exception ex) when (ex is SnmpException or System.Net.Sockets.SocketException or FormatException or ArgumentException or InvalidOperationException)
            {
                result.Foreground = HudUi.Crit;
                result.Text = ex is SnmpException se
                    ? StatusError(new SnmpDeviceState { Config = Build(), ErrorKind = se.Error, Error = se.Message })
                      + (se.Error == SnmpError.Timeout && version.SelectedIndex == 0 && level.SelectedIndex == 0 ? " " + s["snmp.err.timeoutPriv"] : "")
                    : ex.Message;
            }
            finally
            {
                test.IsEnabled = true;
            }
        };

        var form = new StackPanel { Spacing = 10, MinWidth = 380 };
        form.Children.Add(name);
        form.Children.Add(host);
        form.Children.Add(port);
        form.Children.Add(version);
        form.Children.Add(community);
        form.Children.Add(v3);
        form.Children.Add(interval);
        form.Children.Add(new TextBlock { Text = s["snmp.f.secretsNote"], TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = HudUi.TextDim });
        form.Children.Add(test);
        form.Children.Add(result);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = existing is null ? s["snmp.add"] : s["snmp.edit"],
            Content = new ScrollViewer { Content = form, MaxHeight = 560 },
            PrimaryButtonText = s["files.save"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (host.Text.Trim().Length == 0)
            {
                args.Cancel = true;
                result.Foreground = HudUi.Crit;
                result.Text = s["snmp.f.needHost"];
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var device = Build();
        App.Settings.Update(x => x with
        {
            SnmpDevices = existing is null
                ? [.. x.SnmpDevices ?? [], device]
                : [.. (x.SnmpDevices ?? []).Select(d => d.Id == device.Id ? device : d)],
        });
        App.Snmp.Apply(App.Settings.Current.SnmpDevices);
        _selected = device.Id;
        RenderChips();
        Render();
    }

    // ---- Demo (CI screenshots) ------------------------------------------------------------------------------------

    private static List<SnmpDeviceState> DemoStates()
    {
        var config = new SnmpDeviceConfig { Id = "demo", Name = "ER605", Host = "192.168.0.1", User = "monitor", Auth = SnmpAuth.Sha1, Priv = SnmpPriv.Aes128 };
        SnmpPort P(int i, string name, bool up, double? speed, ulong inOct, ulong outOct, ulong errors = 0) =>
            new(i, name, null, name, 6, true, up, speed, inOct, outOct, true, errors, 0, 0, 0);
        var now = DateTimeOffset.Now;
        var poll = new SnmpPoll(now, new SnmpSystem("TP-Link ER605 v2 · 2.2.5 Build 20240624", "ER605", "1.3.6.1.4.1.11863", TimeSpan.FromHours(316.4), null, "Rack", 7, 41),
        [
            P(1, "WAN1 (Xturbo)", true, 1000, 0, 0),
            P(2, "WAN2 (Neolink)", true, 1000, 0, 0),
            P(3, "LAN1", true, 1000, 0, 0),
            P(4, "LAN2", true, 100, 0, 0, 37),
            P(5, "LAN3", false, null, 0, 0),
        ]);
        var state = new SnmpDeviceState { Config = config, Last = poll, Online = true, LastOk = now };
        var random = new Random(3);
        foreach (var index in new[] { 1, 2, 3, 4 })
        {
            var list = new List<(double, double)>();
            for (var i = 0; i < 180; i++)
            {
                var wave = Math.Sin(i / 14.0) + 1.2;
                list.Add((index switch { 1 => 180, 2 => 60, _ => 25 } * wave * (0.7 + (random.NextDouble() * 0.6)), index switch { 1 => 22, 2 => 9, _ => 6 } * wave * (0.6 + (random.NextDouble() * 0.8))));
            }

            state.History[index] = list;
        }

        state.Rates = [.. state.History.Select(kv => new PortRate(kv.Key, kv.Value[^1].Item1, kv.Value[^1].Item2, kv.Key == 4 ? 12UL : 0))];
        return [state];
    }

    private static IEnumerable<SnmpEvent> DemoEvents(string id)
    {
        var now = DateTimeOffset.Now;
        return
        [
            new(now.AddMinutes(-12), id, SnmpEventKind.SpeedChanged, "LAN2", "1 Gbps → 100 Mbps", true),
            new(now.AddHours(-3), id, SnmpEventKind.PortUp, "WAN2 (Neolink)", "1 Gbps", false),
            new(now.AddHours(-3.1), id, SnmpEventKind.PortDown, "WAN2 (Neolink)", null, true),
        ];
    }
}
