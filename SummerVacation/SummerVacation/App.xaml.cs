using Plugin.Maui.Audio;

namespace SummerVacation
{
    public partial class App : Application
    {
        private readonly IAudioManager _audioManager;

        public App()
        {
            InitializeComponent();
            // TODO: Replace with actual IAudioManager implementation
            _audioManager = new AudioManager(); // You must provide a valid AudioManager class
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage(_audioManager)) { Title = "SummerVacation" };
        }
    }
}
