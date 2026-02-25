using System;

namespace Playserv.Test.RPC
{
    [Rpc]
    public class NotificationService
    {
        public Result BroadcastToAll(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message cannot be empty");
            }

            var notificationEvent = new NotificationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Message = message,
                Timestamp = DateTime.UtcNow,
                EventType = "Broadcast"
            };

            Playserv.Wrapper.PlayServ.Publish(notificationEvent);

            return Result.Ok();
        }

        public Result NotifyGroup(string groupName, string message)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return Result.Fail("Group name cannot be empty");
            }

            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message cannot be empty");
            }

            var notificationEvent = new NotificationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Message = message,
                Timestamp = DateTime.UtcNow,
                EventType = "GroupNotification"
            };

            Playserv.Wrapper.PlayServ.PublishForGroup(groupName, notificationEvent);

            return Result.Ok();
        }

        public Result NotifyUser(string userId, string message)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Fail("User ID cannot be empty");
            }

            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message cannot be empty");
            }

            var notificationEvent = new NotificationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Message = message,
                Timestamp = DateTime.UtcNow,
                EventType = "UserNotification"
            };

            Playserv.Wrapper.PlayServ.PublishForUser(userId, notificationEvent);

            return Result.Ok();
        }

        public Result SendMultiLevelNotification(string message, string groupName, string userId)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message cannot be empty");
            }

            var broadcastEvent = new NotificationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Message = $"[BROADCAST] {message}",
                Timestamp = DateTime.UtcNow,
                EventType = "Broadcast"
            };

            Playserv.Wrapper.PlayServ.Publish(broadcastEvent);

            if (!string.IsNullOrEmpty(groupName))
            {
                var groupEvent = new NotificationEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Message = $"[GROUP] {message}",
                    Timestamp = DateTime.UtcNow,
                    EventType = "GroupNotification"
                };

                Playserv.Wrapper.PlayServ.PublishForGroup(groupName, groupEvent);
            }

            if (!string.IsNullOrEmpty(userId))
            {
                var userEvent = new NotificationEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Message = $"[USER] {message}",
                    Timestamp = DateTime.UtcNow,
                    EventType = "UserNotification"
                };

                Playserv.Wrapper.PlayServ.PublishForUser(userId, userEvent);
            }

            return Result.Ok();
        }
    }
}
