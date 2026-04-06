
//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Telegram.Services.Updates;
using Telegram.Td.Api;
using Windows.Storage;
using Windows.System;
using SAP = Windows.Storage.AccessCache.StorageApplicationPermissions;

namespace Telegram.Services
{
    public interface IChannelRipService
    {
        IReadOnlyList<ChannelRipTarget> GetTargets();
        Task<bool> AddTargetAsync(long chatId);
        Task<bool> AddTargetWithTopicsAsync(long chatId, IReadOnlyList<int> forumTopicIds);
        Task<IReadOnlyList<ChannelRipTopicChoice>> GetTopicChoicesAsync(long chatId);
        Task<IReadOnlyList<ChannelRipTopicChoice>> RefreshTopicChoicesAsync(long chatId);
        Task<bool> UpdateTargetOptionsAsync(long chatId, IReadOnlyList<int> forumTopicIds, ChannelRipMediaKind mediaKinds);
        Task<bool> OpenTargetFolderAsync(long chatId);
        void EnableTarget(long chatId);
        void DisableTarget(long chatId);
        void RemoveTarget(long chatId);
        void ResetTargetLedger(long chatId);
        Task<bool> PickRipRootFolderAsync();
        Task<bool> PickLedgerBackupFolderAsync();
        void SetLayoutMode(ChannelRipLayoutMode mode);
        void SetDedupeMode(ChannelRipDedupeMode mode);
        void Start();
        void Pause();
        ChannelRipStatus GetStatus();
    }

    [Flags]
    public enum ChannelRipMediaKind
    {
        Photo = 1,
        Video = 2,
        Animation = 4,
        VideoNote = 8,
        VideoDocument = 16,
        All = Photo | Video | Animation | VideoNote | VideoDocument
    }

    public enum ChannelRipDedupeMode
    {
        Global = 0,
        PerChat = 1,
        PerTopic = 2
    }

    public enum ChannelRipLayoutMode
    {
        ChannelTopicDate = 0,
        ChannelTopic = 1,
        ChannelOnly = 2
    }

    public sealed class ChannelRipTarget
    {
        public long ChatId { get; set; }
        public string TitleSnapshot { get; set; }
        public bool IsEnabled { get; set; }
        public List<int> SelectedTopicIds { get; set; } = new();
        public List<ChannelRipTopicChoice> KnownTopics { get; set; } = new();
        public ChannelRipMediaKind MediaKinds { get; set; } = ChannelRipMediaKind.All;
        public Dictionary<string, long> LastSeenMessageIdByScope { get; set; } = new();
        public string LastError { get; set; }
        public long LastBackfillUnixTime { get; set; }
        public long LastLiveUnixTime { get; set; }
        public bool IsBackfillRunning { get; set; }
        public int QueuedCount { get; set; }
        public int ActiveCount { get; set; }
        public int DownloadedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
    }

    public sealed class ChannelRipStatus
    {
        public bool IsRunning { get; set; }
        public int QueueCount { get; set; }
        public int ActiveWorkers { get; set; }
        public int TotalDownloaded { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalFailed { get; set; }
        public string LastError { get; set; }
        public string RootFolderPath { get; set; }
        public string LedgerBackupFolderPath { get; set; }
        public bool UseFlatLayout { get; set; }
        public ChannelRipLayoutMode LayoutMode { get; set; }
        public ChannelRipDedupeMode DedupeMode { get; set; }
        public IReadOnlyList<ChannelRipTarget> Targets { get; set; } = Array.Empty<ChannelRipTarget>();
    }

    public sealed class ChannelRipTopicChoice
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int UnreadCount { get; set; }
    }

    public sealed class ChannelRipLedgerEntry
    {
        public string DedupeKey { get; set; }
        public string UniqueId { get; set; }
        public long ChatId { get; set; }
        public long MessageId { get; set; }
        public int FileId { get; set; }
        public string FilePath { get; set; }
        public long FirstSeenUnixTime { get; set; }
    }

    internal sealed class ChannelRipSettings
    {
        public bool IsEnabled { get; set; }
        public int WorkerCount { get; set; } = 4;
        public int RetryCount { get; set; } = 5;
        public string RootFolderToken { get; set; }
        public string RootFolderPath { get; set; }
        public string LedgerBackupFolderToken { get; set; }
        public string LedgerBackupFolderPath { get; set; }
        public bool UseFlatLayout { get; set; }
        public ChannelRipLayoutMode LayoutMode { get; set; }
        public ChannelRipDedupeMode DedupeMode { get; set; }
        public List<ChannelRipTarget> Targets { get; set; } = new();
    }

    internal sealed class ChannelRipWorkItem
    {
        public ChannelRipTarget Target { get; init; }
        public Message Message { get; init; }
        public int? TopicId { get; init; }
        public string ScopeKey { get; init; }
    }

    public sealed class ChannelRipService : IChannelRipService
    {
        private const string RootTokenPrefix = "ChannelRipRoot_";
        private const string BackupTokenPrefix = "ChannelRipBackup_";
        private const int MaxQueueCapacity = 2048;

        private static readonly HashSet<string> VideoDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".3gp",
            ".asf",
            ".avi",
            ".m2ts",
            ".m4v",
            ".mkv",
            ".mov",
            ".mp4",
            ".mpeg",
            ".mpg",
            ".mts",
            ".ts",
            ".webm",
            ".wmv"
        };

        private readonly IClientService _clientService;
        private readonly IEventAggregator _aggregator;
        private readonly object _syncLock = new();
        private readonly SemaphoreSlim _storageLock = new(1, 1);

        private readonly Dictionary<string, ChannelRipLedgerEntry> _ledger = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingMessages = new(StringComparer.Ordinal);

        private ChannelRipSettings _settings = new();
        private ChannelRipStatus _status = new();

        private Channel<ChannelRipWorkItem> _queue;
        private CancellationTokenSource _cts;
        private Task[] _workers = Array.Empty<Task>();
        private readonly Task _initializeTask;

        private string _settingsPath;
        private string _ledgerPath;

        public ChannelRipService(IClientService clientService, IEventAggregator aggregator)
        {
            _clientService = clientService;
            _aggregator = aggregator;

            _aggregator.Subscribe<UpdateNewMessage>(this, Handle);
            _initializeTask = InitializeAsync();
        }

        public IReadOnlyList<ChannelRipTarget> GetTargets()
        {
            lock (_syncLock)
            {
                return _settings.Targets.Select(CloneTarget).ToList();
            }
        }

        public async Task<bool> AddTargetAsync(long chatId)
        {
            await EnsureInitializedAsync();

            _clientService.TryGetChat(chatId, out var chat);
            var title = chat != null ? _clientService.GetTitle(chat) : _clientService.GetTitle(chatId);
            var changed = false;

            lock (_syncLock)
            {
                var existing = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (existing != null)
                {
                    existing.TitleSnapshot = string.IsNullOrWhiteSpace(title) ? existing.TitleSnapshot : title;
                    existing.IsEnabled = true;
                    existing.MediaKinds = existing.MediaKinds == 0 ? ChannelRipMediaKind.All : existing.MediaKinds;
                    existing.LastBackfillUnixTime = 0;
                    existing.LastError = null;
                    changed = true;
                }
                else
                {
                    _settings.Targets.Add(new ChannelRipTarget
                    {
                        ChatId = chatId,
                        TitleSnapshot = string.IsNullOrWhiteSpace(title) ? chatId.ToString() : title,
                        IsEnabled = true,
                        MediaKinds = ChannelRipMediaKind.All,
                        LastBackfillUnixTime = 0
                    });
                    changed = true;
                }
            }

            if (changed)
            {
                await PersistSettingsAsync();
                PublishStatus();
            }

            if (chat != null && IsForumLike(chat))
            {
                _ = WarmTopicsAsync(chatId);
            }

            return true;
        }

        public async Task<bool> AddTargetWithTopicsAsync(long chatId, IReadOnlyList<int> forumTopicIds)
        {
            await EnsureInitializedAsync();

            _clientService.TryGetChat(chatId, out var chat);
            var title = chat != null ? _clientService.GetTitle(chat) : _clientService.GetTitle(chatId);

            lock (_syncLock)
            {
                var existing = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (existing != null)
                {
                    existing.SelectedTopicIds = forumTopicIds?.Distinct().ToList() ?? new List<int>();
                    existing.IsEnabled = true;
                    existing.MediaKinds = existing.MediaKinds == 0 ? ChannelRipMediaKind.All : existing.MediaKinds;
                    existing.LastBackfillUnixTime = 0;
                    existing.LastError = null;
                }
                else
                {
                    _settings.Targets.Add(new ChannelRipTarget
                    {
                        ChatId = chatId,
                        TitleSnapshot = string.IsNullOrWhiteSpace(title) ? chatId.ToString() : title,
                        IsEnabled = true,
                        SelectedTopicIds = forumTopicIds?.Distinct().ToList() ?? new List<int>(),
                        MediaKinds = ChannelRipMediaKind.All,
                        LastBackfillUnixTime = 0
                    });
                }
            }

            await PersistSettingsAsync();
            PublishStatus();
            if (chat != null && IsForumLike(chat))
            {
                _ = WarmTopicsAsync(chatId);
            }
            return true;
        }

        public async Task<bool> UpdateTargetOptionsAsync(long chatId, IReadOnlyList<int> forumTopicIds, ChannelRipMediaKind mediaKinds)
        {
            await EnsureInitializedAsync();

            _clientService.TryGetChat(chatId, out var chat);

            var normalized = mediaKinds == 0 ? ChannelRipMediaKind.All : mediaKinds;
            var found = false;

            lock (_syncLock)
            {
                var existing = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (existing != null)
                {
                    found = true;
                    existing.TitleSnapshot = chat != null ? _clientService.GetTitle(chat) : (_clientService.GetTitle(chatId) ?? existing.TitleSnapshot);
                    existing.MediaKinds = normalized;
                    if (chat != null && IsForumLike(chat))
                    {
                        existing.SelectedTopicIds = forumTopicIds?.Distinct().ToList() ?? new List<int>();
                    }

                    existing.LastError = null;
                    existing.LastBackfillUnixTime = 0;
                }
            }

            if (!found)
            {
                return false;
            }

            await PersistSettingsAsync();
            PublishStatus();
            if (chat != null && IsForumLike(chat))
            {
                _ = WarmTopicsAsync(chatId);
            }
            return true;
        }

        public async Task<IReadOnlyList<ChannelRipTopicChoice>> GetTopicChoicesAsync(long chatId)
        {
            await EnsureInitializedAsync();

            lock (_syncLock)
            {
                var target = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (target?.KnownTopics != null && target.KnownTopics.Count > 0)
                {
                    return (IReadOnlyList<ChannelRipTopicChoice>)target.KnownTopics
                        .Select(CloneTopicChoice)
                        .ToList();
                }
            }

            return Array.Empty<ChannelRipTopicChoice>();
        }

        public async Task<IReadOnlyList<ChannelRipTopicChoice>> RefreshTopicChoicesAsync(long chatId)
        {
            await EnsureInitializedAsync();

            var items = await DiscoverTopicChoicesAsync(chatId, CancellationToken.None);

            lock (_syncLock)
            {
                if (_settings.Targets.FirstOrDefault(x => x.ChatId == chatId) is { } target)
                {
                    target.KnownTopics = items.Select(CloneTopicChoice).ToList();
                }
            }

            await PersistSettingsAsync();
            PublishStatus();
            return items.Select(CloneTopicChoice).ToList();
        }

        private async Task WarmTopicsAsync(long chatId)
        {
            try
            {
                var items = await DiscoverTopicChoicesAsync(chatId, CancellationToken.None);

                lock (_syncLock)
                {
                    if (_settings.Targets.FirstOrDefault(x => x.ChatId == chatId) is { } target)
                    {
                        target.KnownTopics = items.Select(CloneTopicChoice).ToList();
                    }
                }

                await PersistSettingsAsync();
                PublishStatus();
            }
            catch
            {
            }
        }

        private async Task<List<ChannelRipTopicChoice>> DiscoverTopicChoicesAsync(long chatId, CancellationToken token)
        {
            _clientService.TryGetChat(chatId, out var chat);
            if (chat == null)
            {
                chat = await _clientService.SendAsync(new GetChat(chatId)) as Chat;
            }

            if (chat != null)
            {
                _clientService.LoadFullInfo(chat);
            }

            if (chat == null || !IsForumLike(chat))
            {
                return new List<ChannelRipTopicChoice>();
            }

            var items = new List<ChannelRipTopicChoice>();
            var seen = new HashSet<int>();
            var offset = 0;

            while (!token.IsCancellationRequested && items.Count < 200)
            {
                var response = await _clientService.GetForumTopicsAsync(chatId, offset, 100);
                if (response is not ForumTopics2 topics || topics.TopicIds.Count == 0)
                {
                    break;
                }

                foreach (var topic in _clientService.GetForumTopics(chatId, topics.TopicIds))
                {
                    var topicId = topic?.Info?.ForumTopicId ?? 0;
                    if (topicId <= 0 || topicId == ForumTopicService.GeneralId || !seen.Add(topicId))
                    {
                        continue;
                    }

                    items.Add(new ChannelRipTopicChoice
                    {
                        Id = topicId,
                        Name = topic.Info.Name,
                        UnreadCount = topic.UnreadCount
                    });
                }

                if (topics.TopicIds.Count < 100)
                {
                    break;
                }

                offset += topics.TopicIds.Count;
            }

            return items;
        }

        public async Task<bool> OpenTargetFolderAsync(long chatId)
        {
            await EnsureInitializedAsync();

            ChannelRipTarget target;
            lock (_syncLock)
            {
                target = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
            }

            if (target == null)
            {
                return false;
            }

            var folder = await ResolveTargetBrowseFolderAsync(target);
            if (folder == null)
            {
                return false;
            }

            return await Launcher.LaunchFolderAsync(folder);
        }

        public void EnableTarget(long chatId)
        {
            lock (_syncLock)
            {
                var target = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (target != null)
                {
                    target.IsEnabled = true;
                    target.LastError = null;
                    target.LastBackfillUnixTime = 0;
                }
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void DisableTarget(long chatId)
        {
            lock (_syncLock)
            {
                var target = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                if (target != null)
                {
                    target.IsEnabled = false;
                    target.IsBackfillRunning = false;
                    target.QueuedCount = 0;
                    target.ActiveCount = 0;
                }
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void RemoveTarget(long chatId)
        {
            lock (_syncLock)
            {
                _settings.Targets.RemoveAll(x => x.ChatId == chatId);
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void ResetTargetLedger(long chatId)
        {
            lock (_syncLock)
            {
                if (_settings.Targets.FirstOrDefault(x => x.ChatId == chatId) is { } target)
                {
                    target.LastSeenMessageIdByScope.Clear();
                    target.LastError = null;
                    target.QueuedCount = 0;
                    target.ActiveCount = 0;
                    target.DownloadedCount = 0;
                    target.SkippedCount = 0;
                    target.FailedCount = 0;
                    target.LastBackfillUnixTime = 0;
                    target.IsBackfillRunning = false;
                }
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public async Task<bool> PickRipRootFolderAsync()
        {
            await EnsureInitializedAsync();

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return false;
            }

            var token = RootTokenPrefix + _clientService.SessionId;
            SAP.FutureAccessList.AddOrReplace(token, folder);

            lock (_syncLock)
            {
                _settings.RootFolderToken = token;
                _settings.RootFolderPath = folder.Path;
            }

            await PersistSettingsAsync();
            PublishStatus();
            return true;
        }

        public async Task<bool> PickLedgerBackupFolderAsync()
        {
            await EnsureInitializedAsync();

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return false;
            }

            var token = BackupTokenPrefix + _clientService.SessionId;
            SAP.FutureAccessList.AddOrReplace(token, folder);

            lock (_syncLock)
            {
                _settings.LedgerBackupFolderToken = token;
                _settings.LedgerBackupFolderPath = folder.Path;
            }

            await PersistSettingsAsync();
            await PersistLedgerAsync();
            PublishStatus();
            return true;
        }

        public void Start()
        {
            lock (_syncLock)
            {
                if (_status.IsRunning)
                {
                    return;
                }

                _settings.IsEnabled = true;
                _pendingMessages.Clear();
                _status.QueueCount = 0;
                _status.ActiveWorkers = 0;

                foreach (var target in _settings.Targets)
                {
                    target.IsBackfillRunning = false;
                    target.QueuedCount = 0;
                    target.ActiveCount = 0;

                    if (target.IsEnabled)
                    {
                        // Force a fresh reconciliation scan on each start so we can
                        // catch anything missed while the app was closed.
                        target.LastBackfillUnixTime = 0;
                        target.LastError = null;
                    }
                }

                _queue = Channel.CreateBounded<ChannelRipWorkItem>(new BoundedChannelOptions(MaxQueueCapacity)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
                _cts = new CancellationTokenSource();
                _status.IsRunning = true;

                var count = Math.Max(1, _settings.WorkerCount);
                _workers = Enumerable.Range(0, count)
                    .Select(_ => Task.Run(() => WorkerLoopAsync(_cts.Token)))
                    .ToArray();

                _ = Task.Run(() => ScanLoopAsync(_cts.Token));
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void Pause()
        {
            CancellationTokenSource cts;
            lock (_syncLock)
            {
                if (!_status.IsRunning)
                {
                    return;
                }

                _settings.IsEnabled = false;
                _status.IsRunning = false;
                cts = _cts;
                _cts = null;
                _queue = null;
                _workers = Array.Empty<Task>();
                _pendingMessages.Clear();
                _status.QueueCount = 0;
                _status.ActiveWorkers = 0;

                foreach (var target in _settings.Targets)
                {
                    target.IsBackfillRunning = false;
                    target.QueuedCount = 0;
                    target.ActiveCount = 0;
                }
            }

            try
            {
                cts?.Cancel();
            }
            catch { }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void SetLayoutMode(ChannelRipLayoutMode mode)
        {
            lock (_syncLock)
            {
                _settings.LayoutMode = mode;
                _settings.UseFlatLayout = mode == ChannelRipLayoutMode.ChannelOnly;
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public void SetDedupeMode(ChannelRipDedupeMode mode)
        {
            lock (_syncLock)
            {
                _settings.DedupeMode = mode;
            }

            _ = PersistSettingsAsync();
            PublishStatus();
        }

        public ChannelRipStatus GetStatus()
        {
            lock (_syncLock)
            {
                return CloneStatus(_status);
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                var stateFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ChannelRipper", CreationCollisionOption.OpenIfExists);
                _settingsPath = Path.Combine(stateFolder.Path, $"channel-ripper-settings-{_clientService.SessionId}.json");
                _ledgerPath = Path.Combine(stateFolder.Path, $"channel-ripper-ledger-{_clientService.SessionId}.json");

                await LoadSettingsAsync();
                await LoadLedgerAsync();

                if (_settings.IsEnabled)
                {
                    Start();
                }
                else
                {
                    PublishStatus();
                }
            }
            catch (Exception ex)
            {
                SetServiceError(ex.Message);
            }
        }

        private Task EnsureInitializedAsync()
        {
            return _initializeTask ?? Task.CompletedTask;
        }

        private async Task LoadSettingsAsync()
        {
            if (!System.IO.File.Exists(_settingsPath))
            {
                _settings = new ChannelRipSettings();
                return;
            }

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(_settingsPath);
                _settings = JsonSerializer.Deserialize<ChannelRipSettings>(json) ?? new ChannelRipSettings();
                if (_settings.UseFlatLayout && _settings.LayoutMode == ChannelRipLayoutMode.ChannelTopicDate)
                {
                    _settings.LayoutMode = ChannelRipLayoutMode.ChannelOnly;
                }
                foreach (var target in _settings.Targets)
                {
                    if (target.MediaKinds == 0)
                    {
                        target.MediaKinds = ChannelRipMediaKind.All;
                    }
                }
            }
            catch
            {
                _settings = new ChannelRipSettings();
            }
        }

        private async Task LoadLedgerAsync()
        {
            if (!System.IO.File.Exists(_ledgerPath))
            {
                return;
            }

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(_ledgerPath);
                var list = JsonSerializer.Deserialize<List<ChannelRipLedgerEntry>>(json) ?? new List<ChannelRipLedgerEntry>();

                lock (_syncLock)
                {
                    _ledger.Clear();
                    foreach (var item in list)
                    {
                        var key = item.DedupeKey ?? item.UniqueId;
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _ledger[key] = item;
                        }
                    }
                }
            }
            catch
            {
                await TryRecoverLedgerFromBackupAsync();
            }
        }

        private async Task TryRecoverLedgerFromBackupAsync()
        {
            var backupFolder = await ResolveBackupFolderAsync();
            if (backupFolder == null)
            {
                return;
            }

            try
            {
                var file = await backupFolder.GetFileAsync(Path.GetFileName(_ledgerPath));
                var json = await FileIO.ReadTextAsync(file);
                var list = JsonSerializer.Deserialize<List<ChannelRipLedgerEntry>>(json) ?? new List<ChannelRipLedgerEntry>();

                lock (_syncLock)
                {
                    _ledger.Clear();
                    foreach (var item in list)
                    {
                        var key = item.DedupeKey ?? item.UniqueId;
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _ledger[key] = item;
                        }
                    }
                }

                await AtomicWriteAsync(_ledgerPath, json);
            }
            catch
            {
            }
        }

        private void Handle(UpdateNewMessage update)
        {
            _ = HandleUpdateNewMessageAsync(update);
        }

        private async Task HandleUpdateNewMessageAsync(UpdateNewMessage update)
        {
            ChannelRipTarget target;

            lock (_syncLock)
            {
                if (!_status.IsRunning)
                {
                    return;
                }

                target = _settings.Targets.FirstOrDefault(x => x.ChatId == update.Message.ChatId && x.IsEnabled);
                if (target == null)
                {
                    return;
                }
            }

            var topicId = GetForumTopicId(update.Message.TopicId);
            if (target.SelectedTopicIds.Count > 0 && (!topicId.HasValue || !target.SelectedTopicIds.Contains(topicId.Value)))
            {
                return;
            }

            if (!MatchesMediaFilter(update.Message, target.MediaKinds))
            {
                return;
            }

            await EnqueueAsync(target, update.Message, topicId);

            lock (_syncLock)
            {
                target.LastLiveUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            _ = PersistSettingsAsync();
        }

        private async Task ScanLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                List<ChannelRipTarget> targets;
                lock (_syncLock)
                {
                    targets = _settings.Targets
                        .Where(x => x.IsEnabled && !x.IsBackfillRunning && x.LastBackfillUnixTime == 0)
                        .Select(CloneTarget)
                        .ToList();
                }

                if (targets.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                foreach (var target in targets)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    await ScanTargetAsync(target, token);
                }

            }
        }

        private async Task ScanTargetAsync(ChannelRipTarget target, CancellationToken token)
        {
            lock (_syncLock)
            {
                if (_settings.Targets.FirstOrDefault(x => x.ChatId == target.ChatId) is { } current)
                {
                    current.IsBackfillRunning = true;
                    current.LastError = null;
                }
            }
            PublishStatus();

            try
            {
                foreach (var scope in await BuildScopesAsync(target, token))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    await ScanScopeAsync(target.ChatId, scope.topicId, scope.scopeKey, token);
                }

                lock (_syncLock)
                {
                    if (_settings.Targets.FirstOrDefault(x => x.ChatId == target.ChatId) is { } current)
                    {
                        current.LastBackfillUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        current.LastError = null;
                    }
                }

                await PersistSettingsAsync();
            }
            catch (Exception ex)
            {
                lock (_syncLock)
                {
                    if (_settings.Targets.FirstOrDefault(x => x.ChatId == target.ChatId) is { } current)
                    {
                        current.LastError = ex.Message;
                    }
                }
            }
            finally
            {
                SetTargetBackfillState(target.ChatId, false);
                PublishStatus();
            }
        }

        private async Task<List<(int? topicId, string scopeKey)>> BuildScopesAsync(ChannelRipTarget target, CancellationToken token)
        {
            if (target.SelectedTopicIds != null && target.SelectedTopicIds.Count > 0)
            {
                return target.SelectedTopicIds
                    .Distinct()
                    .Select(topicId => ((int?)topicId, topicId.ToString()))
                    .ToList();
            }

            var hasCachedChat = _clientService.TryGetChat(target.ChatId, out var chat);
            if (!hasCachedChat)
            {
                chat = await _clientService.SendAsync(new GetChat(target.ChatId)) as Chat;
            }

            if (chat != null)
            {
                _clientService.LoadFullInfo(chat);
            }

            if (chat == null || !IsForumLike(chat))
            {
                return new List<(int? topicId, string scopeKey)> { (null, "all") };
            }

            var scopes = new List<(int? topicId, string scopeKey)> { (null, "general") };
            var topics = await DiscoverTopicChoicesAsync(target.ChatId, token);

            lock (_syncLock)
            {
                if (_settings.Targets.FirstOrDefault(x => x.ChatId == target.ChatId) is { } current)
                {
                    current.KnownTopics = topics.Select(CloneTopicChoice).ToList();
                }
            }

            foreach (var topic in topics)
            {
                scopes.Add((topic.Id, topic.Id.ToString()));
            }

            return scopes;
        }

        private async Task ScanScopeAsync(long chatId, int? topicId, string scopeKey, CancellationToken token)
        {
            var filters = new SearchMessagesFilter[]
            {
                new SearchMessagesFilterPhotoAndVideo(),
                new SearchMessagesFilterDocument(),
                new SearchMessagesFilterAnimation(),
                new SearchMessagesFilterVideoNote()
            };

            foreach (var filter in filters)
            {
                ChannelRipTarget target;
                lock (_syncLock)
                {
                    target = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
                }

                if (target == null)
                {
                    return;
                }

                await ScanScopeWithFilterAsync(target, chatId, topicId, scopeKey, filter, token);
            }
        }

        private async Task ScanScopeWithFilterAsync(ChannelRipTarget target, long chatId, int? topicId, string scopeKey, SearchMessagesFilter filter, CancellationToken token)
        {
            long fromMessageId = 0;

            while (!token.IsCancellationRequested)
            {
                MessageTopic topic = topicId.HasValue
                    ? new MessageTopicForum(topicId.Value)
                    : scopeKey == "general"
                        ? new MessageTopicForum((int)ForumTopicService.GeneralId)
                        : null;
                var response = await _clientService.SendAsync(new SearchChatMessages(chatId, topic, string.Empty, null, fromMessageId, -99, 100, filter));
                if (response is not FoundChatMessages found || found.Messages.Count == 0)
                {
                    break;
                }

                foreach (var message in found.Messages)
                {
                    if (!MatchesMediaFilter(message, target.MediaKinds) || IsMessageAlreadyArchived(target, message, topicId))
                    {
                        continue;
                    }

                    await EnqueueAsync(chatId, message, topicId, scopeKey);
                }

                if (found.NextFromMessageId == 0)
                {
                    break;
                }

                fromMessageId = found.NextFromMessageId;
            }
        }

        private async Task EnqueueAsync(ChannelRipTarget target, Message message, int? topicId)
        {
            await EnqueueAsync(target.ChatId, message, topicId, topicId?.ToString() ?? "all");
        }

        private async Task EnqueueAsync(long chatId, Message message, int? topicId, string scopeKey)
        {
            if (_queue == null)
            {
                return;
            }

            if (!IsArchivableMedia(message))
            {
                return;
            }

            var key = $"{chatId}:{message.Id}";

            lock (_syncLock)
            {
                if (_pendingMessages.Contains(key))
                {
                    return;
                }

                _pendingMessages.Add(key);
                _status.QueueCount = _pendingMessages.Count;
            }

            ChannelRipTarget runtime;
            lock (_syncLock)
            {
                runtime = _settings.Targets.FirstOrDefault(x => x.ChatId == chatId);
            }

            if (runtime == null)
            {
                lock (_syncLock)
                {
                    _pendingMessages.Remove(key);
                    _status.QueueCount = _pendingMessages.Count;
                }
                return;
            }

            lock (_syncLock)
            {
                runtime.QueuedCount++;
            }

            await _queue.Writer.WriteAsync(new ChannelRipWorkItem
            {
                Target = runtime,
                Message = message,
                TopicId = topicId,
                ScopeKey = scopeKey
            });

            PublishStatus();
        }

        private async Task WorkerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ChannelRipWorkItem item;
                try
                {
                    item = await _queue.Reader.ReadAsync(token);
                }
                catch
                {
                    return;
                }

                lock (_syncLock)
                {
                    _status.ActiveWorkers++;
                    if (_settings.Targets.FirstOrDefault(x => x.ChatId == item.Target.ChatId) is { } target)
                    {
                        target.ActiveCount++;
                        if (target.QueuedCount > 0)
                        {
                            target.QueuedCount--;
                        }
                    }
                }
                PublishStatus();

                try
                {
                    await ProcessItemAsync(item, token);
                }
                catch (Exception ex)
                {
                    lock (_syncLock)
                    {
                        _status.TotalFailed++;
                        _status.LastError = ex.Message;
                        if (_settings.Targets.FirstOrDefault(x => x.ChatId == item.Target.ChatId) is { } target)
                        {
                            target.FailedCount++;
                            target.LastError = ex.Message;
                        }
                    }
                }
                finally
                {
                    var key = $"{item.Target.ChatId}:{item.Message.Id}";
                    lock (_syncLock)
                    {
                        _pendingMessages.Remove(key);
                        _status.QueueCount = _pendingMessages.Count;
                        _status.ActiveWorkers = Math.Max(0, _status.ActiveWorkers - 1);
                        if (_settings.Targets.FirstOrDefault(x => x.ChatId == item.Target.ChatId) is { } target)
                        {
                            target.ActiveCount = Math.Max(0, target.ActiveCount - 1);
                        }
                    }

                    PublishStatus();
                }
            }
        }

        private async Task ProcessItemAsync(ChannelRipWorkItem item, CancellationToken token)
        {
            if (!item.Target.IsEnabled || !MatchesMediaFilter(item.Message, item.Target.MediaKinds))
            {
                return;
            }

            var file = item.Message.GetFile();
            if (file == null)
            {
                return;
            }

            var uniqueId = GetUniqueId(item, file);
            var dedupeKey = GetDedupeKey(item, file);
            lock (_syncLock)
            {
                if (_ledger.ContainsKey(dedupeKey))
                {
                    _status.TotalSkipped++;
                    if (_settings.Targets.FirstOrDefault(x => x.ChatId == item.Target.ChatId) is { } target)
                    {
                        target.LastSeenMessageIdByScope[item.ScopeKey] = Math.Max(GetScopeCheckpoint(target.LastSeenMessageIdByScope, item.ScopeKey), item.Message.Id);
                        target.SkippedCount++;
                        target.LastError = null;
                    }
                    return;
                }
            }

            var root = await ResolveRootFolderAsync();
            if (root == null)
            {
                SetServiceError("Please configure Channel Ripper root folder.");
                return;
            }

            Telegram.Td.Api.File downloaded = null;
            Exception lastError = null;

            for (int attempt = 1; attempt <= Math.Max(1, _settings.RetryCount); attempt++)
            {
                try
                {
                    downloaded = await _clientService.DownloadFileAsync(file, 32);
                    if (downloaded?.Local?.IsDownloadingCompleted == true && !string.IsNullOrWhiteSpace(downloaded.Local.Path))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token);
            }

            if (downloaded?.Local?.IsDownloadingCompleted != true || string.IsNullOrWhiteSpace(downloaded.Local.Path))
            {
                throw lastError ?? new InvalidOperationException("Download failed.");
            }

            var source = await StorageFile.GetFileFromPathAsync(downloaded.Local.Path);
            var destinationFolder = await BuildDestinationFolderAsync(root, item.Target.ChatId, item.TopicId, item.Message.Date);
            var destinationName = await BuildDestinationNameAsync(item.Message, downloaded, source.FileType);
            var destination = await source.CopyAsync(destinationFolder, destinationName, NameCollisionOption.GenerateUniqueName);

            var entry = new ChannelRipLedgerEntry
            {
                DedupeKey = dedupeKey,
                UniqueId = uniqueId,
                ChatId = item.Target.ChatId,
                MessageId = item.Message.Id,
                FileId = downloaded.Id,
                FilePath = destination.Path,
                FirstSeenUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            lock (_syncLock)
            {
                _ledger[dedupeKey] = entry;
                _status.TotalDownloaded++;

                if (_settings.Targets.FirstOrDefault(x => x.ChatId == item.Target.ChatId) is { } target)
                {
                    target.LastSeenMessageIdByScope[item.ScopeKey] = Math.Max(GetScopeCheckpoint(target.LastSeenMessageIdByScope, item.ScopeKey), item.Message.Id);
                    target.LastError = null;
                    target.DownloadedCount++;
                }
            }

            await PersistLedgerAsync();
            await PersistSettingsAsync();
        }

        private bool IsMessageAlreadyArchived(ChannelRipTarget target, Message message, int? topicId)
        {
            var file = message?.GetFile();
            if (file == null)
            {
                return false;
            }

            var workItem = new ChannelRipWorkItem
            {
                Target = target,
                Message = message,
                TopicId = topicId
            };

            var dedupeKey = GetDedupeKey(workItem, file);

            lock (_syncLock)
            {
                return _ledger.ContainsKey(dedupeKey);
            }
        }

        private static bool IsArchivableMedia(Message message)
        {
            return message?.Content switch
            {
                MessagePhoto => true,
                MessageVideo => true,
                MessageAnimation => true,
                MessageVideoNote => true,
                MessageDocument document => IsVideoDocument(document),
                _ => false
            };
        }

        private static bool MatchesMediaFilter(Message message, ChannelRipMediaKind mediaKinds)
        {
            if (mediaKinds == 0)
            {
                mediaKinds = ChannelRipMediaKind.All;
            }

            return message?.Content switch
            {
                MessagePhoto => mediaKinds.HasFlag(ChannelRipMediaKind.Photo),
                MessageVideo => mediaKinds.HasFlag(ChannelRipMediaKind.Video),
                MessageAnimation => mediaKinds.HasFlag(ChannelRipMediaKind.Animation),
                MessageVideoNote => mediaKinds.HasFlag(ChannelRipMediaKind.VideoNote),
                MessageDocument document => IsVideoDocument(document) && mediaKinds.HasFlag(ChannelRipMediaKind.VideoDocument),
                _ => false
            };
        }

        private bool IsForumLike(Chat chat)
        {
            if (chat == null)
            {
                return false;
            }

            return chat.ViewAsTopics || _clientService.IsForum(chat);
        }

        private static bool IsVideoDocument(MessageDocument document)
        {
            if (document?.Document == null)
            {
                return false;
            }

            if (document.IsPhoto())
            {
                return false;
            }

            var extension = Path.GetExtension(document.Document.FileName ?? string.Empty);
            return !string.IsNullOrWhiteSpace(extension) && VideoDocumentExtensions.Contains(extension);
        }

        private static int? GetForumTopicId(MessageTopic topic)
        {
            if (topic is MessageTopicForum forum)
            {
                if (forum.ForumTopicId == ForumTopicService.GeneralId)
                {
                    return null;
                }

                return forum.ForumTopicId;
            }

            return null;
        }

        private async Task<StorageFolder> BuildDestinationFolderAsync(StorageFolder root, long chatId, int? topicId, int unixDate)
        {
            ChannelRipLayoutMode layoutMode;
            lock (_syncLock)
            {
                layoutMode = _settings.LayoutMode;
            }

            var chatTitle = _clientService.GetTitle(chatId);
            var safeChat = Sanitize(chatTitle, "UnknownChat");
            var chatFolder = await root.CreateFolderAsync(safeChat, CreationCollisionOption.OpenIfExists);

            if (layoutMode == ChannelRipLayoutMode.ChannelOnly)
            {
                return chatFolder;
            }

            var topicName = "General";
            if (topicId.HasValue)
            {
                if (_clientService.TryGetForumTopic(chatId, topicId.Value, out var topic))
                {
                    topicName = topic.Info.Name;
                }
                else
                {
                    topicName = $"Topic-{topicId.Value}";
                }
            }

            var safeTopic = Sanitize(topicName, "General");
            var topicFolder = await chatFolder.CreateFolderAsync(safeTopic, CreationCollisionOption.OpenIfExists);
            if (layoutMode == ChannelRipLayoutMode.ChannelTopic)
            {
                return topicFolder;
            }

            var date = DateTimeOffset.FromUnixTimeSeconds(unixDate);
            var day = date.ToString("yyyy-MM-dd");
            return await topicFolder.CreateFolderAsync(day, CreationCollisionOption.OpenIfExists);
        }

        private async Task<string> BuildDestinationNameAsync(Message message, Telegram.Td.Api.File file, string fallbackExtension)
        {
            var extension = fallbackExtension;
            try
            {
                var response = await _clientService.SendAsync(new GetSuggestedFileName(file.Id, string.Empty));
                if (response is Text text)
                {
                    var ext = Path.GetExtension(text.TextValue);
                    if (!string.IsNullOrWhiteSpace(ext))
                    {
                        extension = ext;
                    }
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".bin";
            }

            var date = DateTimeOffset.FromUnixTimeSeconds(message.Date).ToString("yyyyMMdd_HHmmss");
            return $"{date}_{message.Id}_{file.Id}{extension}";
        }

        private string GetUniqueId(ChannelRipWorkItem item, Telegram.Td.Api.File file)
        {
            var uniqueId = file.Remote?.UniqueId;
            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                return uniqueId;
            }

            return $"fallback:{item.Target.ChatId}:{item.Message.Id}:{file.Id}";
        }

        private string GetDedupeKey(ChannelRipWorkItem item, Telegram.Td.Api.File file)
        {
            var uniqueId = GetUniqueId(item, file);
            ChannelRipDedupeMode mode;
            lock (_syncLock)
            {
                mode = _settings.DedupeMode;
            }

            return mode switch
            {
                ChannelRipDedupeMode.PerChat => $"{uniqueId}|chat:{item.Target.ChatId}",
                ChannelRipDedupeMode.PerTopic => $"{uniqueId}|chat:{item.Target.ChatId}|topic:{item.TopicId ?? 0}",
                _ => uniqueId
            };
        }

        private async Task<StorageFolder> ResolveTargetBrowseFolderAsync(ChannelRipTarget target)
        {
            var root = await ResolveRootFolderAsync();
            if (root == null)
            {
                return null;
            }

            ChannelRipLayoutMode layoutMode;
            lock (_syncLock)
            {
                layoutMode = _settings.LayoutMode;
            }

            var chatFolder = await root.CreateFolderAsync(Sanitize(_clientService.GetTitle(target.ChatId), "UnknownChat"), CreationCollisionOption.OpenIfExists);
            if (layoutMode == ChannelRipLayoutMode.ChannelOnly || target.SelectedTopicIds == null || target.SelectedTopicIds.Count != 1)
            {
                return chatFolder;
            }

            var topicId = target.SelectedTopicIds[0];
            var topicName = _clientService.TryGetForumTopic(target.ChatId, topicId, out var topic)
                ? topic.Info.Name
                : $"Topic-{topicId}";
            return await chatFolder.CreateFolderAsync(Sanitize(topicName, "General"), CreationCollisionOption.OpenIfExists);
        }

        private async Task<StorageFolder> ResolveRootFolderAsync()
        {
            string token;
            string path;
            lock (_syncLock)
            {
                token = _settings.RootFolderToken;
                path = _settings.RootFolderPath;
            }

            if (!string.IsNullOrWhiteSpace(token) && SAP.FutureAccessList.ContainsItem(token))
            {
                try
                {
                    return await SAP.FutureAccessList.GetFolderAsync(token);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                try
                {
                    return await StorageFolder.GetFolderFromPathAsync(path);
                }
                catch
                {
                }
            }

            return null;
        }

        private async Task<StorageFolder> ResolveBackupFolderAsync()
        {
            string token;
            string path;
            lock (_syncLock)
            {
                token = _settings.LedgerBackupFolderToken;
                path = _settings.LedgerBackupFolderPath;
            }

            if (!string.IsNullOrWhiteSpace(token) && SAP.FutureAccessList.ContainsItem(token))
            {
                try
                {
                    return await SAP.FutureAccessList.GetFolderAsync(token);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                try
                {
                    return await StorageFolder.GetFolderFromPathAsync(path);
                }
                catch
                {
                }
            }

            return null;
        }

        private async Task PersistSettingsAsync()
        {
            await _storageLock.WaitAsync();
            try
            {
                if (_settingsPath == null)
                {
                    return;
                }

                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                await AtomicWriteAsync(_settingsPath, json);
            }
            finally
            {
                _storageLock.Release();
            }
        }

        private async Task PersistLedgerAsync()
        {
            await _storageLock.WaitAsync();
            try
            {
                if (_ledgerPath == null)
                {
                    return;
                }

                List<ChannelRipLedgerEntry> entries;
                lock (_syncLock)
                {
                    entries = _ledger.Values.ToList();
                }

                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                await AtomicWriteAsync(_ledgerPath, json);

                var backup = await ResolveBackupFolderAsync();
                if (backup != null)
                {
                    var file = await backup.CreateFileAsync(Path.GetFileName(_ledgerPath), CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(file, json);
                }
            }
            finally
            {
                _storageLock.Release();
            }
        }

        private static async Task AtomicWriteAsync(string path, string content)
        {
            var temp = path + ".tmp";
            await System.IO.File.WriteAllTextAsync(temp, content);
            System.IO.File.Copy(temp, path, true);
            System.IO.File.Delete(temp);
        }

        private void SetServiceError(string error)
        {
            lock (_syncLock)
            {
                _status.LastError = error;
            }

            PublishStatus(error);
        }

        private void PublishStatus(string lastError = null)
        {
            ChannelRipStatus snapshot;
            lock (_syncLock)
            {
                _status.Targets = _settings.Targets.Select(CloneTarget).ToList();
                _status.RootFolderPath = _settings.RootFolderPath;
                _status.LedgerBackupFolderPath = _settings.LedgerBackupFolderPath;
                _status.UseFlatLayout = _settings.LayoutMode == ChannelRipLayoutMode.ChannelOnly;
                _status.LayoutMode = _settings.LayoutMode;
                _status.DedupeMode = _settings.DedupeMode;
                if (lastError != null)
                {
                    _status.LastError = lastError;
                }

                snapshot = CloneStatus(_status);
            }

            _aggregator.Publish(new UpdateChannelRipStatus(snapshot));
        }

        private static ChannelRipTarget CloneTarget(ChannelRipTarget target)
        {
            return new ChannelRipTarget
            {
                ChatId = target.ChatId,
                TitleSnapshot = target.TitleSnapshot,
                IsEnabled = target.IsEnabled,
                SelectedTopicIds = target.SelectedTopicIds?.ToList() ?? new List<int>(),
                KnownTopics = target.KnownTopics?.Select(CloneTopicChoice).ToList() ?? new List<ChannelRipTopicChoice>(),
                MediaKinds = target.MediaKinds,
                LastSeenMessageIdByScope = target.LastSeenMessageIdByScope?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, long>(),
                LastError = target.LastError,
                LastBackfillUnixTime = target.LastBackfillUnixTime,
                LastLiveUnixTime = target.LastLiveUnixTime,
                IsBackfillRunning = target.IsBackfillRunning,
                QueuedCount = target.QueuedCount,
                ActiveCount = target.ActiveCount,
                DownloadedCount = target.DownloadedCount,
                SkippedCount = target.SkippedCount,
                FailedCount = target.FailedCount
            };
        }

        private static ChannelRipTopicChoice CloneTopicChoice(ChannelRipTopicChoice topic)
        {
            if (topic == null)
            {
                return null;
            }

            return new ChannelRipTopicChoice
            {
                Id = topic.Id,
                Name = topic.Name,
                UnreadCount = topic.UnreadCount
            };
        }

        private static ChannelRipStatus CloneStatus(ChannelRipStatus status)
        {
            return new ChannelRipStatus
            {
                IsRunning = status.IsRunning,
                QueueCount = status.QueueCount,
                ActiveWorkers = status.ActiveWorkers,
                TotalDownloaded = status.TotalDownloaded,
                TotalSkipped = status.TotalSkipped,
                TotalFailed = status.TotalFailed,
                LastError = status.LastError,
                RootFolderPath = status.RootFolderPath,
                LedgerBackupFolderPath = status.LedgerBackupFolderPath,
                UseFlatLayout = status.UseFlatLayout,
                LayoutMode = status.LayoutMode,
                DedupeMode = status.DedupeMode,
                Targets = status.Targets?.Select(CloneTarget).ToList() ?? new List<ChannelRipTarget>()
            };
        }

        private static long GetScopeCheckpoint(Dictionary<string, long> scopes, string key)
        {
            if (scopes != null && key != null && scopes.TryGetValue(key, out var checkpoint))
            {
                return checkpoint;
            }

            return 0;
        }

        private void SetTargetBackfillState(long chatId, bool value)
        {
            lock (_syncLock)
            {
                if (_settings.Targets.FirstOrDefault(x => x.ChatId == chatId) is { } current)
                {
                    current.IsBackfillRunning = value;
                }
            }
        }

        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            var result = new string(chars).Trim();

            if (string.IsNullOrWhiteSpace(result))
            {
                return fallback;
            }

            return result;
        }
    }
}
