using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;

namespace FSPreview
{
    public class PluginCommandStub : StdPluginCommandStub
    {
        protected override IVstPlugin CreatePluginInstance()
        {
            return new Plugin();
        }
    }
}
