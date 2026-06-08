using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crimson.Browser;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Crimson
{
    public sealed partial class MainPage : Page
    {
        private const string HomeUrl = "https://www.youtube.com/";
        private static readonly bool DisableAcceleratedVideoDecode = false;

        private readonly AdBlocker adBlocker = new();

        private bool isAdBlockEnabled = true;
        private bool isSponsorBlockEnabled = true;
        private bool isXamlReady;
        private bool isHardReloading;
        private string lastSafeYouTubeUrl = HomeUrl;

        public MainPage()
        {
            InitializeComponent();

            isXamlReady = true;
            UpdateToggleLabels();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                Environment.SetEnvironmentVariable(
                    "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                    BuildAdditionalBrowserArguments()
                );

                await YouTubeView.EnsureCoreWebView2Async();

                ConfigureWebView();
                await InjectScriptsAsync();

                Navigate(HomeUrl);
            }
            catch (Exception ex)
            {
                SetLoadingOverlayVisible(false);
                await ShowErrorAsync("Crimson failed to initialize WebView2.", ex);
            }
        }

        private static string BuildAdditionalBrowserArguments()
        {
            List<string> args = new()
            {
                "--disable-features=msSmartScreenProtection"
            };

#if DEBUG
            args.Add("--enable-features=msEdgeDevToolsWdpRemoteDebugging");
#endif

            if (DisableAcceleratedVideoDecode)
            {
                args.Add("--disable-accelerated-video-decode");
            }

            return string.Join(" ", args);
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
            core.WebMessageReceived += Core_WebMessageReceived;
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
            if (!IsAllowedTopLevelNavigation(args.Uri))
            {
                args.Cancel = true;

                if (!isHardReloading)
                {
                    Navigate(BuildYouTubeSearchTarget(args.Uri));
                }

                return;
            }

            SetLoadingOverlayVisible(true);
        }

        private void Core_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            SetLoadingOverlayVisible(false);
            UpdateCurrentAddress(sender.Source);
            UpdateNavigationButtons();
            _ = PushSettingsToPageAsync();
        }

        private void Core_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            UpdateCurrentAddress(sender.Source);
            UpdateNavigationButtons();
        }

        private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;

            string? targetUri = args.Uri;

            if (IsAllowedYouTubeNavigation(targetUri))
            {
                sender.Navigate(targetUri);
                return;
            }

            if (!string.IsNullOrWhiteSpace(targetUri))
            {
                sender.Navigate(BuildYouTubeSearchTarget(targetUri));
            }
        }

        private void Core_ContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            SetChromeVisible(!sender.ContainsFullScreenElement);
        }

        private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string message = args.WebMessageAsJson ?? string.Empty;

            if (!message.Contains("crimson-hard-reload", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = HardReloadCurrentPageAsync();
        }

        private void UpdateCurrentAddress(string? source)
        {
            string currentSource = source ?? string.Empty;

            if (IsAllowedYouTubeNavigation(currentSource))
            {
                lastSafeYouTubeUrl = currentSource;
            }

            if (AddressBox is not null)
            {
                AddressBox.Text = currentSource;
            }
        }

        private void SetChromeVisible(bool isVisible)
        {
            if (TopBar is not null)
            {
                TopBar.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ChromeDivider is not null)
            {
                ChromeDivider.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TopBarRow is not null)
            {
                TopBarRow.Height = isVisible ? new GridLength(58) : new GridLength(0);
            }

            if (ChromeDividerRow is not null)
            {
                ChromeDividerRow.Height = isVisible ? new GridLength(2) : new GridLength(0);
            }
        }

        private void SetLoadingOverlayVisible(bool isVisible)
        {
            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
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
            _ = HardReloadCurrentPageAsync();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Navigate(HomeUrl);
        }

        private void AddressBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText;

            if (string.IsNullOrWhiteSpace(query))
            {
                query = sender.Text;
            }

            Navigate(BuildNavigationTarget(query));
        }

        private void AdBlockToggle_Checked(object sender, RoutedEventArgs e)
        {
            isAdBlockEnabled = true;
            UpdateToggleLabels();
        }

        private void AdBlockToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            isAdBlockEnabled = false;
            UpdateToggleLabels();
        }

        private void SponsorBlockToggle_Checked(object sender, RoutedEventArgs e)
        {
            isSponsorBlockEnabled = true;
            UpdateToggleLabels();

            if (isXamlReady)
            {
                _ = PushSettingsToPageAsync();
            }
        }

        private void SponsorBlockToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            isSponsorBlockEnabled = false;
            UpdateToggleLabels();

            if (isXamlReady)
            {
                _ = PushSettingsToPageAsync();
            }
        }

        private void UpdateToggleLabels()
        {
            if (AdBlockToggle is not null)
            {
                AdBlockToggle.Content = isAdBlockEnabled ? "AD ON" : "AD OFF";
            }

            if (SponsorBlockToggle is not null)
            {
                SponsorBlockToggle.Content = isSponsorBlockEnabled ? "SB ON" : "SB OFF";
            }
        }

        private void Navigate(string target)
        {
            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                return;
            }

            core.Navigate(target);
        }

        private async Task HardReloadCurrentPageAsync()
        {
            if (isHardReloading)
            {
                return;
            }

            if (!TryGetCoreWebView2(out CoreWebView2 core))
            {
                return;
            }

            isHardReloading = true;

            try
            {
                string target = GetCurrentYouTubeTarget(core);

                await core.ExecuteScriptAsync("(() => { for (const media of document.querySelectorAll('video,audio')) { try { media.pause(); media.removeAttribute('src'); media.load(); } catch (_) {} } })();");
                core.Navigate("about:blank");
                await Task.Delay(325);
                core.Navigate(AppendReloadNonce(target));
            }
            finally
            {
                _ = ResetHardReloadFlagAsync();
            }
        }

        private async Task ResetHardReloadFlagAsync()
        {
            await Task.Delay(800);
            isHardReloading = false;
        }

        private string GetCurrentYouTubeTarget(CoreWebView2 core)
        {
            string source = core.Source ?? string.Empty;

            if (IsAllowedYouTubeNavigation(source))
            {
                return source;
            }

            if (IsAllowedYouTubeNavigation(lastSafeYouTubeUrl))
            {
                return lastSafeYouTubeUrl;
            }

            return HomeUrl;
        }

        private static string AppendReloadNonce(string rawUrl)
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri))
            {
                return HomeUrl;
            }

            UriBuilder builder = new(uri);
            string query = builder.Query;
            string separator = string.IsNullOrEmpty(query) ? string.Empty : "&";
            string trimmedQuery = query.StartsWith("?") ? query[1..] : query;

            builder.Query = trimmedQuery + separator + "crimson_reload=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            return builder.Uri.ToString();
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
            string adBlockValue = isAdBlockEnabled ? "true" : "false";
            string platform = IsXboxDeviceFamily() ? "xbox" : "windows";
            string script = $"window.__crimsonSettings = {{ sponsorBlockEnabled: {sponsorBlockValue}, adBlockEnabled: {adBlockValue}, platform: '{platform}', hostReloadAvailable: true }}; window.dispatchEvent(new Event('crimson-settings-changed'));";

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

            string maybeUrl = trimmed;

            if (!maybeUrl.Contains("://") && (maybeUrl.StartsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || maybeUrl.StartsWith("www.youtube.com", StringComparison.OrdinalIgnoreCase) || maybeUrl.StartsWith("youtu.be", StringComparison.OrdinalIgnoreCase)))
            {
                maybeUrl = "https://" + maybeUrl;
            }

            if (Uri.TryCreate(maybeUrl, UriKind.Absolute, out Uri? absoluteUri) && IsAllowedYouTubeNavigation(absoluteUri.ToString()))
            {
                return absoluteUri.ToString();
            }

            return BuildYouTubeSearchTarget(trimmed);
        }

        private static string BuildYouTubeSearchTarget(string? query)
        {
            string trimmed = query?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return HomeUrl;
            }

            return "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(trimmed);
        }

        private static bool IsAllowedTopLevelNavigation(string? rawUri)
        {
            if (string.Equals(rawUri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsAllowedYouTubeNavigation(rawUri);
        }

        private static bool IsAllowedYouTubeNavigation(string? rawUri)
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
            return host == "youtu.be" || host.EndsWith("youtube.com");
        }

        private static bool IsXboxDeviceFamily()
        {
            string deviceFamily = AnalyticsInfo.VersionInfo.DeviceFamily;
            return deviceFamily.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
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
