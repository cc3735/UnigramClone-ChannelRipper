//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Controls;
using Telegram.Services;
using Telegram.Td.Api;
using Telegram.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Telegram.Views.Popups
{
    public sealed partial class ChannelRipTargetOptionsPopup : ContentPopup
    {
        private readonly HashSet<int> _selectedTopicIds;
        private readonly IClientService _clientService;

        public ChannelRipTargetOptionsPopup(IClientService clientService, IEventAggregator aggregator, Chat chat, ChannelRipTarget target)
        {
            InitializeComponent();

            _clientService = clientService;
            Chat = chat;
            _selectedTopicIds = new HashSet<int>((target?.SelectedTopicIds ?? Enumerable.Empty<int>()).Where(x => x > 0));

            Title = $"Ripper options for {clientService.GetTitle(chat)}";

            var mediaKinds = target?.MediaKinds == 0 ? ChannelRipMediaKind.All : target?.MediaKinds ?? ChannelRipMediaKind.All;
            VideosCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Video);
            PhotosCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Photo);
            AnimationsCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.Animation);
            VideoNotesCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.VideoNote);
            VideoDocumentsCheckBox.IsChecked = mediaKinds.HasFlag(ChannelRipMediaKind.VideoDocument);

            if (clientService.IsForum(chat) || chat?.ViewAsTopics == true)
            {
                HintLabel.Text = "Select one or more forum topics, or leave everything unchecked to rip all topics.";
                _ = LoadTopicsAsync(chat);
            }
            else
            {
                TopicsPanel.Visibility = Visibility.Collapsed;
            }
        }

        public Chat Chat { get; }

        public IReadOnlyList<int> SelectedTopicIds => _selectedTopicIds.OrderBy(x => x).ToList();

        public ChannelRipMediaKind SelectedMediaKinds
        {
            get
            {
                ChannelRipMediaKind value = 0;
                if (VideosCheckBox.IsChecked == true) value |= ChannelRipMediaKind.Video;
                if (PhotosCheckBox.IsChecked == true) value |= ChannelRipMediaKind.Photo;
                if (AnimationsCheckBox.IsChecked == true) value |= ChannelRipMediaKind.Animation;
                if (VideoNotesCheckBox.IsChecked == true) value |= ChannelRipMediaKind.VideoNote;
                if (VideoDocumentsCheckBox.IsChecked == true) value |= ChannelRipMediaKind.VideoDocument;
                return value == 0 ? ChannelRipMediaKind.All : value;
            }
        }

        private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var removed in e.RemovedItems.OfType<ForumTopicChoice>())
            {
                _selectedTopicIds.Remove(removed.Id);
            }

            foreach (var added in e.AddedItems.OfType<ForumTopicChoice>())
            {
                _selectedTopicIds.Add(added.Id);
            }
        }

        private void TopicList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue || args.Item is not ForumTopicChoice topic)
            {
                return;
            }

            if (_selectedTopicIds.Contains(topic.Id) && !TopicList.SelectedItems.Contains(topic))
            {
                TopicList.SelectedItems.Add(topic);
            }
        }

        private async Task LoadTopicsAsync(Chat chat)
        {
            if (chat == null)
            {
                TopicList.ItemsSource = Array.Empty<ForumTopicChoice>();
                HintLabel.Text = "No forum topics are available for this chat.";
                return;
            }

            try
            {
                _clientService.LoadFullInfo(chat);
                var refreshed = await _clientService.SendAsync(new GetChat(chat.Id)) as Chat;
                chat = refreshed ?? chat;
                _clientService.Send(new OpenChat(chat.Id));

                var items = new List<ForumTopicChoice>();
                var offsetDate = 0;
                long offsetMessageId = 0;
                int offsetTopicId = 0;

                while (items.Count < 200)
                {
                    var response = await _clientService.SendAsync(new GetForumTopics(chat.Id, string.Empty, offsetDate, offsetMessageId, offsetTopicId, 100));
                    if (response is not ForumTopics forumTopics || forumTopics.Topics.Count == 0)
                    {
                        break;
                    }

                    foreach (var topic in forumTopics.Topics)
                    {
                        var topicId = topic?.Info?.ForumTopicId ?? 0;
                        if (topicId <= 0 || topicId == ForumTopicService.GeneralId || items.Any(x => x.Id == topicId))
                        {
                            continue;
                        }

                        items.Add(new ForumTopicChoice
                        {
                            Id = topicId,
                            Name = topic.Info.Name,
                            UnreadCount = topic.UnreadCount
                        });
                    }

                    if (forumTopics.NextOffsetMessageId == 0 || forumTopics.Topics.Count < 100)
                    {
                        break;
                    }

                    offsetDate = forumTopics.NextOffsetDate;
                    offsetMessageId = forumTopics.NextOffsetMessageId;
                    offsetTopicId = forumTopics.NextOffsetForumTopicId;
                }

                TopicList.ItemsSource = items;

                foreach (var item in items.Where(x => _selectedTopicIds.Contains(x.Id)))
                {
                    if (!TopicList.SelectedItems.Contains(item))
                    {
                        TopicList.SelectedItems.Add(item);
                    }
                }

                if (items.Count == 0)
                {
                    HintLabel.Text = "No forum topics could be loaded. Leave everything unchecked to rip all topics.";
                }
            }
            catch
            {
                TopicList.ItemsSource = Array.Empty<ForumTopicChoice>();
                HintLabel.Text = "No forum topics could be loaded. Leave everything unchecked to rip all topics.";
            }
        }

        private sealed class ForumTopicChoice
        {
            public int Id { get; init; }
            public string Name { get; init; }
            public int UnreadCount { get; init; }
        }
    }
}
