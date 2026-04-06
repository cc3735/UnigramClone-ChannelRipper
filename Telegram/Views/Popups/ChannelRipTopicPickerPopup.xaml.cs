//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System.Collections.Generic;
using System.Linq;
using Telegram.Controls;
using Telegram.Services;
using Telegram.Td.Api;
using Telegram.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Telegram.Views.Popups
{
    public sealed partial class ChannelRipTopicPickerPopup : ContentPopup
    {
        private readonly HashSet<int> _selectedTopicIds;

        public ChannelRipTopicPickerPopup(IClientService clientService, IEventAggregator aggregator, Chat chat, IEnumerable<int> selectedTopicIds = null)
        {
            InitializeComponent();

            Chat = chat;
            _selectedTopicIds = new HashSet<int>((selectedTopicIds ?? Enumerable.Empty<int>()).Where(x => x > 0));

            Title = Strings.ChannelRipperTopicPromptTitle;
            HintLabel.Text = Strings.ChannelRipperTopicPickerHint;

            TopicList.ItemsSource = new TopicListViewModel.ForumTopicsCollection(clientService, aggregator, null, chat);
        }

        public Chat Chat { get; }

        public IReadOnlyList<int> SelectedTopicIds => _selectedTopicIds.OrderBy(x => x).ToList();

        private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var removed in e.RemovedItems.OfType<ForumTopic>())
            {
                _selectedTopicIds.Remove(removed.Info.ForumTopicId);
            }

            foreach (var added in e.AddedItems.OfType<ForumTopic>())
            {
                _selectedTopicIds.Add(added.Info.ForumTopicId);
            }
        }

        private void TopicList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue || args.Item is not ForumTopic topic)
            {
                return;
            }

            if (_selectedTopicIds.Contains(topic.Info.ForumTopicId) && !TopicList.SelectedItems.Contains(topic))
            {
                TopicList.SelectedItems.Add(topic);
            }
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // Selection is already tracked live in _selectedTopicIds.
        }
    }
}
