using Microsoft.Extensions.Logging;
using OpenCvSharp;
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
        private CancellationTokenSource? _gameInteractionCancellation;
        private VSService? _vsService;
        private GameService? _gameService;
        private AllianceService? _allianceService;
        public Form1()
        {
            InitializeComponent();
            button1.Text = "Capture VS Rankings";
            Text = "Last Z Client-o-matic";
            FormClosing += (_, _) => _gameInteractionCancellation?.Cancel();
            var outputPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Last Z Client-o-matic",
                            "Screenshots");
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (_gameInteractionCancellation is not null)
            {
                _gameInteractionCancellation.Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _gameInteractionCancellation = cancellation;
            button1.Text = "Cancel Capture";

            try
            {
                if (!(await VerifyGameService(cancellation.Token)))
                {
                    throw new InvalidOperationException("Game service must exist!");
                };
                _vsService ??= new VSService(_gameService);

                Text = "Capturing VS rankings...";
                var succeeded = await _vsService.CaptureDailyVSRanking((int)numericUpDown1.Value, cancellation.Token);

                //var succeeded = await _vsService.ProcessVSRankingsTableDirectoryToFile(folderBrowserDialog1.SelectedPath, (int)numericUpDown1.Value);
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
                _gameInteractionCancellation = null;
                cancellation.Dispose();
                button1.Text = "Capture VS Rankings";
            }

            this.Activate();
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            if (_gameInteractionCancellation is not null)
            {
                _gameInteractionCancellation.Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _gameInteractionCancellation = cancellation;
            button1.Text = "Cancel Capture";

            try
            {
                if (!(await VerifyGameService(cancellation.Token)))
                {
                    throw new InvalidOperationException("Game service must exist!");
                }
                ;
                if (_allianceService is null)
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
                _gameInteractionCancellation = null;
                cancellation.Dispose();
            }
            this.Activate();

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

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private async void button3_Click(object sender, EventArgs e)
        {
            if (_gameInteractionCancellation is not null)
            {
                _gameInteractionCancellation.Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _gameInteractionCancellation = cancellation;
            button1.Text = "Cancel Capture";

            try
            {
                await VerifyVSService(cancellation);

                await _allianceService.RapidClickBounty((int)numericUpDown1.Value);

            }
            catch (OperationCanceledException)
            {
                Text = "Clicking bounties canceled";
            }
            catch (Exception exception)
            {
                Text = "Failed to click bounties";
                MessageBox.Show(this, exception.Message, "Failed to click bounties", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _gameInteractionCancellation = null;
                cancellation.Dispose();
            }
            this.Activate();

        }

        private async Task VerifyVSService(CancellationTokenSource cancellation)
        {
            if (!(await VerifyGameService(cancellation.Token)))
            {
                throw new InvalidOperationException("Game service must exist!");
            };

            if (_allianceService is null)
            {
                _allianceService = new AllianceService(_gameService);
            }
        }
        private string _screenshotOutputPath = @"D:\Development\WindowsMCP Integrated\Screenshots";
        private void button4_Click(object sender, EventArgs e)
        {

            var folderResult = folderBrowserDialog1.ShowDialog();
            if (folderResult != DialogResult.OK)
            {
                return;
            }

            _screenshotOutputPath = folderBrowserDialog1.SelectedPath;
            if (_gameService is not null)
            {
                _gameService.SetScreenshotOutputPath(_screenshotOutputPath);
                openFileDialog1.InitialDirectory = _screenshotOutputPath;
            }

        }

        async void button5_Click(object sender, EventArgs e)
        {
            if (_gameInteractionCancellation is not null && !_gameInteractionCancellation.IsCancellationRequested)
            {
                _gameInteractionCancellation.Cancel();
                return;
            }
            if (!(await VerifyGameService(CancellationToken.None)))
            {
                throw new InvalidOperationException("Game service must exist!");
            }
            ;
            var cancellation = new CancellationTokenSource();
            _gameInteractionCancellation = cancellation;
            var filePath = openFileDialog1.ShowDialog();
            if(filePath != DialogResult.OK || !File.Exists(openFileDialog1.FileName))
            {
                return;
            }
            //var imageProcessing = new ImageProcessing();
            //var tesseractService = new TesseractService();
            //var imageRead = TesseractVSRankingTableRead.ReadTable(openFileDialog1.FileName, debug: true, TesseractService.GetEngine());
            //TesseractService.CreateVSTableCsvFile(
            //    Path.Combine(Path.GetDirectoryName(openFileDialog1.FileName), $"{Path.GetFileNameWithoutExtension(Path.GetDirectoryName(openFileDialog1.FileName))} table.csv")
            //    , imageRead);
            //return;
            //foreach (string file in Directory.GetFiles(Path.GetDirectoryName(openFileDialog1.FileName), "*.png")) 
            try
            {
                _gameService.PreprocessImageForReadingWithDnn(openFileDialog1.FileName);
            }
            catch (OperationCanceledException)
            {
                Text = "Processing SS canceled";
            }
            catch (Exception exception)
            {
                Text = "Failed to process ss";
                MessageBox.Show(this, exception.Message, "Failed to process ss", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _gameInteractionCancellation = null;
                cancellation.Dispose();
            }


            this.Activate();
        }
    }
}
