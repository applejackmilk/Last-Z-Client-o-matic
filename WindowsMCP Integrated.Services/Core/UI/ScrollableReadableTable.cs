using System.Drawing;

namespace WindowsMCP_Integrated.Core.UI
{
    public record ScrollableReadableTable(string Name, Size ReadRegion, Point ReadTopLeft, Point CursorStartPosition, int EstimatedPartitions, Func<Point> ScrollMethod)
    {
        public static ScrollableReadableTable Create(string name
            , Point upperLeft
            , Point bottomRight
            , int estimatedPartitions)
        {
            var readRegion = new Size(bottomRight.X - upperLeft.X, bottomRight.Y - upperLeft.Y);
            return new ScrollableReadableTable(Name: name,
                                               ReadRegion: readRegion,
                                               ReadTopLeft: upperLeft,
                                               CursorStartPosition: new Point(bottomRight.X - 10, bottomRight.Y - 10),
                                               EstimatedPartitions: estimatedPartitions,
                                               () => new Point(0, readRegion.Height + 3)
                                               );
        }

        public static ScrollableReadableTable Create(string name
            , int readRegionX, int readRegionY
            , int readRegionWidth, int readRegionHeight
            , int cursorStartX, int cursorStartY
            , int estimatedPartitions)
        {
            var readRegion = new Size(readRegionWidth, readRegionHeight);
            return new ScrollableReadableTable(Name: name,
                                               ReadRegion: readRegion,
                                               ReadTopLeft: new Point(readRegionX, readRegionY),
                                               CursorStartPosition: new Point(cursorStartX, cursorStartY),
                                               EstimatedPartitions: estimatedPartitions,
                                               () => new Point(0, readRegion.Height + 3)
                                               );
        }
    }
}
