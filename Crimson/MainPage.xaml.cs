using Crimson.Browser;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Crimson
{
    /// <summary>
    /// Hosts the YouTube WebView2 shell and coordinates request blocking, script injection,
    /// and lightweight browser controls.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string HomeUrl = "https://www.youtube.com/";
        private readonly AdBlocker adBlocker = new();
        private bool isAdBlockEnabled = true;
        private bool isSponsorBlockEnabled = true;

        public MainPage()
        {
            InitializeComponent();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF111111");
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-features=msSmartScreenProtection");

                await YouTubeView.EnsureCoreWebView2Async();

                ConfigureWebView();
                await InjectScriptsAsync();

                YouTubeView.CoreWebView2.Navigate(HomeUrl);
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                await ShowErrorAsync("Crimson failed to initialize WebView2.", ex);
            }
        }

        private void ConfigureWebView()
        {
            CoreWebView2 core = YouTubeView.CoreWebView2;

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += Core_WebResourceRequested;

            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.SourceChanged += Core_SourceChanged;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.ContainsFullScreenElementChanged += Core_ContainsFullScreenElementChanged;
        }

        private async Task InjectScriptsAsync()
        {
            string pruneScript = await ReadPackageFileAsync("ms-appx:///Scripts/youtube-prune.js");
            string cleanupScript = await ReadPackageFileAsync("ms-appx:///Scripts/cleanup.js");
            string sponsorBlockScript = await ReadPackageFileAsync("ms-appx:///Scripts/sponsorblock.js");

            await YouTubeView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(pruneScript);
            await YouTubeView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(cleanupScript);
            await YouTubeView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(sponsorBlockScript);
        }

        private static async Task<string> ReadPackageFileAsync(string uri)
        {
            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
            return await FileIO.ReadTextAsync(file);
        }

        private async void Core_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (!isAdBlockEnabled)
            {
                return;
            }

            string requestUri = args.Request.Uri;

            if (!adBlocker.ShouldBlock(requestUri))
            {
                return;
            }

            // Create an empty InMemoryRandomAccessStream and use it for the response
            var memoryStream = new InMemoryRandomAccessStream();
            var output = memoryStream.GetOutputStreamAt(0);
            var dataWriter = new DataWriter(output);
            dataWriter.WriteBytes(Array.Empty<byte>());
            await dataWriter.StoreAsync();
            await dataWriter.FlushAsync();
            // Reset position so WebView2 can read from the start
            memoryStream.Seek(0);

            args.Response = sender.Environment.CreateWebResourceResponse(
                memoryStream,
                204,
                "No Content",
                "Content-Type: text/plain\r\nCache-Control: no-store"
            );
        }

        private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void Core_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            AddressBox.Text = sender.Source;
            UpdateNavigationButtons();
            _ = PushSettingsToPageAsync();
        }

        private void Core_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            AddressBox.Text = sender.Source;
            UpdateNavigationButtons();
        }

        private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;

            if (IsAllowedNavigation(args.Uri))
            {
                sender.Navigate(args.Uri);
            }
        }

        private void Core_ContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            TopBar.Visibility = sender.ContainsFullScreenElement ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateNavigationButtons()
        {
            CoreWebView2 core = YouTubeView.CoreWebView2;

            BackButton.IsEnabled = core != null && core.CanGoBack;
            ForwardButton.IsEnabled = core != null && core.CanGoForward;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (YouTubeView.CoreWebView2?.CanGoBack == true)
            {
                YouTubeView.CoreWebView2.GoBack();
            }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (YouTubeView.CoreWebView2?.CanGoForward == true)
            {
                YouTubeView.CoreWebView2.GoForward();
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            YouTubeView.CoreWebView2?.Reload();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            YouTubeView.CoreWebView2?.Navigate(HomeUrl);
        }

        private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            YouTubeView.CoreWebView2?.Navigate(BuildNavigationTarget(AddressBox.Text));
        }

        private void AdBlockToggle_Toggled(object sender, RoutedEventArgs e)
        {
            isAdBlockEnabled = AdBlockToggle.IsOn;
        }

        private void SponsorBlockToggle_Toggled(object sender, RoutedEventArgs e)
        {
            isSponsorBlockEnabled = SponsorBlockToggle.IsOn;
            _ = PushSettingsToPageAsync();
        }

        private async Task PushSettingsToPageAsync()
        {
            if (YouTubeView.CoreWebView2 == null)
            {
                return;
            }

            string sponsorBlockEnabled = isSponsorBlockEnabled.ToString().ToLowerInvariant();
            string script = $"window.__crimsonSettings = {{ sponsorBlockEnabled: {sponsorBlockEnabled} }}; window.dispatchEvent(new Event('crimson-settings-changed'));";

            try
            {
                await YouTubeView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Page navigation can race script execution. Harmless; next navigation tick retries.
            }
        }

        private static string BuildNavigationTarget(string input)
        {
            string trimmed = input.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return HomeUrl;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (trimmed.Contains('.') && !trimmed.Contains(' '))
            {
                return "https://" + trimmed;
            }

            return "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(trimmed);
        }

        private static bool IsAllowedNavigation(string rawUri)
        {
            if (!Uri.TryCreate(rawUri, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();

            return host.EndsWith("youtube.com") ||
                   host.EndsWith("youtu.be") ||
                   host.EndsWith("youtube-nocookie.com") ||
                   host.EndsWith("google.com") ||
                   host.EndsWith("gstatic.com") ||
                   host.EndsWith("googleapis.com") ||
                   host.EndsWith("googleusercontent.com");
        }

        private static async Task ShowErrorAsync(string title, Exception ex)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = ex.Message,
                CloseButtonText = "Well, shit",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }
    }
}
