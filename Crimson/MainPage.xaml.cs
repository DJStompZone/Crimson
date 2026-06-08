using Crimson.Browser;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Crimson
{
    public sealed partial class MainPage : Page
    {
        private const string HomeUrl = "https://www.youtube.com/";

        private readonly AdBlocker adBlocker = new();

        private bool isAdBlockEnabled = true;
        private bool isSponsorBlockEnabled = true;
        private bool isXamlReady;

        public MainPage()
        {
            InitializeComponent();

            isXamlReady = true;
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                Environment.SetEnvironmentVariable(
                    "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                    "--disable-features=msSmartScreenProtection"
                );

                await YouTubeView.EnsureCoreWebView2Async();

                ConfigureWebView();
                await InjectScriptsAsync();

                Navigate(HomeUrl);
            }
            catch (Exception ex)
            {
                if (LoadingOverlay is not null)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }

                await ShowErrorAsync("Crimson failed to initialize WebView2.", ex);
            }
        }

        private void ConfigureWebView()
        {
            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                throw new InvalidOperationException("WebView2 initialized, but CoreWebView2 is still null. Extremely normal Microsoft behavior.");
            }

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
            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                return;
            }

            string pruneScript = await ReadPackageFileAsync("ms-appx:///Scripts/youtube-prune.js");
            string cleanupScript = await ReadPackageFileAsync("ms-appx:///Scripts/cleanup.js");
            string sponsorBlockScript = await ReadPackageFileAsync("ms-appx:///Scripts/sponsorblock.js");

            await core.AddScriptToExecuteOnDocumentCreatedAsync(pruneScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(cleanupScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(sponsorBlockScript);
        }

        private static async Task<string> ReadPackageFileAsync(string uri)
        {
            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
            return await FileIO.ReadTextAsync(file);
        }

        private void Core_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (!isAdBlockEnabled)
            {
                return;
            }

            string? requestUri = args.Request.Uri;

            if (!adBlocker.ShouldBlock(requestUri))
            {
                return;
            }

            InMemoryRandomAccessStream emptyStream = new();

            args.Response = sender.Environment.CreateWebResourceResponse(
                emptyStream,
                204,
                "No Content",
                "Content-Type: text/plain\r\nCache-Control: no-store"
            );
        }

        private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
            }
        }

        private void Core_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }

            if (AddressBox is not null)
            {
                AddressBox.Text = sender.Source ?? string.Empty;
            }

            UpdateNavigationButtons();
            _ = PushSettingsToPageAsync();
        }

        private void Core_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            if (AddressBox is not null)
            {
                AddressBox.Text = sender.Source ?? string.Empty;
            }

            UpdateNavigationButtons();
        }

        private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;

            string? targetUri = args.Uri;

            if (!string.IsNullOrWhiteSpace(targetUri) && IsAllowedNavigation(targetUri))
            {
                sender.Navigate(targetUri);
            }
        }

        private void Core_ContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            if (TopBar is null)
            {
                return;
            }

            TopBar.Visibility = sender.ContainsFullScreenElement
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void UpdateNavigationButtons()
        {
            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                if (BackButton is not null)
                {
                    BackButton.IsEnabled = false;
                }

                if (ForwardButton is not null)
                {
                    ForwardButton.IsEnabled = false;
                }

                return;
            }

            if (BackButton is not null)
            {
                BackButton.IsEnabled = core.CanGoBack;
            }

            if (ForwardButton is not null)
            {
                ForwardButton.IsEnabled = core.CanGoForward;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetCoreWebView2(out CoreWebView2 core) && core.CanGoBack)
            {
                core.GoBack();
            }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetCoreWebView2(out CoreWebView2 core) && core.CanGoForward)
            {
                core.GoForward();
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetCoreWebView2(out CoreWebView2 core))
            {
                core.Reload();
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Navigate(HomeUrl);
        }

        private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;

            string target = BuildNavigationTarget(AddressBox?.Text);
            Navigate(target);
        }

        private void AdBlockToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                isAdBlockEnabled = toggle.IsOn;
            }
        }

        private void SponsorBlockToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                isSponsorBlockEnabled = toggle.IsOn;
            }

            if (!isXamlReady)
            {
                return;
            }

            _ = PushSettingsToPageAsync();
        }

        private void Navigate(string target)
        {
            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                return;
            }

            core.Navigate(target);
        }

        private async Task PushSettingsToPageAsync()
        {
            if (!isXamlReady)
            {
                return;
            }

            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                return;
            }

            string sponsorBlockValue = isSponsorBlockEnabled ? "true" : "false";
            string script = $"window.__crimsonSettings = {{ sponsorBlockEnabled: {sponsorBlockValue} }}; window.dispatchEvent(new Event('crimson-settings-changed'));";

            await core.ExecuteScriptAsync(script);
        }

        private bool TryGetCoreWebView2(out CoreWebView2 core)
        {
            core = YouTubeView?.CoreWebView2!;

            return core is not null;
        }

        private static string BuildNavigationTarget(string? input)
        {
            string trimmed = input?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return HomeUrl;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (trimmed.Contains('.') && !trimmed.Contains(' '))
            {
                return "https://" + trimmed;
            }

            return "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(trimmed);
        }

        private static bool IsAllowedNavigation(string? rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri))
            {
                return false;
            }

            if (!Uri.TryCreate(rawUri, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();

            return host.EndsWith("youtube.com") ||
                   host.EndsWith("youtu.be") ||
                   host.EndsWith("google.com") ||
                   host.EndsWith("gstatic.com") ||
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