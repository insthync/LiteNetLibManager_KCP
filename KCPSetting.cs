using UnityEngine;

namespace KCPTransportLayer
{
    [System.Serializable]
    public struct KCPSetting
    {
        [Tooltip("Nodelay: 0:disable(default), 1:enable")]
        public int noDelay;
        [Tooltip("Interval: internal update timer interval in millisec, default is 100ms")]
        public int interval;
        [Tooltip("Resend: 0:disable fast resend(default), 1:enable fast resend")]
        public int resend;
        [Tooltip("NC: 0:normal congestion control(default), 1:disable congestion control")]
        public int nc;

        [Tooltip("Maximum send window size, default is 32")]
        public int sendWindowSize;
        [Tooltip("Maximum receive window size, default is 32")]
        public int receiveWindowSize;

        [Tooltip("MTU size, default is 1400")]
        public int mtu;
    }
}
