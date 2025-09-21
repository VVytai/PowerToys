// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI; // Colors namespace
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input; // KeyRoutedEventArgs
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media; // VisualTreeHelper
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
        private FrameworkElement _appTitleBarCache;

        public SettingsViewModel ViewModel => _vm;

        public SettingsWindow()
        {
            try
            {
                // Use reflection to invoke generated InitializeComponent to satisfy editor analysis without defining a duplicate stub.
                var init = typeof(SettingsWindow).GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                init?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                SafeLogWarning("InitializeComponent fallback: " + ex.Message);
            }

            _vm = new SettingsViewModel(new ToolbarConfigService());
            InitializeWindowStyling();
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

            // Modern styling applied via InitializeWindowStyling
        }

        private void InitializeWindowStyling()
        {
            // Try set Mica backdrop (Base for subtle tint)
            try
            {
                var mica = new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base,
                };
                SystemBackdrop = mica;
            }
            catch
            {
            }

            // Extend into title bar & customize caption buttons
            try
            {
                if (AppWindow?.TitleBar != null)
                {
                    var tb = AppWindow.TitleBar;
                    tb.ExtendsContentIntoTitleBar = true;
                    tb.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
                    tb.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    tb.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(24, 0, 0, 0);
                    tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(36, 0, 0, 0);
                }

                _appTitleBarCache ??= GetAppTitleBar();
                if (_appTitleBarCache is FrameworkElement dragRegion)
                {
                    this.SetTitleBar(dragRegion);
                }
            }
            catch
            {
            }
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

        // InitializeWindowStyling removed.
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

        // Inline rename handlers for groups list
        private void OnStartRenameGroup(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is ButtonGroup group)
                {
                    // Ensure this group is selected
                    if (_vm.SelectedGroup != group)
                    {
                        _vm.SelectedGroup = group;
                    }

                    // Find ListViewItem visual tree, then TextBox
                    // Access GroupsList via root FrameworkElement (Window itself has no FindName in WinUI 3)
                    var root = this.Content as FrameworkElement;
                    var groupsList = root?.FindName("GroupsList") as ListView;
                    var container = groupsList?.ContainerFromItem(group) as ListViewItem;
                    if (container != null)
                    {
                        var editBox = FindChild<TextBox>(container, "NameEdit");
                        var textBlock = FindChild<TextBlock>(container, "NameText");
                        if (editBox != null && textBlock != null)
                        {
                            textBlock.Visibility = Visibility.Collapsed;
                            editBox.Visibility = Visibility.Visible;
                            editBox.SelectAll();
                            _ = editBox.Focus(FocusState.Programmatic);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning("OnStartRenameGroup: " + ex.Message);
            }
        }

        private void CommitGroupRename(TextBox editBox, TextBlock textBlock)
        {
            if (editBox == null || textBlock == null)
            {
                return;
            }

            textBlock.Visibility = Visibility.Visible;
            editBox.Visibility = Visibility.Collapsed;
        }

        private void OnGroupNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    var parent = tb.Parent as FrameworkElement;
                    var textBlock = FindSibling<TextBlock>(tb, "NameText");
                    CommitGroupRename(tb, textBlock);
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    // Revert displayed text (binding already updated progressively, so we reload from VM selected group name)
                    if (_vm.SelectedGroup != null)
                    {
                        tb.Text = _vm.SelectedGroup.Name;
                    }

                    var textBlock = FindSibling<TextBlock>(tb, "NameText");
                    CommitGroupRename(tb, textBlock);
                    e.Handled = true;
                }
            }
        }

        private void OnGroupNameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var textBlock = FindSibling<TextBlock>(tb, "NameText");
                CommitGroupRename(tb, textBlock);
            }
        }

        // Utility visual tree search helpers
        private static T FindChild<T>(DependencyObject root, string name)
            where T : FrameworkElement
        {
            if (root == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T fe)
                {
                    if (string.IsNullOrEmpty(name) || fe.Name == name)
                    {
                        return fe;
                    }
                }

                var result = FindChild<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static T FindSibling<T>(FrameworkElement element, string name)
            where T : FrameworkElement
        {
            if (element?.Parent is DependencyObject parent)
            {
                return FindChild<T>(parent, name);
            }

            return null;
        }

        // Inline group description editing removed per design update; now always displays single-line text.
    }
}
