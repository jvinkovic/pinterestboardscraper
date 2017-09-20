using System;
using System.IO;

namespace PinterestBoardScraper
{
    public static class Logger
    {
        private const string LOGFILE = "log.log";

        /// <summary>
        /// not so safe but...
        /// </summary>
        /// <param name="ex"></param>
        /// <param name="message"></param>
        public static void Log(Exception ex, string message = "")
        {
            string content = message + ":: " + ex.ToString() + "\n------------------------------------\n\n";

            try
            {
                File.AppendAllText(LOGFILE, content);
            }
            catch
            {
                try { File.AppendAllText(LOGFILE, content); } catch { }
            }
        }

        public static void Log(string message)
        {
            string content = message + "\n------------------------------------\n\n";

            try
            {
                File.AppendAllText(LOGFILE, content);
            }
            catch
            {
                try { File.AppendAllText(LOGFILE, content); } catch { }
            }
        }
    }
}
