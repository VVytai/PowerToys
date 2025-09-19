// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using TopToolbar.Models;
using TopToolbar.Services;
using TopToolbar.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TopToolbar
{
    public sealed partial class SettingsWindow : WinUIEx.WindowEx, IDisposable
    {
        private readonly SettingsViewModel _vm;
        private bool _isClosed;
        private bool _disposed;

        public SettingsViewModel ViewModel => _vm;

        public SettingsWindow()
        {
            this.InitializeComponent();
            _vm = new SettingsViewModel(new ToolbarConfigService());
            this.Closed += async (s, e) =>
            {
                await _vm.SaveAsync();
            };
            this.Activated += async (s, e) =>
            {
                if (_vm.Groups.Count == 0)
                {
                    await _vm.LoadAsync(this.DispatcherQueue);
                }
            };

            // Keep left pane visible when no selection so UI doesn't look empty
            _vm.PropertyChanged += ViewModel_PropertyChanged;

            InitializeWindowStyling();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedGroup) ||
                e.PropertyName == nameof(SettingsViewModel.HasNoSelectedGroup))
            {
                EnsureLeftPaneColumn();
                _leftPaneColumnCache ??= GetLeftPaneColumn();
                if (_leftPaneColumnCache != null && _vm.HasNoSelectedGroup)
                {
                    _leftPaneColumnCache.Width = new GridLength(240);
                }
            }
        }

        private void OnToggleGroupsPane(object sender, RoutedEventArgs e)
        {
            EnsureLeftPaneColumn();
            if (_leftPaneColumnCache != null)
            {
                _leftPaneColumnCache.Width = (_leftPaneColumnCache.Width.Value == 0) ? new GridLength(240) : new GridLength(0);
            }
        }

        private async void OnAddGroup(object sender, RoutedEventArgs e)
        {
            _vm.AddGroup();
            await _vm.SaveAsync();
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            await _vm.SaveAsync();
        }

        private async void OnClose(object sender, RoutedEventArgs e)
        {
            if (_isClosed)
            {
                return;
            }

            _isClosed = true; // prevent re-entry

            try
            {
                await _vm.SaveAsync();
            }
            catch (Exception ex)
            {
                try
                {
                    SafeLogWarning($"Save before close failed: {ex.Message}");
                }
                catch
                {
                }
            }

            SafeCloseWindow();
        }

        private async void OnAddButton(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedGroup != null)
            {
                _vm.AddButton(_vm.SelectedGroup);
                await _vm.SaveAsync();
            }
        }

        private async void OnRemoveGroup(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag;
            var group = (tag as ButtonGroup) ?? (_vm.Groups.Contains(_vm.SelectedGroup) ? _vm.SelectedGroup : null);
            if (group != null)
            {
                _vm.RemoveGroup(group);
                await _vm.SaveAsync();
            }
        }

        private async void OnRemoveSelectedGroup(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedGroup != null)
            {
                _vm.RemoveGroup(_vm.SelectedGroup);
                await _vm.SaveAsync();
            }
        }

        private async void OnRemoveButton(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedButton != null && _vm.SelectedGroup != null)
            {
                _vm.RemoveButton(_vm.SelectedGroup, _vm.SelectedButton);
                await _vm.SaveAsync();
            }
        }

        private async void OnBrowseIcon(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedButton == null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".ico");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _vm.SelectedButton.IconType = ToolbarIconType.Image;
                _vm.SelectedButton.IconPath = file.Path;
                await _vm.SaveAsync();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _vm?.Dispose();
            }
            catch
            {
            }
        }

        private void InitializeWindowStyling()
        {
            // Attempt to use Mica (BaseAlt for slightly higher contrast) via SystemBackdrop.
            try
            {
                var mica = new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt,
                };
                SystemBackdrop = mica;
            }
            catch
            {
                // Ignore if not supported (older OS).
            }

            try
            {
                if (AppWindow?.TitleBar != null)
                {
                    var titleBar = AppWindow.TitleBar;
                    titleBar.ExtendsContentIntoTitleBar = true;
                    titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;

                    // Make caption buttons transparent so custom background shows.
                    titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                }

                // Register custom drag region on the named Grid in XAML (AppTitleBar).
                if (_appTitleBarCache == null)
                {
                    _appTitleBarCache = GetAppTitleBar();
                }

                if (_appTitleBarCache is FrameworkElement dragRegion)
                {
                    // Use built‑in drag handling instead of manual WM_NCLBUTTONDOWN loop (prevents stack overflow re-entry)
                    this.SetTitleBar(dragRegion);
                    dragRegion.DoubleTapped += (s, e) => ToggleMaximize();
                }
            }
            catch
            {
            }
        }

        private void ToggleMaximize()
        {
            try
            {
                if (AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                {
                    if (p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
                    {
                        p.Restore();
                    }
                    else
                    {
                        p.Maximize();
                    }
                }
            }
            catch
            {
            }
        }

        private ColumnDefinition _leftPaneColumnCache;
        private FrameworkElement _appTitleBarCache;

        private void EnsureLeftPaneColumn()
        {
            if (_leftPaneColumnCache == null)
            {
                _leftPaneColumnCache = GetLeftPaneColumn();
            }
        }

        private ColumnDefinition GetLeftPaneColumn()
        {
            try
            {
                // The left pane ColumnDefinition has x:Name="LeftPaneColumn" in XAML. Generated partial may expose field; if not, locate via visual tree.
                var root = this.Content as FrameworkElement;
                if (root != null)
                {
                    return (ColumnDefinition)root.FindName("LeftPaneColumn");
                }
            }
            catch
            {
            }

            return null;
        }

        private FrameworkElement GetAppTitleBar()
        {
            try
            {
                var root = this.Content as FrameworkElement;
                if (root != null)
                {
                    return root.FindName("AppTitleBar") as FrameworkElement;
                }
            }
            catch
            {
            }

            return null;
        }

    // Removed manual BeginDragMove implementation: using SetTitleBar now.
        private void SafeCloseWindow()
        {
            try
            {
                Close();
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                // Ignore RO_E_CLOSED or already closed window scenarios
                try
                {
                    SafeLogWarning($"Close COMException 0x{comEx.HResult:X}");
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                try
                {
                    SafeLogError($"Unexpected Close exception: {ex.Message}");
                }
                catch
                {
                }
            }
        }

        private static void SafeLogWarning(string msg)
        {
#if HAS_MANAGEDCOMMON_LOGGER
            try { ManagedCommon.Logger.LogWarning("SettingsWindow: " + msg); } catch { }
#else
            Debug.WriteLine("[SettingsWindow][WARN] " + msg);
#endif
        }

        private static void SafeLogError(string msg)
        {
#if HAS_MANAGEDCOMMON_LOGGER
            try { ManagedCommon.Logger.LogError("SettingsWindow: " + msg); } catch { }
#else
            Debug.WriteLine("[SettingsWindow][ERR ] " + msg);
#endif
        }

    // Removed P/Invoke (ReleaseCapture / SendMessage) no longer required.
    }
}
