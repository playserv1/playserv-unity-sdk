using System;
using UnityEngine;

namespace Playserv.Proxy.Common
{
    [Serializable]
    public sealed class ValidationErrorResponse
    {
        [SerializeField]
        private string error;

        [SerializeField]
        private string receivedJson;

        public string Error => error;
        public string ReceivedJson => receivedJson;
    }
}