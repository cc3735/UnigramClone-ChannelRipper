//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

#if !ENABLE_CALLS

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Telegram.Common;
using Telegram.Controls;
using Telegram.Navigation.Services;
using Telegram.Services;
using Telegram.Td.Api;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Telegram.Native.Calls
{
    public delegate void VoipVideoOutputSinkFrameReceivedEventHandler(VoipVideoOutputSink sender, FrameReceivedEventArgs args);

    public enum VoipAudioState
    {
        Active,
        Muted
    }

    public enum VoipVideoState
    {
        Inactive,
        Paused,
        Active
    }

    public enum VoipReadyState
    {
        None,
        WaitInit,
        WaitInitAck,
        Established,
        Reconnecting,
        Ready
    }

    public enum VoipGroupConnectionMode
    {
        None
    }

    public enum VoipVideoChannelQuality
    {
        Thumbnail,
        Medium,
        Full
    }

    public sealed class RemoteMediaStateUpdatedEventArgs : EventArgs
    {
    }

    public sealed class FrameReceivedEventArgs : EventArgs
    {
        public float PixelWidth { get; set; }
        public float PixelHeight { get; set; }
    }

    public sealed class BroadcastTimeRequestedEventArgs : EventArgs
    {
    }

    public sealed class AudioBroadcastPartRequestedEventArgs : EventArgs
    {
    }

    public sealed class VideoBroadcastPartRequestedEventArgs : EventArgs
    {
    }

    public sealed class MediaChannelDescriptionsRequestedEventArgs : EventArgs
    {
    }

    public sealed class GroupNetworkStateChangedEventArgs : EventArgs
    {
    }

    public sealed class VoipGroupCallTotalStarCountChangedEventArgs : EventArgs
    {
        public long TotalStarCount { get; set; }
    }

    public sealed class VoipDescriptor
    {
    }

    public sealed class VoipVideoSourceGroup
    {
        public VoipVideoSourceGroup(string semantics, IList<int> sourceIds)
        {
            Semantics = semantics;
            SourceIds = sourceIds;
        }

        public string Semantics { get; }
        public IList<int> SourceIds { get; }
    }

    public abstract class VoipCallServerType
    {
    }

    public sealed class VoipCallServerTypeTelegramReflector : VoipCallServerType
    {
        public VoipCallServerTypeTelegramReflector(string peerTag, bool isTcp)
        {
            PeerTag = peerTag;
            IsTcp = isTcp;
        }

        public string PeerTag { get; }
        public bool IsTcp { get; }
    }

    public sealed class VoipCallServerTypeWebrtc : VoipCallServerType
    {
        public VoipCallServerTypeWebrtc(string username, string password, bool supportsTurn, bool supportsStun)
        {
            Username = username;
            Password = password;
            SupportsTurn = supportsTurn;
            SupportsStun = supportsStun;
        }

        public string Username { get; }
        public string Password { get; }
        public bool SupportsTurn { get; }
        public bool SupportsStun { get; }
    }

    public sealed class VoipCallServer
    {
        public VoipCallServer(long id, string ipAddress, string ipv6Address, int port, VoipCallServerType type)
        {
            Id = id;
            IpAddress = ipAddress;
            Ipv6Address = ipv6Address;
            Port = port;
            Type = type;
        }

        public long Id { get; }
        public string IpAddress { get; }
        public string Ipv6Address { get; }
        public int Port { get; }
        public VoipCallServerType Type { get; }
    }

    public sealed class VoipCallProtocol
    {
        public VoipCallProtocol(bool udpP2p, bool udpReflector, int minLayer, int maxLayer, IList<string> libraryVersions)
        {
            UdpP2p = udpP2p;
            UdpReflector = udpReflector;
            MinLayer = minLayer;
            MaxLayer = maxLayer;
            LibraryVersions = libraryVersions;
        }

        public bool UdpP2p { get; }
        public bool UdpReflector { get; }
        public int MinLayer { get; }
        public int MaxLayer { get; }
        public IList<string> LibraryVersions { get; }
    }

    public class VoipCaptureBase
    {
        public void SetOutput(VoipVideoOutputSink sink)
        {
        }

        public void SetState(VoipVideoState state)
        {
        }

        public void Stop()
        {
        }
    }

    public sealed class VoipVideoCapture : VoipCaptureBase
    {
        public void SwitchToDevice(string deviceId)
        {
        }
    }

    public sealed class VoipScreenCapture : VoipCaptureBase
    {
        public static bool IsSupported() => false;
    }

    public sealed class VoipVideoOutputSink
    {
        public event VoipVideoOutputSinkFrameReceivedEventHandler FrameReceived;
        public bool IsMirrored { get; set; }

        public void Stop()
        {
        }

        public void RaiseFrameReceived() => FrameReceived?.Invoke(this, new FrameReceivedEventArgs());
    }

    public static class VoipVideoOutput
    {
        public static VoipVideoOutputSink CreateSink(object surface, bool uniformToFill = false)
        {
            return new VoipVideoOutputSink();
        }
    }

    public sealed class VoipVideoChannelInfo
    {
        public VoipVideoChannelInfo(int audioSourceId, long participantId, string endpointId, IList<VoipVideoSourceGroup> sourceGroups, VoipVideoChannelQuality minQuality, VoipVideoChannelQuality maxQuality)
        {
            AudioSourceId = audioSourceId;
            ParticipantId = participantId;
            EndpointId = endpointId;
            SourceGroups = sourceGroups;
            MinQuality = minQuality;
            MaxQuality = maxQuality;
        }

        public int AudioSourceId { get; }
        public long ParticipantId { get; }
        public string EndpointId { get; }
        public IList<VoipVideoSourceGroup> SourceGroups { get; }
        public VoipVideoChannelQuality MinQuality { get; }
        public VoipVideoChannelQuality MaxQuality { get; }
    }

    public sealed class VoipGroupParticipant
    {
        public int AudioSource { get; set; }
        public float Level { get; set; }
    }

    public delegate string EmitJsonPayloadDelegate(string payload);
    public delegate byte[] EncryptGroupCallDataDelegate(byte[] data);
    public delegate byte[] DecryptGroupCallDataDelegate(byte[] data);

    public class VoipManager
    {
        public void ReceiveSignalingData(IList<byte> data)
        {
        }

        public void SetAudioInputDevice(string id)
        {
        }

        public void SetAudioOutputDevice(string id)
        {
        }

        public void SetIncomingVideoOutput(VoipVideoOutputSink sink)
        {
        }

        public void SetVideoCapture(VoipCaptureBase videoCapture)
        {
        }

        public void Start(VoipDescriptor descriptor)
        {
        }

        public void Stop()
        {
        }
    }

    public class VoipGroupManager
    {
        public void AddIncomingVideoOutput(string endpointId, VoipVideoOutputSink sink)
        {
        }

        public void EmitJoinPayload(EmitJsonPayloadDelegate completion)
        {
        }

        public void SetAudioInputDevice(string id)
        {
        }

        public void SetAudioOutputDevice(string id)
        {
        }

        public void SetConnectionMode(VoipGroupConnectionMode connectionMode, bool keepBroadcastIfWasEnabled, bool isUnifiedBroadcast)
        {
        }

        public void SetEncryptDecrypt(EncryptGroupCallDataDelegate encryptData, DecryptGroupCallDataDelegate decryptData)
        {
        }

        public void SetJoinResponsePayload(string payload)
        {
        }

        public void SetRequestedVideoChannels(IList<VoipVideoChannelInfo> descriptions)
        {
        }

        public void SetVideoCapture(VoipCaptureBase videoCapture)
        {
        }

        public void SetVolume(int ssrc, double volume)
        {
        }

        public void Stop()
        {
        }
    }
}

namespace Telegram.Td.Api
{
    public class AddedProxy : Object
    {
        public AddedProxy(int id, int lastUsedDate, bool isEnabled, Proxy proxy)
        {
            Id = id;
            LastUsedDate = lastUsedDate;
            IsEnabled = isEnabled;
            Proxy = proxy;
        }

        public int Id { get; set; }
        public int LastUsedDate { get; set; }
        public bool IsEnabled { get; set; }
        public Proxy Proxy { get; set; }
    }

    public class AddedProxies : Object
    {
        public AddedProxies(IList<AddedProxy> proxies)
        {
            Proxies = proxies;
        }

        public IList<AddedProxy> Proxies { get; }
    }

    public abstract class UpgradedGiftAttributeRarity
    {
    }

    public sealed class UpgradedGiftAttributeRarityPerMille : UpgradedGiftAttributeRarity
    {
        public UpgradedGiftAttributeRarityPerMille(int perMille)
        {
            PerMille = perMille;
        }

        public int PerMille { get; }
    }

    public sealed class UpgradedGiftAttributeRarityRare : UpgradedGiftAttributeRarity
    {
    }

    public sealed class UpgradedGiftAttributeRarityLegendary : UpgradedGiftAttributeRarity
    {
    }

    public sealed class UpgradedGiftAttributeRarityUncommon : UpgradedGiftAttributeRarity
    {
    }

    public sealed class UpgradedGiftAttributeRarityEpic : UpgradedGiftAttributeRarity
    {
    }
}

namespace Telegram.Services.Calls
{
    using Telegram.Native.Calls;

    public enum VoipConnectionState
    {
        Pending,
        Ready,
        Error
    }

    public enum VoipState
    {
        None,
        Requesting,
        Waiting,
        Ringing,
        Connecting,
        Ready,
        HangingUp,
        Discarded,
        Error
    }

    public enum VoipGroupCallStreamState
    {
        Unknown,
        NotAvailable,
        Available
    }

    public delegate void VoipCallMediaStateChangedEventHandler(VoipCall sender, VoipCallMediaStateChangedEventArgs args);
    public delegate void VoipCallAudioLevelUpdatedEventHandler(VoipCall sender, VoipCallAudioLevelUpdatedEventArgs args);
    public delegate void VoipCallStateChangedEventHandler(VoipCall sender, VoipCallStateChangedEventArgs args);
    public delegate void VoipCallConnectionStateChangedEventHandler(VoipCall sender, VoipCallConnectionStateChangedEventArgs args);
    public delegate void VoipCallRemoteBatteryLevelIsLowChangedEventHandler(VoipCall sender, VoipCallRemoteBatteryLevelIsLowChangedEventArgs args);
    public delegate void VoipCallSignalBarsUpdatedEventHandler(VoipCall sender, VoipCallSignalBarsUpdatedEventArgs args);
    public delegate void VoipGroupCallNetworkStateChangedEventHandler(VoipGroupCall sender, VoipGroupCallNetworkStateChangedEventArgs args);
    public delegate void VoipGroupCallJoinedStateChangedEventHandler(VoipGroupCall sender, VoipGroupCallJoinedStateChangedEventArgs args);
    public delegate void VoipGroupCallStreamStateChangedEventHandler(VoipGroupCall sender, VoipGroupCallStreamStateChangedEventArgs args);
    public delegate void VoipGroupCallVerificationStateChangedEventHandler(VoipGroupCall sender, VoipGroupCallVerificationStateChangedEventArgs args);
    public delegate void VoipGroupCallMessagesChangedEventHandler(VoipGroupCall sender, VoipGroupCallMessagesChangedEventArgs args);
    public delegate void VoipGroupCallReactionsChangedEventHandler(VoipGroupCall sender, VoipGroupCallReactionsChangedEventArgs args);
    public delegate void VoipGroupCallTopDonorsChangedEventHandler(VoipGroupCall sender, VoipGroupCallTopDonorsChangedEventArgs args);
    public delegate void VoipGroupCallStreamerChangedEventHandler(VoipGroupCall sender, VoipGroupCallStreamerChangedEventArgs args);
    public delegate void VoipGroupCallTotalStarCountChangedEventHandler(VoipGroupCall sender, VoipGroupCallTotalStarCountChangedEventArgs args);
    public delegate void VoipGroupCallAudioLevelsUpdatedEventHandler(VoipGroupCall sender, IList<VoipGroupParticipant> args);

    public class VoipCallMediaStateChangedEventArgs : EventArgs
    {
        public VoipAudioState Audio { get; set; }
        public VoipVideoState Video { get; set; }
        public bool IsScreenSharing { get; set; }
    }

    public class VoipCallAudioLevelUpdatedEventArgs : EventArgs
    {
        public float AudioLevel { get; set; }
    }

    public class VoipCallConnectionStateChangedEventArgs : EventArgs
    {
        public VoipConnectionState State { get; set; }
    }

    public class VoipCallRemoteBatteryLevelIsLowChangedEventArgs : EventArgs
    {
        public bool IsLow { get; set; }
    }

    public class VoipCallSignalBarsUpdatedEventArgs : EventArgs
    {
        public int Count { get; set; }
    }

    public class VoipCallStateChangedEventArgs : EventArgs
    {
        public VoipState State { get; set; }
        public VoipReadyState ReadyState { get; set; }
    }

    public class VoipGroupCallNetworkStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public bool IsTransitioningFromBroadcastToRtc { get; set; }
    }

    public class VoipGroupCallJoinedStateChangedEventArgs : EventArgs
    {
        public bool IsJoined { get; set; }
        public bool NeedRejoin { get; set; }
        public bool IsClosed => !IsJoined && !NeedRejoin;
    }

    public class VoipGroupCallStreamStateChangedEventArgs : EventArgs
    {
        public VoipGroupCallStreamState StreamState { get; set; }
    }

    public class VoipGroupCallVerificationStateChangedEventArgs : EventArgs
    {
        public VoipGroupCallVerificationStateChangedEventArgs()
        {
        }

        public VoipGroupCallVerificationStateChangedEventArgs(int generation, IList<string> emojis)
        {
            Generation = generation;
            Emojis = emojis;
        }

        public int Generation { get; set; }
        public IList<string> Emojis { get; set; } = Array.Empty<string>();
    }

    public class VoipGroupCallMessagesChangedEventArgs : EventArgs
    {
        public GroupCallMessage Message { get; set; }
        public bool Deleted { get; set; }
    }

    public class VoipGroupCallReactionsChangedEventArgs : EventArgs
    {
        public MessageSender SenderId { get; set; }
        public long StarCount { get; set; }
    }

    public class VoipGroupCallTopDonorsChangedEventArgs : EventArgs
    {
        public IList<PaidReactor> Donors { get; set; } = Array.Empty<PaidReactor>();
    }

    public class VoipGroupCallStreamerChangedEventArgs : EventArgs
    {
        public GroupCallParticipant Streamer { get; set; }
    }

    public class VoipGroupCallTotalStarCountChangedEventArgs : EventArgs
    {
        public long TotalStarCount { get; set; }
    }

    public abstract class VoipCallBase : INotifyPropertyChanged
    {
        private Call _call;

        protected VoipCallBase(IClientService clientService = null)
        {
            ClientService = clientService;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IClientService ClientService { get; }

        public virtual Call Call
        {
            get => _call;
            protected set
            {
                _call = value;
                OnPropertyChanged();
            }
        }

        public virtual void Discard()
        {
        }

        public virtual void Show()
        {
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class VoipCall : VoipCallBase
    {
        private VoipAudioState _audioState;
        private VoipVideoState _videoState;

        public VoipCall(IClientService clientService = null)
            : base(clientService)
        {
        }

        public long UserId { get; set; }
        public int Duration { get; set; }
        public string AudioInputId { get; set; }
        public string AudioOutputId { get; set; }
        public string VideoInputId { get; set; }
        public bool IsScreenSharing { get; set; }
        public MediaDeviceTracker Devices { get; } = new MediaDeviceTracker();
        public VoipState State { get; set; }
        public VoipReadyState ReadyState { get; set; }
        public VoipVideoState RemoteVideoState { get; set; }

        public VoipAudioState AudioState
        {
            get => _audioState;
            set
            {
                _audioState = value;
                MediaStateChanged?.Invoke(this, new VoipCallMediaStateChangedEventArgs { Audio = value, Video = _videoState });
            }
        }

        public VoipVideoState VideoState
        {
            get => _videoState;
            set
            {
                _videoState = value;
                MediaStateChanged?.Invoke(this, new VoipCallMediaStateChangedEventArgs { Audio = _audioState, Video = value });
            }
        }

        public event VoipCallMediaStateChangedEventHandler MediaStateChanged;
        public event VoipCallMediaStateChangedEventHandler RemoteMediaStateChanged;
        public event VoipCallAudioLevelUpdatedEventHandler AudioLevelUpdated;
        public event VoipCallStateChangedEventHandler StateChanged;
        public event VoipCallConnectionStateChangedEventHandler ConnectionStateChanged;
        public event VoipCallRemoteBatteryLevelIsLowChangedEventHandler RemoteBatteryLevelIsLowChanged;
        public event VoipCallSignalBarsUpdatedEventHandler SignalBarsUpdated;
        public event EventHandler VideoFailed;

        public void Accept(bool withVideo)
        {
        }

        public void NeedUpdates()
        {
        }

        public void SetLocalVideoOutput(object output)
        {
        }

        public void SetRemoteVideoOutput(object output)
        {
        }

        public void ShareScreen(object captureItem)
        {
            IsScreenSharing = captureItem != null;
        }

        public void RaiseAudioLevel(float level)
        {
            AudioLevelUpdated?.Invoke(this, new VoipCallAudioLevelUpdatedEventArgs { AudioLevel = level });
        }
    }

    public class VoipGroupCall : VoipCallBase
    {
        private bool _isMuted = true;

        public VoipGroupCall(IClientService clientService = null)
            : base(clientService)
        {
        }

        public VoipGroupCall(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator, object xamlRoot, Chat chat, GroupCall groupCall, object source, object payload, bool isLiveStream)
            : base(clientService)
        {
            if (groupCall != null)
            {
                Id = groupCall.Id;
                IsRtmpStream = groupCall.IsRtmpStream;
                ParticipantCount = groupCall.ParticipantCount;
                ScheduledStartDate = groupCall.ScheduledStartDate;
            }
        }

        public int Id { get; set; }
        public bool IsRtmpStream { get; set; }
        public bool IsJoined { get; set; }
        public bool NeedRejoin { get; set; }
        public bool LoadedAllParticipants { get; set; }
        public int ParticipantCount { get; set; }
        public int ScheduledStartDate { get; set; }
        public long PaidMessageStarCount { get; set; }
        public MessageSender MessageSenderId { get; set; }
        public GroupCallParticipant Streamer { get; set; }
        public bool IsConnected { get; set; }
        public bool IsClosed { get; set; }
        public bool IsChannel { get; set; }
        public bool IsVideoChat { get; set; }
        public bool CanEnableVideo { get; set; }
        public bool CanSendMessages { get; set; } = true;
        public bool CanBeManaged { get; set; }
        public double VolumeLevel { get; set; } = 1;
        public Chat Chat { get; set; }
        public GroupCallParticipant CurrentUser { get; set; }
        public VoipGroupCallVerificationStateChangedEventArgs VerificationState { get; set; }
        public VoipGroupCallParticipants Participants { get; set; }
        public VoipGroupCallStreamState StreamState { get; set; }
        public IList<GroupCallMessage> Messages { get; } = new List<GroupCallMessage>();
        public IList<GroupCallMessage> PinnedMessages { get; } = new List<GroupCallMessage>();

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                MutedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler MutedChanged;
        public event VoipGroupCallAudioLevelsUpdatedEventHandler AudioLevelsUpdated;
        public event VoipGroupCallNetworkStateChangedEventHandler NetworkStateChanged;
        public event VoipGroupCallJoinedStateChangedEventHandler JoinedStateChanged;
        public event VoipGroupCallStreamStateChangedEventHandler StreamStateChanged;
        public event VoipGroupCallVerificationStateChangedEventHandler VerificationStateChanged;
        public event VoipGroupCallMessagesChangedEventHandler MessagesChanged;
        public event VoipGroupCallMessagesChangedEventHandler PinnedMessagesChanged;
        public event VoipGroupCallReactionsChangedEventHandler ReactionsChanged;
        public event VoipGroupCallTopDonorsChangedEventHandler TopDonorsChanged;
        public event VoipGroupCallStreamerChangedEventHandler StreamerChanged;
        public event VoipGroupCallTotalStarCountChangedEventHandler TotalStarCountChanged;

        public string GetTitle() => Strings.VoipGroupVoiceChat;

        public void AddIncomingVideoOutput(string endpointId, VoipVideoOutputSink sink)
        {
        }

        public void SetRequestedVideoChannels(IList<VoipVideoChannelInfo> channels)
        {
        }

        public void SendMessage(FormattedText text, long starCount)
        {
        }

        public void Discard(bool endGroupCall)
        {
            IsClosed = true;
            JoinedStateChanged?.Invoke(this, new VoipGroupCallJoinedStateChangedEventArgs { IsJoined = false, NeedRejoin = false });
        }

        public void RaiseAudioLevels(IList<VoipGroupParticipant> participants)
        {
            AudioLevelsUpdated?.Invoke(this, participants);
        }
    }

    public class VoipGroupCallParticipants : List<GroupCallParticipant>
    {
        public object Delegate { get; set; }

        public void LoadVideoInfo()
        {
        }

        public bool TryGetFromAudioSourceId(int audioSourceId, out GroupCallParticipant participant)
        {
            participant = null;
            return false;
        }
    }
}

namespace Telegram.Services
{
    using Telegram.Native.Calls;
    using Telegram.Services.Calls;

    public interface IVoipService
    {
        VoipCallBase ActiveCall { get; }

        void StartPrivateCall(INavigationService navigation, Chat chat, bool video);
        void StartPrivateCall(INavigationService navigation, User user, bool video);

        void JoinGroupCall(INavigationService navigation, InputGroupCall groupCall);
        void JoinGroupCall(INavigationService navigation, long chatId, string inviteHash = null);

        void CreateGroupCall(INavigationService navigation, IList<long> userIds);
        void CreateGroupCall(INavigationService navigation, long chatId);
    }

    public sealed class VoipService : ServiceBase, IVoipService
    {
        public VoipService(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator)
            : base(clientService, settingsService, aggregator)
        {
        }

        public VoipCallBase ActiveCall => null;

        public void StartPrivateCall(INavigationService navigation, Chat chat, bool video)
        {
        }

        public void StartPrivateCall(INavigationService navigation, User user, bool video)
        {
        }

        public void JoinGroupCall(INavigationService navigation, InputGroupCall groupCall)
        {
        }

        public void JoinGroupCall(INavigationService navigation, long chatId, string inviteHash = null)
        {
        }

        public void CreateGroupCall(INavigationService navigation, IList<long> userIds)
        {
        }

        public void CreateGroupCall(INavigationService navigation, long chatId)
        {
        }
    }

    public sealed class VoipCoordinator
    {
        public Telegram.Services.Calls.VoipCallBase ActiveCall => null;

        public void StartPrivateCall(IClientService clientService, INavigationService navigation, Chat chat, bool video)
        {
        }

        public void StartPrivateCall(IClientService clientService, INavigationService navigation, User user, bool video)
        {
        }

        public void JoinGroupCall(IClientService clientService, INavigationService navigation, InputGroupCall groupCall)
        {
        }

        public void JoinGroupCall(IClientService clientService, INavigationService navigation, long chatId, string inviteHash)
        {
        }

        public void CreateGroupCall(IClientService clientService, INavigationService navigation, IList<long> userIds)
        {
        }

        public void CreateGroupCall(IClientService clientService, INavigationService navigation, long chatId)
        {
        }

        public void Handle(IClientService clientService, UpdateCall update)
        {
        }

        public void Handle(IClientService clientService, UpdateNewCallSignalingData update)
        {
        }

        public bool Handle(IClientService clientService, UpdateGroupCall update) => false;
        public bool Handle(IClientService clientService, UpdateGroupCallParticipant update) => false;
        public bool Handle(IClientService clientService, UpdateGroupCallVerificationState update) => false;
        public bool Handle(IClientService clientService, UpdateGroupCallMessageSendFailed update) => false;
        public bool Handle(IClientService clientService, UpdateGroupCallMessagesDeleted update) => false;
        public bool Handle(IClientService clientService, UpdateNewGroupCallMessage update) => false;
        public bool Handle(IClientService clientService, UpdateNewGroupCallPaidReaction update) => false;
        public bool Handle(IClientService clientService, UpdateLiveStoryTopDonors update) => false;
    }
}

namespace Telegram.Views.Calls
{
    public enum ParticipantsGridMode
    {
        Compact,
        Expanded,
        Docked
    }

    public sealed class GroupCallPage : Page
    {
    }

    public sealed class LiveStreamPage : Page
    {
    }

    public sealed class VoipPage : Page
    {
    }
}

namespace Telegram.Views.Calls.Popups
{
    using Telegram.Navigation.Services;
    using Telegram.Native.Calls;

    public sealed class ShareGroupCallPopup : ContentPopup
    {
        public ShareGroupCallPopup(IClientService clientService, INavigationService navigation, GroupCall groupCall)
        {
            Title = "Group Call";
        }
    }

    public sealed class RecordVideoChatPopup : ContentPopup
    {
    }

    public sealed class ScheduleVideoChatPopup : ContentPopup
    {
    }

    public sealed class VideoChatAliasesPopup : ContentPopup
    {
    }

    public sealed class VideoChatStreamsPopup : ContentPopup
    {
    }
}

#endif
