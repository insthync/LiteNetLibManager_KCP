using LiteNetLibManager;

namespace KCPTransportLayer
{
    public class KCPTransportFactory : BaseTransportFactory
    {
        public override bool CanUseWithWebGL { get { return false; } }

        public uint iconv;
        public KCPSetting clientSetting = new KCPSetting()
        {
            enableNoDelay = false,
            interval = 100,
            enableFastResend = false,
            disableCongestionControl = false,
            sendWindowSize = 32,
            receiveWindowSize = 32,
            mtu = 1400,
        };

        public KCPSetting serverSetting = new KCPSetting()
        {
            enableNoDelay = false,
            interval = 100,
            enableFastResend = false,
            disableCongestionControl = false,
            sendWindowSize = 32,
            receiveWindowSize = 32,
            mtu = 1400,
        };

        public override ITransport Build()
        {
            return new KCPTransport()
            {
                iconv = iconv,
                clientSetting = clientSetting,
                serverSetting = serverSetting
            };
        }
    }
}
