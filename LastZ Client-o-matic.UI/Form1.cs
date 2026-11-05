using WindowsMCP_Integrated;
using WindowsMCP_Integrated.VSDuel;

namespace LastZ_Client_o_matic.UI
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource? _captureCancellation;
        private VSService? _vsService;

        public Form1()
        {
            InitializeComponent();
            button1.Text = "Capture VS Rankings";
            Text = "Last Z Client-o-matic";
            FormClosing += (_, _) => _captureCancellation?.Cancel();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (_captureCancellation is not null)
            {
                _captureCancellation.Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _captureCancellation = cancellation;
            button1.Text = "Cancel Capture";

            try
            {
                if (_vsService is null)
                {
                    var outputPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Last Z Client-o-matic",
                        "Screenshots");
                    var gameService = await GameServiceFactory.CreateForCurrentGameAsync(
                        outputPath,
                        screenshotElementWhenClicking: true,
                        cancellationToken: cancellation.Token);
                    _vsService = new VSService(gameService, new VSElements());
                }

                Text = "Capturing VS rankings...";
                var succeeded = await _vsService.CaptureDailyVSRanking(null, cancellation.Token);
                Text = cancellation.IsCancellationRequested
                    ? "VS rankings capture canceled"
                    : succeeded ? "VS rankings capture completed" : "VS rankings capture failed";
            }
            catch (OperationCanceledException)
            {
                Text = "VS rankings capture canceled";
            }
            catch (Exception exception)
            {
                Text = "VS rankings capture failed";
                MessageBox.Show(this, exception.Message, "VS rankings capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _captureCancellation = null;
                cancellation.Dispose();
                button1.Text = "Capture VS Rankings";
            }
        }
    }
}
