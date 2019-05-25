using LiteNetLibManager;

namespace KCPTransportLayer
{
    public class KCPTransportFactory : BaseTransportFactory
    {
        public override bool CanUseWithWebGL { get { return false; } }
        
        public KCPSetting clientSetting = new KCPSetting()
        {
            noDelay = 0,
            interval = 30,
            resend = 2,
            noCongestion = 1,
            sendWindowSize = 32,
            receiveWindowSize = 32,
            mtu = 1400,
        };

        public KCPSetting serverSetting = new KCPSetting()
        {
            noDelay = 0,
            interval = 30,
            resend = 2,
            noCongestion = 1,
            sendWindowSize = 32,
            receiveWindowSize = 32,
            mtu = 1400,
        };

        public override ITransport Build()
        {
            return new KCPTransport()
            {
                clientSetting = clientSetting,
                serverSetting = serverSetting
            };
        }
    }
}
