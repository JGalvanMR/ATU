using ATU.CamaraFria.ViewModels;

namespace ATU.CamaraFria.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}