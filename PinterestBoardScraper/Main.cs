using Newtonsoft.Json;
using RestSharp;
using RestSharp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PinterestBoardScraper
{
    public partial class Main : Form
    {
        private Thread ProcessThread;
        private readonly Regex regex = new Regex("\"id\": \"(\\d+)\", \"name");

        private List<string> _urls = new List<string>();

        private const string apiRoot = "https://api.pinterest.com";
        private const string restUrlTemplate = "v1/boards/##BOARD_ID##/pins/?access_token=AWzVSswfTKq5ILJ3gx4tTeOVFtkwFOXlvgQhTD5EUTXzqKA8kAAAAAA&fields=note%2Cimage";

        /*
        private const string urlTemplate = "https://api.pinterest.com/v1/boards/##BOARD_ID##/pins/?access_token=AWzVSswfTKq5ILJ3gx4tTeOVFtkwFOXlvgQhTD5EUTXzqKA8kAAAAAA&fields=note%2Cimage";
        private const string appId = "4922774576785534639";
        private const string appSecret = "538dd558fa63d2c74c1c187b6a2b9e7b3331581b971d1ea13f732dc689597f19";
        private const string accessToken = "AWzVSswfTKq5ILJ3gx4tTeOVFtkwFOXlvgQhTD5EUTXzqKA8kAAAAAA";
        */

        public Main()
        {
            InitializeComponent();
        }

        private void btnFolderBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Place to save the images in separate folders by board.";

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

            tbUrls.ReadOnly = true;
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
                tbUrls.ReadOnly = false;
            });
        }

        private void ProcessBoard(string boardUrl)
        {
            try
            {
                int ctr = 0;
                if (!ProcessThread.IsAlive)
                {
                    return;
                }

                lbProgress.Invoke(new Action<string>(UpdateProgress), "STARTED BOARD: " + boardUrl);

                boardUrl = boardUrl.Trim('/');
                string boardName = boardUrl.Substring(boardUrl.LastIndexOf('/') + 1);
                boardName = GetValidName(boardName); // just in case
                string boardPath = Path.Combine(tbFolder.Text, boardName);
                Directory.CreateDirectory(boardPath);

                string boardId;

                var boardUri = new Uri(boardUrl, UriKind.Absolute);

                var rClient = new RestClient(boardUri.Scheme + @"://" + boardUri.Host);
                string page = rClient.Get(new RestRequest(boardUri.PathAndQuery)).Content;
                boardId = regex.Match(page).Groups[1].Value;

                rClient = new RestClient(apiRoot);

                string pinsUrl = restUrlTemplate.Replace("##BOARD_ID##", boardId);
                var resp = rClient.Get(new RestRequest(pinsUrl, Method.GET));
                string resultJson = resp.Content;
                var result = JsonConvert.DeserializeObject<BoardPinsResponse>(resultJson);

                ProcessPins(result.data, boardPath, boardName);

                while (!string.IsNullOrEmpty(result.page.next))
                {
                    bool success = false;
                    int retryCount = 0;
                    string nextPageUrl = new Uri(result.page.next, UriKind.Absolute).PathAndQuery;
                    var request = new RestRequest(nextPageUrl, Method.GET);
                    while (!success && retryCount < 50)
                    {
                        try
                        {
                            retryCount++;
                            var response = rClient.Get(request);
                            resultJson = response.Content;
                            if (response.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                result = JsonConvert.DeserializeObject<BoardPinsResponse>(resultJson);
                                if (null != result.data)
                                {
                                    success = true;
                                }
                            }
                        }
                        catch
                        { }
                    }

                    ProcessPins(result.data, boardPath, boardName);
                    ctr++;

                    if (ctr >= 10)
                    {
                        var count = Directory.GetFiles(boardPath).Count();
                        lbProgress.Invoke(new Action<string>(UpdateProgress), "Board " + boardName + " got " + count + " pins");
                        ctr = 0;

                        Thread.Sleep(15 * 1000);// 15sec wait between batches
                    }
                }

                lbProgress.Invoke(new Action<string>(UpdateProgress), "END BOARD: " + boardUrl);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, boardUrl);
            }

            if (!ProcessThread.IsAlive)
            {
                return;
            }
        }

        private void ProcessPins(PinData[] pins, string boardPath, string boardName)
        {
            if (null == pins)
            {
                Logger.Log(boardName + " NO PINS");
                lbProgress.Invoke(new Action<string>(UpdateProgress), boardName + " NO PINS");
                return;
            }

            Parallel.ForEach(pins, p =>
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
                        imageName = boardName + "__" + p.id;
                    }

                    string extension = p.image.original.url.Substring(p.image.original.url.LastIndexOf("."));
                    string filePath = Path.Combine(boardPath, imageName);

                    // because full path cannot be more than 248 chars long
                    filePath = filePath.Substring(0, Math.Min(240, filePath.Length)) + extension;
                    if (!File.Exists(filePath))
                    {
                        bool success = false;
                        int retryCount = 0;

                        var imageUri = new Uri(p.image.original.url, UriKind.Absolute);
                        var request = new RestRequest(imageUri.PathAndQuery, Method.GET);
                        var rClient = new RestClient(imageUri.Scheme + @"://" + imageUri.Host);
                        rClient.Timeout = 120 * 1000; // ms = 120 sec
                        while (!success && retryCount < 50)
                        {
                            try
                            {
                                retryCount++;
                                var response = rClient.Get(request);
                                byte[] imageData = Encoding.ASCII.GetBytes(response.Content);
                                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                                {
                                    imageData.SaveAs(filePath);
                                    success = true;
                                }
                            }
                            catch
                            { }
                        }

                        if (!success)
                        {
                            lbProgress.Invoke(new Action<string>(UpdateProgress), "Board " + boardName + " NOT ABLE TO GET " + p.image.original.url);
                            Logger.Log("Board " + boardName + " NOT ABLE TO GET " + p.image.original.url);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, boardName);
                    lbErrors.Invoke(new Action<object[]>(UpdateError), p, ex);
                }

                if (!ProcessThread.IsAlive)
                {
                    return;
                }
            });
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
            if (lbProgress.Items.Count > 350) lbProgress.Items.Clear();

            lbProgress.Items.Add(text);
            lbProgress.TopIndex = lbProgress.Items.Count - 1;
        }

        /// <summary>
        /// update listbox with new error
        /// </summary>
        /// <param name="args">PinData, Exception</param>
        private void UpdateError(params object[] args)
        {
            PinData pin = (PinData)args[0];
            Exception ex = (Exception)args[1];
            lbErrors.Items.Add("Pin url: " + pin.image.original.url);
            lbErrors.Items.Add("Pin id: " + pin.id);
            lbErrors.Items.Add("Exception: " + ex.ToString());
            lbErrors.Items.Add("-------------------------------------------");
            lbErrors.TopIndex = lbErrors.Items.Count - 1;
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
            tbUrls.ReadOnly = false;
            btnFolderBrowse.Enabled = true;
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