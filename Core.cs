using MelonLoader;
using System;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(BR_SteamPress.Core), "BR-SteamPress", "0.3.3", "Rusty")]
[assembly: MelonGame("NestedLoop", "BOXROOM")]
[assembly: MelonAdditionalDependencies("BR_MediaAPI", "BR_BookSystem")]

namespace BR_SteamPress
{
    public sealed class Core : MelonMod
    {
        private Task<string> publishing;
        private bool completionPending;
        private bool sceneReady;
        private bool publicationQueued;
        private bool queuedForce;
        private bool waitingWarningIssued;
        private DateTime queuedAtUtc;
        private DateTime nextReadyCheckUtc;

        public override void OnInitializeMelon()
        {
            SteamPressSettings.Initialize(() => QueuePublishing(true));
            LoggerInstance.Msg("SteamPress initialized. Daily issues are written into the configured BR-BookSystem library.");
            if (SteamPressSettings.Enabled) QueuePublishing(false);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            sceneReady = true;
            nextReadyCheckUtc = DateTime.UtcNow.AddSeconds(1);
        }

        public override void OnUpdate()
        {
            HandleCompletion();
            TryStartQueuedPublication();
        }

        private void HandleCompletion()
        {
            if (!completionPending || publishing == null || !publishing.IsCompleted) return;
            completionPending = false;
            if (publishing.IsFaulted)
            {
                Exception error = publishing.Exception?.GetBaseException();
                LoggerInstance.Warning("SteamPress did not publish an issue: " + (error?.Message ?? "Unknown error"));
                return;
            }

            LoggerInstance.Msg(publishing.Result);
            try
            {
                Boxroom_Books.BookLibrarySystem.LoadCache();
                LoggerInstance.Msg("BR-BookSystem catalogue refreshed.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("The issue was written, but the BookSystem catalogue could not refresh: " + ex.Message);
            }
        }

        private void QueuePublishing(bool force)
        {
            publicationQueued = true;
            queuedForce |= force;
            waitingWarningIssued = false;
            queuedAtUtc = DateTime.UtcNow;
            nextReadyCheckUtc = DateTime.UtcNow;
            LoggerInstance.Msg("SteamPress publication queued until BR-BookSystem finishes loading its library setting.");
            TryStartQueuedPublication();
        }

        private void TryStartQueuedPublication()
        {
            if (!publicationQueued || !sceneReady || DateTime.UtcNow < nextReadyCheckUtc) return;
            if (publishing != null && !publishing.IsCompleted) return;
            nextReadyCheckUtc = DateTime.UtcNow.AddSeconds(2);

            if (!IssuePublisher.HasConfiguredLibrary())
            {
                if (!waitingWarningIssued && DateTime.UtcNow - queuedAtUtc >= TimeSpan.FromSeconds(30))
                {
                    waitingWarningIssued = true;
                    LoggerInstance.Warning("SteamPress is still waiting for BR-BookSystem's Book Folder Location. It will continue automatically when the setting becomes available.");
                }
                return;
            }

            bool force = queuedForce;
            publicationQueued = false;
            queuedForce = false;
            StartPublishing(force);
        }

        private void StartPublishing(bool force)
        {
            if (publishing != null && !publishing.IsCompleted)
            {
                LoggerInstance.Msg("SteamPress is already publishing an issue.");
                return;
            }

            LoggerInstance.Msg("SteamPress is gathering today's Steam news in the background...");
            publishing = Task.Run(() => IssuePublisher.PublishTodayAsync(force));
            completionPending = true;
        }
    }
}
