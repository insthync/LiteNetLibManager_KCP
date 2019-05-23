using UnityEngine;

namespace KCPTransportLayer
{
    [System.Serializable]
    public struct KCPSetting
    {
        [Tooltip("Iconv")]
        public uint iconv;

        [Tooltip("No delay")]
        public bool enableNoDelay;
        [Tooltip("Internal update timer interval in millisec, default is 100ms")]
        public int interval;
        [Tooltip("Fast resend")]
        public bool enableFastResend;
        [Tooltip("Congestion control")]
        public bool disableCongestionControl;

        [Tooltip("Maximum send window size, default is 32")]
        public int sendWindowSize;
        [Tooltip("Maximum receive window size, default is 32")]
        public int receiveWindowSize;

        [Tooltip("MTU size, default is 1400")]
        public int mtu;

        public int GetNoDelay()
        {
            return enableNoDelay ? 1 : 0;
        }

        public int GetFastResend()
        {
            return enableFastResend ? 1 : 0;
        }

        public int GetCongestionControl()
        {
            return disableCongestionControl ? 1 : 0;
        }
    }
}
