using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace ModernNetworkRename
{
    public partial class MainWindow : FluentWindow
    {
        private const string ProfilesPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";
        public ObservableCollection<NetworkProfile> Profiles { get; set; } = new ObservableCollection<NetworkProfile>();

        private enum AppLanguage
        {
            Chinese,
            English
        }

        private AppLanguage _currentLanguage = AppLanguage.Chinese;

        private bool _hasUnsavedChanges = false;
        private bool _isClosingConfirmed = false;

        public MainWindow()
        {
            InitializeComponent();
            NetworksGrid.ItemsSource = Profiles;
            MachineNameText.Text = Environment.MachineName;
            ApplyLanguage();
            LoadProfiles();
        }

        private string L(string zh, string en) => _currentLanguage == AppLanguage.Chinese ? zh : en;

        private void ApplyLanguage()
        {
            Title = L("网络配置重命名工具", "Network Rename Tool");

            if (MainTitleBar != null)
            {
                MainTitleBar.Title = L("网络配置重命名工具", "Network Rename Tool");
            }

            if (LanguageToggleButton != null)
            {
                LanguageToggleButton.Content = _currentLanguage == AppLanguage.Chinese ? "中文" : "English";
            }

            if (CurrentDeviceLabel != null)
            {
                CurrentDeviceLabel.Text = L("当前设备", "Current Device");
            }

            if (ColRegistryKey != null)
            {
                ColRegistryKey.Header = L("注册表键", "Registry Key");
            }

            if (ColProfileName != null)
            {
                ColProfileName.Header = L("配置名称", "Profile Name");
            }

            if (ColDescription != null)
            {
                ColDescription.Header = L("描述", "Description");
            }

            if (ColCategory != null)
            {
                ColCategory.Header = L("类别", "Category");
            }

            if (CleanNetworksButton != null)
            {
                CleanNetworksButton.Content = L("清理休眠网络", "Clean Inactive Networks");
            }

            if (CancelButton != null)
            {
                CancelButton.Content = L("取消", "Cancel");
            }

            if (SaveButton != null)
            {
                SaveButton.Content = L("保存修改", "Save Changes");
            }
        }

        private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _currentLanguage = _currentLanguage == AppLanguage.Chinese
                ? AppLanguage.English
                : AppLanguage.Chinese;
            ApplyLanguage();
        }

        private void LoadProfiles()
        {
            Profiles.Clear();
            _hasUnsavedChanges = false; 

            using (var profilesKey = Registry.LocalMachine.OpenSubKey(ProfilesPath))
            {
                if (profilesKey == null) return;
                foreach (var subkeyName in profilesKey.GetSubKeyNames())
                {
                    using (var subkey = profilesKey.OpenSubKey(subkeyName))
                    {
                        if (subkey == null) continue;
                        Profiles.Add(new NetworkProfile
                        {
                            Key = subkeyName,
                            Name = subkey.GetValue("ProfileName")?.ToString(),
                            Description = subkey.GetValue("Description")?.ToString(),
                            Category = Convert.ToInt32(subkey.GetValue("Category") ?? 0)
                        });
                    }
                }
            }
        }

        private void NetworksGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit)
            {
                _hasUnsavedChanges = true;
            }
        }

        private async Task SaveChangesAsync()
        {
            NetworksGrid.CommitEdit();
            NetworksGrid.CommitEdit();

            await Task.Run(() => {
                foreach (var profile in Profiles)
                {
                    var targetKeyName = $@"{ProfilesPath}\{profile.Key}";
                    using (var subkey = Registry.LocalMachine.OpenSubKey(targetKeyName, true))
                    {
                        if (subkey == null) continue;
                        subkey.SetValue("ProfileName", profile.Name ?? string.Empty);
                        subkey.SetValue("Description", profile.Description ?? string.Empty);
                        subkey.SetValue("Category", profile.Category);
                    }
                }
            });
            
            _hasUnsavedChanges = false;

            // 统一风格：保存成功后弹出带一个确定按钮的现代化对话框
            await ShowSingleButtonDialog(
                L("注册表已更新", "Registry Updated"),
                L("所有修改已成功保存到系统底层。", "All changes have been successfully saved to the system registry."));
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasUnsavedChanges) return; 
            await SaveChangesAsync();
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            await HandleUnsavedChangesAsync("取消编辑");
        }

        // ================= 重构：绝对安全的休眠网络清理 =================
        private async void CleanNetworksButton_Click(object sender, RoutedEventArgs e)
        {
            var activeNames = GetActiveNetworkNames();

            // 核心安全锁：如果没探测到活跃网络，立刻终止，防止误杀！
            if (activeNames.Count == 0)
            {
                await ShowSingleButtonDialog(
                    L("安全拦截", "Safety Intercept"),
                    L("系统未能获取到当前活跃的网络状态。为了防止把你正在使用的网络误删，清理操作已强制终止！\n\n请检查网络连接后重试。",
                      "The system could not detect the currently active network status. To avoid accidentally deleting the network you are using, the cleanup has been aborted.\n\nPlease check your network connection and try again."));
                return;
            }

            // 筛选出名字不在活跃列表里的历史网络
            var profilesToDelete = Profiles.Where(p => string.IsNullOrWhiteSpace(p.Name) || !activeNames.Contains(p.Name)).ToList();

                if (profilesToDelete.Count == 0)
                {
                    await ShowSingleButtonDialog(
                        L("清理提示", "Cleanup Info"),
                        L("当前没有多余的休眠网络，非常干净。", "There are currently no extra inactive networks. Everything is clean."));
                return;
            }

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = L("清理确认", "Cleanup Confirmation"),
                Content = L(
                    $"已安全锁定当前正在使用的网络：[{string.Join(", ", activeNames)}]\n\n检测到 {profilesToDelete.Count} 个休眠的历史网络记录，除了当前网络外，它们将被彻底删除。\n此操作不可逆！是否继续？",
                    $"The currently active network has been safely locked: [{string.Join(", ", activeNames)}].\n\nDetected {profilesToDelete.Count} inactive historical network records. All of them except the current one will be permanently removed.\nThis action cannot be undone. Continue?"),
                PrimaryButtonText = L("立即清理", "Clean Now"),
                CloseButtonText = L("取消", "Cancel")
            };

            var result = await dialog.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    using (var profilesKey = Registry.LocalMachine.OpenSubKey(ProfilesPath, true))
                    {
                        foreach (var profile in profilesToDelete)
                        {
                            profilesKey?.DeleteSubKey(profile.Key, false);
                            Profiles.Remove(profile);
                        }
                    }
                    await ShowSingleButtonDialog(
                        L("清理完成", "Cleanup Completed"),
                        L($"已成功拔除 {profilesToDelete.Count} 个休眠网络记录。", $"Successfully removed {profilesToDelete.Count} inactive network records."));
            }
        }

        // 最稳妥的方法：通过 PowerShell 直接读取系统底层目前有流量连接的 ProfileName
        private System.Collections.Generic.HashSet<string> GetActiveNetworkNames()
        {
            var activeNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Get-NetConnectionProfile | Select-Object -ExpandProperty Name\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var name = line.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        activeNames.Add(name);
                    }
                }
            }
            catch { }
            return activeNames;
        }
        // ==============================================================

        private async void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosingConfirmed) return;

            if (_hasUnsavedChanges)
            {
                e.Cancel = true; 

                var dialogResult = await ShowModernMessageBox(
                    L("未保存的修改", "Unsaved Changes"),
                    L("已进行修改，是否保存？\n\n点击“保存”保存并退出，点击“不保存”放弃修改并退出，点击“取消”继续编辑。",
                      "You have unsaved changes. Do you want to save them?\n\nClick \"Save\" to save and exit, click \"Don't Save\" to discard changes and exit, or click \"Cancel\" to continue editing."));

                if (dialogResult == Wpf.Ui.Controls.MessageBoxResult.Primary) 
                {
                    await SaveChangesAsync();
                    _isClosingConfirmed = true; 
                    this.Close(); 
                }
                else if (dialogResult == Wpf.Ui.Controls.MessageBoxResult.Secondary) 
                {
                    _isClosingConfirmed = true;
                    this.Close(); 
                }
            }
        }

        private async Task HandleUnsavedChangesAsync(string title)
        {
            if (_hasUnsavedChanges)
            {
                var dialogResult = await ShowModernMessageBox(
                    title,
                    L("已进行修改，是否保存？\n\n点击“保存”保存，点击“不保存”撤销所有未保存的修改。",
                      "You have unsaved changes. Do you want to save them?\n\nClick \"Save\" to save, or click \"Don't Save\" to discard all unsaved changes."));
                
                if (dialogResult == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    await SaveChangesAsync();
                    LoadProfiles(); 
                }
                else if (dialogResult == Wpf.Ui.Controls.MessageBoxResult.Secondary)
                {
                    LoadProfiles(); 
                }
            }
            else
            {
                LoadProfiles();
            }
        }

        // 核心双按钮确认弹窗
        private async Task<Wpf.Ui.Controls.MessageBoxResult> ShowModernMessageBox(string title, string content)
        {
            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = content,
                PrimaryButtonText = L("保存", "Save"),
                SecondaryButtonText = L("不保存", "Don't Save"),
                CloseButtonText = L("取消", "Cancel")
            };

            return await uiMessageBox.ShowDialogAsync();
        }

        // 核心单按钮提示弹窗（符合你的质感要求）
        private async Task ShowSingleButtonDialog(string title, string content)
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = content,
                CloseButtonText = L("确定", "OK")
            };
            await dialog.ShowDialogAsync();
        }

        private async void NetworksGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                await HandleUnsavedChangesAsync("刷新提示");
            }
            else if (e.Key == Key.Delete)
            {
                if (NetworksGrid.SelectedItem is NetworkProfile profile)
                {
                    var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = L("危险操作", "Dangerous Operation"),
                        Content = L(
                            $"确定要彻底删除网络配置 [{profile.Name}] 吗？\n此操作立即生效且不可逆转！",
                            $"Are you sure you want to permanently delete network profile [{profile.Name}]?\nThis operation takes effect immediately and cannot be undone."),
                        PrimaryButtonText = L("确定删除", "Delete"),
                        CloseButtonText = L("取消", "Cancel")
                    };

                    var result = await uiMessageBox.ShowDialogAsync();

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        using (var profilesKey = Registry.LocalMachine.OpenSubKey(ProfilesPath, true))
                        {
                            profilesKey?.DeleteSubKey(profile.Key, false);
                        }
                        Profiles.Remove(profile);
                    }
                }
            }
        }
    }

    public class NetworkProfile
    {
        public string Key { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int Category { get; set; }
    }
}