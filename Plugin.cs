using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace FSPreview
{

    public static class PluginFr
    {
        public static BitwigServer s_bwServer;

        static PluginFr() {
            s_bwServer = new BitwigServer();
        }

        public static Plugin Plugin { get; set; }
    }

    public class Plugin : VstPluginWithServices
    {

        // 'F' << 24 | 'S' << 16 | 'P' << 8 | 'P'
        private const int PluginIdentifier = 0x46535050;

        private const string PluginName = "FS-Preview";

        private const string ProductName = "Film Scoring Preview";

        private const string VendorName = "github.com/tpbeldie";

        private const int PluginVersion = 1000;

        private const VstPluginCategory PluginCategory = VstPluginCategory.Analysis;

        private const VstPluginCapabilities PluginCapabilities = VstPluginCapabilities.None;

        private const int InitialDelayInSamples = 0;

        public Plugin() : base(PluginName, PluginIdentifier, new VstProductInfo(ProductName, VendorName, PluginVersion), PluginCategory, InitialDelayInSamples, PluginCapabilities) {
            PluginFr.Plugin = this;
        }

        protected override void ConfigureServices(IServiceCollection services) {
            services.AddSingletonAll<PluginEditor>();
        }
    }
}
