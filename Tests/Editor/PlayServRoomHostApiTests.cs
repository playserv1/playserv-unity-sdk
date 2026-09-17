using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Playserv.Matchmaking;
using Playserv.Wrapper;

namespace Playserv.Tests.Editor
{
    public sealed class PlayServRoomHostApiTests
    {
        [Test]
        public void Public_host_signature_is_additive_and_has_no_caller_selected_room_name()
        {
            Func<PlayServHostRoomRequest, PlayServRoomHostOptions, CancellationToken, Task<PlayServMatchResult>> host = PlayServMatchmaking.HostRoomAsync;
            Assert.That(host, Is.Not.Null);
            Assert.That(typeof(PlayServHostRoomRequest).GetProperty("RoomName"), Is.Null);
            Assert.That(new PlayServRoomHostOptions().Timeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(new PlayServHostRoomRequest().Attributes, Is.Null);
            Assert.That(new PlayServHostRoomRequest().Region, Is.Null);
            Assert.That(typeof(PlayServMatchmaking).GetMethod("HostRoomAsync").GetParameters()[1].IsOptional, Is.True);
            Assert.That(typeof(PlayServMatchmaking).GetMethod("HostRoomAsync").GetParameters()[2].IsOptional, Is.True);
        }

        [Test]
        public void Existing_operation_and_refusal_numeric_values_are_unchanged()
        {
            Assert.That((int)PlayServMatchmakingOperation.FindMatch, Is.EqualTo(0));
            Assert.That((int)PlayServMatchmakingOperation.JoinGame, Is.EqualTo(1));
            Assert.That((int)PlayServMatchmakingOperation.LaunchServer, Is.EqualTo(2));
            Assert.That((int)PlayServMatchmakingOperation.BrowseRooms, Is.EqualTo(3));
            Assert.That((int)PlayServMatchmakingOperation.JoinRoom, Is.EqualTo(4));
            Assert.That((int)PlayServMatchmakingOperation.HostRoom, Is.EqualTo(5));
            Assert.That((int)PlayServRoomFailureCode.Unknown, Is.EqualTo(0));
            Assert.That((int)PlayServRoomFailureCode.RoomNotFound, Is.EqualTo(1));
            Assert.That((int)PlayServRoomFailureCode.RoomFull, Is.EqualTo(2));
            Assert.That((int)PlayServRoomFailureCode.RoomClosed, Is.EqualTo(3));
            Assert.That((int)PlayServRoomFailureCode.RoomTypeNotFound, Is.EqualTo(4));
            Assert.That((int)PlayServRoomFailureCode.RoomRefused, Is.EqualTo(5));
            Assert.That((int)PlayServRoomFailureCode.RoomUnreachable, Is.EqualTo(6));
        }
    }
}
