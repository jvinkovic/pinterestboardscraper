namespace PinterestBoardScraper
{
    public class PinImage
    {
        public ImageData original { get; set; }
    }

    public class ImageData
    {
        public string url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }
}