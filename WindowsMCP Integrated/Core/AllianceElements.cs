using System.Drawing;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated.Core
{
    public static partial class GameElements
    {
        public class AllianceElements
        {
            public static ElementLocation Tech = new ElementLocation("Tech", new Point(850, 667), new Size(197, 62), ContainerName.Alliance);
            public static ElementLocation Gifts = new ElementLocation("Gifts", new Point(1075, 667), new Size(196, 62), ContainerName.Alliance);
            public static ElementLocation Rankings = new ElementLocation("Rankings", new Point(850, 831), new Size(197, 62), ContainerName.Alliance);
            public class AllianceTech
            {
                public static ElementLocation DonateZentsButton = new ElementLocation("Donate Zents Button", new Point(1058, 763), new Size(186, 59), ContainerName.Alliance_Tech_Donate);
                public static ElementLocation DonateZentsLabel = new ElementLocation("Donate Zents Label", new Point(1058, 718), new Size(120, 15), ContainerName.Alliance_Tech_Donate);
            }
            public class AllianceGifts
            {
                public static ElementLocation ClaimAllButton = new ElementLocation("Claim All", new Point(960, 988), new Size(186, 56), ContainerName.Alliance_Gifts);
                public static ElementLocation GiftsTabCommonText = new ElementLocation("Gifts Tab Common Text", new Point(860, 395), new Size(112, 26), ContainerName.Alliance_Gifts);
                public static ElementLocation GiftsTabRareText = new ElementLocation("Gifts Tab Rare Text", new Point(1079, 396), new Size(48, 26), ContainerName.Alliance_Gifts);
                public static ElementLocation GiftsTrashButton = new ElementLocation("Gifts Trash", new Point(1155, 989), new Size(40, 40), ContainerName.Alliance_Gifts);
                public static Point DraggableTable = new Point(970, 500);
                public static Size TableSize = new Size(400, 400);
                public static ElementLocation Table = new ElementLocation("Gifts Table", DraggableTable, TableSize, ContainerName.AllianceGifts);
            }
        }


    }
}