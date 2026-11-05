using System.Drawing;


namespace WindowsMCP_Integrated.Core.UI
{
    public record ElementLocation(string Name, Point Center, Size Size, ContainerName Container)
    {
        public static double ScaleRatio { get; set; } = 1.0;
        public int X => (int)((ScaleRatio * Center.X) - (ScaleRatio * Size.Width) / 2);
        public int Y => (int)((ScaleRatio * Center.Y) - (ScaleRatio * Size.Height) / 2);
        public int ClickX => (int)((ScaleRatio * Center.X) + Random.Shared.Next(-4, 4)); //Randomization to prevent detection of automated clicks, randomization is between -9 and 10 pixels
        public int ClickY => (int)((ScaleRatio * Center.Y) + Random.Shared.Next(-4, 4)); //Randomization to prevent detection of automated clicks, randomization is between -9 and 10 pixels
        public static ElementLocation Create(string name, int centerX, int centerY, int width, int height, ContainerName container)
        {
            return new ElementLocation(name, new Point(centerX, centerY), new Size(width, height), container);
        }

        public static ElementLocation Create(string name, Point topLeft, Size size, ContainerName container)
        {
            var centerX = topLeft.X + size.Width / 2;
            var centerY = topLeft.Y + size.Height / 2;
            return new ElementLocation(name, new Point(centerX, centerY), size, container);
        }

        public static ElementLocation CreateFromTopLeft(string name, int top, int left, int width, int height, ContainerName container)
            => Create(name, new Point(left, top), new Size(width, height), container);
    }
}
