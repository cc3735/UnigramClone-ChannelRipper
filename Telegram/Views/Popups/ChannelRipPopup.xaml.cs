//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Telegram.ViewModels;
using Telegram.Controls;
using Telegram.Services;
using Telegram.Td.Api;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage;

namespace Telegram.Views.Popups
{
    public sealed partial class ChannelRipPopup : ContentPopup
    {
        private ChannelRipTarget _editingTarget;
        private Chat _editingChat;

        public ChannelRipViewModel ViewModel => DataContext as ChannelRipViewModel;

        public ChannelRipPopup()
        {
            InitializeComponent();
            Title = Strings.ChannelRipperTitle;
        }

        private void StartPause_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StartPause();
        }

        private async void AddTarget_Click(object sender, RoutedEventArgs e)
        {
            Close();

            var chat = await ViewModel.PickTargetChatAsync();
            if (chat == null)
            {
                return;
            }

            chat = await ViewModel.EnsureChatAsync(chat);
            await ViewModel.AddTargetByIdAsync(chat.Id);
        }

        private async void PickRoot_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.PickRootAsync();
        }

        private async void PickBackup_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.PickBackupAsync();
        }

        private void DedupeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as ComboBox)?.SelectedItem is ChannelRipChoiceItem<ChannelRipDedupeMode> choice)
            {
                ViewModel.SetDedupeMode(choice.Value);
            }
        }

        private void LayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as ComboBox)?.SelectedItem is ChannelRipChoiceItem<ChannelRipLayoutMode> choice)
            {
                ViewModel.SetLayoutMode(choice.Value);
            }
        }

        private void ToggleTarget_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                ViewModel.ToggleTarget(target);
            }
        }

        private void RemoveTarget_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                ViewModel.RemoveTarget(target);
            }
        }

        private void ResetTarget_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                ViewModel.ResetTarget(target);
            }
        }

        private async void RemoveTopic_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTopicItemViewModel topic)
            {
                await ViewModel.RemoveTopicAsync(topic.Owner, topic.TopicId);
            }
        }

        private async void AddTopic_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTopicItemViewModel topic)
            {
                await ViewModel.AddTopicAsync(topic.Owner, topic.TopicId);
            }
        }

        private async void RefreshTopics_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                await ViewModel.RefreshTopicsAsync(target);
            }
        }

        private void ToggleExpand_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                ViewModel.ToggleExpanded(target);
            }
        }

        private void TopicFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target &&
                sender is TextBox textBox)
            {
                ViewModel.SetTopicFilter(target, textBox.Text);
            }
        }

        private async void EditTarget_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                if (!ViewModel.ClientService.TryGetChat(target.Target.ChatId, out var chat))
                {
                    chat = await ViewModel.ClientService.SendAsync(new GetChat(target.Target.ChatId)) as Chat;
                }

                chat = await ViewModel.EnsureChatAsync(chat);
                if (chat == null)
                {
                    return;
                }

                await BeginInlineEditAsync(chat, target.Target);
            }
        }

        private async void OpenTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChannelRipTargetViewModel target)
            {
                await ViewModel.OpenTargetFolderAsync(target);
            }
        }

        private async Task BeginInlineEditAsync(Chat chat, ChannelRipTarget target)
        {
            try
            {
                AppendUiTrace("BeginInlineEdit:start");
                _editingChat = chat;
                _editingTarget = target;
                ViewModel.SetInlineEditMode(true);
                TargetsList.Visibility = Visibility.Collapsed;
                InlineEditPanel.Visibility = Visibility.Collapsed;
                AppendUiTrace("BeginInlineEdit:list-collapsed");

                InlineEditTitle.Text = $"Edit target: {ViewModel.ClientService.GetTitle(chat)}";
                InlineEditHint.Text = ViewModel.IsForumChat(chat)
                    ? "Pick the forum topics you want, or leave everything unchecked to rip all topics."
                    : "This chat does not use forum topics. Choose which media types to archive.";

                var mediaKinds = target?.MediaKinds == 0 ? ChannelRipMediaKind.All : target?.MediaKinds ?? ChannelRipMediaKind.All;
                InlineVideosCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Video);
                InlinePhotosCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Photo);
                InlineAnimationsCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Animation);
                InlineVideoNotesCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.VideoNote);
                InlineVideoDocumentsCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.VideoDocument);
                AppendUiTrace("BeginInlineEdit:media-set");

                if (ViewModel.IsForumChat(chat))
                {
                    InlineTopicsPanel.Visibility = Visibility.Visible;
                    InlineKnownTopicsTextBox.Text = string.Empty;
                    InlineSelectedTopicIdsTextBox.Text = string.Empty;
                    AppendUiTrace("BeginInlineEdit:loading-topics");
                    await LoadInlineTopicsAsync(chat, target?.SelectedTopicIds);
                }
                else
                {
                    InlineTopicsPanel.Visibility = Visibility.Collapsed;
                    InlineKnownTopicsTextBox.Text = string.Empty;
                    InlineSelectedTopicIdsTextBox.Text = string.Empty;
                }

                InlineEditPanel.Visibility = Visibility.Visible;
                AppendUiTrace("BeginInlineEdit:panel-visible");
                AppendUiTrace("BeginInlineEdit:complete");
            }
            catch (Exception ex)
            {
                AppendUiTrace("BeginInlineEdit:error:" + ex);
                TargetsList.Visibility = Visibility.Visible;
                InlineEditPanel.Visibility = Visibility.Collapsed;
                ViewModel.SetInlineEditMode(false);
                throw;
            }
        }

        private async Task LoadInlineTopicsAsync(Chat chat, IReadOnlyList<int> selectedTopicIds)
        {
            try
            {
                AppendUiTrace("LoadInlineTopics:start");
                var selected = new HashSet<int>((selectedTopicIds ?? Array.Empty<int>()).Where(x => x > 0));
                var items = (await ViewModel.GetTopicChoicesAsync(chat.Id))
                    .Select(topic => new ForumTopicChoice
                    {
                        Id = topic.Id,
                        Name = topic.Name,
                        UnreadCount = topic.UnreadCount
                    })
                    .ToList();

                AppendUiTrace($"LoadInlineTopics:items={items.Count}");
                InlineKnownTopicsTextBox.Text = items.Count == 0
                    ? "No cached forum topics yet. Start ripping once to let the service discover topics, then reopen Edit."
                    : string.Join(Environment.NewLine, items
                        .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(x => x.UnreadCount > 0 ? $"{x.Id}: {x.Name} ({x.UnreadCount})" : $"{x.Id}: {x.Name}"));
                InlineSelectedTopicIdsTextBox.Text = selected.Count == 0
                    ? string.Empty
                    : string.Join(", ", selected.OrderBy(x => x));
                AppendUiTrace("LoadInlineTopics:text-ready");

                if (items.Count == 0)
                {
                    InlineEditHint.Text = "No cached forum topics are available yet. Leave the topic IDs blank to rip all topics, or start a rip once and reopen Edit after topics are discovered.";
                }
            }
            catch (Exception ex)
            {
                InlineKnownTopicsTextBox.Text = string.Empty;
                InlineSelectedTopicIdsTextBox.Text = string.Empty;
                InlineEditHint.Text = $"No forum topics could be loaded. {ex.Message}";
                AppendUiTrace("LoadInlineTopics:error:" + ex);
            }
        }

        private async void InlineEditApply_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChat == null)
            {
                return;
            }

            ChannelRipMediaKind mediaKinds = 0;
            if (InlineVideosCheckBox.IsChecked == true) mediaKinds |= ChannelRipMediaKind.Video;
            if (InlinePhotosCheckBox.IsChecked == true) mediaKinds |= ChannelRipMediaKind.Photo;
            if (InlineAnimationsCheckBox.IsChecked == true) mediaKinds |= ChannelRipMediaKind.Animation;
            if (InlineVideoNotesCheckBox.IsChecked == true) mediaKinds |= ChannelRipMediaKind.VideoNote;
            if (InlineVideoDocumentsCheckBox.IsChecked == true) mediaKinds |= ChannelRipMediaKind.VideoDocument;
            if (mediaKinds == 0)
            {
                mediaKinds = ChannelRipMediaKind.All;
            }

            var topicIds = InlineTopicsPanel.Visibility == Visibility.Visible
                ? ParseTopicIds(InlineSelectedTopicIdsTextBox.Text)
                : new List<int>();

            await ViewModel.UpdateTargetOptionsAsync(_editingChat.Id, topicIds, mediaKinds);
            InlineEditPanel.Visibility = Visibility.Collapsed;
            TargetsList.Visibility = Visibility.Visible;
            _editingTarget = null;
            _editingChat = null;
            ViewModel.SetInlineEditMode(false);
        }

        private void InlineEditCancel_Click(object sender, RoutedEventArgs e)
        {
            InlineEditPanel.Visibility = Visibility.Collapsed;
            TargetsList.Visibility = Visibility.Visible;
            _editingTarget = null;
            _editingChat = null;
            ViewModel.SetInlineEditMode(false);
        }

        private sealed class ForumTopicChoice
        {
            public int Id { get; init; }
            public string Name { get; init; }
            public int UnreadCount { get; init; }
        }

        private static List<int> ParseTopicIds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<int>();
            }

            return value
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        private static void AppendUiTrace(string message)
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "channel-ripper-ui.log");
                System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}
