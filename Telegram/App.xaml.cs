//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Threading.Tasks;
using Telegram.Common;
using Telegram.Native;
using Telegram.Navigation;
using Telegram.Navigation.Services;
using Telegram.Services;
using Telegram.Services.Updates;
using Telegram.ViewModels;
using Telegram.ViewModels.Authorization;
using Telegram.ViewModels.Business;
using Telegram.ViewModels.Chats;
using Telegram.ViewModels.Create;
using Telegram.ViewModels.Delegates;
using Telegram.ViewModels.Folders;
using Telegram.ViewModels.Payments;
using Telegram.ViewModels.Premium;
using Telegram.ViewModels.Settings;
using Telegram.ViewModels.Settings.Privacy;
using Telegram.ViewModels.Stars;
using Telegram.ViewModels.Supergroups;
using Telegram.ViewModels.Users;
using Telegram.Views;
using Telegram.Views.Authorization;
using Telegram.Views.Business;
using Telegram.Views.Chats;
using Telegram.Views.Create;
using Telegram.Views.Folders;
using Telegram.Views.Folders.Popups;
using Telegram.Views.Host;
using Telegram.Views.Payments;
using Telegram.Views.Popups;
using Telegram.Views.Premium.Popups;
using Telegram.Views.Settings;
using Telegram.Views.Settings.Popups;
using Telegram.Views.Settings.Privacy;
using Telegram.Views.Stars;
using Telegram.Views.Stars.Popups;
using Telegram.Views.Stories.Popups;
using Telegram.Views.Supergroups;
using Telegram.Views.Supergroups.Popups;
using Telegram.Views.Users;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.UI.Notifications;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Telegram
{
    sealed partial class App : BootStrapper
    {
        private static ExtendedExecutionSession _extendedSession;

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            StartupTrace.Write("App.ctor begin");

            try
            {
                TypeCrosserGenerator.Generate();
                StartupTrace.Write("App.ctor TypeCrosserGenerator.Generate complete");

                SettingsService.Current.Initialize();
                StartupTrace.Write("App.ctor SettingsService.Initialize complete");

                GarbageCollectionMonitor.Initialize(GC.Collect, SettingsService.Current.Diagnostics.DisableXamlGcCollect, SettingsService.Current.Diagnostics.DisableMemoryPressure);
                StartupTrace.Write("App.ctor GarbageCollectionMonitor.Initialize complete");

                WatchDog.Initialize();
                StartupTrace.Write("App.ctor WatchDog.Initialize complete");

                LifetimeService.Initialize();
                StartupTrace.Write("App.ctor LifetimeService.Initialize complete");

                StartupTrace.Write("App.ctor preparing RequestedTheme");
                RequestedTheme = SettingsService.Current.Appearance.GetCalculatedApplicationTheme();
                StartupTrace.Write($"App.ctor RequestedTheme assigned {RequestedTheme}");

                StartupTrace.Write("App.ctor preparing InitializeComponent");
                InitializeComponent();
                StartupTrace.Write("App.ctor InitializeComponent complete");
            }
            catch (Exception ex)
            {
                StartupTrace.Write("App.ctor failed", ex);
                throw;
            }
        }

        protected override void OnWindowActivated(Window window, bool active)
        {
            SettingsService.Current.Appearance.UpdateTimer();

            var navigation = WindowContext.GetNavigationService(window);
            if (navigation != null)
            {
                var aggregator = navigation.Session.Resolve<IEventAggregator>();
                aggregator?.Publish(new UpdateWindowActivated(active));

                var clientService = navigation.Session.Resolve<IClientService>();
                clientService?.Options.Online = active;
            }
        }

        protected override async void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);

            if (args.TaskInstance.TriggerDetails is AppServiceTriggerDetails appService && string.Equals(appService.CallerPackageFamilyName, Package.Current.Id.FamilyName))
            {
                BridgeApplicationContext.Connect(appService.AppServiceConnection, args.TaskInstance);
            }
            else
            {
                var deferral = args.TaskInstance.GetDeferral();

                if (args.TaskInstance.TriggerDetails is ToastNotificationActionTriggerDetail triggerDetail)
                {
                    var data = Toast.GetData(triggerDetail);
                    if (data == null)
                    {
                        deferral.Complete();
                        return;
                    }

                    var session = LifetimeService.Current.ActiveItem.Id;
                    if (data.TryGetValue("session", out string value) && int.TryParse(value, out int result))
                    {
                        session = result;
                    }

                    if (LifetimeService.Current.TryResolve(session, out INotificationsService service))
                    {
                        await service.ProcessAsync(data);
                    }
                }

                deferral.Complete();
            }
        }

        public override void OnInitialize(IActivatedEventArgs args)
        {
            StartupTrace.Write($"OnInitialize begin {args?.Kind}");

            //Locator.Configure();
            //UnigramContainer.Current.ResolveType<IGenerationService>();

            if (LifetimeService.Current.Passcode.IsEnabled)
            {
                LifetimeService.Current.Passcode.Lock(true);
                InactivityHelper.Initialize(LifetimeService.Current.Passcode.AutolockTimeout);
            }

            StartupTrace.Write("OnInitialize complete");
        }

        public override async void OnStart(StartKind startKind, IActivatedEventArgs args)
        {
            StartupTrace.Write($"OnStart begin startKind={startKind} activationKind={args?.Kind}");

#if DEBUG
            DebugSettings.EnableFrameRateCounter = false;
#endif

            try
            {
                if (startKind == StartKind.Activate)
                {
                    var sessionId = Toast.GetSession(args);
                    StartupTrace.Write($"OnStart activation sessionId={sessionId?.ToString() ?? "null"}");

                    if (sessionId != null)
                    {
                        if (LifetimeService.Current.ActiveItem.Id != sessionId && LifetimeService.Current.TryResolve(sessionId.Value, out ISession session))
                        {
                            LifetimeService.Current.ActiveItem = session;

                            if (WindowContext.Current.Content is RootPage root)
                            {
                                root.Switch(LifetimeService.Current.ActiveItem);
                            }
                        }
                    }
                }

                var activeSession = LifetimeService.Current.ActiveItem;
                StartupTrace.Write($"OnStart activeSession={activeSession?.Id}");

                var navigation = WindowContext.Current.NavigationServices.GetByFrameId($"{activeSession.Id}");
                StartupTrace.Write("OnStart navigation resolved");

                var update = activeSession.Resolve<ICloudUpdateService>();
                StartupTrace.Write("OnStart ICloudUpdateService resolved");

                var service = activeSession.Resolve<IClientService>();
                StartupTrace.Write("OnStart IClientService resolved");

                var state = await service.GetAuthorizationStateAsync();
                StartupTrace.Write($"OnStart authorization state resolved: {state?.GetType().Name ?? "null"}");

                if (args is not ShareTargetActivatedEventArgs share)
                {
                    WindowContext.Current.Activate(args, navigation, state);
                    StartupTrace.Write("OnStart WindowContext.Activate complete");

                    _ = Task.Run(() => OnStartSync(startKind, update));
                    StartupTrace.Write("OnStart OnStartSync queued");

                    if (startKind != StartKind.Launch && WindowContext.Current.IsInMainView)
                    {
                        var view = ApplicationView.GetForCurrentView();
                        await ApplicationViewSwitcher.TryShowAsStandaloneAsync(view.Id);
                        StartupTrace.Write("OnStart standalone switch complete");
                        //view.TryResizeView(WindowContext.Current.Bounds.ToSize());
                    }
                }
                else if (WindowContext.Current.Content is SharePage sharePage)
                {
                    sharePage.Activate(share, navigation, state);
                    StartupTrace.Write("OnStart SharePage.Activate complete");
                }

                StartupTrace.Write("OnStart complete");
            }
            catch (Exception ex)
            {
                StartupTrace.Write("OnStart failed", ex);
                throw;
            }
        }

        public override UIElement CreateRootElement(IActivatedEventArgs args, WindowContext window)
        {
            StartupTrace.Write($"CreateRootElement(window) begin activationKind={args?.Kind}");

            try
            {
                var sessionId = Toast.GetSession(args);
                if (sessionId != null)
                {
                    if (LifetimeService.Current.ActiveItem.Id != sessionId && LifetimeService.Current.TryResolve(sessionId.Value, out ISession session))
                    {
                        LifetimeService.Current.ActiveItem = session;
                    }
                }

                var activeSession = LifetimeService.Current.ActiveItem;
                StartupTrace.Write($"CreateRootElement(window) activeSession={activeSession?.Id}");

                var navigationService = NavigationServiceFactory(activeSession, window, BackButton.Ignore, $"{activeSession.Id}", true) as NavigationService;
                StartupTrace.Write("CreateRootElement(window) navigation service created");

                if (args is ShareTargetActivatedEventArgs)
                {
                    var sharePage = new SharePage(window, activeSession)
                    {
                        FlowDirection = LocaleService.Current.FlowDirection
                    };

                    StartupTrace.Write("CreateRootElement(window) SharePage created");
                    return sharePage;
                }

                var rootPage = new RootPage(window, navigationService)
                {
                    FlowDirection = LocaleService.Current.FlowDirection
                };

                StartupTrace.Write("CreateRootElement(window) RootPage created");
                return rootPage;
            }
            catch (Exception ex)
            {
                StartupTrace.Write("CreateRootElement(window) failed", ex);
                throw;
            }
        }

        public override UIElement CreateRootElement(INavigationService navigationService)
        {
            return new StandalonePage(navigationService)
            {
                FlowDirection = LocaleService.Current.FlowDirection
            };
        }

        protected override INavigationService CreateNavigationService(ISession session, WindowContext window, Frame frame, string id, bool root)
        {
            if (root)
            {
                return new TLRootNavigationService(session, window, frame, id);
            }

            return new TLNavigationService(session, window, frame, id);
        }

        private async void OnStartSync(StartKind startKind, ICloudUpdateService updateService = null)
        {
            await RequestExtendedExecutionSessionAsync();
            await Toast.RegisterBackgroundTasks();

            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication("App").Clear();
            }
            catch { }

            try
            {
                ToastNotificationManager.History.Clear("App");
            }
            catch { }

            if (Constants.RELEASE && startKind == StartKind.Launch)
            {
                if (await CloudUpdateService.LaunchAsync(true))
                {
                    return;
                }
            }

            if (SettingsService.Current.IsTrayVisible)
            {
                await BridgeApplicationContext.LaunchAsync();
            }
            else if (Constants.RELEASE && startKind == StartKind.Launch)
            {
                await BridgeApplicationContext.AddLoopbackExemptionAsync();
            }

            Windows.ApplicationModel.Core.CoreApplication.EnablePrelaunch(true);

            if (updateService != null)
            {
                await updateService.UpdateAsync(false);
            }
        }

        private async Task RequestExtendedExecutionSessionAsync()
        {
            if (_extendedSession == null && ApiInfo.IsDesktop)
            {
                var session = new ExtendedExecutionSession();
                session.Reason = ExtendedExecutionReason.Unspecified;
                session.Revoked += ExtendedExecutionSession_Revoked;

                var result = await session.RequestExtensionAsync();
                if (result == ExtendedExecutionResult.Allowed)
                {
                    _extendedSession = session;

                    Logger.Info("ExtendedExecutionResult.Allowed");
                }
                else
                {
                    session.Revoked -= ExtendedExecutionSession_Revoked;
                    session.Dispose();

                    Logger.Warning("ExtendedExecutionResult.Denied");
                }
            }
        }

        private void ExtendedExecutionSession_Revoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            Logger.Warning(args.Reason);
            _extendedSession?.Dispose();
            _extendedSession = null;
        }

        public override void OnResuming(object s, object e, AppExecutionState previousExecutionState)
        {
            Logger.Info("OnResuming");

            // #1225: Will this work? No one knows.
            foreach (var network in LifetimeService.Current.ResolveAll<INetworkService>())
            {
                network.Reconnect();
            }

            //foreach (var client in TLContainer.Current.ResolveAll<IClientService>())
            //{
            //    client.TryInitialize();
            //}

            // #2034: Will this work? No one knows.
            SettingsService.Current.Appearance.UpdateNightMode(null);

            OnStartSync(StartKind.Activate);
        }

        public override Task OnSuspendingAsync(object s, SuspendingEventArgs e)
        {
            Logger.Info("OnSuspendingAsync");

            LifetimeService.Current.Passcode.CloseTime = DateTime.UtcNow;

            //return Task.WhenAll(LifetimeService.Current.ResolveAll<IVoipService>().Select(x => x.DiscardAsync()));
            //await Task.WhenAll(LifetimeService.Current.ResolveAll<IClientService>().Select(x => x.CloseAsync()));
            return Task.CompletedTask;
        }

        public override ViewModelBase ViewModelForPage(UIElement page, ISession session)
        {
            var sessionId = session.Id;
            return page switch
            {
                DiagnosticsPage => session.Resolve<DiagnosticsViewModel>(),
                LogOutPopup => session.Resolve<LogOutViewModel>(),
                ProfilePage profile => session.Resolve<ProfileViewModel, IProfileDelegate>(profile),
                InstantPage => session.Resolve<InstantViewModel>(),
                //
                SettingsPage settings => session.Resolve<SettingsViewModel, ISettingsDelegate>(settings),
                NewContactPopup => session.Resolve<NewContactViewModel>(),
                NewChannelPopup => session.Resolve<NewChannelViewModel>(),
                NewGroupPopup => session.Resolve<NewGroupViewModel>(),
                UserEditPage userEdit => session.Resolve<UserEditViewModel, IUserDelegate>(userEdit),
                UserAffiliatePage => session.Resolve<UserAffiliateViewModel>(),
                //
                SupergroupChooseMemberPopup => session.Resolve<SupergroupChooseMemberViewModel>(),
                SupergroupAdministratorsPage supergroupAdministrators => session.Resolve<SupergroupAdministratorsViewModel, ISupergroupDelegate>(supergroupAdministrators),
                SupergroupBannedPage supergroupBanned => session.Resolve<SupergroupBannedViewModel, ISupergroupDelegate>(supergroupBanned),
                SupergroupEditAdministratorPopup supergroupEditAdministrator => session.Resolve<SupergroupEditAdministratorViewModel, IMemberPopupDelegate>(supergroupEditAdministrator),
                SupergroupEditLinkedChatPage supergroupEditLinkedChat => session.Resolve<SupergroupEditLinkedChatViewModel, ISupergroupDelegate>(supergroupEditLinkedChat),
                SupergroupEditRestrictedPopup supergroupEditRestricted => session.Resolve<SupergroupEditRestrictedViewModel, IMemberPopupDelegate>(supergroupEditRestricted),
                SupergroupEditStickerSetPopup => session.Resolve<SupergroupEditStickerSetViewModel>(),
                SupergroupEditTypePage supergroupEditType => session.Resolve<SupergroupEditTypeViewModel, ISupergroupEditDelegate>(supergroupEditType),
                SupergroupEditPage supergroupEdit => session.Resolve<SupergroupEditViewModel, ISupergroupEditDelegate>(supergroupEdit),
                SupergroupMembersPage supergroupMembers => session.Resolve<SupergroupMembersViewModel, ISupergroupDelegate>(supergroupMembers),
                SupergroupPermissionsPage supergroupPermissions => session.Resolve<SupergroupPermissionsViewModel, ISupergroupDelegate>(supergroupPermissions),
                SupergroupTopicsPage => session.Resolve<SupergroupTopicsViewModel>(),
                SupergroupDirectMessagesPage => session.Resolve<SupergroupDirectMessagesViewModel>(),
                SupergroupReactionsPopup => session.Resolve<SupergroupReactionsViewModel>(),
                SupergroupProfileColorPage => session.Resolve<SupergroupProfileColorViewModel>(),
                ChatBoostsPage => session.Resolve<ChatBoostsViewModel>(),
                ChatAffiliatePage => session.Resolve<ChatAffiliateViewModel>(),
                //
                AuthorizationRecoveryPage => session.Resolve<AuthorizationRecoveryViewModel>(),
                AuthorizationRegistrationPage => session.Resolve<AuthorizationRegistrationViewModel>(),
                AuthorizationPasswordPage => session.Resolve<AuthorizationPasswordViewModel>(),
                AuthorizationCodePage => session.Resolve<AuthorizationCodeViewModel>(),
                AuthorizationEmailAddressPage => session.Resolve<AuthorizationEmailAddressViewModel>(),
                AuthorizationEmailCodePage => session.Resolve<AuthorizationEmailCodeViewModel>(),
                AuthorizationPage signIn => session.Resolve<AuthorizationViewModel, ISignInDelegate>(signIn),
                //
                FoldersPage => session.Resolve<FoldersViewModel>(),
                FolderPage => session.Resolve<FolderViewModel>(),
                ShareFolderPopup => session.Resolve<ShareFolderViewModel>(),
                AddFolderPopup => session.Resolve<AddFolderViewModel>(),
                RemoveFolderPopup => session.Resolve<RemoveFolderViewModel>(),
                //
                SettingsBlockedChatsPage => session.Resolve<SettingsBlockedChatsViewModel>(),
                SettingsStickersPage => session.Resolve<SettingsStickersViewModel>(),
                //
                SettingsThemePage => session.Resolve<SettingsThemeViewModel>(),
                //
                SettingsAdvancedPage => session.Resolve<SettingsAdvancedViewModel>(),
                SettingsAppearancePage => session.Resolve<SettingsAppearanceViewModel>(),
                SettingsAutoDeletePage => session.Resolve<SettingsAutoDeleteViewModel>(),
                SettingsBackgroundsPage => session.Resolve<SettingsBackgroundsViewModel>(),
                SettingsDataAndStoragePage => session.Resolve<SettingsDataAndStorageViewModel>(),
                SettingsLanguagePage => session.Resolve<SettingsLanguageViewModel>(),
                SettingsNetworkPage => session.Resolve<SettingsNetworkViewModel>(),
                SettingsNightModePage => session.Resolve<SettingsNightModeViewModel>(),
                SettingsNotificationsExceptionsPage => session.Resolve<SettingsNotificationsExceptionsViewModel>(),
                SettingsPasscodePage => session.Resolve<SettingsPasscodeViewModel>(),
                SettingsPasswordPage => session.Resolve<SettingsPasswordViewModel>(),
                SettingsPasskeysPage => session.Resolve<SettingsPasskeysViewModel>(),
                SettingsPrivacyAndSecurityPage => session.Resolve<SettingsPrivacyAndSecurityViewModel>(),
                SettingsProxyPage => session.Resolve<SettingsProxyViewModel>(),
                SettingsProxyPopup => session.Resolve<SettingsProxyViewModel>(),
                SettingsShortcutsPage => session.Resolve<SettingsShortcutsViewModel>(),
                SettingsThemesPage => session.Resolve<SettingsThemesViewModel>(),
                SettingsWebSessionsPage => session.Resolve<SettingsWebSessionsViewModel>(),
                SettingsNotificationsPage => session.Resolve<SettingsNotificationsViewModel>(),
                SettingsSessionsPage => session.Resolve<SettingsSessionsViewModel>(),
                SettingsStoragePage => session.Resolve<SettingsStorageViewModel>(),
                SettingsProfilePage settingsProfilePage => session.Resolve<SettingsProfileViewModel, IUserDelegate>(settingsProfilePage),
                SettingsProfileColorPage => session.Resolve<SettingsProfileColorViewModel>(),
                SettingsPowerSavingPage => session.Resolve<SettingsPowerSavingViewModel>(),
                SettingsPrivacyAllowCallsPage => session.Resolve<SettingsPrivacyAllowCallsViewModel>(),
                SettingsPrivacyAllowChatInvitesPage => session.Resolve<SettingsPrivacyAllowChatInvitesViewModel>(),
                SettingsPrivacyAllowP2PCallsPage => session.Resolve<SettingsPrivacyAllowP2PCallsViewModel>(),
                SettingsPrivacyAllowPrivateVoiceAndVideoNoteMessagesPage => session.Resolve<SettingsPrivacyAllowPrivateVoiceAndVideoNoteMessagesViewModel>(),
                SettingsPrivacyShowForwardedPage => session.Resolve<SettingsPrivacyShowForwardedViewModel>(),
                SettingsPrivacyPhonePage => session.Resolve<SettingsPrivacyPhoneViewModel>(),
                SettingsPrivacyShowPhotoPage privacyShowPhotoPage => session.Resolve<SettingsPrivacyShowPhotoViewModel, IUserDelegate>(privacyShowPhotoPage),
                SettingsPrivacyShowProfileAudioPage privacyShowProfileAudioPage => session.Resolve<SettingsPrivacyShowProfileAudioViewModel>(),
                SettingsPrivacyShowStatusPage => session.Resolve<SettingsPrivacyShowStatusViewModel>(),
                SettingsPrivacyShowBioPage => session.Resolve<SettingsPrivacyShowBioViewModel>(),
                SettingsPrivacyShowBirthdatePage => session.Resolve<SettingsPrivacyShowBirthdateViewModel>(),
                SettingsPrivacyNewChatPage => session.Resolve<SettingsPrivacyNewChatViewModel>(),
                SettingsPrivacyAutosaveGiftsPage => session.Resolve<SettingsPrivacyAutosaveGiftsViewModel>(),

                BusinessPage => session.Resolve<BusinessViewModel>(),
                BusinessLocationPage => session.Resolve<BusinessLocationViewModel>(),
                BusinessHoursPage => session.Resolve<BusinessHoursViewModel>(),
                BusinessRepliesPage businessRepliesPage => session.Resolve<BusinessRepliesViewModel, IBusinessRepliesDelegate>(businessRepliesPage),
                BusinessGreetPage => session.Resolve<BusinessGreetViewModel>(),
                BusinessAwayPage => session.Resolve<BusinessAwayViewModel>(),
                BusinessBotsPage => session.Resolve<BusinessBotsViewModel>(),
                BusinessIntroPage => session.Resolve<BusinessIntroViewModel>(),
                BusinessChatLinksPage businessChatLinksPage => session.Resolve<BusinessChatLinksViewModel, IBusinessChatLinksDelegate>(businessChatLinksPage),

                RevenuePage => session.Resolve<RevenueViewModel>(),

                PaymentFormPage => session.Resolve<PaymentFormViewModel>(),
                MessageStatisticsPage => session.Resolve<MessageStatisticsViewModel>(),
                ChatInviteLinksPage => session.Resolve<ChatInviteLinksViewModel>(),
                ChatStatisticsPage => session.Resolve<ChatStatisticsViewModel>(),
                ChatRevenuePage => session.Resolve<ChatRevenueViewModel>(),
                ChatStarsPage => session.Resolve<ChatStarsViewModel>(),
                ChatStoriesPage => session.Resolve<ChatStoriesViewModel>(),

                // Popups
                ContactsPopup => session.Resolve<ContactsViewModel>(),
                CallsPopup => session.Resolve<CallsViewModel>(),
                DownloadsPopup => session.Resolve<DownloadsViewModel>(),
                ChannelRipPopup => session.Resolve<ChannelRipViewModel>(),
                SettingsUsernamePopup => session.Resolve<SettingsUsernameViewModel>(),
                ChooseChatsPopup => session.Resolve<ChooseChatsViewModel>(),
                ChooseSoundPopup => session.Resolve<ChooseSoundViewModel>(),
                ChatNotificationsPopup => session.Resolve<ChatNotificationsViewModel>(),
                CreateChatPhotoPopup => session.Resolve<CreateChatPhotoViewModel>(),
                PromoPopup => session.Resolve<PromoViewModel>(),
                StarsPage => session.Resolve<StarsViewModel>(),
                BuyPopup => session.Resolve<BuyViewModel>(),
                PayPopup => session.Resolve<PayViewModel>(),
                StoryInteractionsPopup => session.Resolve<StoryInteractionsViewModel>(),
                BackgroundsPopup => session.Resolve<SettingsBackgroundsViewModel>(),
                BackgroundPopup backgroundPopup => session.Resolve<BackgroundViewModel, IBackgroundDelegate>(backgroundPopup),
                _ => null
            };
        }
    }
}
