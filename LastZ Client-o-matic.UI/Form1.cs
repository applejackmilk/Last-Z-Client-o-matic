using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WindowsMCP_Integrated;
using WindowsMCP_Integrated.Alliance;
using WindowsMCP_Integrated.Core;
using WindowsMCP_Integrated.VSDuel;

namespace LastZ_Client_o_matic.UI
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource? _captureCancellation;
        private VSService? _vsService;
        private GameService? _gameService;
        private AllianceService? _allianceService;
        public Form1()
        {
            InitializeComponent();
            button1.Text = "Capture VS Rankings";
            Text = "Last Z Client-o-matic";
            FormClosing += (_, _) => _captureCancellation?.Cancel();
            var outputPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Last Z Client-o-matic",
                            "Screenshots");
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
                if (!(await VerifyGameService(cancellation.Token)))
                {
                    throw new InvalidOperationException("Game service must exist!");
                };

                if (_vsService is null)
                {
                    _vsService = new VSService(_gameService);
                }

                Text = "Capturing VS rankings...";
                var succeeded = await _vsService.CaptureDailyVSRanking((int)numericUpDown1.Value, cancellation.Token);
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

        private async void button2_Click(object sender, EventArgs e)
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
                if (!(await VerifyGameService(cancellation.Token)))
                {
                    throw new InvalidOperationException("Game service must exist!");
                };
                if(_allianceService is null)
                {
                    _allianceService = new AllianceService(_gameService);
                }

                await _allianceService.RapidClickHeli(null, (int)numericUpDown1.Value);

            }
            catch (OperationCanceledException)
            {
                Text = "Clicking heli canceled";
            }
            catch (Exception exception)
            {
                Text = "Failed to click heli";
                MessageBox.Show(this, exception.Message, "Failed to click Heli", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _captureCancellation = null;
                cancellation.Dispose();
            }
        }

        private async Task<bool> VerifyGameService(CancellationToken token)
        {
            if (_gameService is null)
            {
                var outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Last Z Client-o-matic",
                    "Screenshots");
                _gameService = await GameServiceFactory.CreateForCurrentGameAsync(
                    outputPath,
                    cancellationToken: token);
            }
            return _gameService != null;
        }
    }
}
