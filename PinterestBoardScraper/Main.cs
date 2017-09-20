using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PinterestBoardScraper
{
    public partial class Main : Form
    {
        private Thread ProcessThread;
        private Regex regex = new Regex("\"id\": \"(\\d+)\", \"name");

        private List<string> _urls = new List<string>();

        private string urlTemplate = "https://api.pinterest.com/v1/boards/##BOARD_ID##/pins/?access_token=AWzVSswfTKq5ILJ3gx4tTeOVFtkwFOXlvgQhTD5EUTXzqKA8kAAAAAA&fields=note%2Cimage";
        private string appId = "4922774576785534639";
        private string appSecret = "538dd558fa63d2c74c1c187b6a2b9e7b3331581b971d1ea13f732dc689597f19";

        private string accessToken = "AWzVSswfTKq5ILJ3gx4tTeOVFtkwFOXlvgQhTD5EUTXzqKA8kAAAAAA";

        public Main()
        {
            InitializeComponent();
        }

        private void btnFolderBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Place to save the images in separate folders by board.";
                fbd.RootFolder = Environment.SpecialFolder.DesktopDirectory;
                fbd.ShowNewFolderButton = true;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    btnStart.Enabled = true;
                    tbFolder.Text = fbd.SelectedPath;
                }
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            _urls = tbUrls.Text.Split('\n').Select(u => u.Trim()).Distinct().Where(u => !string.IsNullOrEmpty(u)).ToList();

            btnFolderBrowse.Enabled = false;
            btnStart.Enabled = false;
            btnStop.Enabled = true;

            ProcessThread = new Thread(ProcessUrls);
            ProcessThread.IsBackground = true;
            ProcessThread.Start();
        }

        private void ProcessUrls()
        {
            Parallel.ForEach(_urls, u =>
            {
                if (!ProcessThread.IsAlive)
                {
                    return;
                }

                ProcessBoard(u);
            });

            this.Invoke((MethodInvoker)delegate ()
            {
                MessageBox.Show("Finished!");

                btnStop.Enabled = false;
                btnStart.Enabled = true;
                btnFolderBrowse.Enabled = true;
            });
        }

        private void ProcessBoard(string u)
        {
            try
            {
                if (!ProcessThread.IsAlive)
                {
                    return;
                }

                lbProgress.Invoke(new Action<string>(UpdateProgress), "STARTED BOARD: " + u);

                u = u.Trim('/');
                string boardName = u.Substring(u.LastIndexOf('/') + 1);
                boardName = GetValidName(boardName); // just in case
                string boardPath = Path.Combine(tbFolder.Text, boardName);
                Directory.CreateDirectory(boardPath);

                string boardId;
                using (var client = new WebClient())
                {
                    string page = client.DownloadString(u);
                    boardId = regex.Match(page).Groups[1].Value;

                    string pinsUrl = urlTemplate.Replace("##BOARD_ID##", boardId);
                    string resultJson = client.DownloadString(pinsUrl);
                    var result = JsonConvert.DeserializeObject<BoardPinsResponse>(resultJson);

                    ProcessPins(result.data, boardPath, boardName);

                    while (!string.IsNullOrEmpty(result.page.next))
                    {
                        pinsUrl = urlTemplate.Replace("##BOARD_ID##", boardId);
                        resultJson = client.DownloadString(result.page.next);
                        result = JsonConvert.DeserializeObject<BoardPinsResponse>(resultJson);

                        ProcessPins(result.data, boardPath, boardName);
                    }
                }

                lbProgress.Invoke(new Action<string>(UpdateProgress), "END BOARD: " + u);
            }
            catch
            {
                // nothing...
            }
        }

        private void ProcessPins(PinData[] pins, string boardPath, string boardName)
        {
            using (var client = new WebClient())
            {
                foreach (var p in pins)
                {
                    try
                    {
                        if (!ProcessThread.IsAlive)
                        {
                            return;
                        }

                        string imageName = GetValidName(p.note.Trim());
                        if (string.IsNullOrEmpty(imageName))
                        {
                            imageName = boardName + "__" + Guid.NewGuid();
                        }

                        lbProgress.Invoke(new Action<string>(UpdateProgress), "Getting pin: " + imageName);

                        string extension = p.image.original.url.Substring(p.image.original.url.LastIndexOf("."));
                        string filePath = Path.Combine(boardPath, imageName);

                        // because full path cannot be more than 248 chars long
                        filePath = filePath.Substring(0, Math.Min(240, filePath.Length)) + extension;
                        if (!File.Exists(filePath))
                        {
                            client.DownloadFile(p.image.original.url, filePath);
                        }
                    }
                    catch
                    {
                        // nothing
                    }
                }
            }
        }

        private string GetValidName(string note)
        {
            string ret = note;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                ret = ret.Replace(c, '_');
            }

            return ret;
        }

        private void UpdateProgress(string text)
        {
            lbProgress.Items.Add(text);
            lbProgress.TopIndex = lbProgress.Items.Count - 1;
        }

        private void StopProcessing()
        {
            try
            {
                ProcessThread.Interrupt();
                ProcessThread.Abort();

                UpdateProgress("");
                UpdateProgress("********STOPPING - 30 sec wait********");
                UpdateProgress("");

                ProcessThread.Join();

                if (!ProcessThread.IsAlive)
                {
                    UpdateProgress("");
                    UpdateProgress("********STOPPED********");
                    UpdateProgress("");
                }
            }
            catch
            {
                UpdateProgress("");
                UpdateProgress("********ERROR********");
                UpdateProgress("");

                Close();
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopProcessing();
            btnStop.Enabled = false;
            btnStart.Enabled = true;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            lbProgress.Items.Add("");
            lbProgress.Items.Add("********STOPPING AND CLOSING********");
            lbProgress.Items.Add("");

            lbProgress.TopIndex = lbProgress.Items.Count - 1;
        }
    }
}