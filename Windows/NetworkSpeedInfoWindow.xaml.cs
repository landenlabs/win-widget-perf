// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Windows;
using WinWidgetPerf.Services;

namespace WinWidgetPerf.Windows;

/// <summary>Explains what the saved network speed file (network-max.json) holds and why it exists.</summary>
public partial class NetworkSpeedInfoWindow : Window
{
    public NetworkSpeedInfoWindow()
    {
        InitializeComponent();
        PathText.Text = NetworkMaxStore.StorePath;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
