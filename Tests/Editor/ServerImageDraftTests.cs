using NUnit.Framework;
namespace Playserv.Editor.Tests
{
    public class ServerImageDraftTests
    {
        [Test] public void SavedServerSurvivesDrawingBeforeConnectAndDuringRefresh()
        {
            var draft = new ServerImageDraft("api", "key", "tank-room");
            draft.SelectServer(0); Assert.That(draft.Server, Is.EqualTo("tank-room"));
            draft.CompleteConnection(new[] { "tank-room" }); draft.BeginConnection();
            draft.SelectServer(0); Assert.That(draft.Server, Is.EqualTo("tank-room"));
            draft.CompleteConnection(new[] { "other" }); Assert.That(draft.Server, Is.Empty);
        }
        [Test] public void CredentialsSavedInAnotherTabRefreshOnlyUneditedFields()
        {
            var draft = new ServerImageDraft("old-api", "old-key", "");
            draft.RefreshCredentials("new-api", "new-key");
            Assert.That(draft.Api, Is.EqualTo("new-api")); Assert.That(draft.Key, Is.EqualTo("new-key"));
            draft.Key = "unsaved-draft";
            draft.RefreshCredentials("third-api", "third-key");
            Assert.That(draft.Api, Is.EqualTo("third-api")); Assert.That(draft.Key, Is.EqualTo("unsaved-draft"));
        }
    }
}
