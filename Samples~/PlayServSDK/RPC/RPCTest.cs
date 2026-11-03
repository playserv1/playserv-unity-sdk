using Playserv.Wrapper;
using UnityEngine;

namespace Playserv.Test.RPC
{
    public class RPCTest : MonoBehaviour
    {
        private const string NotificationServiceName = nameof(NotificationService);
        private const string BroadcastMethodName = nameof(NotificationService.BroadcastToAll);

        private void Update()
        {
            if (Input.GetKeyUp(KeyCode.Space))
            {
                PlayServRpc.Invoke(
                    NotificationServiceName,
                    BroadcastMethodName,
                    new { message = "Hello" });
            }
        }

        public static void RPCTestInvokeRequest()
        {
            PlayServRpc.Invoke(
                NotificationServiceName,
                BroadcastMethodName,
                new { message = "Hello from RPCTestInvokeRequest" });
        }
    }
}
