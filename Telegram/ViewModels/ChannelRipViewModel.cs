//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Services.Updates;
using Telegram.Td.Api;
using Telegram.Views.Popups;
using Windows.UI.Xaml.Controls;

namespace Telegram.ViewModels
{
    public sealed class ChannelRipViewModel : ViewModelBase, IHandle
    {
        private readonly IChannelRipService _channelRipService;
        private readonly HashSet<long> _expandedTargetIds = new HashSet<long>();
        private readonly Dictionary<long, string> _topicFilters = new Dictionary<long, string>();
        private ChannelRipStatus _lastStatus;

        public ChannelRipViewModel(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator, IChannelRipService channelRipService)
            : base(clientService, settingsService, aggregator)
        {
            _channelRipService = channelRipService;
            Items = new ObservableCollection<ChannelRipTargetViewModel>();
            DedupeModes = new ObservableCollection<ChannelRipChoiceItem<ChannelRipDedupeMode>>
            {
                new("Global dedupe", ChannelRipDedupeMode.Global),
                new("Per-chat dedupe", ChannelRipDedupeMode.PerChat),
                new("Per-topic dedupe", ChannelRipDedupeMode.PerTopic)
            };
            LayoutModes = new ObservableCollection<ChannelRipChoiceItem<ChannelRipLayoutMode>>
            {
                new("Channel -> Topic -> Date", ChannelRipLayoutMode.ChannelTopicDate),
                new("Channel -> Topic", ChannelRipLayoutMode.ChannelTopic),
                new("Channel only", ChannelRipLayoutMode.ChannelOnly)
            };
            SortModes = new ObservableCollection<ChannelRipChoiceItem<ChannelRipSortMode>>
            {
                new("Activity first", ChannelRipSortMode.ActivityFirst),
                new("Name", ChannelRipSortMode.Name),
                new("Queue size", ChannelRipSortMode.QueueSize),
                new("Recently updated", ChannelRipSortMode.RecentlyUpdated)
            };
            Refresh();
        }

        public ObservableCollection<ChannelRipTargetViewModel> Items { get; }
        public ObservableCollection<ChannelRipChoiceItem<ChannelRipDedupeMode>> DedupeModes { get; }
        public ObservableCollection<ChannelRipChoiceItem<ChannelRipLayoutMode>> LayoutModes { get; }
        public ObservableCollection<ChannelRipChoiceItem<ChannelRipSortMode>> SortModes { get; }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => Set(ref _isRunning, value);
        }

        private string _status;
        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        private string _rootPath;
        public string RootPath
        {
            get => _rootPath;
            set => Set(ref _rootPath, value);
        }

        private string _backupPath;
        public string BackupPath
        {
            get => _backupPath;
            set => Set(ref _backupPath, value);
        }

        private ChannelRipChoiceItem<ChannelRipLayoutMode> _selectedLayoutMode;
        public ChannelRipChoiceItem<ChannelRipLayoutMode> SelectedLayoutMode
        {
            get => _selectedLayoutMode;
            set => Set(ref _selectedLayoutMode, value);
        }

        private string _lastError;
        public string LastError
        {
            get => _lastError;
            set => Set(ref _lastError, value);
        }

        private bool _suppressItemRefresh;
        private bool _showActiveOnly;
        public bool ShowActiveOnly
        {
            get => _showActiveOnly;
            set
            {
                if (Set(ref _showActiveOnly, value))
                {
                    ReapplyStatus();
                }
            }
        }

        private ChannelRipChoiceItem<ChannelRipDedupeMode> _selectedDedupeMode;
        public ChannelRipChoiceItem<ChannelRipDedupeMode> SelectedDedupeMode
        {
            get => _selectedDedupeMode;
            set => Set(ref _selectedDedupeMode, value);
        }

        private ChannelRipChoiceItem<ChannelRipSortMode> _selectedSortMode;
        public ChannelRipChoiceItem<ChannelRipSortMode> SelectedSortMode
        {
            get => _selectedSortMode;
            set
            {
                if (Set(ref _selectedSortMode, value))
                {
                    ReapplyStatus();
                }
            }
        }

        public override void Subscribe()
        {
            Aggregator.Subscribe<UpdateChannelRipStatus>(this, Handle);
        }

        public void Handle(UpdateChannelRipStatus update)
        {
            BeginOnUIThread(() => ApplyStatus(update.Status));
        }

        public void StartPause()
        {
            if (_channelRipService.GetStatus().IsRunning)
            {
                _channelRipService.Pause();
            }
            else
            {
                _channelRipService.Start();
            }

            Refresh();
        }

        public async Task PickRootAsync()
        {
            await _channelRipService.PickRipRootFolderAsync();
            Refresh();
        }

        public async Task PickBackupAsync()
        {
            await _channelRipService.PickLedgerBackupFolderAsync();
            Refresh();
        }

        public async Task AddTargetAsync()
        {
            var chat = await PickTargetChatAsync();
            if (chat == null)
            {
                return;
            }

            chat = await EnsureChatAsync(chat);

            if (IsForumLike(chat))
            {
                await _channelRipService.AddTargetAsync(chat.Id);

                var popup = new ChannelRipTargetOptionsPopup(ClientService, Aggregator, chat, null);
                await Task.Delay(50);
                var confirm = await ShowPopupAsync(popup);
                if (confirm == ContentDialogResult.Primary)
                {
                    await _channelRipService.UpdateTargetOptionsAsync(chat.Id, popup.SelectedTopicIds, popup.SelectedMediaKinds);
                }
            }
            else
            {
                await _channelRipService.AddTargetAsync(chat.Id);
                var popup = new ChannelRipTargetOptionsPopup(ClientService, Aggregator, chat, _channelRipService.GetTargets().FirstOrDefault(x => x.ChatId == chat.Id));
                await Task.Delay(50);
                var confirm = await ShowPopupAsync(popup);
                if (confirm == ContentDialogResult.Primary)
                {
                    await _channelRipService.UpdateTargetOptionsAsync(chat.Id, popup.SelectedTopicIds, popup.SelectedMediaKinds);
                }
            }

            Refresh();
        }

        public async Task<Chat> PickTargetChatAsync()
        {
            var options = new ChooseChatsOptions
            {
                AllowChannelChats = true,
                AllowGroupChats = true,
                AllowBotChats = false,
                AllowUserChats = false,
                AllowSecretChats = false,
                AllowSelf = false,
                CanPostMessages = false,
                CanInviteUsers = false,
                CanShareContact = false,
                Mode = ChooseChatsMode.Chats
            };

            return await ChooseChatsPopup.PickChatAsync(NavigationService, Strings.ChannelRipperAddTarget, options);
        }

        public async Task<Chat> EnsureChatAsync(Chat chat)
        {
            if (chat == null)
            {
                return null;
            }

            ClientService.LoadFullInfo(chat);
            await Task.Yield();

            var refreshed = await ClientService.SendAsync(new GetChat(chat.Id)) as Chat;
            return refreshed ?? (ClientService.TryGetChat(chat.Id, out var cached) ? cached : chat);
        }

        public bool IsForumChat(Chat chat)
        {
            return IsForumLike(chat);
        }

        public async Task<bool> AddTargetByIdAsync(long chatId)
        {
            var result = await _channelRipService.AddTargetAsync(chatId);
            Refresh();
            return result;
        }

        public async Task<bool> UpdateTargetOptionsAsync(long chatId, System.Collections.Generic.IReadOnlyList<int> topicIds, ChannelRipMediaKind mediaKinds)
        {
            var result = await _channelRipService.UpdateTargetOptionsAsync(chatId, topicIds, mediaKinds);
            Refresh();
            return result;
        }

        public Task<System.Collections.Generic.IReadOnlyList<ChannelRipTopicChoice>> GetTopicChoicesAsync(long chatId)
        {
            return _channelRipService.GetTopicChoicesAsync(chatId);
        }

        public async Task RefreshTopicsAsync(ChannelRipTargetViewModel target)
        {
            await _channelRipService.RefreshTopicChoicesAsync(target.Target.ChatId);
            Refresh();
        }

        public void ToggleTarget(ChannelRipTargetViewModel target)
        {
            if (target.Target.IsEnabled)
            {
                _channelRipService.DisableTarget(target.Target.ChatId);
            }
            else
            {
                _channelRipService.EnableTarget(target.Target.ChatId);
            }

            Refresh();
        }

        public void RemoveTarget(ChannelRipTargetViewModel target)
        {
            _channelRipService.RemoveTarget(target.Target.ChatId);
            Refresh();
        }

        public void ResetTarget(ChannelRipTargetViewModel target)
        {
            _channelRipService.ResetTargetLedger(target.Target.ChatId);
            Refresh();
        }

        public async Task RemoveTopicAsync(ChannelRipTargetViewModel target, int topicId)
        {
            var remaining = (target.Target.SelectedTopicIds ?? new System.Collections.Generic.List<int>())
                .Where(x => x != topicId)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (remaining.Count == 0)
            {
                _channelRipService.RemoveTarget(target.Target.ChatId);
            }
            else
            {
                var mediaKinds = target.Target.MediaKinds == 0 ? ChannelRipMediaKind.All : target.Target.MediaKinds;
                await _channelRipService.UpdateTargetOptionsAsync(target.Target.ChatId, remaining, mediaKinds);
            }

            Refresh();
        }

        public async Task AddTopicAsync(ChannelRipTargetViewModel target, int topicId)
        {
            var updated = (target.Target.SelectedTopicIds ?? new System.Collections.Generic.List<int>())
                .Append(topicId)
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var mediaKinds = target.Target.MediaKinds == 0 ? ChannelRipMediaKind.All : target.Target.MediaKinds;
            await _channelRipService.UpdateTargetOptionsAsync(target.Target.ChatId, updated, mediaKinds);
            Refresh();
        }

        public void ToggleExpanded(ChannelRipTargetViewModel target)
        {
            if (target == null)
            {
                return;
            }

            target.IsExpanded = !target.IsExpanded;
            if (target.IsExpanded)
            {
                _expandedTargetIds.Add(target.Target.ChatId);
            }
            else
            {
                _expandedTargetIds.Remove(target.Target.ChatId);
            }
        }

        public void SetTopicFilter(ChannelRipTargetViewModel target, string value)
        {
            if (target == null)
            {
                return;
            }

            value = value?.Trim() ?? string.Empty;
            target.TopicFilterText = value;

            if (string.IsNullOrWhiteSpace(value))
            {
                _topicFilters.Remove(target.Target.ChatId);
            }
            else
            {
                _topicFilters[target.Target.ChatId] = value;
            }
        }

        public async Task EditTargetAsync(ChannelRipTargetViewModel target)
        {
            if (!ClientService.TryGetChat(target.Target.ChatId, out var chat))
            {
                return;
            }

            ClientService.LoadFullInfo(chat);
            await Task.Yield();
            if (ClientService.TryGetChat(target.Target.ChatId, out var refreshed))
            {
                chat = refreshed;
            }

            var popup = new ChannelRipTargetOptionsPopup(ClientService, Aggregator, chat, target.Target);
            await Task.Delay(50);
            var confirm = await ShowPopupAsync(popup);
            if (confirm != ContentDialogResult.Primary)
            {
                return;
            }

            await _channelRipService.UpdateTargetOptionsAsync(chat.Id, popup.SelectedTopicIds, popup.SelectedMediaKinds);
            Refresh();
        }

        public async Task OpenTargetFolderAsync(ChannelRipTargetViewModel target)
        {
            await _channelRipService.OpenTargetFolderAsync(target.Target.ChatId);
        }

        public void SetLayoutMode(ChannelRipLayoutMode mode)
        {
            _channelRipService.SetLayoutMode(mode);
            Refresh();
        }

        public void SetDedupeMode(ChannelRipDedupeMode mode)
        {
            _channelRipService.SetDedupeMode(mode);
            Refresh();
        }

        public void Refresh()
        {
            ApplyStatus(_channelRipService.GetStatus());
        }

        public void SetInlineEditMode(bool isActive)
        {
            _suppressItemRefresh = isActive;
            if (!isActive)
            {
                Refresh();
            }
        }

        private void ApplyStatus(ChannelRipStatus status)
        {
            _lastStatus = status;
            IsRunning = status.IsRunning;
            Status = $"Queue: {status.QueueCount}  Active: {status.ActiveWorkers}  Downloaded: {status.TotalDownloaded}  Skipped: {status.TotalSkipped}  Failed: {status.TotalFailed}";

            if (!_suppressItemRefresh)
            {
                var orderedTargets = OrderTargets(status.Targets).ToList();
                var byChatId = Items.ToDictionary(x => x.Target.ChatId, x => x);
                var desiredIds = new HashSet<long>(orderedTargets.Select(x => x.ChatId));

                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (!desiredIds.Contains(Items[i].Target.ChatId))
                    {
                        Items.RemoveAt(i);
                    }
                }

                for (int i = 0; i < orderedTargets.Count; i++)
                {
                    var target = orderedTargets[i];
                    if (byChatId.TryGetValue(target.ChatId, out var existing))
                    {
                        if (existing.Target.IsEnabled != target.IsEnabled ||
                            existing.Target.QueuedCount != target.QueuedCount ||
                            existing.Target.ActiveCount != target.ActiveCount ||
                            existing.Target.DownloadedCount != target.DownloadedCount ||
                            existing.Target.SkippedCount != target.SkippedCount ||
                            existing.Target.FailedCount != target.FailedCount ||
                            existing.Target.IsBackfillRunning != target.IsBackfillRunning ||
                            existing.Target.LastError != target.LastError ||
                            existing.Target.LastLiveUnixTime != target.LastLiveUnixTime ||
                            existing.Target.LastBackfillUnixTime != target.LastBackfillUnixTime ||
                            existing.Target.TitleSnapshot != target.TitleSnapshot ||
                            existing.Target.MediaKinds != target.MediaKinds ||
                            !SameTopics(existing.Target.SelectedTopicIds, target.SelectedTopicIds) ||
                            !SameKnownTopics(existing.Target.KnownTopics, target.KnownTopics))
                        {
                            existing.UpdateTarget(target);
                        }

                        var currentIndex = Items.IndexOf(existing);
                        if (currentIndex != i && currentIndex >= 0)
                        {
                            Items.Move(currentIndex, i);
                        }
                    }
                    else
                    {
                        var viewModel = new ChannelRipTargetViewModel(
                            ClientService,
                            target,
                            _expandedTargetIds.Contains(target.ChatId),
                            _topicFilters.TryGetValue(target.ChatId, out var topicFilter) ? topicFilter : string.Empty);
                        Items.Insert(i, viewModel);
                    }
                }
            }

            RootPath = string.IsNullOrWhiteSpace(status.RootFolderPath) ? "Root: (not set)" : $"Root: {status.RootFolderPath}";
            BackupPath = string.IsNullOrWhiteSpace(status.LedgerBackupFolderPath) ? "Ledger backup: (not set)" : $"Ledger backup: {status.LedgerBackupFolderPath}";
            LastError = string.IsNullOrWhiteSpace(status.LastError) ? string.Empty : $"Last error: {status.LastError}";
            SelectedDedupeMode = DedupeModes.FirstOrDefault(x => x.Value == status.DedupeMode) ?? DedupeModes.FirstOrDefault();
            SelectedLayoutMode = LayoutModes.FirstOrDefault(x => x.Value == status.LayoutMode) ?? LayoutModes.FirstOrDefault();
            SelectedSortMode ??= SortModes.FirstOrDefault();
        }

        private bool IsForumLike(Chat chat)
        {
            return chat != null && (chat.ViewAsTopics || ClientService.IsForum(chat));
        }

        private static bool SameTopics(System.Collections.Generic.IReadOnlyList<int> first, System.Collections.Generic.IReadOnlyList<int> second)
        {
            first ??= Array.Empty<int>();
            second ??= Array.Empty<int>();

            if (first.Count != second.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameKnownTopics(System.Collections.Generic.IReadOnlyList<ChannelRipTopicChoice> first, System.Collections.Generic.IReadOnlyList<ChannelRipTopicChoice> second)
        {
            first ??= Array.Empty<ChannelRipTopicChoice>();
            second ??= Array.Empty<ChannelRipTopicChoice>();

            if (first.Count != second.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                if (first[i]?.Id != second[i]?.Id || first[i]?.Name != second[i]?.Name || first[i]?.UnreadCount != second[i]?.UnreadCount)
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<ChannelRipTarget> OrderTargets(IEnumerable<ChannelRipTarget> targets)
        {
            var query = targets ?? Enumerable.Empty<ChannelRipTarget>();

            if (ShowActiveOnly)
            {
                query = query.Where(x => x.IsEnabled || x.ActiveCount > 0 || x.QueuedCount > 0 || x.IsBackfillRunning);
            }

            var sortMode = SelectedSortMode?.Value ?? ChannelRipSortMode.ActivityFirst;

            return sortMode switch
            {
                ChannelRipSortMode.Name => query
                    .OrderBy(x => x.TitleSnapshot, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.ChatId),
                ChannelRipSortMode.QueueSize => query
                    .OrderByDescending(x => x.QueuedCount + x.ActiveCount)
                    .ThenByDescending(x => x.DownloadedCount)
                    .ThenBy(x => x.TitleSnapshot, StringComparer.CurrentCultureIgnoreCase),
                ChannelRipSortMode.RecentlyUpdated => query
                    .OrderByDescending(x => Math.Max(x.LastLiveUnixTime, x.LastBackfillUnixTime))
                    .ThenByDescending(x => x.ActiveCount)
                    .ThenBy(x => x.TitleSnapshot, StringComparer.CurrentCultureIgnoreCase),
                _ => query
                    .OrderByDescending(x => x.ActiveCount > 0 || x.QueuedCount > 0 || x.IsBackfillRunning)
                    .ThenByDescending(x => x.ActiveCount)
                    .ThenByDescending(x => x.QueuedCount)
                    .ThenByDescending(x => x.IsEnabled)
                    .ThenBy(x => x.TitleSnapshot, StringComparer.CurrentCultureIgnoreCase)
            };
        }

        private void ReapplyStatus()
        {
            if (_lastStatus != null)
            {
                ApplyStatus(_lastStatus);
            }
        }
    }

    public enum ChannelRipSortMode
    {
        ActivityFirst,
        Name,
        QueueSize,
        RecentlyUpdated
    }

    public sealed class ChannelRipChoiceItem<T>
    {
        public ChannelRipChoiceItem(string label, T value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public T Value { get; }
    }

    public sealed class ChannelRipTargetViewModel : INotifyPropertyChanged
    {
        private readonly IClientService _clientService;
        private ChannelRipTarget _target;

        public ChannelRipTargetViewModel(IClientService clientService, ChannelRipTarget target, bool isExpanded, string topicFilterText)
        {
            _clientService = clientService;
            _target = target;
            _isExpanded = isExpanded;
            _topicFilterText = topicFilterText ?? string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ChannelRipTarget Target => _target;

        public string Title => string.IsNullOrWhiteSpace(Target.TitleSnapshot)
            ? Target.ChatId.ToString()
            : Target.TitleSnapshot;

        public string Details
        {
            get
            {
                return $"{TopicSummary}  |  Media: {MediaSummary}";
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    RaisePropertyChanged(nameof(IsExpanded));
                    RaisePropertyChanged(nameof(ExpandButtonText));
                    RaisePropertyChanged(nameof(ExpandGlyph));
                    RaisePropertyChanged(nameof(TopicPanelVisibility));
                }
            }
        }

        private string _topicFilterText;
        public string TopicFilterText
        {
            get => _topicFilterText;
            set
            {
                var normalized = value ?? string.Empty;
                if (_topicFilterText != normalized)
                {
                    _topicFilterText = normalized;
                    RaisePropertyChanged(nameof(TopicFilterText));
                    RaisePropertyChanged(nameof(FilteredTopicItems));
                    RaisePropertyChanged(nameof(FilteredAvailableTopicItems));
                    RaisePropertyChanged(nameof(HasVisibleSelectedTopics));
                    RaisePropertyChanged(nameof(HasVisibleAvailableTopics));
                    RaisePropertyChanged(nameof(TopicStatusHint));
                }
            }
        }

        public string State => Target.IsEnabled ? Strings.ChannelRipperEnabled : Strings.ChannelRipperDisabled;

        public string ProgressSummary => $"Queue: {Target.QueuedCount}  Active: {Target.ActiveCount}  Downloaded: {Target.DownloadedCount}  Skipped: {Target.SkippedCount}  Failed: {Target.FailedCount}";

        public double ProgressValue => Target.DownloadedCount + Target.SkippedCount + Target.FailedCount;

        public double ProgressMax
        {
            get
            {
                var remaining = Target.QueuedCount + Target.ActiveCount;
                var processed = Target.DownloadedCount + Target.SkippedCount + Target.FailedCount;
                var total = remaining + processed;
                return Math.Max(1, total);
            }
        }

        public bool IsBackfillRunning => Target.IsBackfillRunning;

        public string BackfillStateText => Target.IsBackfillRunning ? "Backfill running" : "Backfill idle";

        public bool HasError => !string.IsNullOrWhiteSpace(Target.LastError);

        public string ErrorText => string.IsNullOrWhiteSpace(Target.LastError) ? string.Empty : $"Last error: {Target.LastError}";

        public bool CanEditInPopup
        {
            get
            {
                return !_clientService.TryGetChat(Target.ChatId, out var chat) || !(chat.ViewAsTopics || _clientService.IsForum(chat));
            }
        }

        public string EditButtonText => CanEditInPopup ? "Edit" : "Edit in topic";

        public bool HasSelectedTopics => Target.SelectedTopicIds != null && Target.SelectedTopicIds.Count > 0;

        public bool IsForumTarget
        {
            get
            {
                if (_clientService.TryGetChat(Target.ChatId, out var chat))
                {
                    return chat.ViewAsTopics || _clientService.IsForum(chat);
                }

                return Target.KnownTopics != null && Target.KnownTopics.Count > 0 || HasSelectedTopics;
            }
        }

        public ObservableCollection<ChannelRipTopicItemViewModel> TopicItems => new ObservableCollection<ChannelRipTopicItemViewModel>(
            (Target.SelectedTopicIds ?? Enumerable.Empty<int>())
                .Distinct()
                .OrderBy(x => x)
                .Select(x => new ChannelRipTopicItemViewModel(this, x, GetTopicName(x))));

        public bool HasAvailableTopics => AvailableTopicItems.Count > 0;

        public ObservableCollection<ChannelRipTopicItemViewModel> AvailableTopicItems => new ObservableCollection<ChannelRipTopicItemViewModel>(
            (Target.KnownTopics ?? Enumerable.Empty<ChannelRipTopicChoice>())
                .Where(x => x != null && x.Id > 0 && !(Target.SelectedTopicIds ?? Enumerable.Empty<int>()).Contains(x.Id))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new ChannelRipTopicItemViewModel(this, x.Id, x.Name)));

        public ObservableCollection<ChannelRipTopicItemViewModel> FilteredTopicItems => new ObservableCollection<ChannelRipTopicItemViewModel>(
            TopicItems.Where(MatchesTopicFilter));

        public ObservableCollection<ChannelRipTopicItemViewModel> FilteredAvailableTopicItems => new ObservableCollection<ChannelRipTopicItemViewModel>(
            AvailableTopicItems.Where(MatchesTopicFilter));

        public bool HasVisibleSelectedTopics => FilteredTopicItems.Count > 0;

        public bool HasVisibleAvailableTopics => FilteredAvailableTopicItems.Count > 0;

        public string TopicSummary
        {
            get
            {
                if (!IsForumTarget)
                {
                    return Target.SelectedTopicIds != null && Target.SelectedTopicIds.Count > 0
                        ? string.Format(Strings.ChannelRipperTopicsSelected, string.Join(", ", Target.SelectedTopicIds.Select(GetTopicName)))
                        : Strings.ChannelRipperTopicsAll;
                }

                var selectedCount = (Target.SelectedTopicIds ?? Enumerable.Empty<int>()).Distinct().Count();
                var availableCount = (Target.KnownTopics ?? Enumerable.Empty<ChannelRipTopicChoice>())
                    .Count(x => x != null && x.Id > 0 && !(Target.SelectedTopicIds ?? Enumerable.Empty<int>()).Contains(x.Id));

                if (selectedCount == 0)
                {
                    return availableCount > 0 ? $"All topics enabled  |  {availableCount} cached topics" : "All topics enabled";
                }

                return selectedCount == 1
                    ? $"1 selected topic  |  {availableCount} more available"
                    : $"{selectedCount} selected topics  |  {availableCount} more available";
            }
        }

        public string ExpandButtonText => IsExpanded ? "Hide topics" : "Show topics";

        public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE70E";

        public Windows.UI.Xaml.Visibility TopicPanelVisibility => IsForumTarget && IsExpanded
            ? Windows.UI.Xaml.Visibility.Visible
            : Windows.UI.Xaml.Visibility.Collapsed;

        public string TopicModeHint => HasSelectedTopics
            ? "Selected topics"
            : "All topics enabled. Adding a topic here will switch this target to specific-topic mode.";

        public string TopicStatusHint
        {
            get
            {
                if (!IsForumTarget)
                {
                    return string.Empty;
                }

                if (HasVisibleAvailableTopics || HasVisibleSelectedTopics)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(TopicFilterText))
                {
                    return "No topics match the current filter.";
                }

                return "No cached topics yet. Click Refresh topics to load them for this forum target.";
            }
        }

        public void UpdateTarget(ChannelRipTarget target)
        {
            _target = target;
            RaisePropertyChanged(nameof(Target));
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(Details));
            RaisePropertyChanged(nameof(TopicSummary));
            RaisePropertyChanged(nameof(State));
            RaisePropertyChanged(nameof(ProgressSummary));
            RaisePropertyChanged(nameof(ProgressValue));
            RaisePropertyChanged(nameof(ProgressMax));
            RaisePropertyChanged(nameof(IsBackfillRunning));
            RaisePropertyChanged(nameof(BackfillStateText));
            RaisePropertyChanged(nameof(HasError));
            RaisePropertyChanged(nameof(ErrorText));
            RaisePropertyChanged(nameof(CanEditInPopup));
            RaisePropertyChanged(nameof(EditButtonText));
            RaisePropertyChanged(nameof(HasSelectedTopics));
            RaisePropertyChanged(nameof(TopicItems));
            RaisePropertyChanged(nameof(FilteredTopicItems));
            RaisePropertyChanged(nameof(IsForumTarget));
            RaisePropertyChanged(nameof(HasAvailableTopics));
            RaisePropertyChanged(nameof(HasVisibleSelectedTopics));
            RaisePropertyChanged(nameof(HasVisibleAvailableTopics));
            RaisePropertyChanged(nameof(AvailableTopicItems));
            RaisePropertyChanged(nameof(FilteredAvailableTopicItems));
            RaisePropertyChanged(nameof(TopicModeHint));
            RaisePropertyChanged(nameof(TopicStatusHint));
            RaisePropertyChanged(nameof(TopicPanelVisibility));
            RaisePropertyChanged(nameof(ExpandButtonText));
            RaisePropertyChanged(nameof(ExpandGlyph));
        }

        private string MediaSummary
        {
            get
            {
                var kinds = Target.MediaKinds == 0 ? ChannelRipMediaKind.All : Target.MediaKinds;
                var values = new[]
                {
                    (ChannelRipMediaKind.Video, "videos"),
                    (ChannelRipMediaKind.Photo, "photos"),
                    (ChannelRipMediaKind.Animation, "animations"),
                    (ChannelRipMediaKind.VideoNote, "video notes"),
                    (ChannelRipMediaKind.VideoDocument, "video docs")
                }.Where(x => kinds.HasFlag(x.Item1)).Select(x => x.Item2).ToList();

                return values.Count == 0 ? "all" : string.Join(", ", values);
            }
        }

        private string GetTopicName(int topicId)
        {
            return _clientService.TryGetForumTopic(Target.ChatId, topicId, out var topic)
                ? topic.Info.Name
                : topicId.ToString();
        }

        private bool MatchesTopicFilter(ChannelRipTopicItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(TopicFilterText))
            {
                return true;
            }

            return item.Name?.IndexOf(TopicFilterText, StringComparison.CurrentCultureIgnoreCase) >= 0
                || item.TopicId.ToString().IndexOf(TopicFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ChannelRipTopicItemViewModel
    {
        public ChannelRipTopicItemViewModel(ChannelRipTargetViewModel owner, int topicId, string name)
        {
            Owner = owner;
            TopicId = topicId;
            Name = name;
        }

        public ChannelRipTargetViewModel Owner { get; }
        public int TopicId { get; }
        public string Name { get; }
        public string Label => $"{Name} ({TopicId})";
    }
}



