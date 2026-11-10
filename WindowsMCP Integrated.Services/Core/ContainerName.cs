using OpenCvSharp.Flann;
using System.Drawing;
using WindowsMCP_Integrated.Core.UI;

namespace WindowsMCP_Integrated.Core
{

    public enum ContainerName
    {
        Unknown,
        Main,
        Alliance,
        AllianceTech,
        AllianceGifts,
        Warehouse,
        Alliance_Gifts,
        Alliance_Tech_Donate,
        ConfirmCancelModal,
        MapSearch,
        MapSearch_SavedList,
        Overlay,
        WorldMap_Bounty,
        WorldMap_Tight,
        VSDuel
    }
}

namespace WindowsMCP_Integrated.Core
{
    public static partial class GameElements
    {

        public static Size ReferenceSize = new Size(1920, 1080);
        public static ScrollableReadableTable SvSPointsRankingTable = ScrollableReadableTable.Create("SvS Alliance Ranking Table"
            , new Point(750, 330)
            , new Point(1165, 765)
            , 20);

        public class ConfirmCancelModal
        {

            public static ElementLocation ModalCancelButton = new ElementLocation("Modal Cancel", new Point(1060, 614), new Size(185, 60), ContainerName.ConfirmCancelModal);
            public static ElementLocation ModalConfirmButton = new ElementLocation("Modal Confirm", new Point(861, 614), new Size(185, 60), ContainerName.ConfirmCancelModal);

        }
        public class Main
        {

            public static ElementLocation AllianceButton = new ElementLocation("Alliance", new Point(1883, 782), new Size(54, 62), ContainerName.Main);
            public static ElementLocation EventsButton = new ElementLocation("Events", new Point(1883, 106), new Size(63, 65), ContainerName.Main);
            public static ElementLocation HelpButton = new ElementLocation("Help", new Point(1075, 585), new Size(196, 63), ContainerName.Main);
            public static ElementLocation HotEventsButton = new ElementLocation("Hot Events", new Point(1883, 173), new Size(70, 58), ContainerName.Main);
            public static ElementLocation MailButton = new ElementLocation("Mail", new Point(1883, 844), new Size(52, 62), ContainerName.Main);
            public static ElementLocation VSButton = new ElementLocation("Vs", new Point(1883, 241), new Size(65, 58), ContainerName.Main);
            public static ElementLocation WarehouseButton = new ElementLocation("Warehouse", new Point(1883, 908), new Size(70, 63), ContainerName.Main);

        }
        public class Warehouse
        {
            public ElementLocation GetItemLocationByName(string itemName)
            {
                return itemName.ToLower() switch
                {
                    "shield" => new ElementLocation("Shield", new Point(1013, 639), new Size(10, 10), ContainerName.Warehouse),
                    _ => throw new ArgumentException($"Item '{itemName}' not found in Warehouse."),
                };
            }
        }
        public class MapSearch
        {

            public static ElementLocation SearchSpecialButton = new ElementLocation("Search Special", new Point(850, 81), new Size(50, 50), ContainerName.MapSearch);

            public class SavedList
            {
                public static ElementLocation SearchSpecialFirst = new ElementLocation("Search Special First", new Point(1111, 198), new Size(50, 50), ContainerName.MapSearch_SavedList);
                public static ElementLocation SearchSpecialFirstTrash = new ElementLocation("Search Special First Trash", new Point(981, 195), new Size(50, 50), ContainerName.MapSearch_SavedList);

            }
        }
        public class Overlay
        {
            public static ElementLocation BackButton = new ElementLocation("Overlay Back Button", new Point(764, 981), new Size(50, 50), ContainerName.Overlay);
            public static ElementLocation HeaderPanel = new ElementLocation("Overlay Header Panel", new Point(960, 516), new Size(464, 1000), ContainerName.Overlay);
            public static ElementLocation HeaderText = new ElementLocation("Overlay Header Text", new Point(960, 50), new Size(465, 30), ContainerName.Overlay);
            public static double HeaderTextBinaryMaskThreshold { get; set; } = 190;
        }

        public class WorldMap
        {
            public class Bounty
            {
                public static ElementLocation BountySelectedHelp = new ElementLocation("Bounty Selected Help", new Point(960, 600), new Size(30, 30), ContainerName.WorldMap_Bounty);
                public static ElementLocation BountySelectedTimer = new ElementLocation("Bounty Selected Timer", new Point(939, 425), new Size(65, 20), ContainerName.WorldMap_Bounty);
            }

            public static ElementLocation SearchButton = new ElementLocation("Search Map", new Point(33, 838), new Size(50, 50), ContainerName.WorldMap_Tight);

        }


    }
}