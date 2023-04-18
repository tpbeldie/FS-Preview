using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Common;

namespace FSPreview
{
    internal sealed class PluginEditor : IVstPluginEditor
    {
        private readonly WinFormsControlWrapper<PluginEditorUi> m_wrapper;

        public PluginEditor() {
            m_wrapper = new WinFormsControlWrapper<PluginEditorUi>();
        }

        public Rectangle Bounds {
            get { return m_wrapper.Bounds; }
        }

        public void Close() {
            m_wrapper.SafeInstance.Player.DisposeInternal();
            m_wrapper.Close();
        }

        public bool KeyDown(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifers) {
            return false;
        }

        public bool KeyUp(byte ascii, VstVirtualKey virtualKey, VstModifierKeys modifers) {
            return false;
        }

        public VstKnobMode KnobMode { get; set; }

        public void Open(IntPtr hWnd) {
            m_wrapper.Open(hWnd);
        }

        public void ProcessIdle() { }
    }
}
