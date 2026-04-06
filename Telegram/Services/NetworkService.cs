//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Telegram.Td.Api;
using Telegram.Common;
using Windows.Networking.Connectivity;

namespace Telegram.Services
{
    public interface INetworkService
    {
        void Reconnect();

        NetworkType Type { get; }
        bool IsMetered { get; }
    }

    public partial class NetworkService : INetworkService
    {
        private readonly IClientService _clientService;
        private readonly ISettingsService _settingsService;
        private readonly IEventAggregator _aggregator;

        public NetworkService(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator)
        {
            StartupTrace.Write("NetworkService.ctor begin");
            _clientService = clientService;
            _settingsService = settingsService;
            _aggregator = aggregator;
            StartupTrace.Write("NetworkService.ctor fields assigned");

            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            StartupTrace.Write("NetworkService.ctor subscribed to NetworkStatusChanged");

            try
            {
                Update(NetworkInformation.GetInternetConnectionProfile());
            }
            catch (System.Exception ex)
            {
                StartupTrace.Write("NetworkService.ctor startup probe failed", ex);
            }

            StartupTrace.Write("NetworkService.ctor complete");
        }

        public void Reconnect()
        {
            try
            {
                StartupTrace.Write($"NetworkService.Reconnect send type={_type?.GetType().Name}");
                _clientService.Send(new SetNetworkType(_type));
            }
            catch (System.Exception ex)
            {
                StartupTrace.Write("NetworkService.Reconnect", ex);
            }
        }

        private void OnNetworkStatusChanged(object sender)
        {
            StartupTrace.Write("NetworkService.OnNetworkStatusChanged begin");
            try
            {
                Update(NetworkInformation.GetInternetConnectionProfile());
            }
            catch (System.Exception ex)
            {
                StartupTrace.Write("NetworkService.OnNetworkStatusChanged", ex);
            }
        }

        private void Update(ConnectionProfile profile)
        {
            try
            {
                _type = GetNetworkType(profile);
                StartupTrace.Write($"NetworkService.Update send type={_type?.GetType().Name} metered={IsMetered}");
                _clientService.Send(new SetNetworkType(_type));
            }
            catch (System.Exception ex)
            {
                StartupTrace.Write("NetworkService.Update", ex);
            }
        }

        private NetworkType GetNetworkType(ConnectionProfile profile)
        {
            if (profile == null)
            {
                //return new NetworkTypeNone();
                return new NetworkTypeWiFi();
            }

            var cost = profile.GetConnectionCost();
            if (cost != null)
            {
                IsMetered = cost.NetworkCostType is not NetworkCostType.Unrestricted and not NetworkCostType.Unknown;
            }
            else
            {
                IsMetered = false;
            }

            var level = profile.GetNetworkConnectivityLevel();
            if (level is NetworkConnectivityLevel.LocalAccess or NetworkConnectivityLevel.None)
            {
                //return new NetworkTypeNone();
                return new NetworkTypeWiFi();
            }

            if (cost != null && cost.Roaming)
            {
                return new NetworkTypeMobileRoaming();
            }
            else if (profile.IsWlanConnectionProfile)
            {
                return new NetworkTypeWiFi();
            }
            else if (profile.IsWwanConnectionProfile)
            {
                return new NetworkTypeMobile();
            }

            // This is most likely cable connection.
            //return new NetworkTypeOther();
            return new NetworkTypeWiFi();
        }

        private NetworkType _type = new NetworkTypeOther();
        public NetworkType Type
        {
            get => _type;
            private set => _type = value;
        }

        private bool _isMetered;
        public bool IsMetered
        {
            get => _isMetered;
            set => _isMetered = value;
        }
    }
}
