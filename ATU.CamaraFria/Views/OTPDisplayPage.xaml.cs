using System;
using ATU.CamaraFria.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using ZXing.Net.Maui;

namespace ATU.CamaraFria.Views;

public partial class OTPDisplayPage : ContentPage
{
    private readonly OTPViewModel _viewModel;

    public OTPDisplayPage(OTPViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private void OnEntryCompleted(object sender, EventArgs e)
    {
        _viewModel.ProcessScannedCodeCommand.Execute(null);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateConnectionIndicator();
    }

    private void UpdateConnectionIndicator()
    {
        var isConnected = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        ConnectionIndicator.BackgroundColor = isConnected
            ? Colors.LimeGreen
            : Colors.Red;
    }

    protected override void OnDisappearing()
    {
        _viewModel.ResetScanCommand.Execute(null);
        base.OnDisappearing();
    }
}