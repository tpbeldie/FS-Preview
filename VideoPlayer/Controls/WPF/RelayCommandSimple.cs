using System.Windows.Input;

namespace FSPreview.Controls.WPF
{
    public class RelayCommandSimple : ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        readonly Action execute;
        public RelayCommandSimple(Action execute) { this.execute = execute; }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { execute(); }
    }
}
